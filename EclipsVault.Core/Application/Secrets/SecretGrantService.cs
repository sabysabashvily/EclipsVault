using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Secrets;

/// <summary>
/// Orchestrates secret access grants: resolves grantees, prevents duplicates and
/// self-grants, and audits every share/revoke. The ABAC handler calls
/// <see cref="HasActiveGrantAsync"/> to let a grant satisfy the project rule.
/// </summary>
public sealed class SecretGrantService : ISecretGrantService
{
    private readonly ISecretGrantRepository _grants;
    private readonly ISecretRepository _secrets;
    private readonly IUserRepository _users;
    private readonly IAuditSink _audit;
    private readonly IAuditContext _actor;
    private readonly TimeProvider _clock;

    public SecretGrantService(
        ISecretGrantRepository grants,
        ISecretRepository secrets,
        IUserRepository users,
        IAuditSink audit,
        IAuditContext actor,
        TimeProvider clock)
    {
        _grants = grants;
        _secrets = secrets;
        _users = users;
        _audit = audit;
        _actor = actor;
        _clock = clock;
    }

    public Task<bool> HasActiveGrantAsync(Guid userId, Guid secretId, CancellationToken ct)
        => _grants.HasActiveGrantAsync(userId, secretId, _clock.GetUtcNow(), ct);

    public async Task<IReadOnlyList<SecretGrantDto>> ListForSecretAsync(Guid secretId, CancellationToken ct)
    {
        var grants = await _grants.ListForSecretAsync(secretId, ct);
        return grants
            .Select(g => new SecretGrantDto(g.Id, g.GranteeUserId, g.GranteeUsername, g.GrantedBy, g.CreatedAtUtc, g.ExpiresAtUtc))
            .ToList();
    }

    public Task<IReadOnlyList<SharedSecretDto>> ListSharedWithUserAsync(Guid userId, CancellationToken ct)
        => _grants.ListSharedWithUserAsync(userId, _clock.GetUtcNow(), ct);

    public Task<IReadOnlyList<OutgoingShareDto>> ListIssuedByAsync(Guid issuerUserId, CancellationToken ct)
        => _grants.ListIssuedByAsync(issuerUserId, _clock.GetUtcNow(), ct);

    public async Task GrantAsync(Guid secretId, string secretName, string granteeUsernameOrEmail, int? ttlDays, CancellationToken ct)
    {
        var grantee = await _users.FindByUsernameOrEmailAsync(granteeUsernameOrEmail.Trim(), ct)
                      ?? throw new SharingException($"No user found for '{granteeUsernameOrEmail}'.");

        if (grantee.Id == _actor.UserId)
        {
            throw new SharingException("You already have access — you cannot share a secret with yourself.");
        }

        if (await _grants.ExistsAsync(grantee.Id, secretId, ct))
        {
            throw new SharingException($"'{grantee.Username}' already has a grant on this secret.");
        }

        var now = _clock.GetUtcNow();
        var grant = new SecretGrant
        {
            Id = Guid.NewGuid(),
            SecretId = secretId,
            GranteeUserId = grantee.Id,
            GranteeUsername = grantee.Username,
            IssuerUserId = _actor.UserId,
            GrantedBy = _actor.Username ?? "system",
            CreatedAtUtc = now,
            ExpiresAtUtc = ttlDays is > 0 ? now.AddDays(ttlDays.Value) : null
        };

        await _grants.AddAsync(grant, ct);
        await _audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.SecretShared,
            ResourceType = nameof(Secret),
            ResourceId = secretId,
            ResourceName = secretName,
            Details = $"Shared with '{grantee.Username}'" + (grant.ExpiresAtUtc is { } e ? $" until {e:u}" : "")
        }, ct);
    }

    public async Task<bool> RevokeAsync(Guid grantId, Guid secretId, CancellationToken ct)
    {
        var grant = await _grants.FindAsync(grantId, ct);

        // Not found, or it belongs to a different secret → refuse without distinguishing the two.
        // The caller was authorised against `secretId`, never against this grant, so accepting a
        // grant id from another secret would turn "may share one secret" into "may revoke any grant
        // in the vault" — including on secrets the caller cannot see. Same refusal shape as
        // RevokeIssuedAsync, and for the same reason: it must not double as a grant-id probe.
        if (grant is null || grant.SecretId != secretId)
        {
            return false;
        }

        await RevokeInternalAsync(grant, ct);
        return true;
    }

    public async Task<bool> RevokeIssuedAsync(Guid grantId, Guid issuerUserId, CancellationToken ct)
    {
        var grant = await _grants.FindAsync(grantId, ct);

        // Not found, or issued by another account → refuse without distinguishing the two cases, so a
        // caller can neither revoke a share that isn't theirs nor probe which share ids exist. A share
        // with no known issuer belongs to nobody here.
        if (grant is null || grant.IssuerUserId != issuerUserId)
        {
            return false;
        }

        await RevokeInternalAsync(grant, ct);
        return true;
    }

    private async Task RevokeInternalAsync(SecretGrant grant, CancellationToken ct)
    {
        await _grants.RemoveAsync(grant.Id, ct);

        var secretName = (await _secrets.FindAsync(grant.SecretId, ct))?.Name ?? "secret";
        await _audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.SecretShareRevoked,
            ResourceType = nameof(Secret),
            ResourceId = grant.SecretId,
            ResourceName = secretName,
            Details = $"Revoked access for '{grant.GranteeUsername}'"
        }, ct);
    }
}
