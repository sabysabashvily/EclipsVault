using System.Text;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Tests.Fakes;
using EclipsVault.Tests.TestDoubles;
using Xunit;

namespace EclipsVault.Tests.Secrets;

/// <summary>
/// Rotating a managed secret changes a credential that is live on a real backend, so the ordering
/// carries the safety properties: a backend that refuses moves nothing, a new password the vault
/// cannot store is put back upstream rather than left as a password nobody holds, and a restore that
/// also fails is recorded as drift instead of being swallowed.
///
/// The happy path is proven end-to-end against a real SQL Server login; these pin the failure paths,
/// which are the ones an operator never sees coming.
/// </summary>
public class ManagedRotationTests
{
    private const string OriginalPassword = "OldPassw0rdProbe123";
    private const string Principal = "app_probe";
    private static readonly Guid SecretId = Guid.Parse("d64eb6fc-e2df-4c5c-8eb5-d0f7003e654e");

    /// <summary>Records every password the principal was set to, in order.</summary>
    private sealed class FakeBackend : IManagedSecretBackend
    {
        public int FailAfter { get; init; } = int.MaxValue;
        public List<string> Rotations { get; } = [];

        public DynamicSecretBackend Backend => DynamicSecretBackend.SqlServer;

        public Task RotatePrincipalAsync(string principal, string newPassword, CancellationToken ct)
        {
            if (Rotations.Count >= FailAfter)
            {
                throw new InvalidOperationException("backend refused");
            }

            Rotations.Add(newPassword);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepository : ISecretRepository
    {
        private readonly Secret _secret;
        public FakeRepository(Secret secret) => _secret = secret;

        public bool RotateThrows { get; init; }
        public List<SecretVersion> Versions { get; } = [];

        // Note: the real repository also rewinds the entity when a write fails, so the caller never
        // holds a change that did not happen. That contract is the repository's own and is not
        // modelled here — these tests assert what the service does, not what its store does.

        public Task<Secret?> FindAsync(Guid id, CancellationToken ct)
            => Task.FromResult<Secret?>(id == _secret.Id ? _secret : null);

        public Task RotateAsync(Secret secret, SecretVersion archivedVersion, CancellationToken ct)
        {
            if (RotateThrows)
            {
                throw new InvalidOperationException("audit write failed");
            }

            Versions.Add(archivedVersion);
            return Task.CompletedTask;
        }

        public Task<int> CountVersionsAsync(Guid secretId, CancellationToken ct) => Task.FromResult(Versions.Count);

        public Task<IReadOnlyList<Secret>> ListActiveAsync(DateTimeOffset asOfUtc, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Secret>>([_secret]);

        public Task<IReadOnlyList<Secret>> ListExpiredAsync(DateTimeOffset asOfUtc, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Secret>>([]);

        public Task<IReadOnlyList<Secret>> ListExpiringAsync(DateTimeOffset asOfUtc, DateTimeOffset horizonUtc, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Secret>>([]);

        public Task MarkExpiryNoticeSentAsync(Secret secret, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> ExistsByNameAsync(string name, CancellationToken ct) => Task.FromResult(false);

        public Task AddAsync(Secret secret, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(Secret secret, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteAsync(Secret secret, CancellationToken ct) => Task.CompletedTask;

        public Task ShredAsync(Secret secret, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<SecretVersion>> ListVersionsAsync(Guid secretId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SecretVersion>>(Versions);

        public Task<SecretVersion?> FindVersionAsync(Guid secretId, Guid versionId, CancellationToken ct)
            => Task.FromResult(Versions.FirstOrDefault(v => v.Id == versionId));
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

    private sealed class NullCache : ISecretCache
    {
        public Task<EncryptedSecretEnvelope?> GetAsync(Guid secretId, CancellationToken ct = default)
            => Task.FromResult<EncryptedSecretEnvelope?>(null);

        public Task SetAsync(EncryptedSecretEnvelope envelope, CancellationToken ct = default) => Task.CompletedTask;

        public Task EvictAsync(Guid secretId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class UnusedIntrusionResponse : IIntrusionResponseService
    {
        public Task TriggerHoneyTokenAsync(Guid secretId, string secretName, CancellationToken ct)
            => throw new InvalidOperationException("no honey-token is involved in these tests");
    }

    private sealed class StubActor : IAuditContext
    {
        public Guid? UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Username => "dev-user";
        public string? SourceIp => "::1";
    }

    private static Secret ManagedSecret(bool bound = true)
    {
        // Sealed the way the service would seal it: bound to this secret's own id. The fake engine
        // completes synchronously, so resolving the task inline in this sync helper never blocks.
        var sealedSecret = new FakeCryptoEngine()
            .SealAsync(Encoding.UTF8.GetBytes(OriginalPassword), SecretBinding.ForCurrentValue(SecretId), default)
            .GetAwaiter().GetResult();

        return new Secret
        {
            Id = SecretId,
            Name = "app_probe_password",
            ProjectKey = "PHOENIX",
            Environment = SecretEnvironment.Production,
            Sensitivity = SensitivityLevel.Secret,
            Ciphertext = sealedSecret.Ciphertext,
            WrappedDek = [],
            KekId = "test-kek",
            Algorithm = "FAKE",
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-30),
            RotationBackend = bound ? DynamicSecretBackend.SqlServer : null,
            RotationPrincipal = bound ? Principal : null
        };
    }

    private static SecretService Build(FakeRepository repository, RecordingAuditSink audit, params IManagedSecretBackend[] backends)
        => Build(repository, audit, new RecordingPremiumFeatureUsage(), backends);

    private static SecretService Build(FakeRepository repository, RecordingAuditSink audit, RecordingPremiumFeatureUsage usage, params IManagedSecretBackend[] backends)
        => new(repository, new FakeCryptoEngine(), new NullCache(), new UnusedIntrusionResponse(),
               audit, new StubActor(), backends, TimeProvider.System, usage);

    private static string StoredValue(Secret secret)
        => Encoding.UTF8.GetString(FakeCryptoEngine.ValueOf(secret.Ciphertext));

    [Fact]
    public async Task Rotating_sets_a_new_password_upstream_and_stores_that_same_password()
    {
        // The point of the feature: the live credential and the stored copy move together.
        var secret = ManagedSecret();
        var repository = new FakeRepository(secret);
        var backend = new FakeBackend();

        await Build(repository, new RecordingAuditSink(), backend).RotateManagedAsync(SecretId, null, CancellationToken.None);

        var applied = Assert.Single(backend.Rotations);
        Assert.Equal(applied, StoredValue(secret));
        Assert.NotEqual(OriginalPassword, applied);
    }

    [Fact]
    public async Task The_replaced_password_is_archived_so_it_is_not_simply_lost()
    {
        var secret = ManagedSecret();
        var repository = new FakeRepository(secret);

        await Build(repository, new RecordingAuditSink(), new FakeBackend()).RotateManagedAsync(SecretId, null, CancellationToken.None);

        var archived = Assert.Single(repository.Versions);
        Assert.Equal(OriginalPassword, Encoding.UTF8.GetString(FakeCryptoEngine.ValueOf(archived.Ciphertext)));
        Assert.Contains(Principal, archived.ChangeNote);
    }

    [Fact]
    public async Task Rotating_can_also_push_the_expiry_out()
    {
        var secret = ManagedSecret();
        secret.ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1);

        await Build(new FakeRepository(secret), new RecordingAuditSink(), new FakeBackend())
            .RotateManagedAsync(SecretId, 90, CancellationToken.None);

        Assert.InRange(secret.ExpiresAtUtc!.Value, DateTimeOffset.UtcNow.AddDays(89), DateTimeOffset.UtcNow.AddDays(91));
    }

    [Fact]
    public async Task The_rotation_is_recorded_as_an_upstream_rotation()
    {
        var audit = new RecordingAuditSink();

        await Build(new FakeRepository(ManagedSecret()), audit, new FakeBackend())
            .RotateManagedAsync(SecretId, null, CancellationToken.None);

        var entry = Assert.Single(audit.Entries, e => e.Action == AuditAction.SecretUpstreamRotated);
        Assert.Equal(SecretId, entry.ResourceId);
        Assert.False(entry.IsCritical);
    }

    [Fact]
    public async Task An_unmanaged_secret_cannot_be_rotated_upstream()
    {
        // Nothing binds this value to a principal, so there is no credential to change — and
        // guessing at one would be worse than refusing.
        var backend = new FakeBackend();

        await Assert.ThrowsAsync<VaultAdminException>(
            () => Build(new FakeRepository(ManagedSecret(bound: false)), new RecordingAuditSink(), backend)
                .RotateManagedAsync(SecretId, null, CancellationToken.None));

        Assert.Empty(backend.Rotations);
    }

    [Fact]
    public async Task A_secret_bound_to_a_backend_nobody_configured_is_refused()
    {
        var secret = ManagedSecret();
        var repository = new FakeRepository(secret);

        await Assert.ThrowsAsync<VaultAdminException>(
            () => Build(repository, new RecordingAuditSink()).RotateManagedAsync(SecretId, null, CancellationToken.None));

        Assert.Equal(OriginalPassword, StoredValue(secret));
        Assert.Empty(repository.Versions);
    }

    [Fact]
    public async Task A_backend_that_refuses_leaves_the_stored_value_untouched()
    {
        // Upstream goes first precisely so this case is boring: nothing moved, so the stored value
        // is still the truth.
        var secret = ManagedSecret();
        var repository = new FakeRepository(secret);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(repository, new RecordingAuditSink(), new FakeBackend { FailAfter = 0 })
                .RotateManagedAsync(SecretId, null, CancellationToken.None));

        Assert.Equal(OriginalPassword, StoredValue(secret));
        Assert.Empty(repository.Versions);
    }

    [Fact]
    public async Task A_password_that_cannot_be_stored_is_put_back_upstream()
    {
        // The dangerous case: the real credential changed, but the vault could not record it. Left
        // alone, the vault serves a dead password — the exact drift this feature exists to prevent.
        var secret = ManagedSecret();
        var repository = new FakeRepository(secret) { RotateThrows = true };
        var backend = new FakeBackend();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(repository, new RecordingAuditSink(), backend).RotateManagedAsync(SecretId, null, CancellationToken.None));

        Assert.Equal(2, backend.Rotations.Count);
        Assert.NotEqual(OriginalPassword, backend.Rotations[0]);
        Assert.Equal(OriginalPassword, backend.Rotations[^1]);
    }

    [Fact]
    public async Task Putting_the_password_back_successfully_is_not_reported_as_drift()
    {
        var repository = new FakeRepository(ManagedSecret()) { RotateThrows = true };
        var audit = new RecordingAuditSink();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(repository, audit, new FakeBackend()).RotateManagedAsync(SecretId, null, CancellationToken.None));

        Assert.DoesNotContain(audit.Entries, e => e.Action == AuditAction.SecretUpstreamRotationDrifted);
        Assert.DoesNotContain(audit.Entries, e => e.Action == AuditAction.SecretUpstreamRotated);
    }

    [Fact]
    public async Task A_restore_that_also_fails_is_recorded_as_critical_drift()
    {
        // Worst case: changed upstream, not stored, and not put back. The stored value no longer
        // opens the credential and only a human can reconcile that, so it must be impossible to miss.
        var repository = new FakeRepository(ManagedSecret()) { RotateThrows = true };
        var audit = new RecordingAuditSink();
        var backend = new FakeBackend { FailAfter = 1 };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(repository, audit, backend).RotateManagedAsync(SecretId, null, CancellationToken.None));

        var drift = Assert.Single(audit.Entries, e => e.Action == AuditAction.SecretUpstreamRotationDrifted);
        Assert.True(drift.IsCritical);
        Assert.Equal(SecretId, drift.ResourceId);
        Assert.Contains(Principal, drift.Details);
    }

    [Fact]
    public async Task Drift_is_recorded_without_leaking_either_password()
    {
        // The audit trail is the widest-read surface in the vault. A drift row names the principal
        // so a human can go fix it by hand — it must not hand them the credential to do it with.
        var repository = new FakeRepository(ManagedSecret()) { RotateThrows = true };
        var audit = new RecordingAuditSink();
        var backend = new FakeBackend { FailAfter = 1 };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(repository, audit, backend).RotateManagedAsync(SecretId, null, CancellationToken.None));

        var applied = Assert.Single(backend.Rotations);
        var drift = Assert.Single(audit.Entries, e => e.Action == AuditAction.SecretUpstreamRotationDrifted);
        Assert.DoesNotContain(applied, drift.Details);
        Assert.DoesNotContain(OriginalPassword, drift.Details);
    }

    [Fact]
    public async Task Rotating_a_managed_secret_records_premium_usage()
    {
        var secret = ManagedSecret();
        var repository = new FakeRepository(secret);
        var usage = new RecordingPremiumFeatureUsage();

        await Build(repository, new RecordingAuditSink(), usage, new FakeBackend())
            .RotateManagedAsync(SecretId, null, CancellationToken.None);

        Assert.Equal(LicenseFeatures.ManagedRotation, Assert.Single(usage.Recorded));
    }

    [Fact]
    public async Task Managed_rotation_is_refused_when_not_licensed()
    {
        var backend = new FakeBackend();
        var usage = new RecordingPremiumFeatureUsage();
        usage.Denied.Add(LicenseFeatures.ManagedRotation);

        await Assert.ThrowsAsync<PremiumFeatureNotLicensedException>(
            () => Build(new FakeRepository(ManagedSecret()), new RecordingAuditSink(), usage, backend)
                .RotateManagedAsync(SecretId, null, CancellationToken.None));

        Assert.Empty(backend.Rotations); // backend untouched; the stored value stays the truth
    }
}
