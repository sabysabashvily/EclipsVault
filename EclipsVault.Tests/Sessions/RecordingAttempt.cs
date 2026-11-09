using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Tests.Sessions;

/// <summary>
/// Runs an authentication attempt in memory. Its events reach <see cref="Recorded"/> only when the
/// attempt commits, and <see cref="RejectCommit"/> makes the commit fail as a rejected audit insert does.
/// </summary>
internal sealed class RecordingAttempt : IAuthenticationAttempt
{
    private List<AuditEntry>? _running;

    public List<AuditEntry> Recorded { get; } = [];
    public bool RejectCommit { get; init; }

    public async Task<T> RunAsync<T>(User account, Func<CancellationToken, Task<T>> attempt, CancellationToken ct)
    {
        _running = [];
        try
        {
            var result = await attempt(ct);
            if (RejectCommit)
            {
                throw new AuditWriteFailedException("The audit store is unavailable.");
            }

            Recorded.AddRange(_running);
            return result;
        }
        finally
        {
            _running = null;
        }
    }

    public void Record(AuditEntry entry)
        => (_running ?? throw new InvalidOperationException("An authentication event needs a running attempt.")).Add(entry);
}
