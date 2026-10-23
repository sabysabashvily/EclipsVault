using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Authentication;

/// <summary>
/// Accepts each authenticator code and each recovery code at most once, however many requests present
/// it at the same moment. Sign-in, step-up and account verification all spend codes here, so a code
/// spent by one of them is spent for all.
/// </summary>
public sealed class SecondFactorVerifier
{
    private readonly IUserRepository _users;
    private readonly IMfaRecoveryCodeRepository _recoveryCodes;
    private readonly ITotpService _totp;
    private readonly IPasswordHasher _hasher;
    private readonly TimeProvider _clock;

    public SecondFactorVerifier(
        IUserRepository users,
        IMfaRecoveryCodeRepository recoveryCodes,
        ITotpService totp,
        IPasswordHasher hasher,
        TimeProvider clock)
    {
        _users = users;
        _recoveryCodes = recoveryCodes;
        _totp = totp;
        _hasher = hasher;
        _clock = clock;
    }

    /// <summary>
    /// True when <paramref name="code"/> is a current code from the account's enrolled authenticator
    /// and this call spent its time step. A replayed code fails like a wrong one.
    /// </summary>
    public async Task<bool> TryAcceptAuthenticatorCodeAsync(User account, string code, CancellationToken ct)
    {
        if (account.IsDisabled
            || account.IsLockedOut(_clock.GetUtcNow())
            || !account.TotpEnabled
            || string.IsNullOrEmpty(account.TotpSecret)
            || !_totp.TryValidateCode(account.TotpSecret, code, account.LastTotpStep, out var step))
        {
            return false;
        }

        // The account was read before the code was checked, so another request may have spent this
        // step, or reset the authenticator, in between. Only the database can say which came first.
        return await _users.TryAcceptTotpStepAsync(account, step, ct);
    }

    /// <summary>True when <paramref name="code"/> is one of the account's unused recovery codes and this call spent it.</summary>
    public async Task<bool> TryRedeemRecoveryCodeAsync(User account, string code, CancellationToken ct)
    {
        if (account.IsDisabled || account.IsLockedOut(_clock.GetUtcNow()) || !account.TotpEnabled)
        {
            return false;
        }

        var unused = await _recoveryCodes.ListUnusedAsync(account.Id, ct);
        var match = FindMatchingCode(unused, RecoveryCodeFormat.Normalize(code));
        return match is not null && await _recoveryCodes.TryRedeemAsync(match, _clock.GetUtcNow(), ct);
    }

    /// <summary>
    /// Verifies the input against every unused code so a wrong code costs the same
    /// regardless of which (if any) it would have matched — no early-out timing signal.
    /// </summary>
    private MfaRecoveryCode? FindMatchingCode(IReadOnlyList<MfaRecoveryCode> codes, string normalized)
    {
        MfaRecoveryCode? match = null;
        foreach (var candidate in codes)
        {
            if (_hasher.Verify(normalized, candidate.CodeHash, candidate.Salt))
            {
                match = candidate;
            }
        }

        return match;
    }
}
