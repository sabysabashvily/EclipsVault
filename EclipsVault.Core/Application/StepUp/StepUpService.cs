using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.StepUp;

public sealed class StepUpService : IStepUpService
{
    private readonly StepUpOptions _options;
    private readonly IUserRepository _users;
    private readonly SecondFactorVerifier _secondFactors;
    private readonly IAuthenticationAttempt _attempt;
    private readonly AuthenticationFailures _failures;
    private readonly TimeProvider _clock;
    private readonly ISessionRevocationService _revocation;

    public StepUpService(StepUpOptions options, IUserRepository users, SecondFactorVerifier secondFactors,
        IAuthenticationAttempt attempt, AuthenticationFailures failures, TimeProvider clock, ISessionRevocationService revocation)
    {
        _options = options;
        _users = users;
        _secondFactors = secondFactors;
        _attempt = attempt;
        _failures = failures;
        _clock = clock;
        _revocation = revocation;
    }

    public int MaxAuthAgeMinutes => _options.MaxAuthAgeMinutes;

    public bool IsRequired(SensitivityLevel sensitivity, DateTimeOffset lastStrongAuthUtc, DateTimeOffset nowUtc)
        => (int)sensitivity >= (int)_options.MinimumSensitivity
           && nowUtc - lastStrongAuthUtc > TimeSpan.FromMinutes(_options.MaxAuthAgeMinutes);

    public async Task<bool> VerifyAsync(Guid userId, SessionStamp stamp, string code, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);

        if (user is null)
        {
            return false;
        }

        return await _attempt.RunAsync(user, async token =>
        {
            user.RequireCredentialGeneration(stamp.CredentialGeneration);
            if (await _revocation.FindAsync(user.Id, token) != stamp.RevocationVersion)
            {
                throw new StaleProofException();
            }
            if (user.IsDisabled || user.IsLockedOut(_clock.GetUtcNow()))
            {
                _attempt.RecordUserEvent(AuditAction.StepUpFailed, user, "Account is disabled or locked out");
                return false;
            }

            if (!await _secondFactors.TryAcceptAuthenticatorCodeAsync(user, code, token))
            {
                await _failures.RecordAsync(user, AuditAction.StepUpFailed, "Step-up re-authentication failed", token);
                return false;
            }

            await _failures.ResetAsync(user, token);
            _attempt.RecordUserEvent(AuditAction.StepUpVerified, user, "Re-authenticated for a sensitive reveal");
            return true;
        }, ct);
    }
}
