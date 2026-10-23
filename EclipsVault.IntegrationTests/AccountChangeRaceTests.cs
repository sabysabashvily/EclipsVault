using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Account changes that overlap on each database. A request holding a copy of the account read
/// before another request's change cannot undo that change, and a proof two requests present at once
/// is spent by one of them. Every test makes its own account in one shared database.
/// </summary>
public abstract class AccountChangeRaceTests(AccountChangeDatabases databases)
{
    private static readonly byte[] ChangedPasswordHash = [.. Enumerable.Repeat((byte)7, 32)];

    [Fact]
    public async Task A_reset_started_before_the_first_authenticator_step_is_refused_and_keeps_the_spent_step()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-reset-before-step");
        await using var stale = databases.NewRequest();
        await stale.LoadAsync(account.Id);
        var step = Totp.CurrentStep();
        await using (var signIn = databases.NewRequest())
        {
            var current = await signIn.LoadAsync(account.Id);
            Assert.True(await signIn.SecondFactors.TryAcceptAuthenticatorCodeAsync(current, Totp.Code(account.TotpSecret!, step), default));
        }

        await Assert.ThrowsAsync<AccountChangedException>(() => stale.Profile.ResetOwnMfaAsync(account.Id, default));

        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.True(stored.TotpEnabled);
        Assert.Equal(account.TotpSecret, stored.TotpSecret);
        Assert.Equal(step, stored.LastTotpStep);
    }

    [Fact]
    public async Task A_reset_started_before_enrollment_is_refused_and_the_new_authenticator_stays_enrolled()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-reset-before-enroll");
        await using (var pending = databases.NewRequest())
        {
            var current = await pending.LoadAsync(account.Id);
            current.TotpEnabled = false;
            await pending.Users.UpdateAsync(current, default);
        }

        await using var stale = databases.NewRequest();
        await stale.LoadAsync(account.Id);
        var step = Totp.CurrentStep();
        await using (var enrolling = databases.NewRequest())
        {
            var current = await enrolling.LoadAsync(account.Id);
            Assert.True(await enrolling.Attempt.RunAsync(current, async token =>
            {
                var accepted = await enrolling.Users.TryCompleteTotpEnrollmentAsync(current, step, token);
                enrolling.Attempt.RecordUserEvent(AuditAction.TotpEnrolled, current, null);
                return accepted;
            }, default));
        }

        await Assert.ThrowsAsync<AccountChangedException>(() => stale.Profile.ResetOwnMfaAsync(account.Id, default));

        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.True(stored.TotpEnabled);
        Assert.Equal(account.TotpSecret, stored.TotpSecret);
        Assert.Equal(step, stored.LastTotpStep);
        Assert.Equal([AuditAction.TotpEnrolled], await EventsAsync(check, account.Id));
    }

    [Fact]
    public async Task A_name_saved_from_a_copy_read_before_a_password_change_is_refused_and_the_new_password_stays()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-name-after-password");
        await using var stale = databases.NewRequest();
        var staleCopy = await stale.LoadAsync(account.Id);
        await using (var change = databases.NewRequest())
        {
            var current = await change.LoadAsync(account.Id);
            current.PasswordHash = ChangedPasswordHash;
            await change.Changes.CommitAsync(current, AuditAction.PasswordChanged, "Password changed by owner", default);
        }

        staleCopy.DisplayName = "Renamed from a stale copy";

        await Assert.ThrowsAsync<AccountChangedException>(
            () => stale.Changes.CommitAsync(staleCopy, AuditAction.ProfileUpdated, "Display name / email updated", default));
        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.Equal(ChangedPasswordHash, stored.PasswordHash);
        Assert.Equal("it-race-name-after-password", stored.DisplayName);
        Assert.Equal([AuditAction.PasswordChanged], await EventsAsync(check, account.Id));
    }

    [Fact]
    public async Task A_picture_saved_from_a_copy_read_before_an_authenticator_reset_is_refused_and_the_reset_stays()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-picture-after-reset");
        await using var stale = databases.NewRequest();
        var staleCopy = await stale.LoadAsync(account.Id);
        await using (var reset = databases.NewRequest())
        {
            var current = await reset.LoadAsync(account.Id);
            current.TotpSecret = null;
            current.TotpEnabled = false;
            await reset.Changes.CommitAsync(current, AuditAction.SelfMfaReset, "Authenticator reset by owner", default);
        }

        await stale.Users.StageAvatarAsync(staleCopy, LegacyProfile.TinyPng, default);

        await Assert.ThrowsAsync<AccountChangedException>(
            () => stale.Changes.CommitAsync(staleCopy, AuditAction.AvatarUpdated, "Profile picture updated", default));
        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.Null(stored.TotpSecret);
        Assert.False(stored.TotpEnabled);
        Assert.Null(stored.AvatarUpdatedAtUtc);
        Assert.False(await check.Context.UserAvatars.AnyAsync(avatar => avatar.UserId == account.Id));
    }

    [Fact]
    public async Task A_name_saved_from_a_copy_read_before_an_administrator_disabled_and_demoted_the_account_is_refused()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-name-after-admin");
        await using var stale = databases.NewRequest();
        var staleCopy = await stale.LoadAsync(account.Id);
        await using (var admin = databases.NewRequest())
        {
            var current = await admin.LoadAsync(account.Id);
            current.Clearance = ClearanceLevel.Secret;
            current.IsDisabled = true;
            await admin.Users.UpdateAsync(current, default);
        }

        staleCopy.DisplayName = "Renamed from a stale copy";

        await Assert.ThrowsAsync<AccountChangedException>(
            () => stale.Changes.CommitAsync(staleCopy, AuditAction.ProfileUpdated, "Display name / email updated", default));
        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.Equal(ClearanceLevel.Secret, stored.Clearance);
        Assert.True(stored.IsDisabled);
    }

    [Fact]
    public async Task A_save_from_a_copy_read_before_a_sign_in_is_refused_and_keeps_the_spent_step()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-name-after-sign-in");
        var step = Totp.CurrentStep();
        await using var stale = databases.NewRequest();
        var staleCopy = await stale.LoadAsync(account.Id);
        await using (var signIn = databases.NewRequest())
        {
            var current = await signIn.LoadAsync(account.Id);
            Assert.True(await signIn.SecondFactors.TryAcceptAuthenticatorCodeAsync(current, Totp.Code(account.TotpSecret!, step), default));
            await signIn.Users.RecordFailedAttemptAsync(current, LockoutPolicy.Default, DateTimeOffset.UtcNow, default);
        }

        staleCopy.Clearance = ClearanceLevel.Elevated;
        await Assert.ThrowsAsync<AccountChangedException>(() => stale.Users.UpdateAsync(staleCopy, default));

        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.Equal(ClearanceLevel.Standard, stored.Clearance);
        Assert.Equal(step, stored.LastTotpStep);
        Assert.Equal(1, stored.FailedAccessCount);
    }

    [Fact]
    public async Task An_authenticator_code_presented_by_two_requests_at_once_is_accepted_once()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-same-code");
        await using var first = databases.NewRequest();
        await using var second = databases.NewRequest();
        var firstCopy = await first.LoadAsync(account.Id);
        var secondCopy = await second.LoadAsync(account.Id);
        var code = Totp.Code(account.TotpSecret!, Totp.CurrentStep());

        var accepted = await Task.WhenAll(
            first.SecondFactors.TryAcceptAuthenticatorCodeAsync(firstCopy, code, default),
            second.SecondFactors.TryAcceptAuthenticatorCodeAsync(secondCopy, code, default));

        Assert.Single(accepted, wasAccepted => wasAccepted);
    }

    [Fact]
    public async Task An_authenticator_code_checked_against_a_copy_read_before_a_reset_is_refused()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-code-after-reset");
        await using var stale = databases.NewRequest();
        var staleCopy = await stale.LoadAsync(account.Id);
        await using (var reset = databases.NewRequest())
        {
            var current = await reset.LoadAsync(account.Id);
            current.TotpSecret = null;
            current.TotpEnabled = false;
            await reset.Changes.CommitAsync(current, AuditAction.SelfMfaReset, "Authenticator reset by owner", default);
        }

        Assert.False(await stale.SecondFactors.TryAcceptAuthenticatorCodeAsync(
            staleCopy, Totp.Code(account.TotpSecret!, Totp.CurrentStep()), default));
    }

    [Fact]
    public async Task A_recovery_code_presented_by_two_requests_at_once_is_redeemed_once()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-same-recovery-code");
        await using var setup = databases.NewRequest();
        var codes = await setup.Recovery.GenerateAsync(account.Id, default);
        await using var first = databases.NewRequest();
        await using var second = databases.NewRequest();
        var firstCopy = await first.LoadAsync(account.Id);
        var secondCopy = await second.LoadAsync(account.Id);

        var redeemed = await Task.WhenAll(
            first.SecondFactors.TryRedeemRecoveryCodeAsync(firstCopy, codes[0], default),
            second.SecondFactors.TryRedeemRecoveryCodeAsync(secondCopy, codes[0], default));

        Assert.Single(redeemed, wasRedeemed => wasRedeemed);
        await using var check = databases.NewRequest();
        Assert.Equal(9, await check.RecoveryCodes.CountUnusedAsync(account.Id, default));
    }

    [Fact]
    public async Task A_first_recovery_code_set_made_from_a_copy_read_before_another_is_refused_leaving_one_set()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-two-first-sets");
        await using var stale = databases.NewRequest();
        await stale.LoadAsync(account.Id);
        await using (var other = databases.NewRequest())
        {
            await other.Recovery.GenerateAsync(account.Id, default);
        }

        await Assert.ThrowsAsync<AccountChangedException>(() => stale.Recovery.GenerateAsync(account.Id, default));

        await using var check = databases.NewRequest();
        Assert.Equal(10, await check.Context.MfaRecoveryCodes.CountAsync(code => code.UserId == account.Id));
        Assert.Equal([AuditAction.RecoveryCodesGenerated], await EventsAsync(check, account.Id));
    }

    [Fact]
    public async Task Failed_attempts_made_at_once_each_count()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-failures");
        await using var first = databases.NewRequest();
        await using var second = databases.NewRequest();
        await using var third = databases.NewRequest();
        var firstCopy = await first.LoadAsync(account.Id);
        var secondCopy = await second.LoadAsync(account.Id);
        var thirdCopy = await third.LoadAsync(account.Id);

        await Task.WhenAll(
            first.Users.RecordFailedAttemptAsync(firstCopy, LockoutPolicy.Default, DateTimeOffset.UtcNow, default),
            second.Users.RecordFailedAttemptAsync(secondCopy, LockoutPolicy.Default, DateTimeOffset.UtcNow, default),
            third.Users.RecordFailedAttemptAsync(thirdCopy, LockoutPolicy.Default, DateTimeOffset.UtcNow, default));

        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.Equal(3, stored.FailedAccessCount);
        Assert.Null(stored.LockedUntilUtc);
    }

    [Fact]
    public async Task The_failed_attempt_that_reaches_the_limit_locks_the_account_once()
    {
        var lockout = new LockoutPolicy(MaxFailedAttempts: 2, LockoutDuration: TimeSpan.FromMinutes(15));
        var account = await databases.CreateEnrolledAccountAsync("it-race-lockout");
        await using var first = databases.NewRequest();
        await using var second = databases.NewRequest();
        var firstCopy = await first.LoadAsync(account.Id);
        var secondCopy = await second.LoadAsync(account.Id);

        var locked = await Task.WhenAll(
            first.Users.RecordFailedAttemptAsync(firstCopy, lockout, DateTimeOffset.UtcNow, default),
            second.Users.RecordFailedAttemptAsync(secondCopy, lockout, DateTimeOffset.UtcNow, default));

        Assert.Single(locked, lockedIt => lockedIt);
        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.Equal(0, stored.FailedAccessCount);
        Assert.NotNull(stored.LockedUntilUtc);
    }

    [Fact]
    public async Task A_change_spanning_every_recovery_code_is_recorded_as_one_event()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-one-event");
        await using var setup = databases.NewRequest();
        await setup.Recovery.GenerateAsync(account.Id, default);
        await using var reset = databases.NewRequest();
        var current = await reset.LoadAsync(account.Id);
        current.TotpSecret = null;
        current.TotpEnabled = false;
        current.LastTotpStep = null;
        await reset.RecoveryCodes.StageRemovalOfAllAsync(account.Id, default);

        await reset.Changes.CommitAsync(current, AuditAction.SelfMfaReset, "Authenticator reset by owner", default);

        await using var check = databases.NewRequest();
        Assert.Equal(0, await check.Context.MfaRecoveryCodes.CountAsync(code => code.UserId == account.Id));
        Assert.Equal([AuditAction.RecoveryCodesGenerated, AuditAction.SelfMfaReset], await EventsAsync(check, account.Id));
    }

    [Fact]
    public async Task A_change_that_changes_nothing_saves_nothing_and_records_nothing()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-race-no-change");
        await using var request = databases.NewRequest();
        var current = await request.LoadAsync(account.Id);
        current.DisplayName = account.DisplayName;

        var committed = await request.Changes.CommitAsync(current, AuditAction.ProfileUpdated, "Display name / email updated", default);

        Assert.False(committed);
        Assert.Empty(await EventsAsync(request, account.Id));
    }

    private static Task<AuditAction[]> EventsAsync(AccountRequest request, Guid accountId)
        => request.Context.AuditLogs
            .Where(row => row.ResourceId == accountId)
            .OrderBy(row => row.Sequence)
            .Select(row => row.Action)
            .ToArrayAsync();
}

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class SqlServerAccountChangeRaceTests(SqlServerAccountChangeDatabases databases)
    : AccountChangeRaceTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresAccountChangeRaceTests(PostgresAccountChangeDatabases databases)
    : AccountChangeRaceTests(databases);
