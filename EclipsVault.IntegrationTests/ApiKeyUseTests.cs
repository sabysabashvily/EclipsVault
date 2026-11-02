using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.ServiceAccounts;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence.Repositories;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Public API stamps when a key was last used. An administrator can revoke the key while a request
/// holding it is between reading it and stamping it; the stamp must not write the key back as that
/// request read it.
/// </summary>
public abstract class ApiKeyUseTests(AccountChangeDatabases databases)
{
    [Fact]
    public async Task Stamping_a_keys_use_keeps_a_revoke_committed_after_the_key_was_read()
    {
        var (account, apiKey) = await IssueKeyAsync("it-use-after-revoke");
        await using var request = databases.NewRequest();
        var service = PublicApi(request, new ClockThatRevokesTheKeyFirst(() => Revoke(account.Keys.Single().Id)));

        await service.AuthenticateAsync(apiKey, null, default);

        await using var check = databases.NewRequest();
        Assert.NotNull((await check.Context.ApiKeys.AsNoTracking().SingleAsync(key => key.ServiceAccountId == account.Id)).RevokedAtUtc);
        Assert.Null(await PublicApi(check, TimeProvider.System).AuthenticateAsync(apiKey, null, default));
    }

    private static ServiceAccountService PublicApi(AccountRequest request, TimeProvider clock)
        => new(new ServiceAccountRepository(request.Context), new ApiKeyFactory(), new UnusedChanges(), clock);

    /// <summary>A Standard service account with one key that has never been used, saved directly.</summary>
    private async Task<(ServiceAccount Account, string ApiKey)> IssueKeyAsync(string name)
    {
        var generated = new ApiKeyFactory().Generate();
        var account = new ServiceAccount { Id = Guid.NewGuid(), Name = name, Clearance = ClearanceLevel.Standard, ProjectKey = "GLOBAL", CreatedAtUtc = DateTimeOffset.UtcNow };
        account.Keys.Add(new ApiKey { Id = Guid.NewGuid(), KeyHash = generated.Hash, Prefix = generated.Prefix, CreatedAtUtc = DateTimeOffset.UtcNow });
        await using var request = databases.NewRequest();
        request.Context.ServiceAccounts.Add(account);
        await request.Context.SaveChangesAsync();
        return (account, generated.RawToken);
    }

    private void Revoke(Guid keyId)
    {
        using var administrator = databases.NewRequest().Context;
        administrator.ApiKeys.Where(key => key.Id == keyId).ExecuteUpdate(key => key.SetProperty(k => k.RevokedAtUtc, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// The authenticator reads the time once, after it has read the key and before it stamps the key's use,
    /// so the revoke made here lands between the two.
    /// </summary>
    private sealed class ClockThatRevokesTheKeyFirst(Action revoke) : TimeProvider
    {
        private Action? _revoke = revoke;

        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Exchange(ref _revoke, null)?.Invoke();
            return base.GetUtcNow();
        }
    }

    /// <summary>Authenticating a key changes nothing an administrator changes, so it never starts a change.</summary>
    private sealed class UnusedChanges : IServiceAccountChanges
    {
        public Task<IServiceAccountChange> BeginAsync(Guid serviceAccountId, CancellationToken ct) => throw new NotSupportedException();
    }
}

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class SqlServerApiKeyUseTests(SqlServerAccountChangeDatabases databases) : ApiKeyUseTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresApiKeyUseTests(PostgresAccountChangeDatabases databases) : ApiKeyUseTests(databases);
