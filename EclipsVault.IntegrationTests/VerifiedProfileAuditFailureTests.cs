using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditFailureCollection.Name)]
public sealed class VerifiedProfileAuditFailureTests(AuditFailureFixture failure)
{
    private VaultFixture Vault => failure.Vault;

    [Fact]
    public async Task Profile_sign_out_reports_revoked_unrecorded_when_only_auditing_fails()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-ui87-ui-signout-audit", "100.67.87.10");
        var copy = Vault.CreateSession(owner.Browser.SessionCookie);
        await failure.RejectAuditWritesAsync();
        try
        {
            await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
                "/api/ui/profile/sign-out-everywhere"), 503, "revoked_unrecorded");
            Assert.Null(owner.Browser.SessionCookie);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await copy.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Theory]
    [InlineData("ChangePassword", "/api/ui/profile/password", "100.67.87.1", "Rollback-Candidate-Password-87!")]
    [InlineData("ResetAuthenticator", "/api/ui/profile/authenticator/reset", "100.67.87.2", null)]
    [InlineData("ReplaceRecoveryCodes", "/api/ui/profile/recovery-codes", "100.67.87.3", null)]
    public async Task Rejected_mutation_auditing_keeps_the_verified_grant_and_original_credentials(string kind, string path, string address, string? newPassword)
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-ui87-ui-rollback-" + kind, address);
        await ProfileHttp.GenerateRecoveryCodesAsync(owner.Browser);
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner,
            new { kind, newPassword });
        var original = await ProfileHttp.AccountAsync(Vault, owner.Stored.Username);
        var originalCodes = await ProfileHttp.RecoveryCodesAsync(Vault, owner.Id);
        var before = await AuditTrail.HeadAsync(Vault);
        await failure.RejectAuditWritesAsync();
        try
        {
            var rejected = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, path, new { operationId });

            await UiApiFailureContractTests.AssertProblemAsync(rejected, 503, "audit_unavailable");
            var stored = await ProfileHttp.AccountAsync(Vault, owner.Stored.Username);
            Assert.Equal(original.PasswordHash, stored.PasswordHash);
            Assert.Equal(original.TotpSecret, stored.TotpSecret);
            Assert.Equal(original.LastTotpStep, stored.LastTotpStep);
            Assert.Equal(original.CredentialGeneration, stored.CredentialGeneration);
            Assert.Equal(originalCodes.Select(code => code.Id), (await ProfileHttp.RecoveryCodesAsync(Vault, owner.Id)).Select(code => code.Id));
            Assert.NotNull((await ProfileApi.OperationAsync(Vault, operationId)).VerifiedAtUtc);
            Assert.Empty(await ProfileHttp.EventsAfterAsync(Vault, before, owner.Id));
            await ProofDisclosure.AssertNotDisclosedAsync(Vault, before, owner.Id, path, owner.Password, "Rollback-Candidate-Password-87!");
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Rejected_verification_auditing_rolls_back_the_authenticator_step_and_grant()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-ui87-ui-proof-rollback", "100.67.87.4");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var code = Totp.NextCode(owner.TotpSecret);
        var before = await AuditTrail.HeadAsync(Vault);
        await failure.RejectAuditWritesAsync();
        try
        {
            await UiApiFailureContractTests.AssertProblemAsync(await ProfileApi.VerifyAsync(owner.Browser, operation,
                "PasswordAndAuthenticatorCode", owner.Password, code), 503, "audit_unavailable");

            Assert.Null((await ProfileApi.OperationAsync(Vault, operation)).VerifiedAtUtc);
            var stored = await ProfileHttp.AccountAsync(Vault, owner.Stored.Username);
            Assert.Equal(owner.Stored.LastTotpStep, stored.LastTotpStep);
            Assert.Equal(0, stored.FailedAccessCount);
            Assert.Empty(await ProfileHttp.EventsAfterAsync(Vault, before, owner.Id));
            await ProofDisclosure.AssertNotDisclosedAsync(Vault, before, owner.Id, $"/api/ui/profile/operations/{operation}/verify", owner.Password, code);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await ProfileApi.VerifyAsync(owner.Browser, operation,
            "PasswordAndAuthenticatorCode", owner.Password, code)).StatusCode);
    }

    [Fact]
    public async Task Rejected_recovery_verification_auditing_leaves_the_code_unused_and_no_grant()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-ui87-ui-recovery-proof-rollback", "100.67.87.9");
        var codes = await ProfileApi.RecoverySetAsync(owner);
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        await failure.RejectAuditWritesAsync();
        try
        {
            await UiApiFailureContractTests.AssertProblemAsync(await ProfileApi.VerifyAsync(owner.Browser, operation,
                "PasswordAndRecoveryCode", owner.Password, codes[0]), 503, "audit_unavailable");

            Assert.All(await ProfileHttp.RecoveryCodesAsync(Vault, owner.Id), code => Assert.Null(code.UsedAtUtc));
            Assert.Null((await ProfileApi.OperationAsync(Vault, operation)).VerifiedAtUtc);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Rejected_wrong_password_auditing_rolls_back_the_failure_count()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-ui87-ui-counter-rollback", "100.67.87.5");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        await failure.RejectAuditWritesAsync();
        try
        {
            await UiApiFailureContractTests.AssertProblemAsync(await ProfileApi.VerifyAsync(owner.Browser, operation,
                "PasswordAndAuthenticatorCode", "Rejected-Wrong-Password-87!", "000000"), 503, "audit_unavailable");

            Assert.Equal(0, (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).FailedAccessCount);
            Assert.Null((await ProfileApi.OperationAsync(Vault, operation)).VerifiedAtUtc);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Rejected_passkey_verification_restores_its_challenge_counter_and_unverified_operation()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-ui87-ui-key-proof-rollback", "100.67.87.6");
        using var key = new PasskeyAuthenticator();
        await ProfileApi.AddPasskeyAsync(owner, key, "Proof");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var prompt = await ProfileApi.PasskeyPromptAsync(owner.Browser, $"/api/ui/profile/operations/{operation}/passkey/begin");
        var challenge = (await ProfileApi.OperationAsync(Vault, operation)).Challenge;
        var counter = Assert.Single(await ProfileHttp.PasskeysAsync(Vault, owner.Id)).SignCount;
        var assertion = key.Assert(prompt);
        var before = await AuditTrail.HeadAsync(Vault);
        await failure.RejectAuditWritesAsync();
        try
        {
            await UiApiFailureContractTests.AssertProblemAsync(await ProfileApi.CompletePasskeyAsync(owner.Browser, operation, assertion), 503, "audit_unavailable");

            Assert.Equal(counter, Assert.Single(await ProfileHttp.PasskeysAsync(Vault, owner.Id)).SignCount);
            var stored = await ProfileApi.OperationAsync(Vault, operation);
            Assert.Equal(challenge, stored.Challenge);
            Assert.Null(stored.VerifiedAtUtc);
            await ProofDisclosure.AssertNotDisclosedAsync(Vault, before, owner.Id, $"/api/ui/profile/operations/{operation}/passkey/complete",
                ProofDisclosure.CredentialComponents(assertion));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Rejected_passkey_add_auditing_stores_no_key_or_generation_and_keeps_the_grant()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-ui87-ui-key-add-rollback", "100.67.87.7");
        using var key = new PasskeyAuthenticator();
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "AddPasskey", passkeyNickname = "New key" });
        var prompt = await ProfileApi.PasskeyPromptAsync(owner.Browser, "/api/ui/profile/passkeys/registration/begin", new { operationId });
        var originalPrompt = await ProfileApi.OperationAsync(Vault, operationId);
        var attestation = key.Register(prompt);
        var before = await AuditTrail.HeadAsync(Vault);
        await failure.RejectAuditWritesAsync();
        try
        {
            await UiApiFailureContractTests.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
                "/api/ui/profile/passkeys/registration/complete", new { operationId, credential = attestation }), 503, "audit_unavailable");

            Assert.Empty(await ProfileHttp.PasskeysAsync(Vault, owner.Id));
            var retained = await ProfileApi.OperationAsync(Vault, operationId);
            Assert.Equal(originalPrompt.Challenge, retained.Challenge);
            Assert.Equal(originalPrompt.ChallengePurpose, retained.ChallengePurpose);
            Assert.Equal(originalPrompt.ChallengeExpiresAtUtc, retained.ChallengeExpiresAtUtc);
            await ProofDisclosure.AssertNotDisclosedAsync(Vault, before, owner.Id, "/api/ui/profile/passkeys/registration/complete",
                ProofDisclosure.CredentialComponents(attestation));
            Assert.Equal(owner.Stored.CredentialGeneration, (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).CredentialGeneration);
            Assert.NotNull((await ProfileApi.OperationAsync(Vault, operationId)).VerifiedAtUtc);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Rejected_passkey_removal_auditing_keeps_the_key_generation_and_grant()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-ui87-ui-key-remove-rollback", "100.67.87.8");
        using var key = new PasskeyAuthenticator();
        var id = await ProfileApi.AddPasskeyAsync(owner, key, "Keep key");
        var operationId = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "RemovePasskey", passkeyId = id });
        await ProfileApi.SendAsync(owner.Browser, HttpMethod.Delete, $"/api/ui/profile/operations/{operationId}");
        var codes = await ProfileApi.RecoverySetAsync(owner);
        operationId = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "RemovePasskey", passkeyId = id });
        Assert.Equal(HttpStatusCode.OK, (await ProfileApi.VerifyAsync(owner.Browser, operationId,
            "PasswordAndRecoveryCode", owner.Password, codes[0])).StatusCode);
        var original = await ProfileHttp.AccountAsync(Vault, owner.Stored.Username);
        await failure.RejectAuditWritesAsync();
        try
        {
            await UiApiFailureContractTests.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Delete,
                $"/api/ui/profile/passkeys/{id}", new { operationId }), 503, "audit_unavailable");

            Assert.Equal(id, Assert.Single(await ProfileHttp.PasskeysAsync(Vault, owner.Id)).Id);
            Assert.Equal(original.CredentialGeneration, (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).CredentialGeneration);
            Assert.NotNull((await ProfileApi.OperationAsync(Vault, operationId)).VerifiedAtUtc);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }
}
