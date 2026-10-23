using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class PasskeyLockoutTests(VaultFixture vault)
{
    [Fact]
    public async Task An_unverified_passkey_response_does_not_disclose_that_its_account_is_locked()
    {
        var owner = await vault.SignInNewAccountAsync("it-passkey-locked-error", IPAddress.Parse("203.0.113.184"));
        var account = await LegacyProfile.AccountAsync(vault, "it-passkey-locked-error");
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Locked account key");
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var stored = await db.Users.SingleAsync(user => user.Id == account.Id);
            stored.FailedAccessCount = 2;
            stored.LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(15);
            await db.SaveChangesAsync();
        }

        var browser = vault.CreateSession(source: IPAddress.Parse("203.0.113.185"));
        var page = await browser.Client.GetStringAsync("/Account/Login");
        var options = await PostSignInJsonAsync(browser.Client, "/Account/PasskeyLoginBegin", page, new { username = account.Username });
        var before = await AuditTrail.HeadAsync(vault);

        var result = await PostSignInJsonAsync(browser.Client, "/Account/PasskeyLoginComplete", page,
            new { credential = authenticator.AssertWithWrongSignature(options) });

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("This passkey could not be verified.", result.GetProperty("error").GetString());
        Assert.False(result.TryGetProperty("redirect", out _));
        Assert.Equal(2, (await LegacyProfile.AccountAsync(vault, account.Username)).FailedAccessCount);
        Assert.Equal(0, Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id)).SignCount);
        Assert.Equal([AuditAction.LoginFailed], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task An_unverified_passkey_response_does_not_disclose_that_its_account_is_disabled()
    {
        var owner = await vault.SignInNewAccountAsync("it-passkey-disabled-error", IPAddress.Parse("203.0.113.186"));
        var account = await LegacyProfile.AccountAsync(vault, "it-passkey-disabled-error");
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Disabled account key");
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var stored = await db.Users.SingleAsync(user => user.Id == account.Id);
            stored.FailedAccessCount = 2;
            stored.IsDisabled = true;
            await db.SaveChangesAsync();
        }

        var browser = vault.CreateSession(source: IPAddress.Parse("203.0.113.187"));
        var page = await browser.Client.GetStringAsync("/Account/Login");
        var options = await PostSignInJsonAsync(browser.Client, "/Account/PasskeyLoginBegin", page, new { username = account.Username });
        var before = await AuditTrail.HeadAsync(vault);

        var result = await PostSignInJsonAsync(browser.Client, "/Account/PasskeyLoginComplete", page,
            new { credential = authenticator.AssertWithWrongSignature(options) });

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("This passkey could not be verified.", result.GetProperty("error").GetString());
        Assert.False(result.TryGetProperty("redirect", out _));
        Assert.Equal(2, (await LegacyProfile.AccountAsync(vault, account.Username)).FailedAccessCount);
        Assert.Equal(0, Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id)).SignCount);
        Assert.Equal([AuditAction.LoginFailed], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task A_passkey_read_before_another_sign_in_advanced_its_counter_signs_in_without_counting_a_failure()
    {
        var owner = await vault.SignInNewAccountAsync("it-passkey-overlap", IPAddress.Parse("203.0.113.183"));
        var account = await LegacyProfile.AccountAsync(vault, "it-passkey-overlap");
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Overlapping key");
        var key = Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id));
        using var first = vault.Services.CreateScope();
        using var second = vault.Services.CreateScope();
        var firstPasskeys = first.ServiceProvider.GetRequiredService<IPasskeyService>();
        var secondPasskeys = second.ServiceProvider.GetRequiredService<IPasskeyService>();
        var firstCeremony = await firstPasskeys.BeginAssertionAsync(account.Username, default);
        var secondCeremony = await secondPasskeys.BeginAssertionAsync(account.Username, default);
        var firstResponse = JsonSerializer.Serialize(authenticator.Assert(JsonSerializer.Deserialize<JsonElement>(firstCeremony.OptionsJson)));
        var secondResponse = JsonSerializer.Serialize(authenticator.Assert(JsonSerializer.Deserialize<JsonElement>(secondCeremony.OptionsJson)));
        // The second scope now holds the credential as it was before the first sign-in. Its service
        // looks the credential up again, and the scope hands back this copy, so it starts from the
        // old counter exactly as a request that read it just before the first one committed.
        await second.ServiceProvider.GetRequiredService<IPasskeyCredentialRepository>().FindByCredentialIdAsync(key.CredentialId, default);
        var before = await AuditTrail.HeadAsync(vault);

        var firstOutcome = await firstPasskeys.CompleteAssertionAsync(firstCeremony.Challenge, firstResponse, default);
        var secondOutcome = await secondPasskeys.CompleteAssertionAsync(secondCeremony.Challenge, secondResponse, default);

        Assert.True(firstOutcome.Success);
        Assert.True(secondOutcome.Success);
        var stored = await LegacyProfile.AccountAsync(vault, account.Username);
        Assert.Equal(0, stored.FailedAccessCount);
        Assert.Null(stored.LockedUntilUtc);
        Assert.Equal(2, Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id)).SignCount);
        Assert.Equal([AuditAction.PasskeyLogin, AuditAction.PasskeyLogin], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task Incorrect_passkey_signatures_presented_concurrently_reach_account_lockout()
    {
        var owner = await vault.SignInNewAccountAsync("it-passkey-wrong-signature", IPAddress.Parse("203.0.113.180"));
        var account = await LegacyProfile.AccountAsync(vault, "it-passkey-wrong-signature");
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Known key");
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            (await db.Users.SingleAsync(user => user.Id == account.Id)).FailedAccessCount = 3;
            await db.SaveChangesAsync();
        }

        using var first = vault.Services.CreateScope();
        using var second = vault.Services.CreateScope();
        var firstPasskeys = first.ServiceProvider.GetRequiredService<IPasskeyService>();
        var secondPasskeys = second.ServiceProvider.GetRequiredService<IPasskeyService>();
        var firstCeremony = await firstPasskeys.BeginAssertionAsync(account.Username, default);
        var secondCeremony = await secondPasskeys.BeginAssertionAsync(account.Username, default);
        var firstResponse = JsonSerializer.Serialize(authenticator.AssertWithWrongSignature(JsonSerializer.Deserialize<JsonElement>(firstCeremony.OptionsJson)));
        var secondResponse = JsonSerializer.Serialize(authenticator.AssertWithWrongSignature(JsonSerializer.Deserialize<JsonElement>(secondCeremony.OptionsJson)));
        var before = await AuditTrail.HeadAsync(vault);

        var outcomes = await Task.WhenAll(
            firstPasskeys.CompleteAssertionAsync(firstCeremony.Challenge, firstResponse, default),
            secondPasskeys.CompleteAssertionAsync(secondCeremony.Challenge, secondResponse, default));

        Assert.All(outcomes, outcome => Assert.False(outcome.Success));
        var stored = await LegacyProfile.AccountAsync(vault, account.Username);
        Assert.Equal(0, stored.FailedAccessCount);
        Assert.NotNull(stored.LockedUntilUtc);
        Assert.Equal(0, Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id)).SignCount);
        Assert.Equal([AuditAction.LoginFailed, AuditAction.LoginFailed, AuditAction.AccountLockedOut],
            await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task A_nonadvancing_passkey_counter_counts_a_failure_and_locks_at_the_threshold()
    {
        var owner = await vault.SignInNewAccountAsync("it-passkey-clone", IPAddress.Parse("203.0.113.181"));
        var account = await LegacyProfile.AccountAsync(vault, "it-passkey-clone");
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Clone check key");
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            (await db.Users.SingleAsync(user => user.Id == account.Id)).FailedAccessCount = 4;
            (await db.PasskeyCredentials.SingleAsync(key => key.UserId == account.Id)).SignCount = 2;
            await db.SaveChangesAsync();
        }

        using var request = vault.Services.CreateScope();
        var passkeys = request.ServiceProvider.GetRequiredService<IPasskeyService>();
        var ceremony = await passkeys.BeginAssertionAsync(account.Username, default);
        var response = JsonSerializer.Serialize(authenticator.Assert(JsonSerializer.Deserialize<JsonElement>(ceremony.OptionsJson)));
        var before = await AuditTrail.HeadAsync(vault);

        var outcome = await passkeys.CompleteAssertionAsync(ceremony.Challenge, response, default);

        Assert.False(outcome.Success);
        Assert.NotNull((await LegacyProfile.AccountAsync(vault, account.Username)).LockedUntilUtc);
        Assert.Equal(2, Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id)).SignCount);
        Assert.Equal([AuditAction.LoginFailed, AuditAction.AccountLockedOut], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task A_successful_passkey_assertion_clears_failures_with_only_its_sign_in_event()
    {
        var owner = await vault.SignInNewAccountAsync("it-passkey-clear-failures", IPAddress.Parse("203.0.113.182"));
        var account = await LegacyProfile.AccountAsync(vault, "it-passkey-clear-failures");
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Successful key");
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            (await db.Users.SingleAsync(user => user.Id == account.Id)).FailedAccessCount = 2;
            await db.SaveChangesAsync();
        }

        using var request = vault.Services.CreateScope();
        var passkeys = request.ServiceProvider.GetRequiredService<IPasskeyService>();
        var ceremony = await passkeys.BeginAssertionAsync(account.Username, default);
        var response = JsonSerializer.Serialize(authenticator.Assert(JsonSerializer.Deserialize<JsonElement>(ceremony.OptionsJson)));
        var before = await AuditTrail.HeadAsync(vault);

        var outcome = await passkeys.CompleteAssertionAsync(ceremony.Challenge, response, default);

        Assert.True(outcome.Success);
        Assert.Equal(0, (await LegacyProfile.AccountAsync(vault, account.Username)).FailedAccessCount);
        Assert.Equal(1, Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id)).SignCount);
        Assert.Equal([AuditAction.PasskeyLogin], await LegacyProfile.EventsAfterAsync(vault, before, account.Id));
    }

    private static async Task<JsonElement> PostSignInJsonAsync(HttpClient client, string path, string page, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", VaultFixture.FormTokenOf(page, "/Account/Login"));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
