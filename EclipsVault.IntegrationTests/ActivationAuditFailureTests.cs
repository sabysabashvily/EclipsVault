using System.Net;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>Activating an invited account when the trail refuses its <c>AccountActivated</c> row.</summary>
[Collection(AuditFailureCollection.Name)]
public sealed class ActivationAuditFailureTests(AuditFailureFixture failure)
{
    private const string Password = "It-activation-audit-password-180";

    private VaultFixture Vault => failure.Vault;

    [Fact]
    public async Task A_rejected_AccountActivated_row_leaves_the_account_invited_and_the_token_unspent()
    {
        (await UserAccounts.CreateAsync(await Vault.AdminClientAsync(), "it-act-audit")).EnsureSuccessStatusCode();
        var (_, _, _, token) = await Vault.Mailbox!.LatestInvitationForAsync(UserAccounts.EmailOf("it-act-audit"));
        var visitor = ActivationApi.Visitor(Vault, "198.18.181.1");
        var secret = await ActivationApi.SecretAsync(visitor, token);
        var invited = await ProfileHttp.AccountAsync(Vault, "it-act-audit");
        var head = await AuditTrail.HeadAsync(Vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            await UiApiFailureContractTests.AssertProblemAsync(
                await ActivationApi.CompleteAsync(visitor, token, Password, await ActivationApi.FreshCodeAsync(secret)), 503, "audit_unavailable");

            var account = await ProfileHttp.AccountAsync(Vault, "it-act-audit");
            Assert.True(account.AwaitingActivation);
            Assert.False(account.TotpEnabled);
            Assert.Empty(account.PasswordHash);
            Assert.Equal(invited.CredentialGeneration, account.CredentialGeneration);
            using var scope = Vault.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var invitation = await db.AccountInvitations.AsNoTracking().SingleAsync(row => row.UserId == invited.Id);
            Assert.Null(invitation.SpentAtUtc);
            Assert.Equal(secret, invitation.PendingTotpSecret);
            // A later save in the same scope must not commit what the refused activation staged.
            await db.SaveChangesAsync();
            Assert.Empty(await ProfileHttp.EventsAfterAsync(Vault, head, invited.Id));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.Equal(HttpStatusCode.NoContent,
            (await ActivationApi.CompleteAsync(visitor, token, Password, await ActivationApi.FreshCodeAsync(secret))).StatusCode);
    }
}
