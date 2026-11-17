using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.DynamicSecrets;

/// <summary>
/// Lease lifecycle for dynamic credentials. Mint, hand over once, destroy on time.
/// </summary>
public sealed class DynamicSecretService : IDynamicSecretService
{
    private const int LeaseListLimit = 50;

    /// <summary>No lease is issued for longer than a day, whatever its role allows.</summary>
    public const int LeaseCeilingMinutes = 1440;

    public const string RoleMissingNote =
        "The role that minted this credential no longer exists, so it could not be destroyed automatically.";

    public const string BackendRefusedNote = "The backend refused to destroy the credential; it may still work.";

    private readonly IDynamicSecretRepository _repository;
    private readonly IReadOnlyDictionary<DynamicSecretBackend, IDynamicSecretBackend> _backends;
    private readonly IAuditContext _actor;
    private readonly TimeProvider _clock;
    private readonly IPremiumFeatureUsage _premiumUsage;
    private readonly IAuditSink _audit;

    public DynamicSecretService(
        IDynamicSecretRepository repository,
        IEnumerable<IDynamicSecretBackend> backends,
        IAuditContext actor,
        TimeProvider clock,
        IPremiumFeatureUsage premiumUsage,
        IAuditSink audit)
    {
        _repository = repository;
        _backends = backends.ToDictionary(b => b.Backend);
        _actor = actor;
        _clock = clock;
        _premiumUsage = premiumUsage;
        _audit = audit;
    }

    public async Task<IReadOnlyList<DynamicSecretRoleDto>> ListRolesAsync(CancellationToken ct)
        => (await _repository.ListRolesAsync(ct)).Select(Map).ToList();

    public async Task<DynamicSecretRoleDto?> FindRoleAsync(Guid roleId, CancellationToken ct)
        => await _repository.FindRoleAsync(roleId, ct) is { } role ? Map(role) : null;

    public Task<DynamicSecretLease?> FindLeaseAsync(Guid leaseId, CancellationToken ct)
        => _repository.FindLeaseAsync(leaseId, ct);

