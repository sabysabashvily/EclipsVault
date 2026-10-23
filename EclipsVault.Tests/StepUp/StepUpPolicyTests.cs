using EclipsVault.Core.Application.StepUp;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.StepUp;

/// <summary>
/// The pure step-up decision: reveal requires a fresh re-auth only when the secret is at or
/// above the sensitivity threshold AND the last strong authentication is older than the window.
/// </summary>
public class StepUpPolicyTests
{
    private static IStepUpService Service(SensitivityLevel minimum = SensitivityLevel.Secret, int maxAgeMinutes = 10)
        => new StepUpService(
            new StepUpOptions { MinimumSensitivity = minimum, MaxAuthAgeMinutes = maxAgeMinutes },
            users: null!, secondFactors: null!, attempt: null!, failures: null!, clock: null!); // IsRequired is pure; the collaborators are unused

    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fresh_auth_never_requires_step_up_even_for_topsecret()
        => Assert.False(Service().IsRequired(SensitivityLevel.TopSecret, Now.AddMinutes(-1), Now));

    [Fact]
    public void Stale_auth_requires_step_up_at_or_above_the_threshold()
        => Assert.True(Service(minimum: SensitivityLevel.Secret).IsRequired(SensitivityLevel.Secret, Now.AddMinutes(-30), Now));

    [Fact]
    public void Below_the_threshold_never_requires_step_up_however_stale()
        => Assert.False(Service(minimum: SensitivityLevel.Secret).IsRequired(SensitivityLevel.Confidential, Now.AddDays(-1), Now));

    [Fact]
    public void The_window_boundary_is_exclusive()
    {
        var service = Service(maxAgeMinutes: 10);
        Assert.False(service.IsRequired(SensitivityLevel.TopSecret, Now.AddMinutes(-10), Now)); // exactly 10m: still fresh
        Assert.True(service.IsRequired(SensitivityLevel.TopSecret, Now.AddMinutes(-10).AddSeconds(-1), Now)); // just over
    }

    [Fact]
    public void A_lower_threshold_pulls_more_secrets_into_step_up()
        => Assert.True(Service(minimum: SensitivityLevel.Confidential).IsRequired(SensitivityLevel.Confidential, Now.AddMinutes(-30), Now));

    [Fact]
    public void MaxAuthAgeMinutes_is_surfaced_for_the_prompt()
        => Assert.Equal(15, Service(maxAgeMinutes: 15).MaxAuthAgeMinutes);
}
