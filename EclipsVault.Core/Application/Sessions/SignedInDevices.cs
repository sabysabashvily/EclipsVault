namespace EclipsVault.Core.Application.Sessions;

public sealed class SignedInDevices : ISignedInDevices
{
    private readonly IUserRepository _users;
    private readonly ISessionRevocationService _revocation;
    private readonly ISessionRegistry _registry;

    public SignedInDevices(IUserRepository users, ISessionRevocationService revocation, ISessionRegistry registry)
    {
        _users = users;
        _revocation = revocation;
        _registry = registry;
    }

    public async Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, CancellationToken ct)
    {
        var account = await _users.FindByIdAsync(userId, ct);
        var version = await _revocation.FindAsync(userId, ct);
        if (account is null || version is null)
        {
            return [];
        }

        return await _registry.ListAsync(userId, new SessionStamp(account.CredentialGeneration, version), ct);
    }
}
