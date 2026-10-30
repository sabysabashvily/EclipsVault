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
public sealed class ProfileVerificationTests(VaultFixture vault)
{
    [Fact]
    public async Task Wrong_password_counts_a_failure_without_spending_the_correct_authenticator_code()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-wrong-password", "100.66.87.1");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var code = Totp.NextCode(owner.TotpSecret);
        var before = await AuditTrail.HeadAsync(vault);

        await ProfileApi.AssertProblemAsync(await ProfileApi.VerifyAsync(owner.Browser, operation,
            "PasswordAndAuthenticatorCode", "wrong-current-87!", code), 400, "verification_failed");

        var failed = await ProfileHttp.AccountAsync(vault, owner.Stored.Username);
        Assert.Equal(1, failed.FailedAccessCount);
        Assert.Equal(owner.Stored.LastTotpStep, failed.LastTotpStep);
        Assert.Equal(HttpStatusCode.OK, (await owner.Browser.Client.GetAsync("/api/ui/me")).StatusCode);
        var verified = await ProfileApi.VerifyAsync(owner.Browser, operation, "PasswordAndAuthenticatorCode", owner.Password, code);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal(["commitByUtc", "operationId"], ProfileApi.NamesOf(await verified.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Equal(0, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).FailedAccessCount);
        Assert.Equal([AuditAction.AccountVerificationFailed, AuditAction.AccountVerified], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, owner.Id, $"/api/ui/profile/operations/{operation}/verify", owner.Password, code, "wrong-current-87!");
    }

    [Fact]
    public async Task Two_simultaneous_proofs_create_only_one_grant_and_one_verification_event()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-concurrent", "100.66.87.2");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var code = Totp.NextCode(owner.TotpSecret);
        var before = await AuditTrail.HeadAsync(vault);

