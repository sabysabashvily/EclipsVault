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
        Assert.Empty(await sessions.ListAsync(userId));
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
        Assert.Equal(current, Assert.Single(await sessions.ListAsync(userId)).SessionId);
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
        Assert.Equal(current, Assert.Single(await sessions.ListAsync(userId)).SessionId);
    }

    private static SessionsController Controller(ISessionRegistry sessions, IAuditSink audit, Guid userId, Guid current)
        => new(sessions, audit, null!)
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
        => sessions.RecordSeenAsync(new SessionObservation(userId, device, "Chrome on macOS", "198.51.100.1",
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
