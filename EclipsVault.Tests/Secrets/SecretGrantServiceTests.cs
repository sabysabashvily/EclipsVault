using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Xunit;

namespace EclipsVault.Tests.Secrets;

/// <summary>
/// Sharing refuses a recipient a share could not help and replaces an expired share. The
/// self-scoped revoke on the "Shared by me" page must never let a caller revoke a share they
/// didn't issue. These tests pin that authorization boundary (the IDOR guard), its indistinguishable
/// failure, and that the issuer is an account, never a username a new account could be given.
/// </summary>
public class SecretGrantServiceTests
{
    private static readonly Guid AliceId = Guid.Parse("a11ce000-0000-0000-0000-000000000001");

    private static readonly User Alice = new() { Id = AliceId, Username = "alice", Clearance = ClearanceLevel.TopSecret };

    private static readonly User Carol = new()
    {
        Id = Guid.Parse("ca201000-0000-0000-0000-000000000001"),
        Username = "carol",
        Clearance = ClearanceLevel.Standard,
    };

    private sealed class FakeGrants : ISecretGrantRepository
    {
        private readonly SecretGrant? _grant;
        public int RemoveCalls { get; private set; }
        public SecretGrant? Added { get; private set; }
        public SecretGrant? Replaced { get; private set; }

        /// <summary>The recipient's existing share, active or expired.</summary>
        public SecretGrant? ForRecipient { get; init; }

        /// <summary>False when another share for the recipient was saved first.</summary>
        public bool SaveSucceeds { get; init; } = true;

        public FakeGrants(SecretGrant? grant) => _grant = grant;

