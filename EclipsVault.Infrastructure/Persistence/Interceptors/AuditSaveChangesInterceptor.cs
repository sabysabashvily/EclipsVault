using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EclipsVault.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Injects an AuditLog row for every change to an audited entity (see <see cref="SavingChangesAsync"/>
/// for the set) into the SAME SaveChanges batch, which SQL Server executes as one implicit
/// transaction. Fail-closed by construction: if the audit row cannot be written the entire
/// transaction — including the change itself — is rolled back, and the failure is surfaced as a
/// critical event. This is why these rows are injected here rather than written through
/// <c>IAuditSink</c>, which commits separately and so could leave the change persisted unaudited.
///
/// It is also the single choke point for <b>every</b> audit insert (whether injected here or
/// added by the <c>AuditSink</c>), so it stamps each new row into the tamper-evidence hash
/// chain just before persistence — advancing the chain head only once the batch commits.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IAuditContext _actor;
    private readonly AuditChain _chain;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;

    // The chain batch stamped for the in-flight SaveChanges (the chain lock is held while set).
    private AuditBatch? _pending;

    private IReadOnlyList<AuditEntry>? _operationEvents;

    public AuditSaveChangesInterceptor(IAuditContext actor, AuditChain chain, TimeProvider clock, ILogger<AuditSaveChangesInterceptor> logger)
    {
        _actor = actor;
        _chain = chain;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Stamping the chain takes a database lock and reads the head, so it is inherently async.
    /// Rather than block a thread on it — or, far worse, quietly skip it and leave the row outside
    /// the tamper-evidence chain — the synchronous path refuses.
    /// </summary>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        InjectAuditEntries(eventData.Context);
        if (CollectAddedAuditRows(eventData.Context).Count > 0 || PendingShareChanges(eventData.Context).Count > 0)
        {
            // Throwing leaves the rows just injected staged; the context unstages them (see
            // EclipsVaultDbContext.DiscardPendingChanges), since anything left pending on this
            // shared context is committed by whoever saves next.
            throw new NotSupportedException(
                "Audit rows must be persisted with SaveChangesAsync. Stamping the tamper-evidence hash " +
                "chain reads its head under a database lock, which the synchronous path cannot do, and " +
                "writing the row unstamped would silently leave it outside the chain.");
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        InjectAuditEntries(eventData.Context);
        await InjectShareAuditEntriesAsync(eventData.Context, cancellationToken);
        var rows = CollectAddedAuditRows(eventData.Context);
        if (rows.Count > 0 && eventData.Context is { } context)
        {
            // Holds the chain until SavedChangesAsync commits or SaveChangesFailedAsync rolls back.
            _pending = await _chain.BeginAsync(context, rows, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// An operation owns its semantic events, including multi-row resets and authentication
    /// bookkeeping. User rows alone cannot identify whether a change was a Profile edit.
    /// </summary>
    public async Task SaveOperationAsync(DbContext context, IReadOnlyList<AuditEntry> events, CancellationToken ct)
    {
        _operationEvents = events;
        try
        {
            await context.SaveChangesAsync(ct);
        }
        finally
        {
            _operationEvents = null;
        }
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        => base.SavedChanges(eventData, result);

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        // The rows are in; committing here both persists them and releases the chain.
        if (_pending is { } batch)
        {
            _pending = null;
            await AuditChain.CommitAsync(batch, cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        LogFailure(eventData);
        base.SaveChangesFailed(eventData);
    }

    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        LogFailure(eventData);

        if (_pending is { } batch)
        {
            _pending = null;
            await AuditChain.AbortAsync(batch);
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private static List<AuditLog> CollectAddedAuditRows(DbContext? context)
        => context?.ChangeTracker.Entries<AuditLog>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList() ?? [];

    private void LogFailure(DbContextErrorEventData eventData)
        => _logger.LogCritical(eventData.Exception,
            "SaveChanges failed with pending audit entries — transaction aborted, no unaudited change was persisted (fail-closed)");

    /// <summary>
    /// The audited entities, then the operation being saved, if any. A type belongs here only
    /// when its audit row must be atomic with the change itself; everything else is written through
    /// <c>IAuditSink</c> from the service that owns the operation.
    /// </summary>
    private void InjectAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        Inject<Secret>(context, DescribeSecretChange);
        Inject<TrustedNetwork>(context, DescribeTrustedNetworkChange);
        Inject<DynamicSecretLease>(context, DescribeLeaseChange);
        InjectOperationEvents(context);
    }

    private void InjectOperationEvents(DbContext context)
    {
        if (_operationEvents is not { } events)
        {
            return;
        }

        _operationEvents = null;
        var now = _clock.GetUtcNow();
        foreach (var entry in events)
        {
            AddRow(context,
                new AuditRow(entry.Action, entry.ResourceType, entry.ResourceId, entry.ResourceName, entry.Details, entry.IsCritical),
                entry.ActorUserId ?? (entry.InheritActorUserId ? _actor.UserId : null),
                entry.ActorUsername ?? _actor.Username ?? "system", now);
        }
    }

    /// <summary>
    /// Appends one audit row per pending change to <typeparamref name="TEntity"/>, shaped by
    /// <paramref name="describe"/>. A null description means the change is not audited.
    /// </summary>
    private void Inject<TEntity>(DbContext context, Func<EntityEntry<TEntity>, AuditRow?> describe)
        where TEntity : class
    {
        var entries = context.ChangeTracker.Entries<TEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        foreach (var entry in entries)
        {
            if (describe(entry) is not { } row)
            {
                continue;
            }

            AddRow(context, row, _actor.UserId, _actor.Username ?? "system", now);
        }
    }

    private void AddRow(DbContext context, AuditRow row, Guid? actorId, string actorName, DateTimeOffset now)
        => context.Set<AuditLog>().Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TimestampUtc = now,
            UserId = actorId,
            Username = actorName,
            SourceIp = _actor.SourceIp ?? "internal",
            Action = row.Action,
            ResourceType = row.ResourceType,
            ResourceId = row.ResourceId,
            ResourceName = row.ResourceName,
            Details = row.Details,
            IsCritical = row.IsCritical
        });

    private static List<EntityEntry<SecretGrant>> PendingShareChanges(DbContext? context)
        => context?.ChangeTracker.Entries<SecretGrant>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Deleted)
            .ToList() ?? [];

    private async Task InjectShareAuditEntriesAsync(DbContext? context, CancellationToken ct)
    {
        var shares = PendingShareChanges(context);
        if (context is null || shares.Count == 0)
        {
            return;
        }

        // Issuer-scoped revocation does not read the secret first. Resolve its name here so every
        // persistence path records the secret itself, rather than trusting a caller's display text.
        var secretNames = new Dictionary<Guid, string>();
        foreach (var secretId in shares.Select(entry => entry.Entity.SecretId).Distinct())
        {
            var secret = await context.Set<Secret>().FindAsync([secretId], ct);
            secretNames[secretId] = secret?.Name ?? "secret";
        }

        var replaced = ReplacedExpiredShares(shares);
        Inject<SecretGrant>(context, entry => DescribeShareChange(
            entry, secretNames[entry.Entity.SecretId], replaced.Contains((entry.Entity.SecretId, entry.Entity.GranteeUserId))));
    }

    /// <summary>
    /// The (secret, recipient) pairs whose expired share is deleted and whose new share is added in
    /// this save. Nobody revoked that expired share, so the pair is recorded as one share event.
    /// </summary>
    private HashSet<(Guid SecretId, Guid RecipientId)> ReplacedExpiredShares(List<EntityEntry<SecretGrant>> shares)
    {
        var now = _clock.GetUtcNow();
        var added = shares
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => (entry.Entity.SecretId, entry.Entity.GranteeUserId));
        return shares
            .Where(entry => entry.State == EntityState.Deleted && !entry.Entity.IsActive(now))
            .Select(entry => (entry.Entity.SecretId, entry.Entity.GranteeUserId))
            .Intersect(added)
            .ToHashSet();
    }

    private static AuditRow? DescribeShareChange(EntityEntry<SecretGrant> entry, string secretName, bool replacesExpiredShare)
    {
        var share = entry.Entity;
        return entry.State switch
        {
            EntityState.Added => new AuditRow(
                AuditAction.SecretShared, nameof(Secret), share.SecretId, secretName,
                $"Shared with '{share.GranteeUsername}'"
                + (share.ExpiresAtUtc is { } expiry ? $" until {expiry:u}" : "")
                + (replacesExpiredShare ? ", replacing an expired share" : "")),
            EntityState.Deleted when replacesExpiredShare => null,
            EntityState.Deleted => new AuditRow(
                AuditAction.SecretShareRevoked, nameof(Secret), share.SecretId, secretName,
                $"Revoked access for '{share.GranteeUsername}'"),
            _ => null
        };
    }

    private static AuditRow? DescribeSecretChange(EntityEntry<Secret> entry)
    {
        // Stamping the expiry-notice marker is bookkeeping, not a change to the secret: reporting it
        // as SecretUpdated would tell an auditor someone edited a secret that nobody touched, once
        // per notice. The notice itself is recorded in the notification outbox. Narrow by
        // construction — if anything else changed in the same write, it audits normally.
        if (entry.State == EntityState.Modified && OnlyChanged(entry, nameof(Secret.ExpiryNoticeSentForUtc)))
        {
            return null;
        }

        var action = entry.State switch
        {
            EntityState.Added => AuditAction.SecretCreated,
            EntityState.Deleted => AuditAction.SecretDeleted,
            _ when IsShredTransition(entry) => AuditAction.SecretShredded,
            _ => AuditAction.SecretUpdated
        };

        return new AuditRow(action, nameof(Secret), entry.Entity.Id, entry.Entity.Name, $"EntityState={entry.State}");
    }

    /// <summary>
    /// Trusting or untrusting a range widens or narrows the ABAC network rule, so it is audited
    /// atomically with the change. There is no update path — a range is added or removed.
    /// </summary>
    private static AuditRow? DescribeTrustedNetworkChange(EntityEntry<TrustedNetwork> entry)
        => entry.State switch
        {
            EntityState.Added => new AuditRow(
                AuditAction.TrustedNetworkAdded, nameof(TrustedNetwork), entry.Entity.Id, entry.Entity.Cidr, entry.Entity.Label),
            EntityState.Deleted => new AuditRow(
                AuditAction.TrustedNetworkRemoved, nameof(TrustedNetwork), entry.Entity.Id, entry.Entity.Cidr, entry.Entity.Label),
            _ => null
        };

    /// <summary>
    /// A dynamic credential exists on a live backend, so issuing and destroying it are audited in
    /// the same transaction that opens and closes the lease — the trail can never claim a credential
    /// was reclaimed when the row saying so was rolled back.
    /// </summary>
    private static AuditRow? DescribeLeaseChange(EntityEntry<DynamicSecretLease> entry)
    {
        var lease = entry.Entity;

        if (entry.State == EntityState.Added)
        {
            return new AuditRow(
                AuditAction.DynamicCredentialIssued, nameof(DynamicSecretLease), lease.Id, lease.RoleName,
                $"Minted '{lease.CredentialIdentity}' for {lease.Username}; lease elapses at {lease.ExpiresAtUtc:u}");
        }

        if (entry.State != EntityState.Modified)
        {
            return null;
        }

        return lease.Status switch
        {
            LeaseStatus.Revoked => new AuditRow(
                AuditAction.DynamicCredentialRevoked, nameof(DynamicSecretLease), lease.Id, lease.RoleName,
                $"Handed back '{lease.CredentialIdentity}' before its lease elapsed"),

            LeaseStatus.Expired => new AuditRow(
                AuditAction.DynamicCredentialExpired, nameof(DynamicSecretLease), lease.Id, lease.RoleName,
                $"Lease elapsed; destroyed '{lease.CredentialIdentity}'"),

            // The credential may still be live on the backend — the one lease outcome that needs a human.
            LeaseStatus.RevocationFailed => new AuditRow(
                AuditAction.DynamicCredentialRevocationFailed, nameof(DynamicSecretLease), lease.Id, lease.RoleName,
                $"Could NOT destroy '{lease.CredentialIdentity}': {lease.RevocationError}", IsCritical: true),

            _ => null
        };
    }

    private static bool IsShredTransition(EntityEntry<Secret> entry)
        => entry.Property(s => s.IsShredded) is { OriginalValue: false, CurrentValue: true };

    /// <summary>True when <paramref name="propertyName"/> is the only modified property on the entry.</summary>
    private static bool OnlyChanged(EntityEntry entry, string propertyName)
    {
        var modified = entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToList();
        return modified.Count == 1 && modified[0] == propertyName;
    }

    /// <summary>The audit-relevant shape of one entity change, before the actor and clock are stamped on.</summary>
    private readonly record struct AuditRow(
        AuditAction Action,
        string ResourceType,
        Guid? ResourceId,
        string? ResourceName,
        string? Details,
        bool IsCritical = false);
}
