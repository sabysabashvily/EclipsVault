namespace EclipsVault.Core.Application.Sessions;

/// <summary>One live interactive session ("signed-in device") as shown to its owner.</summary>
public sealed record ActiveSession(
    Guid SessionId,
    string Device,
    string IpAddress,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc);

/// <summary>
/// A single sighting of a session, handed to the registry on sign-in and on each request.
/// The registry creates the session on first sight and refreshes its last-seen thereafter.
/// The stamp is the one the session was issued under; the registry keeps the first one it sees.
/// </summary>
public sealed record SessionObservation(
    Guid UserId,
    Guid SessionId,
    SessionStamp Stamp,
    string Device,
    string IpAddress,
    DateTimeOffset SeenAtUtc,
    DateTimeOffset ExpiresAtUtc);
