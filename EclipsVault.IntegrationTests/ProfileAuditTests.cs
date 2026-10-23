using System.Net;
using System.Security.Cryptography;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Each Profile change commits with exactly the one event that describes it, through the Legacy
/// pages that still make these changes, and a request that changes nothing records nothing.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class ProfileAuditTests(VaultFixture vault)
{
    [Fact]
    public async Task Saving_new_profile_details_records_one_profile_update()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-details", IPAddress.Parse("203.0.113.100"));
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-details");
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyProfile.UpdateDetailsAsync(owner.Client, "Renamed Owner", "renamed.owner@example.test");

        Assert.Equal([AuditAction.ProfileUpdated], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
        Assert.Equal("Renamed Owner", (await LegacyProfile.AccountAsync(vault, "it-profile-details")).DisplayName);
    }

    [Fact]
    public async Task An_invalid_email_changes_nothing_and_records_nothing()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-bad-email", IPAddress.Parse("203.0.113.101"));
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-bad-email");
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyProfile.UpdateDetailsAsync(owner.Client, "Changed Name", "not-an-email");

        Assert.Empty(await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
        Assert.Equal(account.DisplayName, (await LegacyProfile.AccountAsync(vault, "it-profile-bad-email")).DisplayName);
    }

    [Fact]
    public async Task Changing_the_password_records_one_password_change()
    {
        var password = NewPassword();
        var owner = await vault.SignInNewAccountAsync("it-profile-password", IPAddress.Parse("203.0.113.102"), password: password);
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-password");
        var before = await AuditTrail.HeadAsync(vault);

        var changed = await LegacyProfile.ChangePasswordAsync(owner.Client, password, NewPassword());

        Assert.Equal("/Profile", changed.Headers.Location?.OriginalString);
        Assert.Equal([AuditAction.PasswordChanged], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
        Assert.NotEqual(account.PasswordHash, (await LegacyProfile.AccountAsync(vault, "it-profile-password")).PasswordHash);
    }

    [Fact]
    public async Task Uploading_a_picture_records_one_avatar_update()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-avatar", IPAddress.Parse("203.0.113.103"));
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-avatar");
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyProfile.UploadAvatarAsync(owner.Client, LegacyProfile.TinyPng);

        Assert.True(await LegacyProfile.HasAvatarAsync(vault, account.Id));
        Assert.Equal([AuditAction.AvatarUpdated], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task Removing_a_picture_records_one_avatar_removal()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-avatar-remove", IPAddress.Parse("203.0.113.115"));
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-avatar-remove");
        await LegacyProfile.UploadAvatarAsync(owner.Client, LegacyProfile.TinyPng);
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyProfile.RemoveAvatarAsync(owner.Client);

        Assert.False(await LegacyProfile.HasAvatarAsync(vault, account.Id));
        Assert.Equal([AuditAction.AvatarRemoved], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task Removing_an_absent_picture_records_nothing()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-no-avatar", IPAddress.Parse("203.0.113.116"));
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-no-avatar");
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyProfile.RemoveAvatarAsync(owner.Client);

        Assert.False(await LegacyProfile.HasAvatarAsync(vault, account.Id));
        Assert.Empty(await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task Saving_unchanged_profile_details_records_nothing()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-no-change", IPAddress.Parse("203.0.113.117"));
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-no-change");
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyProfile.UpdateDetailsAsync(owner.Client, account.DisplayName, account.Email);

        Assert.Empty(await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task Generating_recovery_codes_records_one_event_for_the_whole_set()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-recovery", IPAddress.Parse("203.0.113.104"));
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-recovery");
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyProfile.GenerateRecoveryCodesAsync(owner.Client);

        Assert.Equal([AuditAction.RecoveryCodesGenerated], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
        Assert.Equal(10, (await LegacyProfile.RecoveryCodesAsync(vault, account.Id)).Length);
    }

    [Fact]
    public async Task Resetting_the_authenticator_retires_it_with_its_replay_marker_and_recovery_codes_as_one_event()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-reset", IPAddress.Parse("203.0.113.105"));
        await LegacyProfile.GenerateRecoveryCodesAsync(owner.Client);
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-reset");
        var before = await AuditTrail.HeadAsync(vault);

        var reset = await LegacyProfile.ResetAuthenticatorAsync(owner.Client);

        var stored = await LegacyProfile.AccountAsync(vault, "it-profile-reset");
        Assert.Equal("/Account/Login", reset.Headers.Location?.OriginalString);
        Assert.Equal([AuditAction.SelfMfaReset], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
        Assert.NotNull(account.LastTotpStep);
        Assert.Null(stored.TotpSecret);
        Assert.False(stored.TotpEnabled);
        Assert.Null(stored.LastTotpStep);
        Assert.Empty(await LegacyProfile.RecoveryCodesAsync(vault, account.Id));
    }

    [Fact]
    public async Task Adding_a_passkey_records_one_registration()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-passkey", IPAddress.Parse("203.0.113.106"));
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-passkey");
        using var authenticator = new PasskeyAuthenticator();
        var before = await AuditTrail.HeadAsync(vault);

        var added = await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Audit test key");
        var passkey = Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id));

        Assert.Contains("\"success\":true", await added.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("Audit test key", passkey.Nickname);
        Assert.Equal([AuditAction.PasskeyRegistered], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task Removing_an_owned_passkey_records_one_removal()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-passkey-remove", IPAddress.Parse("203.0.113.118"));
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-passkey-remove");
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Removed key");
        var passkey = Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id));
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyProfile.RemovePasskeyAsync(owner.Client, passkey.Id);

        Assert.Empty(await LegacyProfile.PasskeysAsync(vault, account.Id));
        Assert.Equal([AuditAction.PasskeyRemoved], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task Removing_a_passkey_of_another_account_changes_nothing_and_records_nothing()
    {
        var holder = await vault.SignInNewAccountAsync("it-profile-passkey-holder", IPAddress.Parse("203.0.113.107"));
        var other = await vault.SignInNewAccountAsync("it-profile-passkey-other", IPAddress.Parse("203.0.113.108"));
        var holderAccount = await LegacyProfile.AccountAsync(vault, "it-profile-passkey-holder");
        var otherAccount = await LegacyProfile.AccountAsync(vault, "it-profile-passkey-other");
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(holder.Client, authenticator, "Not yours");
        var passkey = Assert.Single(await LegacyProfile.PasskeysAsync(vault, holderAccount.Id));
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyProfile.RemovePasskeyAsync(other.Client, passkey.Id);

        Assert.Single(await LegacyProfile.PasskeysAsync(vault, holderAccount.Id));
        Assert.Empty(await LegacyProfile.EventsAfterAsync(vault, before, holderAccount.Id));
        Assert.Empty(await LegacyProfile.EventsAfterAsync(vault, before, otherAccount.Id));
    }

    [Fact]
    public async Task Signing_in_records_only_sign_in_events_although_it_spends_an_authenticator_step()
    {
        var password = NewPassword();
        await vault.SignInNewAccountAsync("it-profile-sign-in", IPAddress.Parse("203.0.113.109"), password: password);
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-sign-in");
        var browser = vault.CreateSession(source: IPAddress.Parse("203.0.113.110"));
        var before = await AuditTrail.HeadAsync(vault);

        await SignInPasswordAsync(browser.Client, "it-profile-sign-in", password);
        var signedIn = await SubmitAuthenticatorCodeAsync(browser.Client, NextCode(account.TotpSecret!));

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal([AuditAction.LoginSucceeded], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
        Assert.NotEqual(account.LastTotpStep, (await LegacyProfile.AccountAsync(vault, "it-profile-sign-in")).LastTotpStep);
    }

    [Fact]
    public async Task A_spent_authenticator_code_counts_a_failure_without_a_profile_edit()
    {
        var password = NewPassword();
        await vault.SignInNewAccountAsync("it-profile-wrong-code", IPAddress.Parse("203.0.113.119"), password: password);
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-wrong-code");
        var browser = vault.CreateSession(source: IPAddress.Parse("203.0.113.120"));
        await SignInPasswordAsync(browser.Client, "it-profile-wrong-code", password);
        var before = await AuditTrail.HeadAsync(vault);

        await SubmitAuthenticatorCodeAsync(browser.Client, Totp.Code(account.TotpSecret!, account.LastTotpStep!.Value));

        Assert.Equal([AuditAction.TotpFailed], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
        Assert.Equal(1, (await LegacyProfile.AccountAsync(vault, "it-profile-wrong-code")).FailedAccessCount);
    }

    [Fact]
    public async Task One_authenticator_code_presented_by_two_sign_ins_at_once_opens_one_session()
    {
        var password = NewPassword();
        await vault.SignInNewAccountAsync("it-profile-replay", IPAddress.Parse("203.0.113.111"), password: password);
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-replay");
        var first = vault.CreateSession(source: IPAddress.Parse("203.0.113.112"));
        var second = vault.CreateSession(source: IPAddress.Parse("203.0.113.113"));
        await SignInPasswordAsync(first.Client, "it-profile-replay", password);
        await SignInPasswordAsync(second.Client, "it-profile-replay", password);
        var code = NextCode(account.TotpSecret!);
        var before = await AuditTrail.HeadAsync(vault);

        var outcomes = await Task.WhenAll(
            SubmitAuthenticatorCodeAsync(first.Client, code),
            SubmitAuthenticatorCodeAsync(second.Client, code));

        Assert.Single(outcomes, outcome => outcome.StatusCode == HttpStatusCode.Redirect);
        Assert.Single(outcomes, outcome => outcome.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, (await LegacyProfile.AccountAsync(vault, "it-profile-replay")).FailedAccessCount);
        Assert.Equal([AuditAction.LoginSucceeded, AuditAction.TotpFailed], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task An_administrator_authenticator_reset_records_its_own_event_and_clears_the_recovery_codes()
    {
        var owner = await vault.SignInNewAccountAsync("it-profile-admin-reset", IPAddress.Parse("203.0.113.114"));
        await LegacyProfile.GenerateRecoveryCodesAsync(owner.Client);
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-admin-reset");
        var admin = await vault.AdminClientAsync();
        var users = await admin.GetStringAsync("/Admin/Users");
        var before = await AuditTrail.HeadAsync(vault);

        await admin.PostAsync($"/Admin/ResetTotp/{account.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(users, "/Admin/Users"),
        }));

        Assert.Equal([AuditAction.UserTotpReset], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
        var stored = await LegacyProfile.AccountAsync(vault, "it-profile-admin-reset");
        Assert.False(stored.TotpEnabled);
        Assert.Null(stored.TotpSecret);
        Assert.Null(stored.LastTotpStep);
        Assert.Empty(await LegacyProfile.RecoveryCodesAsync(vault, account.Id));
    }

    [Fact]
    public async Task Break_glass_for_a_non_administrator_keeps_failures_and_records_no_sign_in()
    {
        var password = NewPassword();
        await vault.SignInNewAccountAsync("it-profile-denied-recovery", IPAddress.Parse("203.0.113.121"), password: password);
        var account = await LegacyProfile.AccountAsync(vault, "it-profile-denied-recovery");
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var stored = await db.Users.SingleAsync(user => user.Id == account.Id);
            stored.FailedAccessCount = 2;
            await db.SaveChangesAsync();
        }

        var browser = vault.CreateSession(source: IPAddress.Parse("203.0.113.122"));
        var page = await browser.Client.GetStringAsync("/Account/Recover");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await browser.Client.PostAsync("/Account/Recover", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, "/Account/Recover"),
            ["Username"] = account.Username,
            ["Password"] = password,
            ["Code"] = NextCode(account.TotpSecret!),
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await LegacyProfile.AccountAsync(vault, account.Username)).FailedAccessCount);
        Assert.Equal([AuditAction.LoginFailed], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    private static string NewPassword() => "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    // The step after the current one: the drift window accepts it, and enrollment spent the current one.
    private static string NextCode(string secret) => Totp.Code(secret, Totp.CurrentStep() + 1);

    private static async Task SignInPasswordAsync(HttpClient client, string username, string password)
    {
        var login = await client.GetStringAsync("/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(login, "/Account/Login"),
            ["Username"] = username,
            ["Password"] = password,
        }));
        Assert.Equal("/Account/Totp", response.Headers.Location?.OriginalString);
    }

    private static async Task<HttpResponseMessage> SubmitAuthenticatorCodeAsync(HttpClient client, string code)
    {
        var page = await client.GetStringAsync("/Account/Totp");
        return await client.PostAsync("/Account/Totp", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, "/Account/Totp"),
            ["Code"] = code,
        }));
    }
}
