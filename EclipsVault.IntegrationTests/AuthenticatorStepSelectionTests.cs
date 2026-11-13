using EclipsVault.Infrastructure.Security;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A code for the step before the server's is refused when the check happens in the next step.
/// These cases pin that boundary on a fixed server clock: the real verifier, and no wait for a
/// wall-clock rollover.
/// </summary>
public sealed class AuthenticatorStepSelectionTests
{
    // 1_700_000_039 is the last second of step 56_666_667. The next second starts step 56_666_668.
    private static readonly DateTimeOffset LastSecondOfStep = DateTimeOffset.FromUnixTimeSeconds(1_700_000_039);
    private static readonly DateTimeOffset FirstSecondOfNextStep = DateTimeOffset.FromUnixTimeSeconds(1_700_000_040);

    private const long PreviousStep = 56_666_666;
    private const long ServerStep = 56_666_667;
    private const long NextStep = 56_666_668;
    private const long TenStepsBehind = 56_666_657;

    [Fact]
    public void The_previous_step_is_refused_once_the_server_clock_enters_the_next_step()
    {
        var secret = NewSecret();
        var previous = Totp.StepAt(LastSecondOfStep) - 1;

        Assert.Equal(PreviousStep, previous);
        Assert.Equal(ServerStep, Totp.StepAt(LastSecondOfStep));
        Assert.Equal(NextStep, Totp.StepAt(FirstSecondOfNextStep));
        Assert.False(Accepts(FirstSecondOfNextStep, secret, previous));
    }

    [Fact]
    public void The_servers_own_step_is_accepted_on_both_sides_of_that_rollover()
    {
        var secret = NewSecret();
        var step = Totp.StepStillAccepted(LastSecondOfStep);

        Assert.Equal(ServerStep, step);
        Assert.True(Accepts(LastSecondOfStep, secret, step));
        Assert.True(Accepts(FirstSecondOfNextStep, secret, step));
    }

    [Fact]
    public void The_sign_in_after_enrollment_is_newer_and_still_accepted_across_the_rollover()
    {
        var secret = NewSecret();
        var enrolled = Totp.StepStillAccepted(LastSecondOfStep);
        var sameStep = Totp.StepStillAccepted(LastSecondOfStep, enrolled);
        var afterRollover = Totp.StepStillAccepted(FirstSecondOfNextStep, enrolled);

        Assert.Equal(ServerStep, enrolled);
        Assert.Equal(NextStep, sameStep);
        Assert.Equal(NextStep, afterRollover);
        Assert.True(Accepts(LastSecondOfStep, secret, sameStep, spent: enrolled));
        Assert.True(Accepts(FirstSecondOfNextStep, secret, sameStep, spent: enrolled));
    }

    [Fact]
    public void A_code_ten_steps_behind_the_server_stays_refused_across_the_rollover()
    {
        var secret = NewSecret();
        var expired = Totp.StepAt(LastSecondOfStep) - 10;

        Assert.Equal(TenStepsBehind, expired);
        Assert.False(Accepts(LastSecondOfStep, secret, expired));
        Assert.False(Accepts(FirstSecondOfNextStep, secret, expired));
    }

    private static bool Accepts(DateTimeOffset serverNow, string secret, long step, long? spent = null)
        => new TotpService(new FixedClock(serverNow)).TryValidateCode(secret, Totp.Code(secret, step), spent, out _);

    private static string NewSecret() => new TotpService(TimeProvider.System).GenerateSecret();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
