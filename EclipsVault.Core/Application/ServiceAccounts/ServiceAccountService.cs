using System.Net;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.ServiceAccounts;

/// <summary>
/// Administrative service-account lifecycle and API-key issuance. Also implements
/// <see cref="IApiKeyAuthenticator"/> so the API layer can resolve a presented key
/// without a second service. Every change commits with its one audit event.
/// </summary>
public sealed class ServiceAccountService : IServiceAccountService, IApiKeyAuthenticator
{
    private const string ApiKeyPrefix = "evk_";

    private const int MinimumNameLength = 3;
    private const int MaximumNameLength = 64;
    private const int MaximumProjectKeyLength = 64;
    private const int MaximumExpiryDays = 3650;

    /// <summary>The width of the column that stores a key's ranges, joined by semicolons.</summary>
    private const int MaximumAllowedRangesLength = 512;

    private static readonly char[] RangeSeparators = [',', ';', '\n', '\r', ' ', '\t'];

    /// <summary>Minimum gap between persisted "last used" updates for a key (write-amplification damper).</summary>
    private static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceAccountRepository _repository;
    private readonly IApiKeyFactory _keys;
    private readonly IServiceAccountChanges _changes;
    private readonly TimeProvider _clock;

    public ServiceAccountService(IServiceAccountRepository repository, IApiKeyFactory keys, IServiceAccountChanges changes, TimeProvider clock)
    {
        _repository = repository;
        _keys = keys;
        _changes = changes;
        _clock = clock;
    }

    private static AuditEntry Event(AuditAction action, ServiceAccount account, string? details)
        => new() { Action = action, ResourceType = nameof(ServiceAccount), ResourceId = account.Id, ResourceName = account.Name, Details = details };

    public async Task<IReadOnlyList<ServiceAccountSummaryDto>> ListAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var accounts = await _repository.ListAsync(ct);
        var summaries = new List<ServiceAccountSummaryDto>(accounts.Count);
        foreach (var account in accounts)
        {
            var activeKeyCount = await _repository.CountActiveKeysAsync(account.Id, now, ct);
            summaries.Add(new ServiceAccountSummaryDto(account.Id, account.Name, account.Clearance, account.ProjectKey, account.IsDisabled, activeKeyCount, account.CreatedAtUtc));
        }

