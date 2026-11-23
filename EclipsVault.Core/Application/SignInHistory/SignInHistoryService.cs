using EclipsVault.Core.Application.Abstractions;

namespace EclipsVault.Core.Application.SignInHistory;

/// <summary>
/// A thin read-model aggregator: it fetches the caller's own sign-in audit rows (filtered DB-side
/// to just the authentication actions) and defers all shaping to <see cref="SignInHistoryBuilder"/>.
/// Actor-scoped by user id, so it can only ever return the caller's own history.
/// </summary>
public sealed class SignInHistoryService : ISignInHistoryService
{
    /// <summary>How many recent sign-in events to consider — plenty of context without an unbounded scan.</summary>
    public const int MaxEvents = 200;

    private readonly IAuditLogReader _audit;

    public SignInHistoryService(IAuditLogReader audit) => _audit = audit;

    public async Task<SignInHistory> GetForUserAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty)
        {
            return SignInHistory.Empty;
        }

        var rows = await _audit.ListForActorByActionsAsync(
            userId, SignInEventClassifier.RelevantActions, MaxEvents, ct);

        var records = rows
            .Select(r => new SignInAuditRecord(r.TimestampUtc, r.Action, r.SourceIp))
            .ToList();

        return SignInHistoryBuilder.Build(records);
    }
}
