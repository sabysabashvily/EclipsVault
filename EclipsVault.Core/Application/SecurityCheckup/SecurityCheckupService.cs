using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Application.Profile;

namespace EclipsVault.Core.Application.SecurityCheckup;

/// <summary>
/// Read-model aggregator: gathers the four posture inputs from the existing account services and
/// hands them to the pure evaluator. It owns no scoring logic of its own — that lives in
/// <see cref="SecurityCheckupEvaluator"/> — it only composes the reads, each keyed by the caller's
/// own user id so the result is self-scoped by construction.
/// </summary>
public sealed class SecurityCheckupService : ISecurityCheckupService
{
    private readonly IProfileService _profile;
    private readonly IPasskeyService _passkeys;
    private readonly IMfaRecoveryService _recovery;
    private readonly ISignedInDevices _sessions;

    public SecurityCheckupService(
        IProfileService profile,
        IPasskeyService passkeys,
        IMfaRecoveryService recovery,
        ISignedInDevices sessions)
    {
        _profile = profile;
        _passkeys = passkeys;
        _recovery = recovery;
        _sessions = sessions;
    }

    public async Task<SecurityCheckup?> GetForUserAsync(Guid userId, CancellationToken ct)
    {
        var profile = await _profile.GetAsync(userId, ct);
        if (profile is null)
        {
            return null;
        }

        var passkeys = await _passkeys.ListForUserAsync(userId, ct);
        var backupCodes = await _recovery.CountRemainingAsync(userId, ct);
        var devices = await _sessions.ListAsync(userId, ct);

        var posture = new SecurityPosture(
            TwoStepEnabled: profile.TotpEnabled,
            PasskeyCount: passkeys.Count,
            BackupCodesRemaining: backupCodes,
            SignedInDeviceCount: devices.Count);

        return SecurityCheckupEvaluator.Evaluate(posture);
    }
}
