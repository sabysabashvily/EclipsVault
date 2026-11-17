using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Sessions;

/// <summary>
/// The password stage answers a locked account no faster than any other: it hashes the password
/// before it looks at the lockout, so the time to answer does not confirm that the username exists.
/// </summary>
public sealed class PasswordStageTests
{
    private sealed class CountingHasher : IPasswordHasher
    {
        public int Verifications { get; private set; }

        public PasswordHashResult Hash(string password) => throw new NotSupportedException();

        public bool Verify(string password, byte[] hash, byte[] salt)
        {
            Verifications++;
            return password == "correct-password";
        }
    }

    [Theory]
    [InlineData("correct-password")]
    [InlineData("wrong-password")]
    public async Task A_locked_account_hashes_the_password_before_it_is_refused(string password)
    {
        var account = new User { Id = Guid.NewGuid(), Username = "locked", LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var hasher = new CountingHasher();
        var attempt = new RecordingAttempt();
        var service = new VaultAuthenticationService(
            new SessionAccountStore(account), hasher, null!, null!, null!, null!, null!, attempt,
            new InMemorySessionRevocationService(NullLogger<InMemorySessionRevocationService>.Instance), TimeProvider.System);

        var result = await service.ValidateCredentialsAsync("locked", password, CancellationToken.None);

        Assert.Equal(CredentialStatus.Invalid, result.Status);
        Assert.Equal(1, hasher.Verifications);
        var refused = Assert.Single(attempt.Recorded);
        Assert.Equal(AuditAction.LoginFailed, refused.Action);
        Assert.Equal("Account is locked out", refused.Details);
    }
}
