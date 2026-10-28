using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Application.Passkeys;
using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Application.SecurityCheckup;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.SecurityCheckupTests;

/// <summary>
/// The service is a thin read-model aggregator: it gathers the four posture inputs and defers all
/// scoring to the evaluator. These tests pin that it maps each input into the posture, keys every
/// read to the caller's own id (self-scoping), and returns null for a vanished account.
/// </summary>
public class SecurityCheckupServiceTests
{
    // Fakes answer only for User, so a read keyed to anyone else shows up as missing data.
    private static readonly Guid User = Guid.NewGuid();

    private sealed class FakeProfile : IProfileService
    {
        private readonly ProfileDto? _dto;
        public FakeProfile(ProfileDto? dto) => _dto = dto;

        public Task<ProfileDto?> GetAsync(Guid userId, CancellationToken ct)
        {
            return Task.FromResult(userId == User ? _dto : null);
        }

        public Task<string> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> ChangePasswordAsync(Guid userId, long credentialGeneration, string currentPassword, string newPassword, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task SetAvatarAsync(Guid userId, byte[] uploadedBytes, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveAvatarAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResetOwnMfaAsync(Guid userId, long credentialGeneration, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakePasskeys : IPasskeyService
    {
        private readonly int _count;
        public FakePasskeys(int count) => _count = count;

        public Task<IReadOnlyList<PasskeySummary>> ListForUserAsync(Guid userId, CancellationToken ct)
        {
            if (userId != User) return Task.FromResult<IReadOnlyList<PasskeySummary>>([]);
            IReadOnlyList<PasskeySummary> list = Enumerable.Range(0, _count)
                .Select(i => new PasskeySummary(Guid.NewGuid(), $"key{i}", DateTimeOffset.UtcNow))
                .ToList();
            return Task.FromResult(list);
        }

        public Task<PasskeyCeremonyOptions> BeginRegistrationAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PasskeyRegistrationResult> CompleteRegistrationAsync(Guid userId, long credentialGeneration, string expectedChallenge, string responseJson, string? nickname, CancellationToken ct) => throw new NotSupportedException();
        public Task<PasskeyAttestation> VerifyRegistrationAsync(Guid userId, string expectedChallenge, string responseJson, string nickname, CancellationToken ct) => throw new NotSupportedException();
        public Task<PasskeyCeremonyOptions> BeginAccountVerificationAsync(Guid userId, Guid? excludedPasskeyId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PasskeyProof> VerifyAccountAssertionAsync(EclipsVault.Core.Domain.Entities.User account, string expectedChallenge, string responseJson, Guid? excludedPasskeyId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PasskeyCeremonyOptions> BeginAssertionAsync(string? usernameOrEmail, CancellationToken ct) => throw new NotSupportedException();
        public Task<PasskeyAssertionResult> CompleteAssertionAsync(string expectedChallenge, PasskeyNamedAccount? namedAccount, string responseJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<long?> RemoveAsync(Guid userId, long credentialGeneration, Guid passkeyId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeRecovery : IMfaRecoveryService
    {
        private readonly int _remaining;
        public FakeRecovery(int remaining) => _remaining = remaining;
        public Task<int> CountRemainingAsync(Guid userId, CancellationToken ct) => Task.FromResult(userId == User ? _remaining : 0);
        public Task<RecoveryCodeSet> GenerateAsync(Guid userId, long credentialGeneration, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> StageNewSetAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeSessions : ISignedInDevices
    {
        private readonly int _count;
        public FakeSessions(int count) => _count = count;

        public Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, CancellationToken ct)
        {
            if (userId != User) return Task.FromResult<IReadOnlyList<ActiveSession>>([]);
            IReadOnlyList<ActiveSession> list = Enumerable.Range(0, _count)
                .Select(_ => new ActiveSession(Guid.NewGuid(), "Chrome on macOS", "203.0.113.5", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))
                .ToList();
            return Task.FromResult(list);
        }

    }

    private static ProfileDto Profile(bool totp) =>
        new(User, "alice", "Alice", "alice@example.com", ClearanceLevel.Standard, "PHOENIX", totp, false);

    [Fact]
    public async Task A_vanished_account_yields_null()
    {
        var service = new SecurityCheckupService(
            new FakeProfile(null), new FakePasskeys(1), new FakeRecovery(5), new FakeSessions(1));

        Assert.Null(await service.GetForUserAsync(User, CancellationToken.None));
    }

    [Fact]
    public async Task Two_step_a_passkey_and_eight_codes_grade_the_account_strong_and_all_clear()
    {
        var service = new SecurityCheckupService(
            new FakeProfile(Profile(totp: true)), new FakePasskeys(1), new FakeRecovery(8), new FakeSessions(1));

        var checkup = await service.GetForUserAsync(User, CancellationToken.None);

        Assert.NotNull(checkup);
        Assert.Equal(SecurityGrade.Strong, checkup!.Grade);
        Assert.True(checkup.AllClear);
    }

    [Fact]
    public async Task No_two_step_no_passkey_no_codes_surfaces_two_step_as_the_next_step()
    {
        var service = new SecurityCheckupService(
            new FakeProfile(Profile(totp: false)), new FakePasskeys(0), new FakeRecovery(0), new FakeSessions(1));

        var checkup = await service.GetForUserAsync(User, CancellationToken.None);

        Assert.Equal(SecurityCheckKind.TwoStepSignIn, checkup!.TopPriority!.Kind);
    }

    [Fact]
    public async Task Every_posture_input_is_read_for_the_caller_only()
    {
        var service = new SecurityCheckupService(
            new FakeProfile(Profile(totp: true)), new FakePasskeys(2), new FakeRecovery(7), new FakeSessions(4));

        var checkup = await service.GetForUserAsync(User, CancellationToken.None);

        Assert.NotNull(checkup);
        Assert.Equal("An authenticator app is protecting your sign-in.",
            Assert.Single(checkup.Checks, check => check.Kind == SecurityCheckKind.TwoStepSignIn).Detail);
        Assert.Equal("You have 2 passkeys for phishing-resistant sign-in.",
            Assert.Single(checkup.Checks, check => check.Kind == SecurityCheckKind.Passkey).Detail);
        Assert.Equal("You have 7 one-time backup codes.",
            Assert.Single(checkup.Checks, check => check.Kind == SecurityCheckKind.BackupCodes).Detail);
        Assert.Equal("4 devices are signed in to your account.",
            Assert.Single(checkup.Checks, check => check.Kind == SecurityCheckKind.SignedInDevices).Detail);
    }

    [Fact]
    public async Task An_unknown_account_has_no_security_checkup()
    {
        var service = new SecurityCheckupService(
            new FakeProfile(Profile(totp: true)), new FakePasskeys(2), new FakeRecovery(7), new FakeSessions(4));

        Assert.Null(await service.GetForUserAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
