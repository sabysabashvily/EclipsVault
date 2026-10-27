using System.Security.Claims;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Security;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Controllers.Ui;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Sessions;

public sealed class SessionApiRevocationTests
{
    [Fact]
    public async Task A_registry_failure_still_audits_confirmed_revocations_and_reports_an_incomplete_sign_out()
    {
        var userId = Guid.NewGuid();
        var current = Guid.NewGuid();
        var first = Guid.NewGuid();
        var unavailable = Guid.NewGuid();
        var last = Guid.NewGuid();
        var sessions = new PartlyUnavailableRegistry(current, first, unavailable, last);
        var audit = new RecordingAudit();
        var controller = Controller(sessions, audit, userId, current);

        var result = await controller.RevokeOthers(CancellationToken.None);

        var response = Assert.IsType<JsonResult>(result.Result);
        Assert.Equal(503, response.StatusCode);
        var problem = Assert.IsType<SessionProblemResponse>(response.Value);
        Assert.Equal("sign_out_incomplete", problem.Code);
        Assert.Equal(2, problem.SignedOut);
        Assert.StartsWith("Confirmed 2 other sessions signed out.", problem.Title);
        Assert.True(await sessions.IsRevokedAsync(userId, first));
        Assert.True(await sessions.IsRevokedAsync(userId, last));
        Assert.False(await sessions.IsRevokedAsync(userId, unavailable));
        Assert.False(await sessions.IsRevokedAsync(userId, current));
        Assert.Equal([first, last], audit.Recorded.Select(entry => entry.ResourceId));
    }

    [Fact]
    public async Task A_partial_sign_out_reports_an_audit_failure_for_every_confirmed_removal()
    {
        var userId = Guid.NewGuid();
        var current = Guid.NewGuid();
        var first = Guid.NewGuid();
        var unavailable = Guid.NewGuid();
        var last = Guid.NewGuid();
        var sessions = new PartlyUnavailableRegistry(current, first, unavailable, last);
        var audit = new FailingAudit();
        var controller = Controller(sessions, audit, userId, current);

        var result = await controller.RevokeOthers(CancellationToken.None);

        var response = Assert.IsType<JsonResult>(result.Result);
        Assert.Equal(503, response.StatusCode);
        var problem = Assert.IsType<SessionProblemResponse>(response.Value);
        Assert.Equal("sign_out_incomplete", problem.Code);
        Assert.Equal(2, problem.SignedOut);
        Assert.Contains("couldn't record every confirmed sign-out in the audit trail", problem.Title);
        Assert.Equal([first, last], audit.Attempts.Select(entry => entry.ResourceId));
    }

    [Fact]
    public async Task A_failed_audit_write_reports_the_revocation_and_leaves_the_device_signed_out()
    {
        var sessions = new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System);
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        await RecordAsync(sessions, userId, device);
        var audit = new FailingAudit();
        var controller = Controller(sessions, audit, userId, Guid.NewGuid());

        var response = Assert.IsType<JsonResult>(await controller.Revoke(device, CancellationToken.None));

