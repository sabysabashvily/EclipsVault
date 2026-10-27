namespace EclipsVault.Core.Application.Sessions;

/// <summary>
/// What a session or pending sign-in was issued under: the account's credential generation and its
/// session revocation version. When either one has moved on, the session or pending sign-in is refused.
/// </summary>
public sealed record SessionStamp(long CredentialGeneration, string RevocationVersion);
