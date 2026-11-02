using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class AuditFailureFixture : IAsyncLifetime
{
    public VaultFixture Vault { get; } = new() { Mailbox = new SmtpCapture() };

    public Task InitializeAsync() => Vault.InitializeAsync();

    public Task DisposeAsync() => Vault.DisposeAsync();

    public async Task RejectAuditWritesAsync()
    {
        // Startup and real sign-ins must finish before this is enabled: both need working auditing.
        // Reject at the database so injected rows and standalone group commits fail the same way.
        // Both engines leave the rows already written unchecked and check every row written after.
        using var scope = Vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.Database.ExecuteSqlRawAsync(db.Database.IsNpgsql()
            ? """ALTER TABLE "AuditLogs" ADD CONSTRAINT "RejectAuditWrites" CHECK (1 = 0) NOT VALID"""
            : """
              ALTER TABLE [dbo].[AuditLogs] WITH NOCHECK
              ADD CONSTRAINT [RejectAuditWrites] CHECK (1 = 0)
              """);
    }

    public async Task RestoreAuditWritesAsync()
    {
        using var scope = Vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        // Double-quoted names work on both engines.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"AuditLogs\" DROP CONSTRAINT \"RejectAuditWrites\"");
    }
}

// Both hosts configure Program through process-wide environment variables. This collection must
// run alone, and owns its database server so its failure mode cannot affect the ordinary HTTP suite.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AuditFailureCollection : ICollectionFixture<AuditFailureFixture>
{
    public const string Name = "audit-failure-vault";
}
