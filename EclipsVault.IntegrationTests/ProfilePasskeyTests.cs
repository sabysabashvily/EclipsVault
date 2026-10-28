using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class ProfilePasskeyTests(VaultFixture vault)
{
    [Fact]
    public async Task An_existing_passkey_verifies_registration_of_a_different_key_with_a_frozen_name()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-key-existing-proof", "100.69.87.1");
        using var existing = new PasskeyAuthenticator();
        using var added = new PasskeyAuthenticator();
        await ProfileApi.AddPasskeyAsync(owner, existing, "Existing");
        var operationId = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "AddPasskey", passkeyNickname = "  Second key  " });
        var proof = await ProfileApi.PasskeyPromptAsync(owner.Browser, $"/api/ui/profile/operations/{operationId}/passkey/begin");
        var credential = existing.Assert(proof);
        var before = await AuditTrail.HeadAsync(vault);
        Assert.Equal(HttpStatusCode.OK, (await ProfileApi.CompletePasskeyAsync(owner.Browser, operationId, credential)).StatusCode);
        var prompt = await ProfileApi.PasskeyPromptAsync(owner.Browser, "/api/ui/profile/passkeys/registration/begin", new { operationId });

        var response = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/passkeys/registration/complete",
            new { operationId, credential = added.Register(prompt) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var registered = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["createdAtUtc", "id", "nickname"], ProfileApi.NamesOf(registered));
        Assert.Equal("Second key", registered.GetProperty("nickname").GetString());
        Assert.Equal(2, (await LegacyProfile.PasskeysAsync(vault, owner.Id)).Length);
        Assert.Equal([AuditAction.AccountVerified, AuditAction.PasskeyRegistered], await LegacyProfile.EventsAfterAsync(vault, before, owner.Id));
        await ProfileApi.AssertNoticeAsync(vault, owner, "AddPasskey", expectedCount: 2);
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, owner.Id, $"/api/ui/profile/operations/{operationId}/passkey/complete",
            JsonSerializer.SerializeToElement(credential).GetProperty("signature").GetString()!);
    }

    [Fact]
    public async Task Removing_the_last_key_with_proven_authenticator_fallback_rotates_the_session()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-key-delete", "100.69.87.2");
        using var key = new PasskeyAuthenticator();
        // Legacy registration leaves the next authenticator step available to verify the deletion.
        Assert.Equal(HttpStatusCode.OK, (await LegacyProfile.RegisterPasskeyAsync(owner.Browser.Client, key, "Remove")).StatusCode);
        var id = Assert.Single(await LegacyProfile.PasskeysAsync(vault, owner.Id)).Id;
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "RemovePasskey", passkeyId = id });
        var original = owner.Browser.SessionCookie!.Value;
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Delete, $"/api/ui/profile/passkeys/{id}", new { operationId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await LegacyProfile.PasskeysAsync(vault, owner.Id));
        Assert.NotEqual(original, owner.Browser.SessionCookie!.Value);
        Assert.Equal([AuditAction.PasskeyRemoved], await LegacyProfile.EventsAfterAsync(vault, before, owner.Id));
        await ProfileApi.AssertNoticeAsync(vault, owner, "RemovePasskey");
    }

    [Fact]
    public async Task A_registration_prompt_older_than_two_minutes_cannot_add_a_key()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-key-registration-expired", "100.69.87.3");
        using var key = new PasskeyAuthenticator();
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "AddPasskey", passkeyNickname = "Expired" });
        var prompt = await ProfileApi.PasskeyPromptAsync(owner.Browser, "/api/ui/profile/passkeys/registration/begin", new { operationId });
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().ProfileOperations.Where(o => o.Id == operationId)
            .ExecuteUpdateAsync(set => set.SetProperty(o => o.ChallengeExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));

        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/passkeys/registration/complete", new { operationId, credential = key.Register(prompt) }), 409, "ceremony_expired");

        Assert.Empty(await LegacyProfile.PasskeysAsync(vault, owner.Id));
    }

    [Fact]
    public async Task A_valid_removal_grant_answers_the_same_404_for_unknown_and_foreign_keys()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-key-target-owner", "100.69.87.4");
        var other = await ProfileAccount.CreateAsync(vault, "it-ui87-key-target-other", "100.69.87.5");
        using var key = new PasskeyAuthenticator();
        using var foreign = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Browser.Client, key, "Owner");
        await LegacyProfile.RegisterPasskeyAsync(other.Browser.Client, foreign, "Other");
        var id = Assert.Single(await LegacyProfile.PasskeysAsync(vault, owner.Id)).Id;
        var foreignId = Assert.Single(await LegacyProfile.PasskeysAsync(vault, other.Id)).Id;
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "RemovePasskey", passkeyId = id });

        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Delete,
            $"/api/ui/profile/passkeys/{Guid.NewGuid()}", new { operationId }), 404, "passkey_not_found");
        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Delete,
            $"/api/ui/profile/passkeys/{foreignId}", new { operationId }), 404, "passkey_not_found");

        Assert.Equal(id, Assert.Single(await LegacyProfile.PasskeysAsync(vault, owner.Id)).Id);
        Assert.Equal(foreignId, Assert.Single(await LegacyProfile.PasskeysAsync(vault, other.Id)).Id);
    }

    [Theory]
    [InlineData("ResetAuthenticator")]
    [InlineData("ReplaceRecoveryCodes")]
    public async Task Without_an_enrolled_authenticator_reset_and_recovery_generation_are_unavailable(string kind)
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-no-totp-" + kind, "100.69.88." + Interlocked.Increment(ref _address));
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Users.Where(u => u.Id == owner.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.TotpEnabled, false));

        await ProfileApi.AssertProblemAsync(await ProfileApi.PrepareAsync(owner.Browser, new { kind }), 409, "authenticator_not_enrolled");
    }

    private static int _address;
}
