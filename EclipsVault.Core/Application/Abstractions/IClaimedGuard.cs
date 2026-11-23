namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// An administrator's claim on the stuck break-glass guards of one range. The guards keep refusing until
/// <see cref="ReleaseAsync"/>, which runs only once the release's audit row has committed. While the
/// claim is held, no other release can claim them, so one release records one row.
/// </summary>
public interface IClaimedGuard
{
    string Network { get; }

    /// <summary>The release's row committed: removes the claimed guards, and only those.</summary>
    Task ReleaseAsync();

    /// <summary>The release's row was refused: the guards stay in force and the store attempts to list them again. Never throws.</summary>
    Task AbandonAsync();
}
