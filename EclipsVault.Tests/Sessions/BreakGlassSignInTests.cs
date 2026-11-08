using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Security;
using EclipsVault.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Sessions;

public sealed class BreakGlassSignInTests
{
    [Fact]
    public async Task A_refused_session_leaves_the_intrusion_block_in_place()
    {
        var account = new User { Id = Guid.NewGuid(), Username = "glass", CredentialGeneration = 2 };
        var revocation = new InMemorySessionRevocationService(NullLogger<InMemorySessionRevocationService>.Instance);
        var stamp = new SessionStamp(1, await revocation.CaptureAsync(account.Id));
        var source = IPAddress.Parse("198.18.93.9");
        var blacklist = new FakeBlacklist { Blocked = true };
        var attempt = new RecordingAttempt();
        var audit = new RecordingAudit();

        var finished = await Finish(account, revocation, blacklist, attempt, audit)
            .FinishAsync(Context(), UserDto.From(account), stamp, source, default);

        Assert.False(finished.SessionIssued);
        Assert.False(finished.BlockLifted);
        Assert.True(blacklist.Blocked);
        Assert.Equal(0, blacklist.Unblocks);
        Assert.Equal(AuditAction.SignInRefused, Assert.Single(attempt.Recorded).Action);
        Assert.Empty(audit.Recorded);
    }

    [Fact]
    public async Task An_issued_session_records_the_recovery_and_then_lifts_the_block()
    {
        var account = new User { Id = Guid.NewGuid(), Username = "glass", CredentialGeneration = 1 };
        var revocation = new InMemorySessionRevocationService(NullLogger<InMemorySessionRevocationService>.Instance);
        var stamp = new SessionStamp(1, await revocation.CaptureAsync(account.Id));
        var source = IPAddress.Parse("198.18.93.9");
        var blacklist = new FakeBlacklist { Blocked = true };
        var audit = new RecordingAudit { Blacklist = blacklist };

        var finished = await Finish(account, revocation, blacklist, new RecordingAttempt(), audit)
            .FinishAsync(Context(), UserDto.From(account), stamp, source, default);

        Assert.True(finished.SessionIssued);
        Assert.True(finished.BlockLifted);
        Assert.False(blacklist.Blocked);
        var recovery = Assert.Single(audit.Recorded);
        Assert.Equal("Break-glass recovery from 198.18.93.9; block lifted: True", recovery.Details);
        Assert.True(audit.BlockedWhenRecorded);
    }

    [Fact]
    public async Task A_rejected_recovery_row_leaves_the_intrusion_block_in_place()
    {
        var account = new User { Id = Guid.NewGuid(), Username = "glass", CredentialGeneration = 1 };
        var revocation = new InMemorySessionRevocationService(NullLogger<InMemorySessionRevocationService>.Instance);
        var stamp = new SessionStamp(1, await revocation.CaptureAsync(account.Id));
        var source = IPAddress.Parse("198.18.93.9");
        var blacklist = new FakeBlacklist { Blocked = true };
        var attempt = new RecordingAttempt();
        var audit = new RecordingAudit { RejectRecovery = true };

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => Finish(account, revocation, blacklist, attempt, audit)
            .FinishAsync(Context(), UserDto.From(account), stamp, source, default));

        Assert.True(blacklist.Blocked);
        Assert.Equal(0, blacklist.Unblocks);
        Assert.Equal(AuditAction.LoginSucceeded, Assert.Single(attempt.Recorded).Action);
        Assert.Empty(audit.Recorded);
    }

    private static BreakGlassSignIn Finish(
        User account, InMemorySessionRevocationService revocation, FakeBlacklist blacklist, RecordingAttempt attempt, IAuditSink audit)
    {
        var issuer = new SessionIssuer(new SessionAccountStore(account), revocation,
            new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System),
            attempt, NullLogger<SessionIssuer>.Instance);
        return new BreakGlassSignIn(issuer, blacklist, audit);
    }

    private static DefaultHttpContext Context()
        => new()
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(new AcceptingAuthentication()).BuildServiceProvider(),
        };

    private sealed class AcceptingAuthentication : IAuthenticationService
    {
        public Task SignInAsync(HttpContext context, string? scheme, System.Security.Claims.ClaimsPrincipal principal, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => throw new NotSupportedException();
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
    }

    private sealed class RecordingAudit : IAuditSink
    {
        public List<AuditEntry> Recorded { get; } = [];
        public bool RejectRecovery { get; init; }
        public FakeBlacklist? Blacklist { get; init; }
        public bool BlockedWhenRecorded { get; private set; }

        public Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            if (RejectRecovery)
            {
                throw new AuditWriteFailedException("The audit store is unavailable.");
            }

            BlockedWhenRecorded = Blacklist?.Blocked ?? false;
            Recorded.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBlacklist : IIpBlacklist
    {
        public bool Blocked { get; set; }
        public int Unblocks { get; private set; }

        public Task BlockAsync(string sourceIp, string reason, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> IsBlockedAsync(IPAddress address, CancellationToken ct = default) => Task.FromResult(Blocked);

        public Task<IReadOnlyList<BlockedRangeDto>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> UnblockAsync(string network, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> UnblockAddressAsync(IPAddress address, CancellationToken ct = default)
        {
            Unblocks++;
            var lifted = Blocked;
            Blocked = false;
            return Task.FromResult(lifted);
        }
    }
}