        return summaries;
    }

    public async Task<ServiceAccountDetailsDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var account = await _repository.FindAsync(id, ct);
        if (account is null)
        {
            return null;
        }

        var now = _clock.GetUtcNow();
        var keys = await _repository.ListKeysAsync(id, ct);
        var keyDtos = keys
            .Select(key => new ApiKeyDto(key.Id, key.Prefix, key.CreatedAtUtc, key.ExpiresAtUtc, key.RevokedAtUtc, key.LastUsedAtUtc, key.StatusAt(now),
                key.ClearanceCeiling, key.ProjectScope, key.MetadataOnly, key.AllowedCidrList()))
            .ToList();

        return new ServiceAccountDetailsDto(account.Id, account.Name, account.Clearance, account.ProjectKey,
            account.IsDisabled, account.CreatedAtUtc, keyDtos);
    }

    public async Task<Guid> CreateAsync(CreateServiceAccountRequest request, CancellationToken ct)
    {
        var name = CheckedName(request.Name);
        CheckClearance(request.Clearance);
        var project = CheckedProjectKey(request.ProjectKey);
        if (await _repository.ExistsByNameAsync(name, ct))
        {
            throw NameTaken(name);
        }

        var account = new ServiceAccount
        {
            Id = Guid.NewGuid(),
            Name = name,
            Clearance = request.Clearance,
            ProjectKey = project,
            CreatedAtUtc = _clock.GetUtcNow()
        };

        await using var change = await _changes.BeginAsync(account.Id, ct);
        _repository.StageAddition(account);
        try
        {
            await change.CommitAsync(Event(AuditAction.ServiceAccountCreated, account,
                $"Created (clearance {account.Clearance}, project {account.ProjectKey})"), ct);
        }
        catch (ServiceAccountNameTakenException)
        {
            throw NameTaken(name);
        }

        return account.Id;
    }

    private static VaultAdminException NameTaken(string name) => new($"A service account named '{name}' already exists.");

    /// <returns>The name as it is stored: trimmed.</returns>
    private static string CheckedName(string requested)
    {
        var name = requested.Trim();
        if (name.Length is < MinimumNameLength or > MaximumNameLength
            || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
        {
            throw new AccountInputException(nameof(CreateServiceAccountRequest.Name),
                $"Service account names are {MinimumNameLength} to {MaximumNameLength} characters: letters, digits, underscores, hyphens or dots.");
        }

        return name;
    }

    /// <summary>Any integer binds to the enum, and an undefined clearance passes sensitivity checks it should fail.</summary>
    private static void CheckClearance(ClearanceLevel clearance)
    {
        if (!Enum.IsDefined(clearance))
        {
            throw new AccountInputException(nameof(CreateServiceAccountRequest.Clearance), "Choose one of the vault's clearance levels.");
        }
    }

    /// <returns>The project key as it is stored: trimmed and upper case.</returns>
    private static string CheckedProjectKey(string projectKey)
    {
        var project = projectKey.Trim();
        if (project.Length is 0 or > MaximumProjectKeyLength)
        {
            throw new AccountInputException(nameof(CreateServiceAccountRequest.ProjectKey),
                $"Project keys are 1 to {MaximumProjectKeyLength} characters, not counting spaces at either end.");
        }

        return project.ToUpperInvariant();
    }

    public async Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        await using var change = await _changes.BeginAsync(id, ct);
        var account = await _repository.FindAsync(id, ct);
        if (account is null)
        {
            return false;
        }

        if (account.IsDisabled == !enabled)
        {
            return true;
        }

        account.IsDisabled = !enabled;
        await change.CommitAsync(Event(enabled ? AuditAction.ServiceAccountEnabled : AuditAction.ServiceAccountDisabled, account, null), ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var change = await _changes.BeginAsync(id, ct);
        var account = await _repository.FindAsync(id, ct);
        if (account is null)
        {
            return false;
        }

        _repository.StageRemoval(account);
        await change.CommitAsync(Event(AuditAction.ServiceAccountDeleted, account, null), ct);
        return true;
    }

    public async Task<IssuedApiKeyDto?> IssueKeyAsync(Guid serviceAccountId, IssueApiKeyRequest request, CancellationToken ct)
    {
        CheckExpiry(request.ExpiresInDays);
        var projectScope = CheckedProjectScope(request.ProjectScope);
        var allowedCidrs = CheckedAllowedRanges(request.AllowedRanges);

        await using var change = await _changes.BeginAsync(serviceAccountId, ct);
        var account = await _repository.FindAsync(serviceAccountId, ct);
        if (account is null)
        {
            return null;
        }

        CheckCeiling(request.ClearanceCeiling, account.Clearance);
        var generated = _keys.Generate();
        var now = _clock.GetUtcNow();
        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            ServiceAccountId = serviceAccountId,
            KeyHash = generated.Hash,
            Prefix = generated.Prefix,
            CreatedAtUtc = now,
            ExpiresAtUtc = request.ExpiresInDays == 0 ? null : now.AddDays(request.ExpiresInDays),
            ClearanceCeiling = request.ClearanceCeiling,
            ProjectScope = projectScope,
            MetadataOnly = request.MetadataOnly,
            AllowedCidrs = allowedCidrs
        };

        _repository.StageNewKey(key);
        // The audit row names the key by its prefix only: the raw token goes back to the caller and nowhere else.
        await change.CommitAsync(Event(AuditAction.ApiKeyIssued, account,
            $"Key {key.Prefix}… issued" + DescribeScope(key) + (key.ExpiresAtUtc is { } expiresAtUtc ? $", expires {expiresAtUtc:u}" : "")), ct);

        return new IssuedApiKeyDto(key.Id, generated.RawToken, generated.Prefix);
    }

    private static void CheckExpiry(int expiresInDays)
    {
        if (expiresInDays is < 0 or > MaximumExpiryDays)
        {
            throw new AccountInputException(nameof(IssueApiKeyRequest.ExpiresInDays),
                $"A key expires after 0 to {MaximumExpiryDays} days, where 0 means it never expires.");
        }
    }

    /// <summary>A ceiling at or above the account's would not narrow the key, and an undefined one would mean nothing.</summary>
    private static void CheckCeiling(ClearanceLevel? ceiling, ClearanceLevel accountClearance)
    {
        if (ceiling is { } level && (!Enum.IsDefined(level) || level >= accountClearance))
        {
            throw new AccountInputException(nameof(IssueApiKeyRequest.ClearanceCeiling),
                "A clearance ceiling must be one of the vault's clearance levels below the service account's, or none.");
        }
    }

    /// <returns>The scope as it is stored: trimmed and upper case, or null when blank.</returns>
    private static string? CheckedProjectScope(string? projectScope)
    {
        if (string.IsNullOrWhiteSpace(projectScope))
        {
            return null;
        }

        var scope = projectScope.Trim();
        if (scope.Length > MaximumProjectKeyLength)
        {
            throw new AccountInputException(nameof(IssueApiKeyRequest.ProjectScope),
                $"A project scope is at most {MaximumProjectKeyLength} characters, not counting spaces at either end.");
        }

        return scope.ToUpperInvariant();
    }

    /// <returns>The distinct ranges in canonical form joined by semicolons, as they are stored, or null when blank.</returns>
    private static string? CheckedAllowedRanges(string? allowedRanges)
    {
        var canonical = new List<string>();
        foreach (var range in (allowedRanges ?? "").Split(RangeSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!NetworkRules.TryParseCidr(range, out var normalized))
            {
                throw new AccountInputException(nameof(IssueApiKeyRequest.AllowedRanges),
                    $"'{range}' is not a valid IP address or CIDR range (e.g. 203.0.113.7 or 10.8.0.0/24).");
            }

            if (!canonical.Contains(normalized))
            {
                canonical.Add(normalized);
            }
        }

        var stored = string.Join(';', canonical);
        if (stored.Length > MaximumAllowedRangesLength)
        {
            throw new AccountInputException(nameof(IssueApiKeyRequest.AllowedRanges),
                "There are too many allowed source ranges to store. Use fewer or wider ranges.");
        }

        return stored.Length == 0 ? null : stored;
    }

    private static string DescribeScope(ApiKey key)
    {
        var parts = new List<string>();
        if (key.ClearanceCeiling is { } ceiling)
        {
            parts.Add($"clearance ≤ {ceiling}");
        }
        if (!string.IsNullOrEmpty(key.ProjectScope))
        {
            parts.Add($"project {key.ProjectScope}");
        }
        if (key.MetadataOnly)
        {
            parts.Add("metadata-only");
        }
        if (key.AllowedCidrList() is { Count: > 0 } cidrs)
        {
            parts.Add($"from {string.Join("/", cidrs)}");
        }

        return parts.Count == 0 ? " (unscoped)" : $" scoped to {string.Join(", ", parts)}";
    }

    public async Task<bool> RevokeKeyAsync(Guid serviceAccountId, Guid keyId, CancellationToken ct)
    {
        await using var change = await _changes.BeginAsync(serviceAccountId, ct);
        var account = await _repository.FindAsync(serviceAccountId, ct);
        if (account is null)
        {
            return false;
        }

        var key = await _repository.FindKeyAsync(serviceAccountId, keyId, ct);
        if (key is null)
        {
            return false;
        }

        if (key.RevokedAtUtc is not null)
        {
            return true;
        }

        key.RevokedAtUtc = _clock.GetUtcNow();
        await change.CommitAsync(Event(AuditAction.ApiKeyRevoked, account, $"Key {key.Prefix}… revoked"), ct);
        return true;
    }

    public async Task<AuthenticatedServiceAccount?> AuthenticateAsync(string presentedToken, IPAddress? sourceIp, CancellationToken ct)
    {
        presentedToken = presentedToken.Trim();
        if (!presentedToken.StartsWith(ApiKeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var key = await _repository.FindKeyByHashAsync(_keys.Hash(presentedToken), ct);
        var now = _clock.GetUtcNow();
        if (key?.ServiceAccount is null || !key.IsActive(now) || key.ServiceAccount.IsDisabled)
        {
            return null;
        }

        // Network binding: a key with an allow-list is usable only from those ranges. Rejected
        // silently (as with any other invalid key) so the caller learns nothing about why.
        var allowedCidrs = key.AllowedCidrList();
        if (allowedCidrs.Count > 0 && !NetworkRules.IsInAnyCidr(sourceIp, allowedCidrs))
        {
            return null;
        }

        // Throttle the last-used stamp so a read-heavy API caller doesn't incur a DB write on
        // every request — coarse "last used" telemetry doesn't need per-call precision.
        if (key.LastUsedAtUtc is null || now - key.LastUsedAtUtc >= LastUsedWriteInterval)
        {
            await _repository.RecordUseAsync(key.Id, now, ct);
        }

        var account = key.ServiceAccount;
        return new AuthenticatedServiceAccount(
            account.Id,
            account.Name,
            key.EffectiveClearance(account.Clearance),
            account.ProjectKey,
            key.ProjectScope,
            key.MetadataOnly);
    }
}
