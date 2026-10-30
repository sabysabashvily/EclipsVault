namespace EclipsVault.Core.Application.Profile;

/// <summary>The signed-in session a Profile operation belongs to, as its cookie identifies it.</summary>
public sealed record ProfileSession(Guid AccountId, Guid SessionId, SessionStamp Stamp);