    public Task RecordAccessDeniedAsync(DynamicSecretRoleDto role, IEnumerable<string> reasons, CancellationToken ct)
        => _audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.SecretAccessDenied,
            ResourceType = nameof(DynamicSecretRole),
            ResourceId = role.Id,
            ResourceName = role.Name,
            Details = string.Join(" ", reasons),
        }, ct);

    public async Task<IssuedCredentialDto> IssueAsync(Guid roleId, int? ttlMinutes, CancellationToken ct)
    {
        var role = await _repository.FindRoleAsync(roleId, ct)
                   ?? throw new VaultAdminException("That dynamic-secret role no longer exists.");

        if (!role.IsEnabled)
        {
            throw new VaultAdminException($"The role '{role.Name}' is disabled and cannot issue credentials.");
        }

        // Gate: a trial or paid licence is required to issue a dynamic credential. Refused before any
        // credential is minted, so nothing is leased and there is nothing to undo. Existing leases and
        // their revocation are unaffected.
        await _premiumUsage.RequireAsync(LicenseFeatures.DynamicSecrets, ct);

        var backend = ResolveBackend(role);
        var now = _clock.GetUtcNow();
        var expiresAt = now.AddMinutes(ClampTtl(role, ttlMinutes));

        var identity = CredentialMint.NewIdentity(role.Name);
        var password = CredentialMint.NewPassword();

        try
        {
            await backend.MintAsync(role, identity, password, expiresAt, ct);
        }
        catch (Exception ex) when (ex is not MintRefusedException)
        {
            // The recipe runs without a transaction, and a failure or a lost acknowledgement can come after
            // CREATE LOGIN succeeded, so the login may exist whatever the error says.
            if (await TryRemoveAsync(backend, role, identity))
            {
                throw new MintRefusedException(role.Name);
            }

            throw new MintIncompleteException(role.Name, identity);
        }

        var lease = new DynamicSecretLease
        {
            Id = Guid.NewGuid(),
            RoleId = role.Id,
            RoleName = role.Name,
            UserId = _actor.UserId ?? Guid.Empty,
            Username = _actor.Username ?? "system",
            CredentialIdentity = identity,
            IssuedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            Status = LeaseStatus.Active
        };

        try
        {
            // The insert carries its own audit row in the same transaction (the interceptor).
            await _repository.AddLeaseAsync(lease, ct);
        }
        catch (Exception recordFailure)
        {
            // The credential is live but we failed to record it — and an unrecorded live credential
            // is precisely what a leasing engine must never leave behind, because nothing would ever
            // come back to reap it. Undo the mint before surfacing the failure.
            if (await TryRemoveAsync(backend, role, identity))
            {
                throw;
            }

            throw new MintUnrecordedException(role.Name, identity, recordFailure);
        }

        return new IssuedCredentialDto(lease.Id, role.Name, identity, password, expiresAt);
    }

    public async Task<IReadOnlyList<LeaseDto>> ListLeasesAsync(Guid userId, bool includeEveryone, CancellationToken ct)
    {
        var leases = includeEveryone
            ? await _repository.ListAllLeasesAsync(LeaseListLimit, ct)
            : await _repository.ListLeasesForUserAsync(userId, LeaseListLimit, ct);

        return leases.Select(Map).ToList();
    }

    public async Task<LeaseStatus?> RevokeAsync(Guid leaseId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var lease = await _repository.FindLeaseAsync(leaseId, ct);

        // One indistinguishable "no" for unknown / already-closed / someone else's, so a caller
        // cannot use revoke to discover which leases exist.
        if (lease is null || lease.Status != LeaseStatus.Active || (!isAdmin && lease.UserId != userId))
        {
            return null;
        }

        if (!await DestroyAsync(lease, ct))
        {
            return LeaseStatus.RevocationFailed;
        }

        try
        {
            await CloseAsync(lease, LeaseStatus.Revoked, null, ct);
        }
        catch (Exception closeFailure) when (closeFailure is not OperationCanceledException)
        {
            // The lease stays Active until its expiry, when the sweep revokes it again and records that.
            throw new RevokedUnrecordedException(closeFailure);
        }

        return LeaseStatus.Revoked;
    }

    public async Task<int> ReapDueLeasesAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var due = await _repository.ListDueLeasesAsync(now, ct);
        var closed = 0;

        foreach (var lease in due)
        {
            // A close that cannot be recorded stops the sweep and leaves the lease Active for the next one.
            if (await DestroyAsync(lease, ct))
            {
                await CloseAsync(lease, LeaseStatus.Expired, null, ct);
            }

            closed++;
        }

        return closed;
    }

    /// <summary>
    /// Destroys the credential, returning true when the backend confirmed it. Otherwise the lease is
    /// closed as <see cref="LeaseStatus.RevocationFailed"/> with a fixed note rather than thrown away or
    /// retried forever: the credential may still be live, and that is a fact an operator has to see.
    /// The backend's own error goes only to its log, because a server message can quote a statement.
    /// </summary>
    private async Task<bool> DestroyAsync(DynamicSecretLease lease, CancellationToken ct)
    {
        var role = await _repository.FindRoleAsync(lease.RoleId, ct);
        if (role is null)
        {
            await CloseAsync(lease, LeaseStatus.RevocationFailed, RoleMissingNote, ct);
            return false;
        }

        try
        {
            await ResolveBackend(role).RevokeAsync(role, lease.CredentialIdentity, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await CloseAsync(lease, LeaseStatus.RevocationFailed, BackendRefusedNote, ct);
            return false;
        }
    }

    private async Task CloseAsync(DynamicSecretLease lease, LeaseStatus status, string? error, CancellationToken ct)
    {
        lease.Close(status, _clock.GetUtcNow(), error);
        await _repository.UpdateLeaseAsync(lease, ct);
    }

    /// <summary>
    /// Runs the role's revocation statements for a login the vault created but did not lease. It ignores
    /// the request's cancellation, which may be what interrupted the mint; the target's command timeout
    /// bounds it instead.
    /// </summary>
    private static async Task<bool> TryRemoveAsync(IDynamicSecretBackend backend, DynamicSecretRole role, string identity)
    {
        try
        {
            await backend.RevokeAsync(role, identity, CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private IDynamicSecretBackend ResolveBackend(DynamicSecretRole role)
        => _backends.TryGetValue(role.Backend, out var backend)
            ? backend
            : throw new VaultAdminException($"No backend is configured for '{role.Backend}'.");

    /// <summary>A caller may ask for less than the role's ceiling and the vault's, never more.</summary>
    private static int ClampTtl(DynamicSecretRole role, int? requested)
        => Math.Clamp(requested ?? role.DefaultTtlMinutes, 1, Math.Max(1, Math.Min(role.MaxTtlMinutes, LeaseCeilingMinutes)));

    private static DynamicSecretRoleDto Map(DynamicSecretRole r) => new(
        r.Id, r.Name, r.Description, r.ProjectKey, r.Environment, r.Sensitivity,
        r.Backend, r.DefaultTtlMinutes, r.MaxTtlMinutes, r.IsEnabled);

    private static LeaseDto Map(DynamicSecretLease l) => new(
        l.Id, l.RoleId, l.RoleName, l.CredentialIdentity, l.Username,
        l.IssuedAtUtc, l.ExpiresAtUtc, l.ClosedAtUtc, l.Status, l.RevocationError);
}
