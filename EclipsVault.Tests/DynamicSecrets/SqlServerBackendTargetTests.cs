using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.Tests.DynamicSecrets;

/// <summary>
/// Dynamic secrets and managed rotation run <c>CREATE LOGIN</c> / <c>DROP LOGIN</c> /
/// <c>ALTER LOGIN</c>, which need server-level rights on the machine they run against. Those rights
/// permit re-passwording any principal on that instance, so they must never be held by the vault's
/// own login: on the server that stores the audit trail, that turns an application compromise into
/// control of the evidence.
///
/// The backend therefore talks to a separately configured target and, when none is configured,
/// <em>refuses</em>. These tests pin the refusal, because the tempting "fall back to the vault's
/// connection" would restore exactly the coupling this exists to remove — and would do it silently.
/// </summary>
public class SqlServerBackendTargetTests
{
    private static SqlServerBackend Backend(string? connectionString) =>
        new(Options.Create(new DynamicSecretTargetOptions { TargetConnectionString = connectionString }),
            NullLogger<SqlServerBackend>.Instance);

    private static DynamicSecretRole Role() => new()
    {
        Id = Guid.NewGuid(),
        Name = "reporting_ro",
        CreationStatements = "CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';",
        RevocationStatements = "DROP LOGIN [{{name}}];"
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Minting_refuses_when_no_target_is_configured(string? connectionString)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Backend(connectionString).MintAsync(
                Role(), "svc_reporting", "Abc123", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

        // The message has to tell an operator what to set and why it is not the vault's connection.
        Assert.Contains("TargetConnectionString", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ALTER ANY LOGIN", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoking_refuses_when_no_target_is_configured()
        => await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Backend(null).RevokeAsync(Role(), "svc_reporting", CancellationToken.None));

    [Fact]
    public async Task Rotating_a_managed_principal_refuses_when_no_target_is_configured()
    {
        // Nothing was sent, so the refusal is a certain one: the caller need not put a password back.
        var refused = await Assert.ThrowsAsync<UpstreamRotationRefusedException>(() =>
            Backend(null).RotatePrincipalAsync("svc_reporting", "Abc123", CancellationToken.None));

        Assert.IsType<InvalidOperationException>(refused.InnerException);
    }

    [Fact]
    public void A_configured_target_is_reported_as_configured()
    {
        Assert.False(new DynamicSecretTargetOptions { TargetConnectionString = null }.IsConfigured);
        Assert.False(new DynamicSecretTargetOptions { TargetConnectionString = " " }.IsConfigured);
        Assert.True(new DynamicSecretTargetOptions
        {
            TargetConnectionString = "Server=db;Database=app;Encrypt=True"
        }.IsConfigured);
    }
}
