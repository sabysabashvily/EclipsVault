using System.Security.Cryptography;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Stores secrets through the vault's own <see cref="SecretService"/> and AES-GCM engine, sealed under
/// a master key other than the vault's current one: what an operator leaves behind by replacing the
/// current key. Nothing here writes ciphertext by hand.
/// </summary>
internal static class SecretsUnderMasterKey
{
    public static string NewMasterKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>The id the vault gives <paramref name="masterKey"/> and stores beside every data key it wraps.</summary>
    public static string IdOf(string masterKey) => KeysWithCurrent(masterKey).CurrentKekId;

    public static async Task<Guid> CreateAsync(VaultFixture vault, string masterKey, string name, string value)
    {
        using var scope = vault.Services.CreateScope();
        return await SecretsSealedUnder(scope, masterKey).CreateAsync(
            new CreateSecretRequest(name, value, "ROTATION", SecretEnvironment.Development, SensitivityLevel.Internal, TtlDays: 0),
            default);
    }

    /// <summary>Creates <paramref name="count"/> secrets named <paramref name="namePrefix"/>-000 onwards, in that order.</summary>
    public static async Task<List<Guid>> CreateManyAsync(VaultFixture vault, string masterKey, string namePrefix, int count)
    {
        var ids = new List<Guid>(count);
        for (var index = 0; index < count; index++)
        {
            ids.Add(await CreateAsync(vault, masterKey, $"{namePrefix}-{index:D3}", "value"));
        }

        return ids;
    }

    /// <summary>Replaces the secret's value; the value it replaces is archived as a version, both under <paramref name="masterKey"/>.</summary>
    public static async Task ReplaceValueAsync(VaultFixture vault, string masterKey, Guid secretId, string value)
    {
        using var scope = vault.Services.CreateScope();
        await SecretsSealedUnder(scope, masterKey).RotateAsync(secretId, value, changeNote: null, renewTtlDays: null, default);
    }

    private static SecretService SecretsSealedUnder(IServiceScope scope, string masterKey)
        => ActivatorUtilities.CreateInstance<SecretService>(scope.ServiceProvider, new EngineUnder(masterKey));

    private static EnvironmentKekProvider KeysWithCurrent(string masterKey)
        => new(Options.Create(new CryptoOptions
        {
            // Names nothing sets, so the vault's loader takes the key from the fallback below and
            // never reads the host's own ECLIPSVAULT_KEK or ECLIPSVAULT_KEK_RETIRED.
            KekEnvironmentVariable = "ECLIPSVAULT_IT_UNSET_KEK",
            RetiredKekEnvironmentVariable = "ECLIPSVAULT_IT_UNSET_RETIRED_KEK",
            AllowDevelopmentKekFallback = true,
            DevelopmentKekBase64 = masterKey,
        }), NullLogger<EnvironmentKekProvider>.Instance);

    private sealed class EngineUnder(string masterKey) : ICryptoEngineFactory
    {
        private readonly AesGcmCryptoEngine _engine = new(KeysWithCurrent(masterKey), Options.Create(new CryptoOptions()));

        public ICryptoEngine Create() => _engine;
    }
}
