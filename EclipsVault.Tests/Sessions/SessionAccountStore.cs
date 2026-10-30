using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Tests.Sessions;

internal sealed class SessionAccountStore : IUserRepository
{
    private readonly User? _user;
    public SessionAccountStore(User? user) => _user = user;

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
        => Task.FromResult(_user);

    public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => throw new NotSupportedException();

    public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => Task.FromResult<User?>(null);
    public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(_user);
    public Task<bool> ReloadAsync(User user, CancellationToken ct) => Task.FromResult(true);
    public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<User>>([]);
    public Task AddAsync(User user, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateAsync(User user, CancellationToken ct) => Task.CompletedTask;
    public Task DeleteAsync(User user, CancellationToken ct) => Task.CompletedTask;
    public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => Task.FromResult<byte[]?>(null);
    public Task StageAvatarAsync(User user, byte[] png, CancellationToken ct) => Task.CompletedTask;
    public Task StageAvatarRemovalAsync(User user, CancellationToken ct) => Task.CompletedTask;
    public Task<bool> TryAcceptTotpStepAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> TryCompleteTotpEnrollmentAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> RecordFailedAttemptAsync(User user, LockoutPolicy lockout, DateTimeOffset nowUtc, CancellationToken ct) => throw new NotSupportedException();
    public Task ClearFailedAttemptsAsync(User user, CancellationToken ct) => throw new NotSupportedException();
}

