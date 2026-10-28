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
public sealed class ProfileRecoveryTests(VaultFixture vault)
{
    [Fact]
    public async Task Recovery_verification_spends_one_code_without_recording_a_sign_in()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-proof-recovery", "100.68.87.1");
        var codes = await ProfileApi.RecoverySetAsync(owner);
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var before = await AuditTrail.HeadAsync(vault);

        var verified = await ProfileApi.VerifyAsync(owner.Browser, operation, "PasswordAndRecoveryCode", owner.Password, codes[0]);

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal(9, (await ProfileHttp.RecoveryCodesAsync(vault, owner.Id)).Count(code => code.UsedAtUtc is null));
        Assert.Equal([AuditAction.AccountVerified], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
        var next = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        await ProfileApi.AssertProblemAsync(await ProfileApi.VerifyAsync(owner.Browser, next,
            "PasswordAndRecoveryCode", owner.Password, codes[0]), 400, "verification_failed");
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, owner.Id, $"/api/ui/profile/operations/{operation}/verify", owner.Password, codes[0]);
    }

    [Fact]
    public async Task Recovery_replacement_returns_ten_codes_only_once_and_rotates_the_session()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-recovery-replace", "100.68.87.2");
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "ReplaceRecoveryCodes" });
        var cookie = owner.Browser.SessionCookie!.Value;
        var before = await AuditTrail.HeadAsync(vault);

        var replaced = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/recovery-codes", new { operationId });

        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var body = await replaced.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["codes"], ProfileApi.NamesOf(body));
        var codes = body.GetProperty("codes").EnumerateArray().Select(code => code.GetString()!).ToArray();
        Assert.Equal(10, codes.Distinct().Count());
        Assert.NotEqual(cookie, owner.Browser.SessionCookie!.Value);
        Assert.Equal(10, (await ProfileHttp.RecoveryCodesAsync(vault, owner.Id)).Length);
        Assert.Equal([AuditAction.RecoveryCodesGenerated], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
        await ProfileApi.AssertNoticeAsync(vault, owner, "ReplaceRecoveryCodes");
        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/recovery-codes", new { operationId }), 404, "operation_not_found");
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, owner.Id, "/api/ui/profile/recovery-codes", codes);
    }

    [Fact]
    public async Task The_final_recovery_redemption_after_verification_prevents_last_passkey_removal()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-last-key-recovery", "100.68.87.3");
        using var key = new PasskeyAuthenticator();
        var passkeyId = await ProfileApi.AddPasskeyAsync(owner, key, "Last key");
        var codes = await ProfileApi.RecoverySetAsync(owner);
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        // Keep two unused codes: one proves removal, another can disappear before the final commit.
        var retained = await db.MfaRecoveryCodes.Where(code => code.UserId == owner.Id).OrderBy(code => code.Id).Take(2).Select(code => code.Id).ToArrayAsync();
        await db.MfaRecoveryCodes.Where(code => code.UserId == owner.Id && !retained.Contains(code.Id)).ExecuteDeleteAsync();
        var operationId = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "RemovePasskey", passkeyId });
        // Use the real verifier to find a retained code without assuming database/random-code order.
        var hasher = scope.ServiceProvider.GetRequiredService<EclipsVault.Core.Application.Abstractions.IPasswordHasher>();
        var rows = await db.MfaRecoveryCodes.Where(code => code.UserId == owner.Id).ToArrayAsync();
        var proof = codes.Single(code => hasher.Verify(EclipsVault.Core.Application.Mfa.RecoveryCodeFormat.Normalize(code), rows[0].CodeHash, rows[0].Salt));
        Assert.Equal(HttpStatusCode.OK, (await ProfileApi.VerifyAsync(owner.Browser, operationId, "PasswordAndRecoveryCode", owner.Password, proof)).StatusCode);
        var last = codes.Single(code => hasher.Verify(EclipsVault.Core.Application.Mfa.RecoveryCodeFormat.Normalize(code), rows[1].CodeHash, rows[1].Salt));
        var account = await db.Users.SingleAsync(user => user.Id == owner.Id);
        var attempts = scope.ServiceProvider.GetRequiredService<EclipsVault.Core.Application.Abstractions.IAuthenticationAttempt>();
        var factors = scope.ServiceProvider.GetRequiredService<EclipsVault.Core.Application.Authentication.SecondFactorVerifier>();
        Assert.True(await attempts.RunAsync(account, token => factors.TryRedeemRecoveryCodeAsync(account, last, token), default));

        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Delete,
            $"/api/ui/profile/passkeys/{passkeyId}", new { operationId }), 409, "alternate_method_required");

        Assert.Equal(passkeyId, Assert.Single(await ProfileHttp.PasskeysAsync(vault, owner.Id)).Id);
        Assert.NotNull((await ProfileApi.OperationAsync(vault, operationId)).VerifiedAtUtc);
    }
}
