using EclipsVault.Core.Domain;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct);

    /// <summary>
    /// Looks up an account by either its login username or its email address. Finds none when the
    /// identifier is one account's username and another's email: guessing between them could
    /// hand one person's sign-in to someone else.
    /// </summary>
    Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct);

    /// <summary>Looks up an account by its email address alone, compared as <see cref="EmailIdentity"/> compares it.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken ct);

    /// <summary>
    /// Whether an account other than <paramref name="accountId"/> signs in with this address, as its
    /// email or as its username.
    /// </summary>
    Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct);

    /// <summary>Returns the normalized emails whose local part is <c>localPrefix.*</c> at the given domain — used to pick the next unique sequence.</summary>
    Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct);

    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Replaces the values of <paramref name="user"/> with the account as it is stored now.</summary>
    /// <returns>False when the account no longer exists.</returns>
    Task<bool> ReloadAsync(User user, CancellationToken ct);

    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct);

    /// <exception cref="EmailAddressTakenException">Another account took the user's email first.</exception>
    Task AddAsync(User user, CancellationToken ct);

    /// <summary>
    /// Saves the edits made to <paramref name="user"/> as loaded, writing only the fields that changed.
    /// </summary>
    /// <exception cref="EmailAddressTakenException">Another account took the user's email first.</exception>
    /// <exception cref="AccountChangedException">Another change to the account was saved after it was loaded.</exception>
    Task UpdateAsync(User user, CancellationToken ct);

    Task DeleteAsync(User user, CancellationToken ct);

    Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Stages the avatar blob and <see cref="User.AvatarUpdatedAtUtc"/>, to be saved through
    /// <see cref="IAccountChangeCommitter"/>.
    /// </summary>
    Task StageAvatarAsync(User user, byte[] png, CancellationToken ct);

    /// <summary>
    /// Stages deleting the avatar blob and clearing <see cref="User.AvatarUpdatedAtUtc"/>, to be saved
    /// through <see cref="IAccountChangeCommitter"/>. Stages nothing when the account has no avatar.
    /// </summary>
    Task StageAvatarRemovalAsync(User user, CancellationToken ct);

    /// <summary>
    /// Records <paramref name="step"/> as the last authenticator step the account accepted, immediately
    /// and only if the account still has the enabled authenticator it was loaded with and has not
    /// accepted this step or a later one.
    /// </summary>
    /// <returns>True when this call accepted the step; false when another request got there first.</returns>
    Task<bool> TryAcceptTotpStepAsync(User user, long step, CancellationToken ct);

    /// <summary>
    /// Enables the authenticator the account is enrolling, spending <paramref name="step"/> and clearing
    /// failed attempts, immediately and only if the enrollment is still pending with that secret.
    /// </summary>
    /// <returns>True when this call completed the enrollment; false when another request got there first.</returns>
    Task<bool> TryCompleteTotpEnrollmentAsync(User user, long step, CancellationToken ct);

    /// <summary>
    /// Counts one failed attempt, immediately, locking the account when it reaches the policy's limit.
    /// Concurrent failures each count.
    /// </summary>
    /// <returns>True when this failure locked the account.</returns>
    Task<bool> RecordFailedAttemptAsync(User user, LockoutPolicy lockout, DateTimeOffset nowUtc, CancellationToken ct);

    /// <summary>Clears failed attempts and any lockout, immediately.</summary>
    Task ClearFailedAttemptsAsync(User user, CancellationToken ct);
}
