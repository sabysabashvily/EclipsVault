using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.SignInHistory;

/// <summary>The minimal projection of an audit row the builder needs — kept tiny so it is trivial to test.</summary>
public sealed record SignInAuditRecord(DateTimeOffset TimestampUtc, AuditAction Action, string SourceIp);

/// <summary>
/// Pure, deterministic assembly of a <see cref="SignInHistory"/> from a user's raw sign-in audit
/// rows. It classifies each row, derives the location signal from the stream itself (no external
/// lookup), and rolls up a summary. No I/O and no clock — given the same rows it always yields the
/// same result, so the whole thing is exhaustively testable.
///
/// <para>Location signal (order-independent): an IP is "established" if the user has *any* successful
/// sign-in from it anywhere in the history. The first successful sign-in from an established IP reads
/// as <see cref="SignInLocationFlag.FirstSeen"/> ("you've not signed in from here before"). A
/// failed/blocked attempt from an IP with <em>no</em> success anywhere is
/// <see cref="SignInLocationFlag.Unfamiliar"/> — "someone tried from a place you've never signed in
/// from". A mistyped password that is immediately followed by a success from the same new device is
/// therefore not flagged, because that IP does end up with a success.</para>
/// </summary>
public static class SignInHistoryBuilder
{
    public static SignInHistory Build(IReadOnlyList<SignInAuditRecord> rows)
    {
        if (rows.Count == 0)
        {
            return SignInHistory.Empty;
        }

        // Walk oldest → newest so "first successful sign-in from here" is the chronologically first.
        // OrderBy is a stable sort, so rows sharing an instant keep their given order.
        var classified = rows
            .OrderBy(r => r.TimestampUtc)
            .Select(r => (Row: r, Descriptor: SignInEventClassifier.Classify(r.Action)))
            .Where(x => x.Descriptor is not null)
            .Select(x => (x.Row, Descriptor: x.Descriptor!))
            .ToList();

        // IPs the user has ever successfully signed in from — the set of "established" locations.
        var establishedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (row, descriptor) in classified)
        {
            if (descriptor.Outcome == SignInOutcome.Success)
            {
                establishedIps.Add(row.SourceIp);
            }
        }

        var firstSuccessSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eventsOldestFirst = new List<SignInEvent>(classified.Count);

        foreach (var (row, descriptor) in classified)
        {
            var flag = descriptor.Outcome switch
            {
                // Add returns true only the first time we pass this IP's success → "first seen".
                SignInOutcome.Success => firstSuccessSeen.Add(row.SourceIp)
                    ? SignInLocationFlag.FirstSeen
                    : SignInLocationFlag.None,

                // A rejected/blocked attempt from a place with no success anywhere is the alarm.
                SignInOutcome.Failed or SignInOutcome.Blocked => establishedIps.Contains(row.SourceIp)
                    ? SignInLocationFlag.None
                    : SignInLocationFlag.Unfamiliar,

                _ => SignInLocationFlag.None
            };

            // An administrator's unlock is account state, not a sign-in from their address.
            // Retain the notification without disclosing that address outside the audit viewer.
            var sourceIp = row.Action == AuditAction.AccountUnlocked ? "internal" : row.SourceIp;
            eventsOldestFirst.Add(new SignInEvent(
                row.TimestampUtc, descriptor.Outcome, descriptor.Method, descriptor.Title, sourceIp, flag));
        }

        var summary = Summarize(eventsOldestFirst);

        // Present newest first, matching every other timeline in the app.
        eventsOldestFirst.Reverse();
        return new SignInHistory(eventsOldestFirst, summary);
    }

    private static SignInSummary Summarize(IReadOnlyList<SignInEvent> events)
    {
        if (events.Count == 0)
        {
            return SignInSummary.Empty;
        }

        var successCount = 0;
        var failedCount = 0;
        var suspiciousCount = 0;
        DateTimeOffset? lastSuccess = null;
        DateTimeOffset? lastFailed = null;
        var distinctIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in events)
        {
            if (e.Outcome != SignInOutcome.Info)
            {
                distinctIps.Add(e.SourceIp);
            }

            switch (e.Outcome)
            {
                case SignInOutcome.Success:
                    successCount++;
                    if (lastSuccess is null || e.TimestampUtc > lastSuccess) lastSuccess = e.TimestampUtc;
                    break;
                case SignInOutcome.Failed:
                case SignInOutcome.Blocked:
                    failedCount++;
                    if (lastFailed is null || e.TimestampUtc > lastFailed) lastFailed = e.TimestampUtc;
                    if (e.LocationFlag == SignInLocationFlag.Unfamiliar) suspiciousCount++;
                    break;
            }
        }

        return new SignInSummary(successCount, failedCount, suspiciousCount, distinctIps.Count, lastSuccess, lastFailed);
    }
}
