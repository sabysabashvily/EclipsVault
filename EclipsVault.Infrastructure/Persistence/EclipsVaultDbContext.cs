using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence;

/// <summary>
/// The vault's persistence root. All mapping lives in IEntityTypeConfiguration
/// classes — domain entities stay free of database attributes.
/// </summary>
public sealed class EclipsVaultDbContext : DbContext
{
    public EclipsVaultDbContext(DbContextOptions<EclipsVaultDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Secret> Secrets => Set<Secret>();

    public DbSet<SecretVersion> SecretVersions => Set<SecretVersion>();

    public DbSet<SecretGrant> SecretGrants => Set<SecretGrant>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<AuditCheckpoint> AuditCheckpoints => Set<AuditCheckpoint>();

    public DbSet<PasskeyCredential> PasskeyCredentials => Set<PasskeyCredential>();

    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();

    public DbSet<TrustedNetwork> TrustedNetworks => Set<TrustedNetwork>();

    public DbSet<UserAvatar> UserAvatars => Set<UserAvatar>();

    public DbSet<ServiceAccount> ServiceAccounts => Set<ServiceAccount>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();

    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();

    /// <summary>Recipes for minting short-lived backend credentials on demand.</summary>
    public DbSet<DynamicSecretRole> DynamicSecretRoles => Set<DynamicSecretRole>();

    /// <summary>Issued dynamic credentials, tracked so they can be destroyed when their lease ends.</summary>
    public DbSet<DynamicSecretLease> DynamicSecretLeases => Set<DynamicSecretLease>();

    /// <summary>When each Licensed Capability was first exercised, which opens its evaluation period.</summary>
    public DbSet<FeatureEvaluation> FeatureEvaluations => Set<FeatureEvaluation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EclipsVaultDbContext).Assembly);

        // The unique sequence index skips not-yet-chained rows (Sequence 0), so the constraint can
        // exist before the one-time back-fill runs. A partial index's predicate is raw SQL in the
        // provider's own identifier quoting — SQL Server and SQLite take [Sequence], PostgreSQL
        // takes "Sequence" — so it is declared here, where the provider is known, rather than in
        // AuditLogConfiguration, which cannot see one.
        var sequence = Database.IsNpgsql() ? "\"Sequence\"" : "[Sequence]";
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.Sequence)
            .IsUnique()
            .HasFilter($"{sequence} <> 0");

        // A normalized email is already folded, so it compares ordinally, never by a collation that
        // ignores accents or width and would make two addresses one. The collation's name is the
        // provider's; SQLite compares ordinally anyway.
        var ordinal = Database.IsNpgsql() ? "C" : Database.IsSqlServer() ? "Latin1_General_100_BIN2" : null;
        modelBuilder.Entity<User>()
            .Property(u => u.NormalizedEmail)
            .UseCollation(ordinal);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch
        {
            DiscardPendingChanges();
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch
        {
            DiscardPendingChanges();
            throw;
        }
    }

    /// <summary>
    /// Puts the tracker back to what the database holds after a save that did not commit.
    ///
    /// SaveChanges is all-or-nothing, so a failure means the database took none of it — yet EF keeps
    /// every change staged, assuming a failure is something you fix and retry. That assumption does
    /// not hold here. This context is scoped to the request and shared (the audit sink saves on it
    /// too), and SaveChanges flushes everything pending rather than only its caller's rows, so a
    /// staged failure is not waiting for a retry that never comes: it is waiting for the next
    /// SaveChanges by anyone at all, which commits it.
    ///
    /// A later audit write must not commit a failed credential or secret change. Nothing in this
    /// vault retries a failed save: it fails closed and surfaces the error, so pending changes must
    /// be discarded to preserve the guarantee that a write reporting failure changed nothing.
    ///
    /// This lives on the context rather than in a SaveChangesInterceptor because EF does not route a
    /// throw from the SavingChanges phase through SaveChangesFailed — and that phase is where the
    /// audit chain takes its lock, making a timeout there one of the likelier ways to fail. An
    /// interceptor would miss exactly the case that matters most. Interceptors run inside
    /// base.SaveChanges, so this sees every failure, whatever raised it.
    /// </summary>
    private void DiscardPendingChanges()
    {
        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;

                // Reverts the entity itself, not merely its tracker state: callers still hold these
                // instances and act on what they see. A caller putting an upstream password back
                // while still holding the new one would restore the wrong value.
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    break;
            }
        }
    }
}
