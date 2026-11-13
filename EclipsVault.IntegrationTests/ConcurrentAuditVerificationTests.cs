using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditTamperingCollection.Name)]
public sealed class ConcurrentAuditVerificationTests(AuditTamperingFixture fixture)
{
    private readonly VaultFixture _vault = fixture.Vault;

    [Fact]
    public async Task A_second_verification_while_one_runs_is_refused_as_busy()
    {
        await CreateCheckpointAsync();

        Task<AuditIntegrityReport> first;
        await using (var held = await HeldAuditChain.TakeAsync(_vault))
        {
            first = VerifyAsync();
            // The first has walked the trail and now waits on the chain to record its outcome.
            await held.WaitForQueuedWritersAsync(1);

            await Assert.ThrowsAsync<AuditBusyException>(VerifyAsync);
        }

        Assert.Equal(AuditChainOutcome.Anchored, (await first).Outcome);
        Assert.Equal(AuditChainOutcome.Anchored, (await VerifyAsync()).Outcome);
    }

    private async Task CreateCheckpointAsync()
    {
        await using var scope = _vault.Services.CreateAsyncScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAuditCheckpointService>().CreateCheckpointAsync(default));
    }

    private async Task<AuditIntegrityReport> VerifyAsync()
    {
        await using var scope = _vault.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAuditCheckpointService>().VerifyAsync(default);
    }
}
