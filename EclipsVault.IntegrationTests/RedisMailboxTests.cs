using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(RedisVaultCollection.Name)]
public sealed class RedisMailboxTests(RedisVaultFixture fixture)
{
    [Fact]
    public async Task Store_failure_after_SMTP_acceptance_reports_the_recorded_request_without_activating_its_code()
    {
        var vault = fixture.Vault;
        var owner = await ProfileAccount.CreateAsync(vault, "it-redis-mailbox-partial", "100.70.91.1");
        const string address = "redis.mailbox.partial@example.test";
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "ChangeEmail", newEmail = address });
        var head = await AuditTrail.HeadAsync(vault);
        await vault.Mailbox!.PauseAsync();
        var request = ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/email/request", new { operationId });
        try
        {
            try
            {
                await WaitForReservationAsync(vault, operationId);
                await fixture.Redis.PauseAsync();
            }
            finally
            {
                await vault.Mailbox.ResumeAsync();
            }

            var problem = await ProfileApi.AssertProblemAsync(await request, 503, "email_request_incomplete");

            Assert.Equal("The code was sent, but the request could not be completed. Your current email is unchanged. Verify again before deliberately resending.",
                problem.GetProperty("title").GetString());
        }
        finally
        {
            await fixture.Redis.ResumeAsync();
            await request;
        }

        var pending = await ProfileApi.OperationAsync(vault, operationId);
        Assert.NotNull(pending.MailboxCodeHash);
        Assert.Null(pending.VerifiedAtUtc);
        Assert.False(pending.MailboxDelivered);
        Assert.Equal(owner.Stored.Email, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).Email);
        Assert.Equal(owner.Stored.CredentialGeneration, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).CredentialGeneration);
        Assert.Single(await AuditTrail.RowsAfterAsync(vault, head), row => row.Action == AuditAction.EmailChangeRequested);
        Assert.True(await vault.Mailbox.HasMessageForAsync(address));
        var code = await vault.Mailbox.CodeForAsync(address);
        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/email/confirm", new { operationId, code }), 409, "mail_unavailable");
    }

    private static async Task WaitForReservationAsync(VaultFixture vault, Guid operationId)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var scope = vault.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        while (!await database.ProfileOperations.AsNoTracking()
            .AnyAsync(operation => operation.Id == operationId && operation.MailboxCodeHash != null && operation.VerifiedAtUtc == null, deadline.Token))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
        }
    }
}
