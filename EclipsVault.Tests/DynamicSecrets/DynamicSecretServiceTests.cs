using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.DynamicSecrets;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Tests.TestDoubles;
using Xunit;

namespace EclipsVault.Tests.DynamicSecrets;

/// <summary>
/// A dynamic credential is live on a real backend, so the lease lifecycle carries the safety
/// properties: a caller cannot lease one for longer than the role allows, a credential the vault
/// failed to create or record is removed rather than left orphaned, a revocation failure is surfaced
/// instead of swallowed, and revoke cannot be used to probe for other people's leases.
/// </summary>
public class DynamicSecretServiceTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class StubActor : IAuditContext
    {
        public Guid? UserId => Owner;
        public string? Username => "dev-user";
        public string? SourceIp => "::1";
    }

    private sealed class RecordingAuditSink : IAuditSink
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private static DynamicSecretRole Role(bool enabled = true, int defaultTtl = 15, int maxTtl = 60) => new()
    {
        Id = Guid.NewGuid(),
        Name = "phoenix_db_reader",
        ProjectKey = "PHOENIX",
        Backend = DynamicSecretBackend.SqlServer,
        CreationStatements = "CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';",
        RevocationStatements = "DROP LOGIN [{{name}}];",
        DefaultTtlMinutes = defaultTtl,
        MaxTtlMinutes = maxTtl,
        IsEnabled = enabled
    };

    private static DynamicSecretService Build(
        FakeLeaseRepository repository, FakeDynamicTarget target, IPremiumFeatureUsage? usage = null, IAuditSink? audit = null)
        => new(repository, [target], new StubActor(), TimeProvider.System, usage ?? new RecordingPremiumFeatureUsage(),
            audit ?? new RecordingAuditSink());

    [Fact]
    public async Task Issuing_records_premium_usage_and_still_leases()
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var usage = new RecordingPremiumFeatureUsage();

        var issued = await Build(repository, new FakeDynamicTarget(), usage).IssueAsync(role.Id, null, CancellationToken.None);

        Assert.Equal(LicenseFeatures.DynamicSecrets, Assert.Single(usage.Recorded));
        Assert.NotEmpty(issued.Secret);
    }

    [Fact]
    public async Task Issuing_mints_a_credential_and_opens_a_lease()
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var target = new FakeDynamicTarget();

        var issued = await Build(repository, target).IssueAsync(role.Id, null, CancellationToken.None);

        Assert.Equal(Assert.Single(target.Logins), issued.Identity);
        Assert.NotEmpty(issued.Secret);

        var lease = Assert.Single(repository.Leases);
        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Equal(issued.Identity, lease.CredentialIdentity);
        Assert.Equal(Owner, lease.UserId);
    }

    [Fact]
    public async Task The_issued_password_is_never_written_to_the_lease()
    {
        // The whole premise of a dynamic secret: the vault keeps no copy. A lease table leak must
        // not yield live credentials, so the secret exists only in the issuing response.
        var role = Role();
        var repository = new FakeLeaseRepository(role);

        var issued = await Build(repository, new FakeDynamicTarget()).IssueAsync(role.Id, null, CancellationToken.None);

        var lease = Assert.Single(repository.Leases);
        Assert.DoesNotContain(issued.Secret, lease.CredentialIdentity);
        Assert.DoesNotContain(issued.Secret, lease.RoleName);
        Assert.DoesNotContain(issued.Secret, lease.Username);
    }

    [Theory]
    [InlineData(15, 60, null, 15)]      // no ask -> the role's default
    [InlineData(15, 60, 5, 5)]          // under the ceiling -> honoured
    [InlineData(15, 60, 600, 60)]       // over the role's ceiling -> clamped, not rejected
    [InlineData(15, 60, 0, 1)]          // nonsense -> floor
    [InlineData(15, 60, -99, 1)]
    [InlineData(2880, 2880, null, 1440)] // a default longer than a day -> a day
    [InlineData(30, 2880, null, 30)]     // a short default under a long ceiling -> the default
    [InlineData(30, 2880, 1440, 1440)]   // a day under a longer ceiling -> a day
    [InlineData(30, 2880, 2000, 1440)]   // past a day under a longer ceiling -> a day
    public async Task A_lease_lasts_what_was_asked_within_the_roles_ceiling_and_a_day(
        int defaultTtl, int maxTtl, int? requested, int expectedMinutes)
    {
        var role = Role(defaultTtl: defaultTtl, maxTtl: maxTtl);
        var repository = new FakeLeaseRepository(role);

        var before = DateTimeOffset.UtcNow;
        var issued = await Build(repository, new FakeDynamicTarget()).IssueAsync(role.Id, requested, CancellationToken.None);

        var actual = (issued.ExpiresAtUtc - before).TotalMinutes;
        Assert.InRange(actual, expectedMinutes - 1, expectedMinutes + 1);
    }

    [Fact]
    public async Task A_disabled_role_issues_nothing()
    {
        var role = Role(enabled: false);
        var target = new FakeDynamicTarget();

        await Assert.ThrowsAsync<VaultAdminException>(
            () => Build(new FakeLeaseRepository(role), target).IssueAsync(role.Id, null, CancellationToken.None));

        Assert.Empty(target.Passwords);
    }

    [Fact]
    public async Task An_unknown_role_issues_nothing()
    {
        var target = new FakeDynamicTarget();

        await Assert.ThrowsAsync<VaultAdminException>(
            () => Build(new FakeLeaseRepository(Role()), target).IssueAsync(Guid.NewGuid(), null, CancellationToken.None));

        Assert.Empty(target.Passwords);
    }

    public static TheoryData<string, int, Exception> FailedMints => new()
    {
        { "CREATE LOGIN fails", 0, new InvalidOperationException("Cannot create the login.") },
        { "CREATE LOGIN runs, its acknowledgement is lost", 1, new IOException("The connection was reset.") },
        { "CREATE USER fails", 1, new InvalidOperationException("Cannot create the user.") },
        { "CREATE USER runs, its acknowledgement is lost", 2, new IOException("The connection was reset.") },
    };

    [Theory]
    [MemberData(nameof(FailedMints))]
    public async Task A_failed_mint_removes_its_login_without_the_request_token_and_issues_nothing(
        string failure, int statementsThatRun, Exception backendError)
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var target = new FakeDynamicTarget { StatementsThatRun = statementsThatRun, MintFailure = backendError };

        var refused = await Assert.ThrowsAsync<MintRefusedException>(
            () => Build(repository, target).IssueAsync(role.Id, null, CancellationToken.None));

        Assert.Equal("The backend refused to mint a credential for 'phoenix_db_reader'. Nothing was issued.", refused.Message);
        Assert.Empty(target.Logins);
        Assert.Empty(target.Users);
        Assert.False(Assert.Single(target.RevokeTokens).CanBeCanceled, failure);
        Assert.Empty(repository.Leases);
    }

    [Fact]
    public async Task A_mint_that_never_reached_the_target_is_refused_without_a_cleanup()
    {
        var role = Role();
        var target = new FakeDynamicTarget { StatementsThatRun = 0, MintFailure = new MintRefusedException("phoenix_db_reader") };

        await Assert.ThrowsAsync<MintRefusedException>(
            () => Build(new FakeLeaseRepository(role), target).IssueAsync(role.Id, null, CancellationToken.None));

        Assert.Empty(target.RevokeTokens);
    }

    [Fact]
    public async Task A_request_cancelled_mid_mint_still_removes_its_login()
    {
        var role = Role();
        var cancelled = new CancellationToken(canceled: true);
        var target = new FakeDynamicTarget { StatementsThatRun = 1, MintFailure = new OperationCanceledException(cancelled) };

        await Assert.ThrowsAsync<MintRefusedException>(
            () => Build(new FakeLeaseRepository(role), target).IssueAsync(role.Id, null, cancelled));

        Assert.Empty(target.Logins);
    }

    public static TheoryData<string, int, Exception, Exception> FailedMintsWithFailedCleanups => new()
    {
        { "CREATE LOGIN's acknowledgement is lost; the revocation fails", 1, new IOException("The connection was reset."), new InvalidOperationException("Cannot drop the login.") },
        { "CREATE LOGIN's acknowledgement is lost; the revocation times out", 1, new IOException("The connection was reset."), new TimeoutException("The command timed out.") },
        { "CREATE USER fails; the revocation fails", 1, new InvalidOperationException("Cannot create the user."), new InvalidOperationException("Cannot drop the login.") },
        { "CREATE USER fails; the revocation times out", 1, new InvalidOperationException("Cannot create the user."), new TimeoutException("The command timed out.") },
        { "CREATE USER's acknowledgement is lost; the revocation fails", 2, new IOException("The connection was reset."), new InvalidOperationException("Cannot drop the login.") },
        { "CREATE USER's acknowledgement is lost; the revocation times out", 2, new IOException("The connection was reset."), new TimeoutException("The command timed out.") },
    };

    [Theory]
    [MemberData(nameof(FailedMintsWithFailedCleanups))]
    public async Task A_failed_mint_whose_cleanup_fails_names_the_login_that_may_remain_and_never_its_password(
        string failure, int statementsThatRun, Exception backendError, Exception cleanupError)
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var target = new FakeDynamicTarget
        {
            StatementsThatRun = statementsThatRun,
            MintFailure = backendError,
            RevokeFailure = cleanupError,
        };

        var incomplete = await Assert.ThrowsAsync<MintIncompleteException>(
            () => Build(repository, target).IssueAsync(role.Id, null, CancellationToken.None));

        Assert.Equal("phoenix_db_reader", incomplete.RoleName);
        Assert.Equal(Assert.Single(target.Logins), incomplete.Login);
        Assert.Equal(
            "The backend failed part-way through, and the vault could not confirm that the login it started to create was removed. " +
            "No password was shown or kept. An administrator must check the target.",
            incomplete.Message);
        Assert.DoesNotContain(Assert.Single(target.Passwords), incomplete.ToString());
        Assert.Empty(repository.Leases);
        Assert.False(Assert.Single(target.RevokeTokens).CanBeCanceled, failure);
    }

    public static TheoryData<string, Exception> FailedCleanups => new()
    {
        { "the revocation statements fail", new InvalidOperationException("Cannot drop the login.") },
        { "the revocation statements time out", new TimeoutException("The command timed out.") },
    };

    [Theory]
    [MemberData(nameof(FailedCleanups))]
    public async Task A_request_cancelled_mid_mint_whose_cleanup_fails_names_the_login_that_may_remain(
        string failure, Exception cleanupError)
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var cancelled = new CancellationToken(canceled: true);
        var target = new FakeDynamicTarget
        {
            StatementsThatRun = 1,
            MintFailure = new OperationCanceledException(cancelled),
            RevokeFailure = cleanupError,
        };

        var incomplete = await Assert.ThrowsAsync<MintIncompleteException>(
            () => Build(repository, target).IssueAsync(role.Id, null, cancelled));

        Assert.Equal(("phoenix_db_reader", Assert.Single(target.Logins)), (incomplete.RoleName, incomplete.Login));
        Assert.DoesNotContain(Assert.Single(target.Passwords), incomplete.ToString());
        Assert.Empty(repository.Leases);
        Assert.False(Assert.Single(target.RevokeTokens).CanBeCanceled, failure);
    }

    [Theory]
    [MemberData(nameof(FailedCleanups))]
    public async Task A_failed_create_login_whose_cleanup_fails_is_reported_unconfirmed_though_no_login_was_made(
        string failure, Exception cleanupError)
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var target = new FakeDynamicTarget
        {
            StatementsThatRun = 0,
            MintFailure = new InvalidOperationException("Cannot create the login."),
            RevokeFailure = cleanupError,
        };

        var incomplete = await Assert.ThrowsAsync<MintIncompleteException>(
            () => Build(repository, target).IssueAsync(role.Id, null, CancellationToken.None));

        Assert.Equal("phoenix_db_reader", incomplete.RoleName);
        Assert.Empty(target.Logins);
        Assert.Empty(target.Users);
        Assert.DoesNotContain(Assert.Single(target.Passwords), incomplete.ToString());
        Assert.Empty(repository.Leases);
        Assert.False(Assert.Single(target.RevokeTokens).CanBeCanceled, failure);
    }

    [Fact]
    public async Task A_credential_that_cannot_be_recorded_is_removed_and_the_audit_failure_surfaced()
    {
        // The dangerous case: minted for real, but the lease (and its audit row) failed to persist.
        // Left alone that is a live credential nothing will ever reap. It must be undone.
        var role = Role();
        var target = new FakeDynamicTarget();
        var repository = new FakeLeaseRepository(role) { AddFails = true };
        using var request = new CancellationTokenSource();

        await Assert.ThrowsAsync<AuditWriteFailedException>(
            () => Build(repository, target).IssueAsync(role.Id, null, request.Token));

        Assert.Empty(target.Logins);
        Assert.False(Assert.Single(target.RevokeTokens).CanBeCanceled);
    }

    [Fact]
    public async Task An_unrecorded_credential_whose_removal_fails_names_the_login_that_may_remain()
    {
        var role = Role();
        var target = new FakeDynamicTarget { RevokeFailure = new InvalidOperationException("Cannot drop the login.") };
        var repository = new FakeLeaseRepository(role) { AddFails = true };

        var unrecorded = await Assert.ThrowsAsync<MintUnrecordedException>(
            () => Build(repository, target).IssueAsync(role.Id, null, CancellationToken.None));

        Assert.Equal("phoenix_db_reader", unrecorded.RoleName);
        Assert.Equal(Assert.Single(target.Logins), unrecorded.Login);
        Assert.DoesNotContain(Assert.Single(target.Passwords), unrecorded.ToString());
    }

    [Fact]
    public async Task A_refused_access_is_recorded_as_a_denial_on_the_role()
    {
        var role = Role();
        var audit = new RecordingAuditSink();
        var service = Build(new FakeLeaseRepository(role), new FakeDynamicTarget(), audit: audit);
        var offered = await service.FindRoleAsync(role.Id, CancellationToken.None);

        await service.RecordAccessDeniedAsync(offered!, ["Clearance too low.", "Outside your project."], CancellationToken.None);

        var denial = Assert.Single(audit.Entries);
        Assert.Equal(AuditAction.SecretAccessDenied, denial.Action);
        Assert.Equal("DynamicSecretRole", denial.ResourceType);
        Assert.Equal(role.Id, denial.ResourceId);
        Assert.Equal("phoenix_db_reader", denial.ResourceName);
        Assert.Equal("Clearance too low. Outside your project.", denial.Details);
        Assert.False(denial.IsCritical);
    }

    [Fact]
    public async Task Revoking_destroys_the_credential_and_closes_the_lease()
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var target = new FakeDynamicTarget();
        var service = Build(repository, target);

        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None);

        Assert.Equal(LeaseStatus.Revoked, await service.RevokeAsync(issued.LeaseId, Owner, isAdmin: false, CancellationToken.None));
        Assert.Empty(target.Logins);
        Assert.Equal(LeaseStatus.Revoked, Assert.Single(repository.Leases).Status);
    }

    [Fact]
    public async Task A_stranger_cannot_revoke_someone_elses_lease()
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var target = new FakeDynamicTarget();
        var service = Build(repository, target);

        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None);

        Assert.Null(await service.RevokeAsync(issued.LeaseId, Stranger, isAdmin: false, CancellationToken.None));
        Assert.Empty(target.RevokeTokens);
        Assert.Equal(LeaseStatus.Active, Assert.Single(repository.Leases).Status);
    }

    [Fact]
    public async Task An_admin_can_revoke_anyones_lease()
    {
        var role = Role();
        var service = Build(new FakeLeaseRepository(role), new FakeDynamicTarget());

        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None);

        Assert.Equal(LeaseStatus.Revoked, await service.RevokeAsync(issued.LeaseId, Stranger, isAdmin: true, CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_lease_is_indistinguishable_from_a_forbidden_one()
    {
        var service = Build(new FakeLeaseRepository(Role()), new FakeDynamicTarget());

        Assert.Null(await service.RevokeAsync(Guid.NewGuid(), Owner, isAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task A_lease_cannot_be_revoked_twice()
    {
        var role = Role();
        var target = new FakeDynamicTarget();
        var service = Build(new FakeLeaseRepository(role), target);

        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None);
        Assert.Equal(LeaseStatus.Revoked, await service.RevokeAsync(issued.LeaseId, Owner, isAdmin: false, CancellationToken.None));
        Assert.Null(await service.RevokeAsync(issued.LeaseId, Owner, isAdmin: false, CancellationToken.None));

        Assert.Single(target.RevokeTokens);
    }

    [Fact]
    public async Task A_backend_that_refuses_to_revoke_is_reported_with_a_fixed_note_rather_than_its_error()
    {
        // The credential may still be live. Closing the lease as "Revoked" would be a lie the audit
        // trail then repeats, and the backend's text can quote a statement, so the note is fixed.
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var target = new FakeDynamicTarget();
        var service = Build(repository, target);
        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None);
        target.RevokeFailure = new InvalidOperationException("Could not drop login 'x' because it is still connected.");

        Assert.Equal(LeaseStatus.RevocationFailed, await service.RevokeAsync(issued.LeaseId, Owner, isAdmin: false, CancellationToken.None));

        var lease = Assert.Single(repository.Leases);
        Assert.Equal(LeaseStatus.RevocationFailed, lease.Status);
        Assert.Equal("The backend refused to destroy the credential; it may still work.", lease.RevocationError);
    }

    [Fact]
    public async Task A_lease_whose_role_is_gone_is_reported_with_its_own_fixed_note()
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var service = Build(repository, new FakeDynamicTarget());
        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None);
        repository.RoleRemoved = true;

        Assert.Equal(LeaseStatus.RevocationFailed, await service.RevokeAsync(issued.LeaseId, Owner, isAdmin: false, CancellationToken.None));

        Assert.Equal(
            "The role that minted this credential no longer exists, so it could not be destroyed automatically.",
            Assert.Single(repository.Leases).RevocationError);
    }

    [Fact]
    public async Task A_destroyed_credential_whose_close_cannot_be_recorded_is_reported_as_destroyed_and_stays_active()
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var target = new FakeDynamicTarget();
        var service = Build(repository, target);
        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None);
        repository.UpdatesToFail = 1;

        await Assert.ThrowsAsync<RevokedUnrecordedException>(
            () => service.RevokeAsync(issued.LeaseId, Owner, isAdmin: false, CancellationToken.None));

        Assert.Empty(target.Logins);
        var lease = Assert.Single(repository.Leases);
        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.Null(lease.RevocationError);
    }

    [Fact]
    public async Task Reaping_closes_only_leases_whose_time_is_up()
    {
        var role = Role(defaultTtl: 15, maxTtl: 60);
        var repository = new FakeLeaseRepository(role);
        var target = new FakeDynamicTarget();
        var service = Build(repository, target);

        var live = await service.IssueAsync(role.Id, 60, CancellationToken.None);
        var doomed = await service.IssueAsync(role.Id, 1, CancellationToken.None);

        // Force the second lease past its deadline without waiting a minute for it.
        repository.Leases.Single(l => l.Id == doomed.LeaseId).ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);

        Assert.Equal(1, await service.ReapDueLeasesAsync(CancellationToken.None));
        Assert.Equal(live.Identity, Assert.Single(target.Logins));
        Assert.Equal(LeaseStatus.Active, repository.Leases.Single(l => l.Id == live.LeaseId).Status);
        Assert.Equal(LeaseStatus.Expired, repository.Leases.Single(l => l.Id == doomed.LeaseId).Status);
    }

    [Fact]
    public async Task A_sweep_whose_close_cannot_be_recorded_leaves_the_lease_active_for_the_next_sweep()
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var service = Build(repository, new FakeDynamicTarget());
        await service.IssueAsync(role.Id, null, CancellationToken.None);
        var lease = Assert.Single(repository.Leases);
        lease.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        repository.UpdatesToFail = 1;

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => service.ReapDueLeasesAsync(CancellationToken.None));
        Assert.Equal(LeaseStatus.Active, lease.Status);

        Assert.Equal(1, await service.ReapDueLeasesAsync(CancellationToken.None));
        Assert.Equal(LeaseStatus.Expired, lease.Status);
    }

    [Fact]
    public async Task A_sweep_whose_retry_fails_closes_the_lease_as_revocation_failed_with_its_fixed_note()
    {
        var role = Role();
        var repository = new FakeLeaseRepository(role);
        var target = new FakeDynamicTarget();
        var service = Build(repository, target);
        await service.IssueAsync(role.Id, null, CancellationToken.None);
        var lease = Assert.Single(repository.Leases);
        lease.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        target.RevokeFailure = new InvalidOperationException("Cannot drop the login, because it does not exist.");

        await service.ReapDueLeasesAsync(CancellationToken.None);

        Assert.Equal(LeaseStatus.RevocationFailed, lease.Status);
        Assert.Equal("The backend refused to destroy the credential; it may still work.", lease.RevocationError);
    }

    [Fact]
    public async Task Leases_are_listed_per_user_unless_you_are_an_admin()
    {
        var role = Role();
        var service = Build(new FakeLeaseRepository(role), new FakeDynamicTarget());

        await service.IssueAsync(role.Id, null, CancellationToken.None);

        Assert.Single(await service.ListLeasesAsync(Owner, includeEveryone: false, CancellationToken.None));
        Assert.Empty(await service.ListLeasesAsync(Stranger, includeEveryone: false, CancellationToken.None));
        Assert.Single(await service.ListLeasesAsync(Stranger, includeEveryone: true, CancellationToken.None));
    }

    [Fact]
    public async Task Issue_is_refused_when_dynamic_secrets_are_not_licensed()
    {
        var role = Role();
        var target = new FakeDynamicTarget();
        var usage = new RecordingPremiumFeatureUsage();
        usage.Denied.Add(LicenseFeatures.DynamicSecrets);

        await Assert.ThrowsAsync<PremiumFeatureNotLicensedException>(
            () => Build(new FakeLeaseRepository(role), target, usage).IssueAsync(role.Id, null, CancellationToken.None));

        Assert.Empty(target.Passwords); // refused before the backend was touched — nothing leased
    }

    [Fact]
    public async Task Revoke_still_succeeds_when_dynamic_secrets_are_no_longer_licensed()
    {
        // No-lockout invariant: an existing lease stays revocable after the licence lapses — only
        // issuing is gated. RevokeAsync has no gate; this pins that it stays that way.
        var role = Role();
        var usage = new RecordingPremiumFeatureUsage();
        var service = Build(new FakeLeaseRepository(role), new FakeDynamicTarget(), usage);
        var issued = await service.IssueAsync(role.Id, null, CancellationToken.None); // allowed

        usage.Denied.Add(LicenseFeatures.DynamicSecrets); // licence lapses

        Assert.Equal(LeaseStatus.Revoked, await service.RevokeAsync(issued.LeaseId, Owner, isAdmin: false, CancellationToken.None));
    }
}
