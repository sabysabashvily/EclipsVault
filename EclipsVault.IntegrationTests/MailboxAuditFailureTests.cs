using System.Net;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditFailureCollection.Name)]
public sealed class MailboxAuditFailureTests(AuditFailureFixture failure)
{
    private VaultFixture Vault => failure.Vault;

    [Fact]
    public async Task Rejected_activation_keeps_the_old_alias_generation_mailbox_proof_and_account_grant()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-audit-activate", "100.69.88.1");
        var operation = await ProfileApi.VerifiedWithAuthenticatorAsync(owner,
            new { kind = "ChangeEmail", newEmail = "mailbox.audit.activate@example.test" });
        Assert.Equal(HttpStatusCode.OK, (await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/email/request", new { operationId = operation })).StatusCode);
        var code = await Vault.Mailbox!.CodeForAsync("mailbox.audit.activate@example.test");
        var pending = await ProfileApi.OperationAsync(Vault, operation);
        var head = await AuditTrail.HeadAsync(Vault);
        await failure.RejectAuditWritesAsync();
        try
        {
            await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
                "/api/ui/profile/email/confirm", new { operationId = operation, code }), 503, "audit_unavailable");
            var account = await LegacyProfile.AccountAsync(Vault, owner.Stored.Username);
            Assert.Equal(owner.Stored.Email, account.Email);
            Assert.Equal(owner.Stored.CredentialGeneration, account.CredentialGeneration);
            var unchanged = await ProfileApi.OperationAsync(Vault, operation);
            Assert.Equal(pending.MailboxCodeHash, unchanged.MailboxCodeHash);
            Assert.Equal(pending.VerifiedAtUtc, unchanged.VerifiedAtUtc);
            using var scope = Vault.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SaveChangesAsync();
            Assert.Empty(await LegacyProfile.EventsAfterAsync(Vault, head, owner.Id));
            await ProofDisclosure.AssertNotDisclosedAsync(Vault, head, owner.Id, "/api/ui/profile/email/confirm", code, owner.Password);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.Equal(HttpStatusCode.NoContent, (await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/email/confirm", new { operationId = operation, code })).StatusCode);
    }

    [Fact]
    public async Task Rejected_request_records_no_mailbox_proof_or_send_reservation_and_sends_nothing()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-audit-request", "100.69.88.2");
        var operation = await ProfileApi.VerifiedWithAuthenticatorAsync(owner,
            new { kind = "ChangeEmail", newEmail = "mailbox.audit.request@example.test" });
        var grant = (await ProfileApi.OperationAsync(Vault, operation)).VerifiedAtUtc;
        await failure.RejectAuditWritesAsync();
        try
        {
            await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
                "/api/ui/profile/email/request", new { operationId = operation }), 503, "audit_unavailable");
            var pending = await ProfileApi.OperationAsync(Vault, operation);
            Assert.Null(pending.MailboxCodeHash);
            Assert.Equal(grant, pending.VerifiedAtUtc);
            Assert.False(await Vault.Mailbox!.HasMessageForAsync("mailbox.audit.request@example.test"));
            using var scope = Vault.Services.CreateScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailVerificationSends.AnyAsync(send => send.AccountId == owner.Id));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Rejected_email_account_verification_leaves_no_activation_grant()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-audit-verify", "100.69.88.3");
        var operation = await ProfileApi.PreparedAsync(owner.Browser,
            new { kind = "ChangeEmail", newEmail = "mailbox.audit.verify@example.test" });
        await failure.RejectAuditWritesAsync();
        try
        {
            await ProfileApi.AssertProblemAsync(await ProfileApi.VerifyAsync(owner.Browser, operation,
                "PasswordAndAuthenticatorCode", owner.Password, Totp.NextCode(owner.TotpSecret)), 503, "audit_unavailable");
            Assert.Null((await ProfileApi.OperationAsync(Vault, operation)).VerifiedAtUtc);
            Assert.Equal(owner.Stored.LastTotpStep, (await LegacyProfile.AccountAsync(Vault, owner.Stored.Username)).LastTotpStep);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }
}
