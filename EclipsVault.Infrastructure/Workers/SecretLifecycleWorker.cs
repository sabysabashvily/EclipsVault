using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EclipsVault.Infrastructure.Workers;

public sealed class LifecycleOptions
{
    public const string SectionName = "Lifecycle";

    /// <summary>Seconds between expiry sweeps.</summary>
    public int SweepIntervalSeconds { get; set; } = 60;
}

/// <summary>
/// Background custodian of the secret lifecycle. Wakes on a fixed interval and makes two passes:
/// it warns the owner of a secret whose TTL is nearly up (once per deadline), then shreds key
/// material past its TTL (keeping an auditable tombstone) and evicts stale cache entries. Warning
/// runs before reaping so a secret can never be shredded in the same sweep that first warns about
/// it. A failed sweep never kills the worker.
/// </summary>
public sealed class SecretLifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LifecycleOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SecretLifecycleWorker> _logger;

    public SecretLifecycleWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<LifecycleOptions> options,
        TimeProvider clock,
        ILogger<SecretLifecycleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Secret lifecycle worker started; sweep interval {SweepIntervalSeconds}s", _options.SweepIntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.SweepIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Secret lifecycle sweep failed; will retry on the next interval");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Secret lifecycle worker stopping");
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISecretRepository>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = _clock.GetUtcNow();

        await WarnExpiringAsync(repository, notifications, now, ct);
        await ReapExpiredAsync(repository, secrets, now, ct);
    }

    /// <summary>
    /// Emails the owner of every secret whose deadline has entered the warning window and that has
    /// not already been warned about <i>this</i> deadline. The marker is only stamped once the
    /// notice is recorded in the outbox, so a notification outage retries rather than going silent —
    /// and once recorded it never re-sends, even though this sweep runs every minute.
    /// </summary>
    private async Task WarnExpiringAsync(
        ISecretRepository repository, INotificationService notifications, DateTimeOffset now, CancellationToken ct)
    {
        var expiring = await repository.ListExpiringAsync(now, SecretExpiry.SoonCutoff(now), ct);

        foreach (var secret in expiring)
        {
            if (!SecretExpiry.NeedsExpiryNotice(secret, now))
            {
                continue;
            }

            var recorded = await notifications.NotifyExpiringSecretAsync(
                secret.CreatedByUserId, secret.Name, secret.ExpiresAtUtc!.Value, ct);

            if (!recorded)
            {
                _logger.LogWarning(
                    "Expiry notice for secret {SecretId} ({SecretName}) was not recorded; will retry next sweep",
                    secret.Id, secret.Name);
                continue;
            }

            secret.ExpiryNoticeSentForUtc = secret.ExpiresAtUtc;
            await repository.MarkExpiryNoticeSentAsync(secret, ct); // marker-only write — deliberately not audited

            _logger.LogInformation(
                "Expiry notice sent for secret {SecretId} ({SecretName}); TTL elapses at {ExpiresAtUtc}",
                secret.Id, secret.Name, secret.ExpiresAtUtc);
        }
    }

    private async Task ReapExpiredAsync(
        ISecretRepository repository, ISecretService secrets, DateTimeOffset now, CancellationToken ct)
    {
        var expired = await repository.ListExpiredAsync(now, ct);

        foreach (var secret in expired)
        {
            var expiresAtUtc = secret.ExpiresAtUtc;
            if (!await secrets.ShredIfExpiredAsync(secret.Id, ct))
            {
                _logger.LogInformation(
                    "Left expired secret {SecretId} ({SecretName}) for the next sweep: it changed or is being changed", secret.Id, secret.Name);
                continue;
            }

            _logger.LogInformation(
                "Shredded expired secret {SecretId} ({SecretName}); TTL elapsed at {ExpiresAtUtc}",
                secret.Id, secret.Name, expiresAtUtc);
        }
    }
}
