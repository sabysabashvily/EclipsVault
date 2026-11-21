using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Users;

public sealed record NeverUsedAccountCandidate(
    Guid Id,
    string Username,
    ClearanceLevel Clearance,
    bool IsDisabled,
    bool TotpEnabled,
    bool HasPasskey,
    bool HasRecordedSignIn,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// The upgrade selection rule for never-used accounts (ADR-0033): every account with no enrolled
/// authenticator, no passkey, and no recorded sign-in (<see cref="AuditAction.LoginSucceeded"/> or
/// <see cref="AuditAction.PasskeyLogin"/>) moves to awaiting activation, except the last enabled
/// administrator. When every enabled administrator would otherwise be invited, <c>vault-admin</c>
/// stays if it is in that set; otherwise the earliest created enabled administrator stays.
/// </summary>
public static class NeverUsedAccountSelection
{
    public const string BootstrapAdminUsername = "vault-admin";

    public static IReadOnlySet<Guid> SelectAccountsToInvite(IReadOnlyList<NeverUsedAccountCandidate> accounts)
    {
        var candidates = accounts
            .Where(a => !a.TotpEnabled && !a.HasPasskey && !a.HasRecordedSignIn)
            .ToList();

        var candidateIds = candidates.Select(c => c.Id).ToHashSet();
        var anyActiveEnabledAdmin = accounts.Any(a =>
            a.Clearance == ClearanceLevel.TopSecret
            && !a.IsDisabled
            && !candidateIds.Contains(a.Id));

        if (!anyActiveEnabledAdmin)
        {
            var preserved = candidates
                .Where(c => c.Clearance == ClearanceLevel.TopSecret && !c.IsDisabled)
                .OrderBy(c => string.Equals(c.Username, BootstrapAdminUsername, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(c => c.CreatedAtUtc)
                .ThenBy(c => c.Id)
                .FirstOrDefault();

            if (preserved is not null)
            {
                candidateIds.Remove(preserved.Id);
            }
        }

        return candidateIds;
    }
}
