using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class PreparedProfileTests(VaultFixture vault)
{
    [Fact]
    public async Task Preparing_freezes_only_a_password_hash_without_auditing_or_verification()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-prepared-frozen", "100.65.87.1");
        var before = await AuditTrail.HeadAsync(vault);
        var response = await ProfileApi.PrepareAsync(owner.Browser, new { kind = "ChangePassword", newPassword = "New-Prepared-Password-87!" });
        var prepared = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["expiresAtUtc", "kind", "methods", "operationId"], ProfileApi.NamesOf(prepared));
        Assert.Equal("ChangePassword", prepared.GetProperty("kind").GetString());
        var operation = await ProfileApi.OperationAsync(vault, prepared.GetProperty("operationId").GetGuid());
        Assert.NotNull(operation.CandidatePasswordHash);
        Assert.NotNull(operation.CandidatePasswordSalt);
        Assert.Equal(TimeSpan.FromMinutes(10), operation.ExpiresAtUtc - operation.CreatedAtUtc);
        Assert.Equal(owner.Stored.CredentialGeneration, operation.CredentialGeneration);
        Assert.Null(operation.VerifiedAtUtc);
        Assert.Empty(await LegacyProfile.EventsAfterAsync(vault, before, owner.Id));

    }

    [Fact]
    public async Task Preparing_another_change_cancels_the_sessions_previous_operation()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-prepared-replacement", "100.65.87.12");
        var first = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });

        var replacement = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "AddPasskey", passkeyNickname = "Replacement" });

        Assert.NotEqual(first, replacement);
        await ProfileApi.AssertProblemAsync(await ProfileApi.VerifyAsync(owner.Browser, first,
            "PasswordAndAuthenticatorCode", owner.Password, Totp.NextCode(owner.TotpSecret)), 404, "operation_not_found");
    }

    [Theory]
    [InlineData("{\"kind\":\"ChangePassword\",\"newPassword\":\"short\"}")]
    [InlineData("{\"kind\":\"ResetAuthenticator\",\"newPassword\":\"unrelated\"}")]
    [InlineData("{\"kind\":\"AddPasskey\",\"passkeyId\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("{\"kind\":\"RemovePasskey\"}")]
    [InlineData("{\"kind\":\"1\"}")]
    [InlineData("{\"kind\":\"ResetAuthenticator\",\"userId\":\"11111111-1111-1111-1111-111111111111\"}")]
    public async Task Invalid_preparation_does_not_replace_the_existing_operation(string json)
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-prepare-invalid-" + Guid.NewGuid().ToString("N")[..8], "100.65.88." + Interlocked.Increment(ref _invalidAddress));
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });

        await ProfileApi.AssertProblemAsync(await ProfileApi.PrepareAsync(owner.Browser, JsonSerializer.Deserialize<JsonElement>(json)), 400, "validation_failed");

        Assert.Equal(operation, (await ProfileApi.OperationAsync(vault, operation)).Id);
    }

    private static int _invalidAddress;

    [Fact]
    public async Task Another_account_or_another_session_cannot_cancel_or_prove_an_owned_operation()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-prepared-owner", "100.65.87.2");
        var other = await ProfileAccount.CreateAsync(vault, "it-ui87-prepared-other", "100.65.87.3");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var second = await ProfileApi.SecondSessionAsync(vault, owner, "100.65.87.4");

        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(other.Browser, HttpMethod.Delete,
            $"/api/ui/profile/operations/{operation}"), 404, "operation_not_found");
        await ProfileApi.AssertProblemAsync(await ProfileApi.VerifyAsync(second, operation,
            "PasswordAndAuthenticatorCode", owner.Password, Totp.NextCode(owner.TotpSecret)), 404, "operation_not_found");
        Assert.Null((await ProfileApi.OperationAsync(vault, operation)).VerifiedAtUtc);
    }

    [Fact]
    public async Task An_operation_id_alone_cannot_commit_a_password_or_authorize_another_action()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-prepared-no-proof", "100.65.87.5");
        var operationId = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ChangePassword", newPassword = "New-Different-Password-87!" });

        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/password", new { operationId }), 409, "verification_required");
        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/authenticator/reset", new { operationId }), 409, "operation_changed");
        Assert.Equal(owner.Stored.PasswordHash, (await LegacyProfile.AccountAsync(vault, owner.Stored.Username)).PasswordHash);
    }

    [Fact]
    public async Task Expired_preparation_is_refused_without_spending_the_authenticator_code()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-prepared-expired", "100.65.87.6");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        await ProfileApi.ExpireAsync(vault, operation);
        var code = Totp.NextCode(owner.TotpSecret);
        var before = await AuditTrail.HeadAsync(vault);

        await ProfileApi.AssertProblemAsync(await ProfileApi.VerifyAsync(owner.Browser, operation,
            "PasswordAndAuthenticatorCode", owner.Password, code), 409, "operation_expired");

        Assert.Equal(owner.Stored.LastTotpStep, (await LegacyProfile.AccountAsync(vault, owner.Stored.Username)).LastTotpStep);
        Assert.Empty(await LegacyProfile.EventsAfterAsync(vault, before, owner.Id));
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, owner.Id, $"/api/ui/profile/operations/{operation}/verify", owner.Password, code);
    }

    [Fact]
    public async Task Password_commit_rotates_the_session_and_the_grant_cannot_be_replayed()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-prepared-password", "100.65.87.7");
        var old = vault.CreateSession(owner.Browser.SessionCookie, IPAddress.Parse("100.65.87.8"));
        var before = await AuditTrail.HeadAsync(vault);
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "ChangePassword", newPassword = "Prepared-New-Password-87!" });
        var verified = await ProfileApi.OperationAsync(vault, operationId);

        var response = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/password", new { operationId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.NotEqual(old.SessionCookie!.Value, owner.Browser.SessionCookie!.Value);
        Assert.Equal(owner.Stored.CredentialGeneration + 1, (await LegacyProfile.AccountAsync(vault, owner.Stored.Username)).CredentialGeneration);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.NotNull(verified.VerifiedAtUtc);
        Assert.Equal([AuditAction.AccountVerified, AuditAction.PasswordChanged], await LegacyProfile.EventsAfterAsync(vault, before, owner.Id));
        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/password", new { operationId }), 404, "operation_not_found");
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, owner.Id, "/api/ui/profile/password", owner.Password, "Prepared-New-Password-87!");
    }

    [Fact]
    public async Task A_grant_older_than_five_minutes_cannot_reset_the_authenticator()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-prepared-grant-expired", "100.65.87.9");
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "ResetAuthenticator" });
        await ProfileApi.ExpireGrantAsync(vault, operationId);

        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/authenticator/reset", new { operationId }), 409, "verification_expired");

        Assert.True((await LegacyProfile.AccountAsync(vault, owner.Stored.Username)).TotpEnabled);
    }

    [Fact]
    public async Task Resetting_the_authenticator_ends_all_sessions_and_retires_the_recovery_set()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-prepared-reset", "100.65.87.10");
        await LegacyProfile.GenerateRecoveryCodesAsync(owner.Browser.Client);
        // Legacy generation rotates this browser; its Profile operation still starts under the new session.
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "ResetAuthenticator" });
        var copied = vault.CreateSession(owner.Browser.SessionCookie, IPAddress.Parse("100.65.87.11"));
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/authenticator/reset", new { operationId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(owner.Browser.SessionCookie);
        var account = await LegacyProfile.AccountAsync(vault, owner.Stored.Username);
        Assert.False(account.TotpEnabled);
        Assert.Null(account.TotpSecret);
        Assert.Null(account.LastTotpStep);
        Assert.Empty(await LegacyProfile.RecoveryCodesAsync(vault, owner.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal([AuditAction.SelfMfaReset], await LegacyProfile.EventsAfterAsync(vault, before, owner.Id));
        await ProfileApi.AssertNoticeAsync(vault, owner, "ResetAuthenticator");
    }

    [Fact]
    public async Task Sign_in_explains_an_authenticator_reset_once()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui90-reset-notice", "100.65.90.1");
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "ResetAuthenticator" });
        await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/authenticator/reset", new { operationId });

        var signIn = await owner.Browser.Client.GetStringAsync("/Account/Login");
        var signInAgain = await owner.Browser.Client.GetStringAsync("/Account/Login");

        Assert.Contains("Your authenticator was reset. Sign in to set up a new one.", signIn, StringComparison.Ordinal);
        Assert.DoesNotContain("Your authenticator was reset.", signInAgain, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sign_in_link_cannot_claim_an_authenticator_reset()
    {
        using var client = vault.CreateClient();

        var signIn = await client.GetStringAsync("/Account/Login?signOut=authenticator_reset");

        Assert.DoesNotContain("Your authenticator was reset.", signIn, StringComparison.Ordinal);
    }
}
