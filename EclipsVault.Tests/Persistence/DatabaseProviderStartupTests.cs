using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Infrastructure;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.Tests.Persistence;

/// <summary>
/// Startup refuses an unset database. Guessing SQL Server would hide a PostgreSQL install, and
/// guessing PostgreSQL would point an upgraded SQL Server install at the wrong engine.
/// </summary>
public class DatabaseProviderStartupTests
{
    private const string MissingProvider =
        "Database:Provider is not set. Set it to 'SqlServer' or 'Postgres' (environment variable Database__Provider).";

    [Fact]
    public void Startup_without_a_database_provider_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RegisterWithoutProvider());

        Assert.Equal(MissingProvider, exception.Message);
    }

    [Fact]
    public void An_empty_database_provider_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Register(""));

        Assert.Equal(MissingProvider, exception.Message);
    }

    [Fact]
    public void SqlServer_registers_the_SQL_Server_engine()
    {
        using var services = Register("SqlServer");
        using var scope = services.CreateScope();

        Assert.IsType<SqlServerAuditChainLocker>(scope.ServiceProvider.GetRequiredService<IAuditChainLocker>());
        Assert.True(scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Database.IsSqlServer());
    }

    [Fact]
    public void Postgres_registers_the_PostgreSQL_engine()
    {
        using var services = Register("Postgres");
        using var scope = services.CreateScope();

        Assert.IsType<PostgresAuditChainLocker>(scope.ServiceProvider.GetRequiredService<IAuditChainLocker>());
        Assert.True(scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Database.IsNpgsql());
    }

    private static ServiceProvider RegisterWithoutProvider() =>
        Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=127.0.0.1;Database=unused",
        });

    private static ServiceProvider Register(string provider) =>
        Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=127.0.0.1;Database=unused",
            ["Database:Provider"] = provider,
        });

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuditContext, UnusedActor>();
        services.AddEclipsVaultInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class UnusedActor : IAuditContext
    {
        public Guid? UserId => null;
        public string? Username => null;
        public string? SourceIp => null;
    }
}
