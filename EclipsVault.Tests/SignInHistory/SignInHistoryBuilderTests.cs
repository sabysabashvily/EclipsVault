using EclipsVault.Core.Application.SignInHistory;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.SignInHistoryTests;

/// <summary>
/// The builder is pure: given a user's raw sign-in audit rows it deterministically produces the
/// timeline and summary. These tests pin the classification, the order-independent location signal
/// (first-seen vs unfamiliar), and the rollup counts.
/// </summary>
public class SignInHistoryBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
    private const string Home = "203.0.113.10";
    private const string Cafe = "198.51.100.7";
    private const string Attacker = "192.0.2.66";

    private static SignInAuditRecord Row(int minute, AuditAction action, string ip) =>
        new(T0.AddMinutes(minute), action, ip);

    [Fact]
    public void Empty_input_yields_an_empty_history()
    {
        var history = SignInHistoryBuilder.Build([]);

        Assert.True(history.IsEmpty);
        Assert.Equal(0, history.Summary.SuccessCount);
        Assert.False(history.Summary.NeedsAttention);
    }

    [Fact]
    public void Every_relevant_action_classifies_and_others_do_not()
    {
        foreach (var action in SignInEventClassifier.RelevantActions)
        {
            Assert.NotNull(SignInEventClassifier.Classify(action));
        }

        Assert.Null(SignInEventClassifier.Classify(AuditAction.SecretRevealed));
        Assert.Null(SignInEventClassifier.Classify(AuditAction.ProfileUpdated));
        Assert.Null(SignInEventClassifier.Classify(AuditAction.SessionRevokedByUser));
    }

    [Fact]
    public void Profile_account_verification_is_not_a_sign_in_and_never_makes_an_address_familiar()
    {
        var history = SignInHistoryBuilder.Build(
        [
            Row(0, AuditAction.AccountVerified, Attacker),
            Row(1, AuditAction.AccountVerificationFailed, Attacker),
            Row(2, AuditAction.LoginSucceeded, Attacker)
        ]);

        Assert.DoesNotContain(AuditAction.AccountVerified, SignInEventClassifier.RelevantActions);
        Assert.DoesNotContain(AuditAction.AccountVerificationFailed, SignInEventClassifier.RelevantActions);
        var signIn = Assert.Single(history.Events);
        Assert.Equal(SignInLocationFlag.FirstSeen, signIn.LocationFlag);
    }

    [Fact]
    public void Non_signin_rows_are_excluded_from_the_timeline()
    {
        var history = SignInHistoryBuilder.Build(
        [
            Row(0, AuditAction.LoginSucceeded, Home),
            Row(1, AuditAction.SecretRevealed, Home),
            Row(2, AuditAction.ProfileUpdated, Home)
        ]);

        Assert.Single(history.Events);
        Assert.Equal(SignInOutcome.Success, history.Events[0].Outcome);
    }

    [Fact]
    public void Events_are_returned_newest_first()
    {
        var history = SignInHistoryBuilder.Build(
        [
            Row(0, AuditAction.LoginSucceeded, Home),
            Row(10, AuditAction.LoginFailed, Home),
            Row(20, AuditAction.PasskeyLogin, Home)
        ]);

        Assert.Collection(history.Events,
            e => Assert.Equal(T0.AddMinutes(20), e.TimestampUtc),
            e => Assert.Equal(T0.AddMinutes(10), e.TimestampUtc),
            e => Assert.Equal(T0.AddMinutes(0), e.TimestampUtc));
    }

    [Fact]
    public void First_success_from_an_ip_is_first_seen_then_known()
    {
        var history = SignInHistoryBuilder.Build(
        [
            Row(0, AuditAction.LoginSucceeded, Home),
            Row(30, AuditAction.LoginSucceeded, Home)
        ]);

        // Newest first: the later sign-in is a known location; the earlier one is first-seen.
        Assert.Equal(SignInLocationFlag.None, history.Events[0].LocationFlag);
        Assert.Equal(SignInLocationFlag.FirstSeen, history.Events[1].LocationFlag);
    }

    [Fact]
    public void Failed_attempt_from_a_never_established_ip_is_unfamiliar_and_suspicious()
    {
        var history = SignInHistoryBuilder.Build(
        [
            Row(0, AuditAction.LoginSucceeded, Home),
            Row(5, AuditAction.LoginFailed, Attacker)
        ]);

        var attempt = history.Events.Single(e => e.SourceIp == Attacker);
        Assert.Equal(SignInLocationFlag.Unfamiliar, attempt.LocationFlag);
        Assert.Equal(1, history.Summary.SuspiciousCount);
        Assert.True(history.Summary.NeedsAttention);
    }

    [Fact]
    public void Typo_then_success_from_a_new_device_is_not_flagged_suspicious()
    {
        // A mistyped password immediately followed by a real sign-in from the same new location:
        // the IP ends up established, so the earlier failure is not treated as unfamiliar.
        var history = SignInHistoryBuilder.Build(
        [
            Row(0, AuditAction.LoginFailed, Cafe),
            Row(1, AuditAction.LoginSucceeded, Cafe)
        ]);

        var failure = history.Events.Single(e => e.Outcome == SignInOutcome.Failed);
        Assert.Equal(SignInLocationFlag.None, failure.LocationFlag);
        Assert.Equal(0, history.Summary.SuspiciousCount);
        Assert.False(history.Summary.NeedsAttention);
    }

    [Fact]
    public void Summary_counts_successes_failures_locations_and_last_times()
    {
        var history = SignInHistoryBuilder.Build(
        [
            Row(0, AuditAction.LoginSucceeded, Home),
            Row(10, AuditAction.PasskeyLogin, Home),
            Row(20, AuditAction.TotpFailed, Home),
            Row(30, AuditAction.LoginFailed, Attacker),
            Row(40, AuditAction.AccountLockedOut, Attacker)
        ]);

        var s = history.Summary;
        Assert.Equal(2, s.SuccessCount);           // one password, one passkey
        Assert.Equal(3, s.FailedCount);            // totp-failed + login-failed + locked-out (blocked)
        Assert.Equal(2, s.DistinctLocations);      // Home + Attacker
        Assert.Equal(T0.AddMinutes(10), s.LastSuccessUtc);
        Assert.Equal(T0.AddMinutes(40), s.LastFailedUtc);
        // The TotpFailed came from Home (established), so only the two Attacker attempts are suspicious.
        Assert.Equal(2, s.SuspiciousCount);
    }
}
