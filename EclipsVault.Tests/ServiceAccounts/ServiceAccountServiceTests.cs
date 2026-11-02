using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.ServiceAccounts;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Xunit;

namespace EclipsVault.Tests.ServiceAccounts;

/// <summary>
/// An administrator's change to a service account or its keys reaches the database once, with its one
/// audit event, and the change reads the account only once it holds the account's lock. Each test reads
/// what reached the vault's ports, in order, as one line per step.
/// </summary>
public class ServiceAccountServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AccountId = Guid.Parse("5a000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherAccountId = Guid.Parse("5a000000-0000-0000-0000-000000000002");
    private static readonly Guid KeyId = Guid.Parse("4e000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Creating_a_service_account_commits_it_with_one_ServiceAccountCreated_event()
    {
        var vault = new Vault();

        await vault.Service.CreateAsync(new CreateServiceAccountRequest(" payments-ci ", ClearanceLevel.Secret, " payments "), default);

        Assert.Equal(["lock an unknown service account", "stage new payments-ci", "commit ServiceAccountCreated"], vault.Steps);
        Assert.Equal("Created (clearance Secret, project PAYMENTS)", Assert.Single(vault.Changes.Events).Details);
    }

    [Fact]
    public async Task A_create_that_loses_the_name_race_answers_the_checks_conflict()
    {
        var vault = new Vault();
        vault.Changes.Failures.Enqueue(new ServiceAccountNameTakenException());

        var conflict = await Assert.ThrowsAsync<VaultAdminException>(() =>
            vault.Service.CreateAsync(new CreateServiceAccountRequest("payments-ci", ClearanceLevel.Secret, "PAYMENTS"), default));

        Assert.Equal("A service account named 'payments-ci' already exists.", conflict.Message);
        Assert.Equal(["lock an unknown service account", "stage new payments-ci"], vault.Steps);
    }

    [Theory]
    [InlineData("ci", 3, "PAYMENTS", "Name", "Service account names are 3 to 64 characters: letters, digits, underscores, hyphens or dots.")]
    [InlineData("payments ci", 3, "PAYMENTS", "Name", "Service account names are 3 to 64 characters: letters, digits, underscores, hyphens or dots.")]
    [InlineData("payments-ci-0123456789-0123456789-0123456789-0123456789-012345678", 3, "PAYMENTS", "Name", "Service account names are 3 to 64 characters: letters, digits, underscores, hyphens or dots.")]
    [InlineData("payments-ci", 0, "PAYMENTS", "Clearance", "Choose one of the vault's clearance levels.")]
    [InlineData("payments-ci", 7, "PAYMENTS", "Clearance", "Choose one of the vault's clearance levels.")]
    [InlineData("payments-ci", 3, "   ", "ProjectKey", "Project keys are 1 to 64 characters, not counting spaces at either end.")]
    [InlineData("payments-ci", 3, "PAYMENTS-0123456789-0123456789-0123456789-0123456789-0123456789-0", "ProjectKey", "Project keys are 1 to 64 characters, not counting spaces at either end.")]
    public async Task A_new_service_account_outside_its_rules_is_refused_naming_the_field(
        string name, int clearance, string project, string field, string message)
    {
        var vault = new Vault();

        var refusal = await Assert.ThrowsAsync<AccountInputException>(() =>
            vault.Service.CreateAsync(new CreateServiceAccountRequest(name, (ClearanceLevel)clearance, project), default));

        Assert.Equal((field, message), (refusal.Field, refusal.Message));
        Assert.Empty(vault.Steps);
    }

    [Fact]
    public async Task Disabling_a_service_account_commits_one_ServiceAccountDisabled_event()
    {
        var account = Account(ClearanceLevel.Secret);
        var vault = new Vault(account);

        Assert.True(await vault.Service.SetEnabledAsync(AccountId, enabled: false, default));

        Assert.Equal(["lock payments-ci", "commit ServiceAccountDisabled"], vault.Steps);
        Assert.True(account.IsDisabled);
    }

    [Fact]
    public async Task Enabling_a_service_account_commits_one_ServiceAccountEnabled_event()
    {
        var account = Account(ClearanceLevel.Secret);
        account.IsDisabled = true;
        var vault = new Vault(account);

        Assert.True(await vault.Service.SetEnabledAsync(AccountId, enabled: true, default));

        Assert.Equal(["lock payments-ci", "commit ServiceAccountEnabled"], vault.Steps);
        Assert.False(account.IsDisabled);
    }

    [Fact]
    public async Task Disabling_a_disabled_service_account_records_nothing()
    {
        var account = Account(ClearanceLevel.Secret);
        account.IsDisabled = true;
        var vault = new Vault(account);

        Assert.True(await vault.Service.SetEnabledAsync(AccountId, enabled: false, default));

        Assert.Equal(["lock payments-ci"], vault.Steps);
    }

    [Fact]
    public async Task Enabling_an_enabled_service_account_records_nothing()
    {
        var vault = new Vault(Account(ClearanceLevel.Secret));

        Assert.True(await vault.Service.SetEnabledAsync(AccountId, enabled: true, default));

        Assert.Equal(["lock payments-ci"], vault.Steps);
    }

    [Fact]
    public async Task Deleting_a_service_account_commits_its_removal_with_one_ServiceAccountDeleted_event()
    {
        var vault = new Vault(Account(ClearanceLevel.Secret));

        Assert.True(await vault.Service.DeleteAsync(AccountId, default));

        Assert.Equal(["lock payments-ci", "stage removal payments-ci", "commit ServiceAccountDeleted"], vault.Steps);
    }

    [Fact]
    public async Task Changing_a_service_account_that_does_not_exist_records_nothing()
    {
        var vault = new Vault();

        Assert.False(await vault.Service.SetEnabledAsync(AccountId, enabled: false, default));
        Assert.False(await vault.Service.DeleteAsync(AccountId, default));
        Assert.Null(await vault.Service.IssueKeyAsync(AccountId, new IssueApiKeyRequest(0, null, null, false, null), default));
        Assert.False(await vault.Service.RevokeKeyAsync(AccountId, KeyId, default));

        Assert.Equal(["lock an unknown service account", "lock an unknown service account", "lock an unknown service account", "lock an unknown service account"], vault.Steps);
    }

    [Fact]
    public async Task Issuing_a_key_commits_it_with_one_ApiKeyIssued_event_naming_its_prefix_and_returns_the_raw_key()
    {
        var vault = new Vault(Account(ClearanceLevel.Secret));

        var issued = await vault.Service.IssueKeyAsync(AccountId, new IssueApiKeyRequest(0, null, null, false, null), default);

        Assert.Equal(["lock payments-ci", "stage key evk_Ab12cd", "commit ApiKeyIssued"], vault.Steps);
        Assert.Equal(StubKeys.RawToken, issued!.RawToken);
        Assert.Equal("Key evk_Ab12cd… issued (unscoped)", Assert.Single(vault.Changes.Events).Details);
    }

    [Fact]
    public async Task A_key_issue_whose_commit_fails_returns_no_key()
    {
        var vault = new Vault(Account(ClearanceLevel.Secret));
        vault.Changes.Failures.Enqueue(new AuditWriteFailedException("rejected"));

        await Assert.ThrowsAsync<AuditWriteFailedException>(() =>
            vault.Service.IssueKeyAsync(AccountId, new IssueApiKeyRequest(0, null, null, false, null), default));

        Assert.Equal(["lock payments-ci", "stage key evk_Ab12cd"], vault.Steps);
    }

    /// <summary>The service account is Secret. Ceilings are clearance levels: 1 Standard, 2 Elevated, 3 Secret, 4 Top Secret.</summary>
    [Theory]
    [InlineData(0, null, null, null, null, null, null, null)]
    [InlineData(3650, 2, " payments ", "10.0.0.5, 10.0.0.0/24;10.0.0.5", "2036-10-30 12:00:00Z", "Elevated", "PAYMENTS", "10.0.0.5/32;10.0.0.0/24")]
    [InlineData(1, 1, "   ", "\n2001:db8::1\t", "2026-11-03 12:00:00Z", "Standard", null, "2001:db8::1/128")]
    public async Task Key_options_within_their_rules_are_stored_in_canonical_form(
        int expiresInDays, int? ceiling, string? projectScope, string? allowedRanges,
        string? storedExpiry, string? storedCeiling, string? storedScope, string? storedRanges)
    {
        var vault = new Vault(Account(ClearanceLevel.Secret));

        await vault.Service.IssueKeyAsync(AccountId,
            new IssueApiKeyRequest(expiresInDays, (ClearanceLevel?)ceiling, projectScope, false, allowedRanges), default);

        var key = Assert.Single(vault.Accounts.Keys);
        Assert.Equal(
            (storedExpiry, storedCeiling, storedScope, storedRanges),
            (key.ExpiresAtUtc?.ToString("u"), key.ClearanceCeiling?.ToString(), key.ProjectScope, key.AllowedCidrs));
    }

    /// <summary>The service account is Secret. Ceilings are clearance levels: 1 Standard, 2 Elevated, 3 Secret, 4 Top Secret.</summary>
    [Theory]
    [InlineData(-1, null, null, null, "ExpiresInDays", "A key expires after 0 to 3650 days, where 0 means it never expires.")]
    [InlineData(3651, null, null, null, "ExpiresInDays", "A key expires after 0 to 3650 days, where 0 means it never expires.")]
    [InlineData(0, 0, null, null, "ClearanceCeiling", "A clearance ceiling must be one of the vault's clearance levels below the service account's, or none.")]
    [InlineData(0, 7, null, null, "ClearanceCeiling", "A clearance ceiling must be one of the vault's clearance levels below the service account's, or none.")]
    [InlineData(0, 3, null, null, "ClearanceCeiling", "A clearance ceiling must be one of the vault's clearance levels below the service account's, or none.")]
    [InlineData(0, 4, null, null, "ClearanceCeiling", "A clearance ceiling must be one of the vault's clearance levels below the service account's, or none.")]
    [InlineData(0, null, "PAYMENTS-0123456789-0123456789-0123456789-0123456789-0123456789-0", null, "ProjectScope", "A project scope is at most 64 characters, not counting spaces at either end.")]
    [InlineData(0, null, null, "10.0.0.5, payments", "AllowedRanges", "'payments' is not a valid IP address or CIDR range (e.g. 203.0.113.7 or 10.8.0.0/24).")]
    public async Task A_key_option_outside_its_rule_is_refused_naming_the_field_and_issues_no_key(
        int expiresInDays, int? ceiling, string? projectScope, string? allowedRanges, string field, string message)
    {
        var vault = new Vault(Account(ClearanceLevel.Secret));

        var refusal = await Assert.ThrowsAsync<AccountInputException>(() => vault.Service.IssueKeyAsync(AccountId,
            new IssueApiKeyRequest(expiresInDays, (ClearanceLevel?)ceiling, projectScope, false, allowedRanges), default));

        Assert.Equal((field, message), (refusal.Field, refusal.Message));
        Assert.DoesNotContain(vault.Steps, step => step.StartsWith("commit", StringComparison.Ordinal));
        Assert.Empty(vault.Accounts.Keys);
    }

    [Fact]
    public async Task Allowed_ranges_too_many_to_store_are_refused()
    {
        const string fortyFiveAddresses = """
            10.0.0.1 10.0.0.2 10.0.0.3 10.0.0.4 10.0.0.5 10.0.0.6 10.0.0.7 10.0.0.8 10.0.0.9 10.0.0.10
            10.0.0.11 10.0.0.12 10.0.0.13 10.0.0.14 10.0.0.15 10.0.0.16 10.0.0.17 10.0.0.18 10.0.0.19 10.0.0.20
            10.0.0.21 10.0.0.22 10.0.0.23 10.0.0.24 10.0.0.25 10.0.0.26 10.0.0.27 10.0.0.28 10.0.0.29 10.0.0.30
            10.0.0.31 10.0.0.32 10.0.0.33 10.0.0.34 10.0.0.35 10.0.0.36 10.0.0.37 10.0.0.38 10.0.0.39 10.0.0.40
            10.0.0.41 10.0.0.42 10.0.0.43 10.0.0.44 10.0.0.45
            """;
        var vault = new Vault(Account(ClearanceLevel.Secret));

        var refusal = await Assert.ThrowsAsync<AccountInputException>(() => vault.Service.IssueKeyAsync(AccountId,
            new IssueApiKeyRequest(0, null, null, false, fortyFiveAddresses), default));

        Assert.Equal(("AllowedRanges", "There are too many allowed source ranges to store. Use fewer or wider ranges."), (refusal.Field, refusal.Message));
        Assert.Empty(vault.Accounts.Keys);
    }

    [Fact]
    public async Task Revoking_a_key_commits_one_ApiKeyRevoked_event()
    {
        var key = Key(AccountId);
        var vault = new Vault([Account(ClearanceLevel.Secret)], [key]);

        Assert.True(await vault.Service.RevokeKeyAsync(AccountId, KeyId, default));

        Assert.Equal(["lock payments-ci", "commit ApiKeyRevoked"], vault.Steps);
        Assert.Equal(Now, key.RevokedAtUtc);
        Assert.Equal("Key evk_Zz99yy… revoked", Assert.Single(vault.Changes.Events).Details);
    }

    [Fact]
    public async Task Revoking_a_revoked_key_records_nothing()
    {
        var key = Key(AccountId);
        key.RevokedAtUtc = Now.AddDays(-1);
        var vault = new Vault([Account(ClearanceLevel.Secret)], [key]);

        Assert.True(await vault.Service.RevokeKeyAsync(AccountId, KeyId, default));

        Assert.Equal(["lock payments-ci"], vault.Steps);
        Assert.Equal(Now.AddDays(-1), key.RevokedAtUtc);
    }

    [Fact]
    public async Task Another_service_accounts_key_is_not_found_and_stays_active()
    {
        var key = Key(OtherAccountId);
        var vault = new Vault([Account(ClearanceLevel.Secret), new ServiceAccount { Id = OtherAccountId, Name = "billing-ci" }], [key]);

        Assert.False(await vault.Service.RevokeKeyAsync(AccountId, KeyId, default));

        Assert.Equal(["lock payments-ci"], vault.Steps);
        Assert.Null(key.RevokedAtUtc);
    }

    public static TheoryData<DateTimeOffset?, DateTimeOffset?, ApiKeyStatus> KeyStates => new()
    {
        { null, null, ApiKeyStatus.Active },
        { Now.AddHours(1), null, ApiKeyStatus.Active },
        { Now, null, ApiKeyStatus.Expired },
        { Now.AddHours(-1), null, ApiKeyStatus.Expired },
        { null, Now.AddHours(-2), ApiKeyStatus.Revoked },
        { Now.AddHours(-1), Now.AddHours(-2), ApiKeyStatus.Revoked },
    };

    [Theory]
    [MemberData(nameof(KeyStates))]
    public async Task A_keys_status_reads_revoked_first_then_expired(DateTimeOffset? expiresAtUtc, DateTimeOffset? revokedAtUtc, ApiKeyStatus status)
    {
        var key = Key(AccountId);
        key.ExpiresAtUtc = expiresAtUtc;
        key.RevokedAtUtc = revokedAtUtc;
        var vault = new Vault([Account(ClearanceLevel.Secret)], [key]);

        var details = await vault.Service.GetAsync(AccountId, default);

        Assert.Equal(status, Assert.Single(details!.Keys).Status);
    }

    private static ServiceAccount Account(ClearanceLevel clearance)
        => new() { Id = AccountId, Name = "payments-ci", Clearance = clearance, ProjectKey = "PAYMENTS", CreatedAtUtc = Now.AddDays(-30) };

    private static ApiKey Key(Guid serviceAccountId)
        => new() { Id = KeyId, ServiceAccountId = serviceAccountId, KeyHash = "stored-hash", Prefix = "evk_Zz99yy", CreatedAtUtc = Now.AddDays(-10) };

    private sealed class Vault
    {
        public Vault(params ServiceAccount[] accounts)
            : this(accounts, [])
        {
        }

        public Vault(ServiceAccount[] accounts, ApiKey[] keys)
        {
            Accounts = new FakeServiceAccounts(Steps, [.. accounts], [.. keys]);
            Changes = new RecordingChanges(Steps, Accounts);
            Service = new ServiceAccountService(Accounts, new StubKeys(), Changes, new FixedClock(Now));
        }

        public List<string> Steps { get; } = [];

        public FakeServiceAccounts Accounts { get; }

        public RecordingChanges Changes { get; }

        public ServiceAccountService Service { get; }
    }

    private sealed class FakeServiceAccounts(List<string> steps, List<ServiceAccount> accounts, List<ApiKey> keys) : IServiceAccountRepository
    {
        public List<ServiceAccount> Accounts { get; } = accounts;

        public List<ApiKey> Keys { get; } = keys;

        public Task<ServiceAccount?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Accounts.FirstOrDefault(account => account.Id == id));
        public Task<bool> ExistsByNameAsync(string name, CancellationToken ct) => Task.FromResult(Accounts.Any(account => account.Name == name));
        public Task<ApiKey?> FindKeyAsync(Guid serviceAccountId, Guid keyId, CancellationToken ct)
            => Task.FromResult(Keys.FirstOrDefault(key => key.Id == keyId && key.ServiceAccountId == serviceAccountId));
        public Task<IReadOnlyList<ApiKey>> ListKeysAsync(Guid serviceAccountId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ApiKey>>([.. Keys.Where(key => key.ServiceAccountId == serviceAccountId)]);

        public void StageAddition(ServiceAccount account) => steps.Add($"stage new {account.Name}");

        public void StageRemoval(ServiceAccount account) => steps.Add($"stage removal {account.Name}");

        public void StageNewKey(ApiKey key)
        {
            steps.Add($"stage key {key.Prefix}");
            Keys.Add(key);
        }

        public Task<IReadOnlyList<ServiceAccount>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<ApiKey?> FindKeyByHashAsync(string keyHash, CancellationToken ct) => throw new NotSupportedException();
        public Task RecordUseAsync(Guid keyId, DateTimeOffset usedAtUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountActiveKeysAsync(Guid serviceAccountId, DateTimeOffset asOfUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingChanges(List<string> steps, FakeServiceAccounts accounts) : IServiceAccountChanges, IServiceAccountChange
    {
        /// <summary>The failures the next commits meet, one each, as the database reports them.</summary>
        public Queue<Exception> Failures { get; } = new();

        public List<AuditEntry> Events { get; } = [];

        public Task<IServiceAccountChange> BeginAsync(Guid serviceAccountId, CancellationToken ct)
        {
            var name = accounts.Accounts.FirstOrDefault(account => account.Id == serviceAccountId)?.Name ?? "an unknown service account";
            steps.Add($"lock {name}");
            return Task.FromResult<IServiceAccountChange>(this);
        }

        public Task CommitAsync(AuditEntry entry, CancellationToken ct)
        {
            if (Failures.TryDequeue(out var failure))
            {
                throw failure;
            }

            steps.Add($"commit {entry.Action}");
            Events.Add(entry);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Generates one known key, so a test can tell the raw key from its prefix.</summary>
    private sealed class StubKeys : IApiKeyFactory
    {
        public const string RawToken = "evk_Ab12cdEf34ghIj56klMn78opQr90stUv12wxYz34";

        public GeneratedApiKey Generate() => new(RawToken, "generated-hash", "evk_Ab12cd");

        public string Hash(string token) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
