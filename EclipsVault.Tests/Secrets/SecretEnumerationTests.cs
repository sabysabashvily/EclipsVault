using System.Text;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Tests.Fakes;
using EclipsVault.Tests.TestDoubles;
using Xunit;

namespace EclipsVault.Tests.Secrets;

/// <summary>
/// A decoy must never reach a list.
///
/// A listed decoy is bait that is, by design, indistinguishable from a real secret, and opening it
/// revokes the opener's session and blocks their network range. Listed, it would catch the vault's
/// own staff, the people it is not built for.
///
/// Unlisted, the only way to reach a decoy is with an id obtained out of band — a database dump, a
/// backup, a stolen envelope — which is the reader it exists to catch. That is the difference
/// between a trap and a hazard, so it is pinned here rather than left to the view.
/// </summary>
public class SecretEnumerationTests
{
    private sealed class ListRepository : ISecretRepository
    {
        private readonly IReadOnlyList<Secret> _secrets;
        public ListRepository(params Secret[] secrets) => _secrets = secrets;

        public Task<IReadOnlyList<Secret>> ListActiveAsync(DateTimeOffset asOfUtc, CancellationToken ct)
            => Task.FromResult(_secrets);

        public Task<Secret?> FindAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_secrets.FirstOrDefault(s => s.Id == id));

        public Task RotateAsync(Secret secret, SecretVersion archivedVersion, AuditAction recordedAs, CancellationToken ct) => Task.CompletedTask;
        public Task<int> CountVersionsAsync(Guid secretId, CancellationToken ct) => Task.FromResult(0);
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
            => Task.FromResult<IReadOnlyList<SecretVersion>>([]);
        public Task<SecretVersion?> FindVersionAsync(Guid secretId, Guid versionId, CancellationToken ct)
            => Task.FromResult<SecretVersion?>(null);
    }

    private sealed class NullCache : ISecretCache
    {
        public Task<EncryptedSecretEnvelope?> GetAsync(Guid secretId, CancellationToken ct = default)
            => Task.FromResult<EncryptedSecretEnvelope?>(null);
        public Task SetAsync(EncryptedSecretEnvelope envelope, CancellationToken ct = default) => Task.CompletedTask;
        public Task EvictAsync(Guid secretId, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Listing must never trip the trap — reaching this is the bug.</summary>
    private sealed class UnusedIntrusionResponse : IIntrusionResponseService
    {
        public Task TriggerHoneyTokenAsync(Guid secretId, string secretName, CancellationToken ct)
            => throw new InvalidOperationException("listing must never trip a honey token");
    }

    private sealed class RecordingIntrusionResponse : IIntrusionResponseService
    {
        public List<string> Tripped { get; } = [];

        public Task TriggerHoneyTokenAsync(Guid secretId, string secretName, CancellationToken ct)
        {
            Tripped.Add(secretName);
            return Task.CompletedTask;
        }
    }

    private sealed class NullAuditSink : IAuditSink
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubActor : IAuditContext
    {
        public Guid? UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Username => "dev-user";
        public string? SourceIp => "::1";
    }

    private static Secret Sample(string name, bool isHoneyToken = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ProjectKey = "GLOBAL",
        Environment = SecretEnvironment.Production,
        Sensitivity = SensitivityLevel.TopSecret,
        Ciphertext = Encoding.UTF8.GetBytes("value"),
        WrappedDek = [],
        KekId = "test-kek",
        Algorithm = "FAKE",
        IsHoneyToken = isHoneyToken,
        CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private static SecretService Build(ISecretRepository repository, IIntrusionResponseService? intrusion = null)
        => new(repository, new FakeCryptoEngine(), new NullCache(), intrusion ?? new UnusedIntrusionResponse(),
               new NullAuditSink(), new StubActor(), [], TimeProvider.System, new RecordingPremiumFeatureUsage());

    [Fact]
    public async Task Listing_never_returns_a_decoy()
    {
        var repository = new ListRepository(
            Sample("Phoenix_Staging_Api_Key"),
            Sample("Production_AWS_Root_Key", isHoneyToken: true),
            Sample("Global_SQL_SA_Password", isHoneyToken: true));

        var listed = await Build(repository).ListAsync(CancellationToken.None);

        var only = Assert.Single(listed);
        Assert.Equal("Phoenix_Staging_Api_Key", only.Name);
    }

    [Fact]
    public async Task Searching_never_finds_a_decoy()
    {
        var repository = new ListRepository(
            Sample("Production_Api_Key"),
            Sample("Production_AWS_Root_Key", isHoneyToken: true));

        var found = SecretSearch.Find(await Build(repository).ListAsync(CancellationToken.None), "production");

        Assert.Equal("Production_Api_Key", Assert.Single(found).Name);
    }

    [Fact]
    public async Task A_vault_of_nothing_but_decoys_lists_as_empty_rather_than_as_bait()
    {
        var repository = new ListRepository(
            Sample("Production_AWS_Root_Key", isHoneyToken: true),
            Sample("Global_SQL_SA_Password", isHoneyToken: true));

        Assert.Empty(await Build(repository).ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_decoy_is_still_reachable_by_id_and_still_trips_the_trap()
    {
        // Removing decoys from the list must not disarm them: an id obtained out of band is
        // exactly the reader they exist to catch.
        var decoy = Sample("Production_AWS_Root_Key", isHoneyToken: true);
        var intrusion = new RecordingIntrusionResponse();
        var service = Build(new ListRepository(decoy), intrusion);

        await Assert.ThrowsAsync<HoneyTokenTrippedException>(
            () => service.GetDetailsAsync(decoy.Id, CancellationToken.None));

        Assert.Equal("Production_AWS_Root_Key", Assert.Single(intrusion.Tripped));
    }

    [Fact]
    public void A_list_row_cannot_carry_the_decoy_flag_at_all()
    {
        // A list row has no flag for callers to filter on, so there is no shape in which a decoy can
        // be handed out and hidden by whoever renders it.
        Assert.DoesNotContain(
            typeof(SecretSummaryDto).GetProperties(),
            p => p.Name.Contains("Honey", StringComparison.OrdinalIgnoreCase)
              || p.Name.Contains("Decoy", StringComparison.OrdinalIgnoreCase));
    }
}
