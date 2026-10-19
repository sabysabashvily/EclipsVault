using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Re-seals secrets written before payloads were bound to their row, so an existing vault can adopt
/// the binding without losing what it holds.
///
/// This only runs while <c>Crypto:AllowUnauthenticatedLegacyBlobs</c> is on, and that is not
/// squeamishness. Re-sealing whatever it finds is indistinguishable from blessing it: an attacker
/// who copies one secret's envelope onto another and marks it legacy would have this hand it back,
/// correctly bound, at the next restart. So it is an operator's deliberate act with a start and an
/// end — turn it on, start once, turn it off — not a permanent repair that quietly launders
/// whatever appears.
///
/// Unlike KEK rotation, which only re-wraps DEKs, changing what a payload is bound to means
/// re-encrypting it: every value passes through memory here. That is the cost of the migration and
/// the reason it happens once rather than on every read.
/// </summary>
public static class LegacyBlobUpgrader
{
    public static async Task UpgradeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<EclipsVaultDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("EclipsVault.LegacyBlobUpgrader");
        var options = sp.GetRequiredService<IOptions<CryptoOptions>>().Value;

        string[] bound = [SealAlgorithms.AesGcmLocal, SealAlgorithms.AesGcmVaultTransit];

        var secrets = await db.Secrets
            .Where(s => !s.IsShredded && !bound.Contains(s.Algorithm))
            .ToListAsync(ct);
        var versions = await db.SecretVersions
            .Where(v => !bound.Contains(v.Algorithm))
            .ToListAsync(ct);

        if (secrets.Count == 0 && versions.Count == 0)
        {
            return;
        }

        if (!options.AllowUnauthenticatedLegacyBlobs)
        {
            // Say so once, plainly, rather than let every read of these fail on its own with a
            // message the operator only sees when a user complains.
            logger.LogWarning(
                "{SecretCount} secret(s) and {VersionCount} archived version(s) are sealed without being bound " +
                "to their row, so reading them is refused and nothing proves they belong where they are stored. " +
                "Set Crypto:AllowUnauthenticatedLegacyBlobs=true and restart to re-seal them, then turn it back " +
                "off — while it is on, the binding can be bypassed by anyone who can write the database.",
                secrets.Count, versions.Count);
            return;
        }

        var engine = sp.GetRequiredService<ICryptoEngineFactory>().Create();

        foreach (var secret in secrets)
        {
            var resealed = await ResealAsync(engine, secret, SecretBinding.ForCurrentValue(secret.Id), ct);
            secret.ApplyEnvelope(resealed);
        }

        foreach (var version in versions)
        {
            var resealed = await ResealAsync(engine, version, SecretBinding.ForArchivedVersion(version.SecretId, version.Id), ct);
            version.ApplyEnvelope(resealed);
        }

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Re-sealed {SecretCount} secret(s) and {VersionCount} archived version(s) so each is bound to its own " +
            "row. Turn Crypto:AllowUnauthenticatedLegacyBlobs back off — leaving it on keeps the binding " +
            "bypassable by anyone who can write the database.",
            secrets.Count, versions.Count);
    }

    private static async Task<SealedSecret> ResealAsync(
        ICryptoEngine engine, IEnvelope legacy, byte[] binding, CancellationToken ct)
    {
        // Unbound on the way in — that is what makes it legacy — and bound on the way out.
        var plaintext = await engine.UnsealAsync(legacy.ToSealedSecret(), [], ct);
        try
        {
            return await engine.SealAsync(plaintext, binding, ct);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