        var results = await Task.WhenAll(
            ProfileApi.VerifyAsync(owner.Browser, operation, "PasswordAndAuthenticatorCode", owner.Password, code),
            ProfileApi.VerifyAsync(owner.Browser, operation, "PasswordAndAuthenticatorCode", owner.Password, code));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Select(response => response.StatusCode).OrderBy(status => status));
        Assert.Equal([AuditAction.AccountVerified], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
    }

    [Fact]
    public async Task Five_wrong_proofs_lock_the_account_even_with_a_valid_sixth_proof()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-lockout", "100.66.87.3");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var code = Totp.NextCode(owner.TotpSecret);
        await ProfileApi.VerifyAsync(owner.Browser, operation, "PasswordAndAuthenticatorCode", "wrong-password", code);
        await ProfileApi.VerifyAsync(owner.Browser, operation, "PasswordAndAuthenticatorCode", "wrong-password", code);
        await ProfileApi.VerifyAsync(owner.Browser, operation, "PasswordAndAuthenticatorCode", "wrong-password", code);
        await ProfileApi.VerifyAsync(owner.Browser, operation, "PasswordAndAuthenticatorCode", "wrong-password", code);
        await ProfileApi.VerifyAsync(owner.Browser, operation, "PasswordAndAuthenticatorCode", "wrong-password", code);

        await ProfileApi.AssertProblemAsync(await ProfileApi.VerifyAsync(owner.Browser, operation,
            "PasswordAndAuthenticatorCode", owner.Password, code), 429, "throttled");

        var stored = await ProfileHttp.AccountAsync(vault, owner.Stored.Username);
        Assert.Equal(0, stored.FailedAccessCount);
        Assert.True(stored.LockedUntilUtc > DateTimeOffset.UtcNow);
        Assert.Null((await ProfileApi.OperationAsync(vault, operation)).VerifiedAtUtc);
    }

    [Fact]
    public async Task A_valid_passkey_with_the_wrong_required_password_does_not_forgive_failures()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-key-password", "100.66.87.4");
        using var key = new PasskeyAuthenticator();
        await ProfileApi.AddPasskeyAsync(owner, key, "Proof key");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ChangePassword", newPassword = "Different-Passkey-Password-87!" });
        await ProfileApi.VerifyAsync(owner.Browser, operation, "PasswordAndAuthenticatorCode", "wrong-password", "000000");
        var options = await ProfileApi.PasskeyPromptAsync(owner.Browser, $"/api/ui/profile/operations/{operation}/passkey/begin");
        var credential = key.Assert(options);
        var before = await AuditTrail.HeadAsync(vault);

        await ProfileApi.AssertProblemAsync(await ProfileApi.CompletePasskeyAsync(owner.Browser, operation, credential, "wrong-required-password-87!"), 400, "verification_failed");

        Assert.Equal(2, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).FailedAccessCount);
        Assert.Null((await ProfileApi.OperationAsync(vault, operation)).VerifiedAtUtc);
        Assert.Equal([AuditAction.AccountVerificationFailed], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, owner.Id, $"/api/ui/profile/operations/{operation}/passkey/complete",
            ProofDisclosure.CredentialComponents(credential).Append(owner.Password).Append("wrong-required-password-87!").ToArray());
    }

    [Fact]
    public async Task A_wrong_password_with_an_unchecked_assertion_records_only_the_password_failure()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-truthful-failure", "100.66.87.11");
        using var key = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner.Browser, key, "Existing key");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ChangePassword", newPassword = "Truthful-New-Password-87!" });
        await ProfileApi.PasskeyPromptAsync(owner.Browser, $"/api/ui/profile/operations/{operation}/passkey/begin");
        var before = await AuditTrail.HeadAsync(vault);

        await ProfileApi.AssertProblemAsync(await ProfileApi.CompletePasskeyAsync(owner.Browser, operation,
            new { id = "unchecked-assertion" }, "wrong-password"), 400, "verification_failed");

        var failure = Assert.Single((await AuditTrail.RowsAfterAsync(vault, before)), row => row.ResourceId == owner.Id);
        Assert.Equal(AuditAction.AccountVerificationFailed, failure.Action);
        Assert.Equal("Account verification failed: wrong password", failure.Details);
        Assert.Equal(1, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).FailedAccessCount);
    }

    [Fact]
    public async Task Another_accounts_signed_passkey_response_cannot_verify_the_session_account()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-key-owner", "100.66.87.5");
        var other = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-key-other", "100.66.87.6");
        using var ownerKey = new PasskeyAuthenticator();
        using var foreignKey = new PasskeyAuthenticator();
        await ProfileApi.AddPasskeyAsync(owner, ownerKey, "Owner");
        await ProfileApi.AddPasskeyAsync(other, foreignKey, "Other");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var options = await ProfileApi.PasskeyPromptAsync(owner.Browser, $"/api/ui/profile/operations/{operation}/passkey/begin");

        await ProfileApi.AssertProblemAsync(await ProfileApi.CompletePasskeyAsync(owner.Browser, operation, foreignKey.Assert(options)), 400, "verification_failed");

        Assert.Null((await ProfileApi.OperationAsync(vault, operation)).VerifiedAtUtc);
        Assert.Equal(1, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).FailedAccessCount);
        Assert.Equal(0, (await ProfileHttp.AccountAsync(vault, other.Stored.Username)).FailedAccessCount);
    }

    [Fact]
    public async Task Replacing_a_passkey_prompt_prevents_completion_of_the_earlier_prompt()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-key-expiry", "100.66.87.7");
        using var key = new PasskeyAuthenticator();
        await ProfileApi.AddPasskeyAsync(owner, key, "Proof");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var first = await ProfileApi.PasskeyPromptAsync(owner.Browser, $"/api/ui/profile/operations/{operation}/passkey/begin");
        var replaced = await ProfileApi.PasskeyPromptAsync(owner.Browser, $"/api/ui/profile/operations/{operation}/passkey/begin");
        await ProfileApi.AssertProblemAsync(await ProfileApi.CompletePasskeyAsync(owner.Browser, operation, key.Assert(first)), 400, "verification_failed");
        Assert.NotEqual(first.GetProperty("challenge").GetString(), replaced.GetProperty("challenge").GetString());
        Assert.Equal(1, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).FailedAccessCount);
    }

    [Fact]
    public async Task Expiring_a_passkey_prompt_refuses_it_without_counting_a_failure()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-key-deadline", "100.66.87.9");
        using var key = new PasskeyAuthenticator();
        await ProfileApi.AddPasskeyAsync(owner, key, "Proof");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var prompt = await ProfileApi.PasskeyPromptAsync(owner.Browser, $"/api/ui/profile/operations/{operation}/passkey/begin");
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().ProfileOperations.Where(o => o.Id == operation)
            .ExecuteUpdateAsync(set => set.SetProperty(o => o.ChallengeExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var before = await AuditTrail.HeadAsync(vault);

        await ProfileApi.AssertProblemAsync(await ProfileApi.CompletePasskeyAsync(owner.Browser, operation, key.Assert(prompt)), 409, "ceremony_expired");

        Assert.Empty(await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
        Assert.Equal(0, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).FailedAccessCount);
    }

    [Fact]
    public async Task Malformed_passkey_data_does_not_count_as_wrong_credentials()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-key-malformed", "100.66.87.8");
        using var key = new PasskeyAuthenticator();
        await ProfileApi.AddPasskeyAsync(owner, key, "Proof");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        await ProfileApi.PasskeyPromptAsync(owner.Browser, $"/api/ui/profile/operations/{operation}/passkey/begin");
        var before = await AuditTrail.HeadAsync(vault);

        await ProfileApi.AssertProblemAsync(await ProfileApi.CompletePasskeyAsync(owner.Browser, operation, new { id = "Malformed-Proof-87!" }), 400, "validation_failed");

        Assert.Equal(0, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).FailedAccessCount);
        Assert.Empty(await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, owner.Id, $"/api/ui/profile/operations/{operation}/passkey/complete", "Malformed-Proof-87!");
    }
    [Fact]
    public async Task Cancelling_a_passkey_prompt_does_not_count_as_wrong_credentials()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-key-cancel", "100.66.87.10");
        using var key = new PasskeyAuthenticator();
        await ProfileApi.AddPasskeyAsync(owner, key, "Proof");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        await ProfileApi.PasskeyPromptAsync(owner.Browser, $"/api/ui/profile/operations/{operation}/passkey/begin");
        var before = await AuditTrail.HeadAsync(vault);

        Assert.Equal(HttpStatusCode.NoContent, (await ProfileApi.SendAsync(owner.Browser, HttpMethod.Delete,
            $"/api/ui/profile/operations/{operation}")).StatusCode);

        Assert.Equal(0, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).FailedAccessCount);
        Assert.Empty(await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
    }

}
