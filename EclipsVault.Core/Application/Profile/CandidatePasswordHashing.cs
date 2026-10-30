namespace EclipsVault.Core.Application.Profile;

/// <summary>
/// Hashes the new password of a prepared password change, a few at a time per node, so preparing
/// changes cannot occupy every core with Argon2id. A singleton: its slots are shared by every request.
/// </summary>
public sealed class CandidatePasswordHashing(IPasswordHasher hasher)
{
    private readonly SemaphoreSlim _slots = new(ProfileOperationLimits.ConcurrentCandidateHashes);

    /// <returns>Null when no slot freed up in time.</returns>
    public async Task<PasswordHashResult?> TryHashAsync(string password, CancellationToken ct)
    {
        if (!await _slots.WaitAsync(ProfileOperationLimits.CandidateHashWait, ct))
        {
            return null;
        }

        try
        {
            return hasher.Hash(password);
        }
        finally
        {
            _slots.Release();
        }
    }
}
