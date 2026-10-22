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

    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct);

    /// <exception cref="EmailAddressTakenException">Another account took the user's email first.</exception>
    Task AddAsync(User user, CancellationToken ct);

    /// <exception cref="EmailAddressTakenException">Another account took the user's email first.</exception>
    Task UpdateAsync(User user, CancellationToken ct);

    Task DeleteAsync(User user, CancellationToken ct);

    Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct);

    /// <summary>Upserts the avatar blob and stamps <see cref="User.AvatarUpdatedAtUtc"/> in one transaction.</summary>
    Task SetAvatarAsync(User user, byte[] png, CancellationToken ct);

    /// <summary>Deletes the avatar blob and clears <see cref="User.AvatarUpdatedAtUtc"/> in one transaction.</summary>
    Task RemoveAvatarAsync(User user, CancellationToken ct);

}
