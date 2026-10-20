using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.AccessRequests;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Application.DataExport;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Application.Passkeys;
using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.DataExport;

/// <summary>
/// The export service is a self-scoped read-model aggregator. These tests pin that it keys every
/// read to the caller, maps each source into the export, bounds the activity slice, and — the
/// property that matters most — produces a strictly metadata-only document with no field capable
/// of carrying a secret value or credential material.
/// </summary>
public class PersonalDataExportServiceTests
{
    private static readonly Guid User = Guid.NewGuid();

    private sealed class FakeProfile(ProfileDto? dto) : IProfileService
    {
        public Task<ProfileDto?> GetAsync(Guid userId, CancellationToken ct)
        {
            return Task.FromResult(userId == User ? dto : null);
        }
        public Task<ProfileDto> UpdateAsync(Guid userId, string displayName, string email, CancellationToken ct) => throw new NotSupportedException();
        public Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task SetAvatarAsync(Guid userId, byte[] uploadedBytes, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveAvatarAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResetOwnMfaAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakePasskeys(int count) : IPasskeyService
    {
        public Task<IReadOnlyList<PasskeySummary>> ListForUserAsync(Guid userId, CancellationToken ct)
        {
            if (userId != User) return Task.FromResult<IReadOnlyList<PasskeySummary>>([]);
            IReadOnlyList<PasskeySummary> list = Enumerable.Range(0, count)
                .Select(i => new PasskeySummary(Guid.NewGuid(), $"key{i}", DateTimeOffset.UtcNow)).ToList();
            return Task.FromResult(list);
        }
        public Task<PasskeyCeremonyOptions> BeginRegistrationAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PasskeyRegistrationResult> CompleteRegistrationAsync(Guid userId, string expectedChallenge, string responseJson, string? nickname, CancellationToken ct) => throw new NotSupportedException();
        public Task<PasskeyCeremonyOptions> BeginAssertionAsync(string? usernameOrEmail, CancellationToken ct) => throw new NotSupportedException();
        public Task<PasskeyAssertionResult> CompleteAssertionAsync(string expectedChallenge, string responseJson, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid userId, Guid passkeyId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeRecovery(int remaining) : IMfaRecoveryService
    {
        public Task<int> CountRemainingAsync(Guid userId, CancellationToken ct) => Task.FromResult(userId == User ? remaining : 0);
        public Task<IReadOnlyList<string>> GenerateAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeSessions(int count) : ISessionRegistry
    {
        public Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, CancellationToken ct = default)
        {
            if (userId != User) return Task.FromResult<IReadOnlyList<ActiveSession>>([]);
            IReadOnlyList<ActiveSession> list = Enumerable.Range(0, count)
                .Select(_ => new ActiveSession(Guid.NewGuid(), "Chrome on macOS", "203.0.113.5", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).ToList();
            return Task.FromResult(list);
        }
        public Task RecordSeenAsync(SessionObservation observation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsRevokedAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeAccessRequests(int count) : IAccessRequestService
    {
        public Task<IReadOnlyList<AccessRequestDto>> ListMineAsync(Guid requesterUserId, CancellationToken ct)
        {
            if (requesterUserId != User) return Task.FromResult<IReadOnlyList<AccessRequestDto>>([]);
            IReadOnlyList<AccessRequestDto> list = Enumerable.Range(0, count)
                .Select(i => new AccessRequestDto(Guid.NewGuid(), Guid.NewGuid(), $"prod/db-{i}", "PHOENIX", "alice",
                    "need access", null, AccessRequestStatus.Pending, DateTimeOffset.UtcNow, null, null, null)).ToList();
            return Task.FromResult(list);
        }
        public Task<AccessRequestCreateResult> CreateAsync(Guid secretId, string secretName, string projectKey, Guid requesterUserId, string requesterUsername, string reason, string? deniedReasons, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccessRequestDto>> ListToReviewAsync(bool isAdmin, string reviewerProject, CancellationToken ct) => throw new NotSupportedException();
        public Task<AccessRequestDto?> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ApproveAsync(Guid id, string reviewerUsername, int? ttlDays, string? note, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RejectAsync(Guid id, string reviewerUsername, string? note, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CancelAsync(Guid id, Guid requesterUserId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeAuditReader(int rows) : IAuditLogReader
    {
        public Task<IReadOnlyList<AuditEntryDto>> ListForActorAsync(Guid actorUserId, int skip, int take, CancellationToken ct)
        {
            if (actorUserId != User) return Task.FromResult<IReadOnlyList<AuditEntryDto>>([]);
            IReadOnlyList<AuditEntryDto> list = Enumerable.Range(0, Math.Min(rows, take))
                .Select(_ => new AuditEntryDto(Guid.NewGuid(), DateTimeOffset.UtcNow, "alice", "203.0.113.5",
                    AuditAction.LoginSucceeded, "Account", null, null, false)).ToList();
            return Task.FromResult(list);
        }
        public Task<IReadOnlyList<AuditEntryDto>> ListRecentAsync(int count, string? username, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditEntryDto>> ListForActorByActionsAsync(Guid actorUserId, IReadOnlyCollection<AuditAction> actions, int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountCriticalSinceAsync(DateTimeOffset sinceUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<AuditIntegrityReport> VerifyIntegrityAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static ProfileDto Profile() =>
        new(User, "alice", "Alice Example", "alice@example.com", ClearanceLevel.Standard, "PHOENIX", true, false);

    private static PersonalDataExportService NewService(
        ProfileDto? profile = null, int passkeys = 1, int codes = 5, int devices = 2, int requests = 1, int activity = 3)
        => new(
            new FakeProfile(profile ?? Profile()),
            new FakePasskeys(passkeys),
            new FakeRecovery(codes),
            new FakeSessions(devices),
            new FakeAccessRequests(requests),
            new FakeAuditReader(activity));

    [Fact]
    public async Task A_vanished_account_yields_null()
    {
        var service = new PersonalDataExportService(
            new FakeProfile(null), new FakePasskeys(1), new FakeRecovery(1),
            new FakeSessions(1), new FakeAccessRequests(1), new FakeAuditReader(1));

        Assert.Null(await service.BuildForUserAsync(User, CancellationToken.None));
    }

    [Fact]
    public async Task It_maps_every_source_into_the_export()
    {
        var export = await NewService(passkeys: 2, codes: 7, devices: 3, requests: 4, activity: 5)
            .BuildForUserAsync(User, CancellationToken.None);

        Assert.NotNull(export);
        Assert.Equal("alice", export!.Account.Username);
        Assert.Equal("Standard", export.Account.Clearance);
        Assert.True(export.Security.TwoStepEnabled);
        Assert.Equal(7, export.Security.BackupCodesRemaining);
        Assert.Equal(2, export.Security.Passkeys.Count);
        Assert.Equal(3, export.SignedInDevices.Count);
        Assert.Equal(4, export.AccessRequests.Count);
        Assert.Equal(5, export.RecentActivity.Count);
        Assert.Equal(PersonalDataExport.CurrentSchemaVersion, export.SchemaVersion);
        Assert.Equal(PersonalDataExport.StandardNotice, export.Notice);
    }

    [Fact]
    public async Task Activity_is_rendered_in_plain_language()
    {
        var export = await NewService(activity: 1).BuildForUserAsync(User, CancellationToken.None);

        var entry = Assert.Single(export!.RecentActivity);
        Assert.Equal("Signed in", entry.Action);
    }

    [Fact]
    public async Task The_export_contains_the_callers_account_data()
    {
        var export = await NewService().BuildForUserAsync(User, CancellationToken.None);

        Assert.NotNull(export);
        Assert.Equal("alice", export.Account.Username);
        Assert.Single(export.Security.Passkeys);
        Assert.Equal(5, export.Security.BackupCodesRemaining);
        Assert.Equal(2, export.SignedInDevices.Count);
        Assert.Single(export.AccessRequests);
        Assert.Equal(3, export.RecentActivity.Count);
    }

    [Fact]
    public async Task An_unknown_account_has_no_export()
    {
        var export = await NewService().BuildForUserAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(export);
    }

    [Fact]
    public async Task Recent_activity_contains_at_most_250_entries()
    {
        var export = await NewService(activity: 1000).BuildForUserAsync(User, CancellationToken.None);

        Assert.NotNull(export);
        Assert.Equal(250, export.RecentActivity.Count);
    }

    [Fact]
    public async Task The_serialized_export_contains_only_the_approved_metadata_fields()
    {
        var export = await NewService(passkeys: 1, devices: 1, requests: 1, activity: 1)
            .BuildForUserAsync(User, CancellationToken.None);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(export));
        var root = document.RootElement;

        Assert.Equal(
            ["GeneratedAtUtc", "SchemaVersion", "Account", "Security", "SignedInDevices", "AccessRequests", "RecentActivity", "Notice"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["Username", "DisplayName", "Email", "Clearance", "ProjectKey", "HasCustomAvatar"],
            root.GetProperty("Account").EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["TwoStepEnabled", "BackupCodesRemaining", "Passkeys"],
            root.GetProperty("Security").EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["Nickname", "CreatedAtUtc"],
            root.GetProperty("Security").GetProperty("Passkeys")[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["Device", "IpAddress", "SignedInAtUtc", "LastActiveAtUtc"],
            root.GetProperty("SignedInDevices")[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["SecretName", "ProjectKey", "Status", "Reason", "CreatedAtUtc", "DecidedAtUtc", "DecidedBy"],
            root.GetProperty("AccessRequests")[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["TimestampUtc", "Action", "Resource", "SourceIp"],
            root.GetProperty("RecentActivity")[0].EnumerateObject().Select(property => property.Name));
    }
}