        public Task<SecretGrant?> FindAsync(Guid grantId, CancellationToken ct) => Task.FromResult(_grant);
        public Task<bool> RemoveAsync(Guid grantId, CancellationToken ct)
        {
            RemoveCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> AddAsync(SecretGrant grant, CancellationToken ct)
        {
            Added = grant;
            return Task.FromResult(SaveSucceeds);
        }

        public Task<bool> ReplaceExpiredAsync(SecretGrant expired, SecretGrant grant, CancellationToken ct)
        {
            Replaced = expired;
            Added = grant;
            return Task.FromResult(SaveSucceeds);
        }

        public Task<SecretGrant?> FindForRecipientAsync(Guid userId, Guid secretId, CancellationToken ct) => Task.FromResult(ForRecipient);

        public Task<bool> HasActiveGrantAsync(Guid userId, Guid secretId, DateTimeOffset asOfUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SecretGrant>> ListForSecretAsync(Guid secretId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SharedSecretDto>> ListSharedWithUserAsync(Guid userId, DateTimeOffset asOfUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<OutgoingShareDto>> ListIssuedByAsync(Guid issuerUserId, DateTimeOffset asOfUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeSecrets(Secret secret) : ISecretRepository
    {
        public Task<Secret?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<Secret?>(secret);
        public Task<IReadOnlyList<Secret>> ListActiveAsync(DateTimeOffset asOfUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Secret>> ListExpiredAsync(DateTimeOffset asOfUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Secret>> ListExpiringAsync(DateTimeOffset asOfUtc, DateTimeOffset horizonUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkExpiryNoticeSentAsync(Secret secret, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(Secret secret, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateAsync(Secret secret, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Secret secret, CancellationToken ct) => throw new NotSupportedException();
        public Task RotateAsync(Secret secret, SecretVersion archivedVersion, CancellationToken ct) => throw new NotSupportedException();
        public Task ShredAsync(Secret secret, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SecretVersion>> ListVersionsAsync(Guid secretId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SecretVersion?> FindVersionAsync(Guid secretId, Guid versionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountVersionsAsync(Guid secretId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeUsers(User recipient) : IUserRepository
    {
        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct) => Task.FromResult<User?>(recipient);

        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ReloadAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarAsync(User user, byte[] png, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarRemovalAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryAcceptTotpStepAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryCompleteTotpEnrollmentAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RecordFailedAttemptAsync(User user, LockoutPolicy lockout, DateTimeOffset nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearFailedAttemptsAsync(User user, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubActor : IAuditContext
    {
        public Guid? UserId => AliceId;
        public string? Username => "alice";
        public string? SourceIp => "::1";
    }

    private static SecretGrant Grant(Guid? issuerUserId, string grantedBy = "alice") => new()
    {
        Id = Guid.NewGuid(),
        SecretId = Guid.NewGuid(),
        GranteeUserId = Guid.NewGuid(),
        GranteeUsername = "carol",
        IssuerUserId = issuerUserId,
        GrantedBy = grantedBy,
        CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private static Secret SecretOf(SensitivityLevel sensitivity) => new() { Id = Guid.NewGuid(), Name = "db-password", Sensitivity = sensitivity };

    private static SecretGrant ShareToCarol(DateTimeOffset? expiresAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        SecretId = Guid.NewGuid(),
        GranteeUserId = Carol.Id,
        GranteeUsername = "carol",
        IssuerUserId = AliceId,
        GrantedBy = "alice",
        CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
        ExpiresAtUtc = expiresAtUtc
    };

    private static (SecretGrantService Service, FakeGrants Grants) NewService(SecretGrant? grant)
    {
        var grants = new FakeGrants(grant);
        return (SharingService(grants, Carol, SecretOf(SensitivityLevel.Internal)), grants);
    }

    private static SecretGrantService SharingService(FakeGrants grants, User recipient, Secret secret)
        => new(grants, new FakeSecrets(secret), new FakeUsers(recipient), new StubActor(), TimeProvider.System);

    [Fact]
    public async Task A_new_share_records_the_actor_as_its_issuer()
    {
        var (service, grants) = NewService(grant: null);

        await service.GrantAsync(Guid.NewGuid(), "carol", ttlDays: null, CancellationToken.None);

        var added = Assert.IsType<SecretGrant>(grants.Added);
        Assert.Equal(AliceId, added.IssuerUserId);
        Assert.Equal("alice", added.GrantedBy);
    }

    [Fact]
    public async Task A_recipient_whose_clearance_is_below_the_sensitivity_is_refused_and_nothing_is_saved()
    {
        var grants = new FakeGrants(grant: null);
        var service = SharingService(grants, Carol, SecretOf(SensitivityLevel.Confidential));

        var refused = await Assert.ThrowsAsync<SharingException>(
            () => service.GrantAsync(Guid.NewGuid(), "carol", ttlDays: null, CancellationToken.None));

        Assert.Equal("That account's clearance is below this secret's sensitivity, so a share would not let them open it.", refused.Message);
        Assert.Null(grants.Added);
    }

    [Fact]
    public async Task A_recipient_whose_clearance_meets_the_sensitivity_is_given_the_share()
    {
        var grants = new FakeGrants(grant: null);
        var elevated = new User { Id = Guid.NewGuid(), Username = "erin", Clearance = ClearanceLevel.Elevated };
        var secret = SecretOf(SensitivityLevel.Confidential);
        var service = SharingService(grants, elevated, secret);

        await service.GrantAsync(secret.Id, "erin", ttlDays: null, CancellationToken.None);

        var added = Assert.IsType<SecretGrant>(grants.Added);
        Assert.Equal(elevated.Id, added.GranteeUserId);
        Assert.Equal(secret.Id, added.SecretId);
    }

    [Fact]
    public async Task A_new_share_replaces_the_recipients_expired_share()
    {
        var expired = ShareToCarol(expiresAtUtc: DateTimeOffset.UtcNow.AddHours(-1));
        var grants = new FakeGrants(grant: null) { ForRecipient = expired };
        var service = SharingService(grants, Carol, SecretOf(SensitivityLevel.Internal));

        await service.GrantAsync(expired.SecretId, "carol", ttlDays: 7, CancellationToken.None);

        Assert.Same(expired, grants.Replaced);
        var added = Assert.IsType<SecretGrant>(grants.Added);
        Assert.NotEqual(expired.Id, added.Id);
        Assert.Equal(Carol.Id, added.GranteeUserId);
        Assert.Equal(TimeSpan.FromDays(7), added.ExpiresAtUtc - added.CreatedAtUtc);
    }

    [Fact]
    public async Task A_recipient_with_an_active_share_is_refused_and_nothing_is_saved()
    {
        var active = ShareToCarol(expiresAtUtc: DateTimeOffset.UtcNow.AddDays(1));
        var grants = new FakeGrants(grant: null) { ForRecipient = active };
        var service = SharingService(grants, Carol, SecretOf(SensitivityLevel.Internal));

        var refused = await Assert.ThrowsAsync<SharingException>(
            () => service.GrantAsync(active.SecretId, "carol", ttlDays: null, CancellationToken.None));

        Assert.Equal("'carol' already has a grant on this secret.", refused.Message);
        Assert.Null(grants.Added);
        Assert.Null(grants.Replaced);
    }

    [Fact]
    public async Task Sharing_with_yourself_is_refused_and_nothing_is_saved()
    {
        var grants = new FakeGrants(grant: null);
        var service = SharingService(grants, Alice, SecretOf(SensitivityLevel.Internal));

        var refused = await Assert.ThrowsAsync<SharingException>(
            () => service.GrantAsync(Guid.NewGuid(), "alice", ttlDays: null, CancellationToken.None));

        Assert.Equal("You already have access — you cannot share a secret with yourself.", refused.Message);
        Assert.Null(grants.Added);
    }

    [Fact]
    public async Task A_share_saved_first_by_another_request_answers_that_they_already_have_one()
    {
        var grants = new FakeGrants(grant: null) { SaveSucceeds = false };
        var service = SharingService(grants, Carol, SecretOf(SensitivityLevel.Internal));

        var refused = await Assert.ThrowsAsync<SharingException>(
            () => service.GrantAsync(Guid.NewGuid(), "carol", ttlDays: null, CancellationToken.None));

        Assert.Equal("They already have a share on this secret.", refused.Message);
    }

    [Fact]
    public async Task Revokes_when_the_caller_issued_the_share()
    {
        var (service, grants) = NewService(Grant(issuerUserId: AliceId));

        Assert.True(await service.RevokeIssuedAsync(Guid.NewGuid(), AliceId, CancellationToken.None));
        Assert.Equal(1, grants.RemoveCalls);
    }

    [Fact]
    public async Task Refuses_to_revoke_a_share_issued_by_another_account()
    {
        var (service, grants) = NewService(Grant(issuerUserId: Guid.NewGuid(), grantedBy: "bob"));

        // alice must not be able to revoke a share bob issued, even with its real id.
        Assert.False(await service.RevokeIssuedAsync(Guid.NewGuid(), AliceId, CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }

    /// <summary>
    /// A deleted account's username can be given to a new account. The share still names the old
    /// account as its issuer, so the new one must not be able to revoke it.
    /// </summary>
    [Fact]
    public async Task Refuses_to_revoke_a_share_issued_by_a_deleted_account_with_the_callers_username()
    {
        var (service, grants) = NewService(Grant(issuerUserId: Guid.NewGuid(), grantedBy: "alice"));

        Assert.False(await service.RevokeIssuedAsync(Guid.NewGuid(), AliceId, CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }

    [Fact]
    public async Task Refuses_to_revoke_a_share_with_no_known_issuer()
    {
        var (service, grants) = NewService(Grant(issuerUserId: null, grantedBy: "alice"));

        Assert.False(await service.RevokeIssuedAsync(Guid.NewGuid(), AliceId, CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }

    [Fact]
    public async Task Returns_false_for_a_missing_grant_without_removing_anything()
    {
        var (service, grants) = NewService(grant: null);

        Assert.False(await service.RevokeIssuedAsync(Guid.NewGuid(), AliceId, CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }

    [Fact]
    public async Task Revokes_a_grant_that_belongs_to_the_secret()
    {
        var grant = Grant(issuerUserId: AliceId);
        var (service, grants) = NewService(grant);

        Assert.True(await service.RevokeAsync(grant.Id, grant.SecretId, CancellationToken.None));
        Assert.Equal(1, grants.RemoveCalls);
    }

    /// <summary>
    /// The owner-side revoke is reached from a secret's sharing panel, where the caller has been
    /// authorized against <em>that secret</em> and nothing else. Accepting a grant id belonging to a
    /// different secret would turn "may share one secret" into "may revoke any grant in the vault",
    /// including on secrets in projects the caller cannot see or enumerate.
    /// </summary>
    [Fact]
    public async Task Refuses_a_grant_that_belongs_to_a_different_secret()
    {
        var grant = Grant(issuerUserId: AliceId);
        var (service, grants) = NewService(grant);

        var someOtherSecret = Guid.NewGuid();
        Assert.NotEqual(grant.SecretId, someOtherSecret);

        Assert.False(await service.RevokeAsync(grant.Id, someOtherSecret, CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }

    [Fact]
    public async Task Returns_false_for_a_missing_grant_on_the_owner_side_too()
    {
        var (service, grants) = NewService(grant: null);

        Assert.False(await service.RevokeAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }
}
