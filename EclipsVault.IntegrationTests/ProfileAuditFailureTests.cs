using System.Net;
using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A Profile change whose audit event the database refuses is not saved at all: every row it would
/// have changed is as it was, and nothing it would have handed out reaches the browser.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class ProfileAuditFailureTests(AuditFailureFixture failure)
{
    private const string AuditUnavailablePage = "/Home/Error?code=503";

    [Fact]
    public async Task Rejected_sign_out_auditing_keeps_every_device_revoked_and_preserves_the_warning_on_sign_in()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-failure-sign-out", IPAddress.Parse("192.0.2.86"));
        var copied = vault.CreateSession(owner.SessionCookie, IPAddress.Parse("192.0.2.87"));
        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyProfile.SignOutEverywhereAsync(owner.Client);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("revoked_unrecorded", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Null(owner.SessionCookie);
            Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.Contains("couldn't record it in the audit trail", WebUtility.HtmlDecode(await owner.Client.GetStringAsync("/Account/Login")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_audit_event_leaves_the_profile_details_unchanged()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-failure-details", IPAddress.Parse("203.0.113.150"));
        var account = await LegacyProfile.AccountAsync(vault, "it-failure-details");
        var generation = (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyProfile.UpdateDetailsAsync(owner.Client, "Unaudited Name", "unaudited@example.test");

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            var stored = await LegacyProfile.AccountAsync(vault, "it-failure-details");
            Assert.Equal(account.DisplayName, stored.DisplayName);
            Assert.Equal(account.Email, stored.Email);
            Assert.Equal(generation, (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_refused_audit_event_leaves_the_password_unchanged()
    {
        var vault = failure.Vault;
        var password = "It-failure-password-" + Guid.NewGuid().ToString("N");
        var owner = await vault.SignInNewAccountAsync("it-failure-password", IPAddress.Parse("203.0.113.151"), password: password);
        var account = await LegacyProfile.AccountAsync(vault, "it-failure-password");
        var generation = (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyProfile.ChangePasswordAsync(owner.Client, password, "It-unaudited-new-password-" + Guid.NewGuid().ToString("N"));

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Equal(account.PasswordHash, (await LegacyProfile.AccountAsync(vault, "it-failure-password")).PasswordHash);
            Assert.Equal(generation, (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_refused_audit_event_stores_no_uploaded_picture()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-failure-avatar-upload", IPAddress.Parse("203.0.113.152"));
        var account = await LegacyProfile.AccountAsync(vault, "it-failure-avatar-upload");
        var generation = (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyProfile.UploadAvatarAsync(owner.Client, LegacyProfile.TinyPng);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.False(await LegacyProfile.HasAvatarAsync(vault, account.Id));
            Assert.Null((await LegacyProfile.AccountAsync(vault, "it-failure-avatar-upload")).AvatarUpdatedAtUtc);
            Assert.Equal(generation, (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_refused_audit_event_keeps_the_picture_it_would_have_removed()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-failure-avatar-removal", IPAddress.Parse("203.0.113.153"));
        await LegacyProfile.UploadAvatarAsync(owner.Client, LegacyProfile.TinyPng);
        var account = await LegacyProfile.AccountAsync(vault, "it-failure-avatar-removal");
        var generation = (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyProfile.RemoveAvatarAsync(owner.Client);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.True(await LegacyProfile.HasAvatarAsync(vault, account.Id));
            Assert.Equal(account.AvatarUpdatedAtUtc, (await LegacyProfile.AccountAsync(vault, "it-failure-avatar-removal")).AvatarUpdatedAtUtc);
            Assert.Equal(generation, (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_refused_audit_event_keeps_the_authenticator_its_replay_marker_and_recovery_codes()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-failure-reset", IPAddress.Parse("203.0.113.154"));
        await LegacyProfile.GenerateRecoveryCodesAsync(owner.Client);
        var account = await LegacyProfile.AccountAsync(vault, "it-failure-reset");
        var codes = await LegacyProfile.RecoveryCodesAsync(vault, account.Id);
        var generation = (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyProfile.ResetAuthenticatorAsync(owner.Client);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            var stored = await LegacyProfile.AccountAsync(vault, "it-failure-reset");
            Assert.Equal(account.TotpSecret, stored.TotpSecret);
            Assert.True(stored.TotpEnabled);
            Assert.Equal(account.LastTotpStep, stored.LastTotpStep);
            Assert.Equal(codes.Select(code => code.Id), (await LegacyProfile.RecoveryCodesAsync(vault, account.Id)).Select(code => code.Id));
            Assert.Equal(generation, (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_refused_audit_event_keeps_the_old_recovery_codes_and_shows_no_new_ones()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-failure-recovery", IPAddress.Parse("203.0.113.155"));
        await LegacyProfile.GenerateRecoveryCodesAsync(owner.Client);
        await owner.Client.GetStringAsync("/Profile/RecoveryCodes");
        var account = await LegacyProfile.AccountAsync(vault, "it-failure-recovery");
        var codes = await LegacyProfile.RecoveryCodesAsync(vault, account.Id);
        var generation = (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyProfile.GenerateRecoveryCodesAsync(owner.Client);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Equal(codes.Select(code => code.Id), (await LegacyProfile.RecoveryCodesAsync(vault, account.Id)).Select(code => code.Id));
            Assert.Equal(generation, (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        var display = await owner.Client.GetAsync("/Profile/RecoveryCodes");
        Assert.Equal("/Profile", display.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_refused_audit_event_stores_no_new_passkey()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-failure-passkey-add", IPAddress.Parse("203.0.113.156"));
        var account = await LegacyProfile.AccountAsync(vault, "it-failure-passkey-add");
        using var authenticator = new PasskeyAuthenticator();
        var generation = (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Unaudited key");

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Empty(await LegacyProfile.PasskeysAsync(vault, account.Id));
            Assert.Equal(generation, (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_refused_audit_event_keeps_the_passkey_it_would_have_removed()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-failure-passkey-removal", IPAddress.Parse("203.0.113.157"));
        var account = await LegacyProfile.AccountAsync(vault, "it-failure-passkey-removal");
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Kept key");
        var passkey = Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id));
        var generation = (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyProfile.RemovePasskeyAsync(owner.Client, passkey.Id);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Equal(passkey.Id, Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id)).Id);
            Assert.Equal(generation, (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_refused_administrator_reset_keeps_the_authenticator_marker_and_codes()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-failure-admin-reset", IPAddress.Parse("203.0.113.159"));
        await LegacyProfile.GenerateRecoveryCodesAsync(owner.Client);
        var account = await LegacyProfile.AccountAsync(vault, "it-failure-admin-reset");
        var codes = await LegacyProfile.RecoveryCodesAsync(vault, account.Id);
        var admin = await vault.AdminClientAsync();
        var users = await admin.GetStringAsync("/Admin/Users");
        var generation = (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await admin.PostAsync($"/Admin/ResetTotp/{account.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(users, "/Admin/Users"),
            }));

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            var stored = await LegacyProfile.AccountAsync(vault, "it-failure-admin-reset");
            Assert.Equal(account.TotpSecret, stored.TotpSecret);
            Assert.True(stored.TotpEnabled);
            Assert.Equal(account.LastTotpStep, stored.LastTotpStep);
            Assert.Equal(codes.Select(code => code.Id), (await LegacyProfile.RecoveryCodesAsync(vault, account.Id)).Select(code => code.Id));
            Assert.Equal(generation, (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_change_refused_for_its_audit_event_is_not_saved_by_a_later_save_in_the_same_request()
    {
        var vault = failure.Vault;
        await vault.SignInNewAccountAsync("it-failure-rescue", IPAddress.Parse("203.0.113.158"));
        var account = await LegacyProfile.AccountAsync(vault, "it-failure-rescue");
        using var request = vault.Services.CreateScope();
        var profile = request.ServiceProvider.GetRequiredService<IProfileService>();

        await failure.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(
                () => profile.UpdateAsync(account.Id, account.CredentialGeneration, "Rescued Name", "rescue-rejected@example.test", CancellationToken.None));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await request.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SaveChangesAsync();

        Assert.Equal(account.DisplayName, (await LegacyProfile.AccountAsync(vault, "it-failure-rescue")).DisplayName);
        Assert.Equal(account.Email, (await LegacyProfile.AccountAsync(vault, "it-failure-rescue")).Email);
        Assert.Equal(account.CredentialGeneration, (await LegacyProfile.AccountAsync(vault, "it-failure-rescue")).CredentialGeneration);
    }
}
