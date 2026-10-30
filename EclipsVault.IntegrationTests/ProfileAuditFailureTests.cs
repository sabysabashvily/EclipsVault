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
    public async Task A_refused_audit_event_leaves_the_profile_details_unchanged()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-failure-details", IPAddress.Parse("203.0.113.150"));
        var account = await ProfileHttp.AccountAsync(vault, "it-failure-details");
        var generation = (await ProfileHttp.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await ProfileHttp.UpdateDisplayNameAsync(owner, "Unaudited Name");

            await ProfileApi.AssertProblemAsync(response, 503, "audit_unavailable");
            var stored = await ProfileHttp.AccountAsync(vault, "it-failure-details");
            Assert.Equal(account.DisplayName, stored.DisplayName);
            Assert.Equal(account.Email, stored.Email);
            Assert.Equal(generation, (await ProfileHttp.AccountAsync(vault, account.Username)).CredentialGeneration);
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
        var account = await ProfileHttp.AccountAsync(vault, "it-failure-avatar-upload");
        var generation = (await ProfileHttp.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await ProfileHttp.UploadAvatarAsync(owner, ProfileHttp.TinyPng);

            await ProfileApi.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.False(await ProfileHttp.HasAvatarAsync(vault, account.Id));
            Assert.Null((await ProfileHttp.AccountAsync(vault, "it-failure-avatar-upload")).AvatarUpdatedAtUtc);
            Assert.Equal(generation, (await ProfileHttp.AccountAsync(vault, account.Username)).CredentialGeneration);
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
        await ProfileHttp.UploadAvatarAsync(owner, ProfileHttp.TinyPng);
        var account = await ProfileHttp.AccountAsync(vault, "it-failure-avatar-removal");
        var generation = (await ProfileHttp.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await ProfileHttp.RemoveAvatarAsync(owner);

            await ProfileApi.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.True(await ProfileHttp.HasAvatarAsync(vault, account.Id));
            Assert.Equal(account.AvatarUpdatedAtUtc, (await ProfileHttp.AccountAsync(vault, "it-failure-avatar-removal")).AvatarUpdatedAtUtc);
            Assert.Equal(generation, (await ProfileHttp.AccountAsync(vault, account.Username)).CredentialGeneration);
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
        await ProfileHttp.GenerateRecoveryCodesAsync(owner);
        var account = await ProfileHttp.AccountAsync(vault, "it-failure-admin-reset");
        var codes = await ProfileHttp.RecoveryCodesAsync(vault, account.Id);
        var admin = await vault.AdminClientAsync();
        var users = await admin.GetStringAsync("/Admin/Users");
        var generation = (await ProfileHttp.AccountAsync(vault, account.Username)).CredentialGeneration;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await admin.PostAsync($"/Admin/ResetTotp/{account.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(users, "/Admin/Users"),
            }));

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            var stored = await ProfileHttp.AccountAsync(vault, "it-failure-admin-reset");
            Assert.Equal(account.TotpSecret, stored.TotpSecret);
            Assert.True(stored.TotpEnabled);
            Assert.Equal(account.LastTotpStep, stored.LastTotpStep);
            Assert.Equal(codes.Select(code => code.Id), (await ProfileHttp.RecoveryCodesAsync(vault, account.Id)).Select(code => code.Id));
            Assert.Equal(generation, (await ProfileHttp.AccountAsync(vault, account.Username)).CredentialGeneration);
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
        var account = await ProfileHttp.AccountAsync(vault, "it-failure-rescue");
        using var request = vault.Services.CreateScope();
        var profile = request.ServiceProvider.GetRequiredService<IProfileService>();

        await failure.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(
                () => profile.UpdateDisplayNameAsync(account.Id, "Rescued Name", CancellationToken.None));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await request.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SaveChangesAsync();

        Assert.Equal(account.DisplayName, (await ProfileHttp.AccountAsync(vault, "it-failure-rescue")).DisplayName);
        Assert.Equal(account.Email, (await ProfileHttp.AccountAsync(vault, "it-failure-rescue")).Email);
        Assert.Equal(account.CredentialGeneration, (await ProfileHttp.AccountAsync(vault, "it-failure-rescue")).CredentialGeneration);
    }
}