        Assert.Equal(503, response.StatusCode);
        var problem = Assert.IsType<SessionProblemResponse>(response.Value);
        Assert.Equal("revoked_unrecorded", problem.Code);
        Assert.Null(problem.SignedOut);
        Assert.True(await sessions.IsRevokedAsync(userId, device));
        Assert.Empty(await sessions.ListAsync(userId, Stamp));
        Assert.Equal(device, Assert.Single(audit.Attempts).ResourceId);
    }

    [Fact]
    public async Task Every_other_device_is_revoked_before_audit_failure_and_each_audit_is_still_attempted()
    {
        var sessions = new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System);
        var userId = Guid.NewGuid();
        var current = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await RecordAsync(sessions, userId, current);
        await RecordAsync(sessions, userId, first);
        await RecordAsync(sessions, userId, second);
        var audit = new FailingAudit
        {
            BeforeFailure = async () =>
            {
                Assert.True(await sessions.IsRevokedAsync(userId, first));
                Assert.True(await sessions.IsRevokedAsync(userId, second));
                Assert.False(await sessions.IsRevokedAsync(userId, current));
            }
        };
        var controller = Controller(sessions, audit, userId, current);

        var result = await controller.RevokeOthers(CancellationToken.None);

        var response = Assert.IsType<JsonResult>(result.Result);
        Assert.Equal(503, response.StatusCode);
        var problem = Assert.IsType<SessionProblemResponse>(response.Value);
        Assert.Equal("revoked_unrecorded", problem.Code);
        Assert.Equal(2, problem.SignedOut);
        Assert.Equal(current, Assert.Single(await sessions.ListAsync(userId, Stamp)).SessionId);
        Assert.Equal(2, audit.Attempts.Count);
        Assert.Contains(audit.Attempts, entry => entry.ResourceId == first);
        Assert.Contains(audit.Attempts, entry => entry.ResourceId == second);
    }

    [Fact]
    public async Task A_disconnected_request_still_records_an_already_revoked_device()
    {
        var sessions = new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System);
        var userId = Guid.NewGuid();
        var device = Guid.NewGuid();
        await RecordAsync(sessions, userId, device);
        var audit = new DelayedAudit();
        var controller = Controller(sessions, audit, userId, Guid.NewGuid());
        using var request = new CancellationTokenSource();

        var response = controller.Revoke(device, request.Token);
        await audit.Started.Task;
        Assert.True(await sessions.IsRevokedAsync(userId, device));
        request.Cancel();
        audit.Commit.SetResult();

        Assert.IsType<NoContentResult>(await response);
        Assert.Equal(device, Assert.Single(audit.Recorded).ResourceId);
    }

    [Fact]
    public async Task Disconnecting_during_the_first_bulk_audit_still_records_every_revoked_device()
    {
        var sessions = new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System);
        var userId = Guid.NewGuid();
        var current = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await RecordAsync(sessions, userId, current);
        await RecordAsync(sessions, userId, first);
        await RecordAsync(sessions, userId, second);
        var audit = new DelayedAudit();
        var controller = Controller(sessions, audit, userId, current);
        using var request = new CancellationTokenSource();

        var response = controller.RevokeOthers(request.Token);
        await audit.Started.Task;
        Assert.True(await sessions.IsRevokedAsync(userId, first));
        Assert.True(await sessions.IsRevokedAsync(userId, second));
        request.Cancel();
        audit.Commit.SetResult();

        Assert.Equal(2, Assert.IsType<SignOutOthersResponse>((await response).Value).SignedOut);
        Assert.Equal(2, audit.Recorded.Count);
        Assert.Contains(audit.Recorded, entry => entry.ResourceId == first);
        Assert.Contains(audit.Recorded, entry => entry.ResourceId == second);
        Assert.Equal(current, Assert.Single(await sessions.ListAsync(userId, Stamp)).SessionId);
    }

    private static readonly SessionStamp Stamp = new(0, "current-version");

    private static SessionsController Controller(ISessionRegistry sessions, IAuditSink audit, Guid userId, Guid current)
        => new(sessions, new DevicesUnderStamp(sessions), audit, null!, NullLogger<SessionsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                        new Claim(VaultClaimTypes.SessionId, current.ToString())
                    ], "Cookies"))
                }
            }
        };

    private static Task RecordAsync(ISessionRegistry sessions, Guid userId, Guid device)
        => sessions.RecordSeenAsync(new SessionObservation(userId, device, Stamp, "Chrome on macOS", "198.51.100.1",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));

    private sealed class FailingAudit : IAuditSink
    {
        public List<AuditEntry> Attempts { get; } = [];
        public Func<Task> BeforeFailure { get; init; } = () => Task.CompletedTask;

        public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            Attempts.Add(entry);
            await BeforeFailure();
            throw new AuditWriteFailedException("The audit store is unavailable.");
        }
    }

    private sealed class RecordingAudit : IAuditSink
    {
        public List<AuditEntry> Recorded { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            Recorded.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class PartlyUnavailableRegistry(Guid current, Guid first, Guid unavailable, Guid last) : ISessionRegistry
    {
        private readonly HashSet<Guid> _revoked = [];

        public Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, SessionStamp stamp, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ActiveSession>>(
                [Device(current), Device(first), Device(unavailable), Device(last)]);

        public Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
        {
            if (sessionId == unavailable)
            {
                throw new IOException("The session store disconnected.");
            }

            return Task.FromResult(_revoked.Add(sessionId));
        }

        public Task<bool> TryRetireForReplacementAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> IsRevokedAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
            => Task.FromResult(_revoked.Contains(sessionId));

        public Task RecordSeenAsync(SessionObservation observation, CancellationToken ct = default)
            => throw new NotSupportedException();

        private static ActiveSession Device(Guid id)
            => new(id, "Chrome", "198.51.100.1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    // The account's current stamp is the one every device here was recorded under.
    private sealed class DevicesUnderStamp(ISessionRegistry sessions) : ISignedInDevices
    {
        public Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, CancellationToken ct)
            => sessions.ListAsync(userId, Stamp, ct);
    }

    private sealed class DelayedAudit : IAuditSink
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Commit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<AuditEntry> Recorded { get; } = [];

        public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            if (Recorded.Count == 0)
            {
                var committed = Commit.Task.WaitAsync(ct);
                Started.SetResult();
                await committed;
            }

            Recorded.Add(entry);
        }
    }
}
