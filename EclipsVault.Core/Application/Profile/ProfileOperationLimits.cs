namespace EclipsVault.Core.Application.Profile;

/// <summary>
/// How long and how many prepared Profile operations may exist, so preparing one can never become
/// unbounded pending state or unbounded password hashing (ADR-0022).
/// </summary>
public static class ProfileOperationLimits
{
    /// <summary>A prepared operation must be verified within this time.</summary>
    public static readonly TimeSpan VerificationWindow = TimeSpan.FromMinutes(10);

    /// <summary>A verified operation must be committed within this time.</summary>
    public static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The deadline the server enforces on a passkey prompt, matching the timeout it advertises.</summary>
    public static readonly TimeSpan CeremonyLifetime = TimeSpan.FromSeconds(120);

    /// <summary>Usable operations an account may hold across its sessions; each session holds at most one.</summary>
    public const int LivePerAccount = 5;

    /// <summary>New passwords this node hashes at once while preparing; more wait.</summary>
    public const int ConcurrentCandidateHashes = 2;

    /// <summary>How long a preparation waits for a hashing slot before it is refused as throttled.</summary>
    public static readonly TimeSpan CandidateHashWait = TimeSpan.FromSeconds(2);
}
