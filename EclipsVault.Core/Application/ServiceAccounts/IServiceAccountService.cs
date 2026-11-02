namespace EclipsVault.Core.Application.ServiceAccounts;

/// <summary>
/// Administrative lifecycle for service accounts and their API keys. Every change commits with its one
/// audit event; raw key tokens are returned exactly once (at issue time).
/// </summary>
public interface IServiceAccountService
{
    Task<IReadOnlyList<ServiceAccountSummaryDto>> ListAsync(CancellationToken ct);

    Task<ServiceAccountDetailsDto?> GetAsync(Guid id, CancellationToken ct);

    Task<Guid> CreateAsync(CreateServiceAccountRequest request, CancellationToken ct);

    Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Issues a new API key with an optional narrowing scope. Returns the raw token once, or null if the account does not exist.</summary>
    Task<IssuedApiKeyDto?> IssueKeyAsync(Guid serviceAccountId, IssueApiKeyRequest request, CancellationToken ct);

    /// <summary>Revokes one of the service account's keys. Returns false when that account has no such key.</summary>
    Task<bool> RevokeKeyAsync(Guid serviceAccountId, Guid keyId, CancellationToken ct);
}

/// <summary>Resolves and validates a presented API key into a service-account identity (used by the API auth handler).</summary>
public interface IApiKeyAuthenticator
{
    /// <summary>
    /// Resolves the service account behind a presented token. <paramref name="sourceIp"/> is the
    /// caller's address; a key with a network binding is rejected when it falls outside its allow-list.
    /// </summary>
    Task<AuthenticatedServiceAccount?> AuthenticateAsync(string presentedToken, System.Net.IPAddress? sourceIp, CancellationToken ct);
}
