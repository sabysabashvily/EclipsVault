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
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.Tests.Sessions;

/// <summary>
/// Break-glass keeps its rows and their order, and a lift whose row is refused is undone exactly. The
/// source stays refused from before any block is removed until the lift is settled, and stays refused
/// when its blocks cannot be confirmed back as they were (ADR-0032). These run on the in-process
/// blacklist; <c>RedisRecoveryLiftTests</c> runs the same guard on Redis across two replicas.
/// </summary>
public sealed class BreakGlassSignInTests
{
    private static readonly IPAddress Source = IPAddress.Parse("198.18.93.9");

    [Fact]
    public async Task A_refused_session_leaves_the_intrusion_block_in_place()
    {
        var glass = await Glass.CreateAsync(credentialGeneration: 2);
        await glass.Blacklist.BlockAsync(Source.ToString(), "probe");
        var before = await glass.Blacklist.ListAsync();

        var finished = await glass.FinishAsync(Source);

        Assert.False(finished.SessionIssued);
        Assert.False(finished.BlockLifted);
        Assert.Equal(before, await glass.Blacklist.ListAsync());
        Assert.True(await glass.Blacklist.IsBlockedAsync(Source));
        Assert.Equal(AuditAction.SignInRefused, Assert.Single(glass.Attempt.Recorded).Action);
        Assert.Empty(glass.Audit.Recorded);
    }

    [Fact]
    public async Task An_issued_session_authorizes_the_lift_then_lifts_the_block_and_records_it()
    {
        var glass = await Glass.CreateAsync();
        await glass.Blacklist.BlockAsync(Source.ToString(), "probe");

        var finished = await glass.FinishAsync(Source);

        Assert.True(finished.SessionIssued);
        Assert.True(finished.BlockLifted);
        Assert.False(await glass.Blacklist.IsBlockedAsync(Source));
        Assert.Equal([AuditAction.BreakGlassRecovery, AuditAction.IpRangeUnblocked], glass.Audit.Recorded.Select(entry => entry.Action));
        // The source is still refused while the lift's own row is written: nothing is granted before it commits.
        Assert.Equal([true, true], glass.Audit.SourceBlockedAtEachWrite);
        Assert.Equal("198.18.93.9/32", glass.Audit.Recorded[1].ResourceName);
        Assert.Equal("Intrusion-defence block lifted by break-glass recovery: 198.18.93.9/32", glass.Audit.Recorded[1].Details);
    }

    [Fact]
    public async Task The_lift_row_names_every_range_the_lift_removed()
    {
        var glass = await Glass.CreateAsync();
        await glass.Blacklist.BlockAsync(Source.ToString(), "exact host");
        glass.Options.BlockSurroundingRange = true;
        await glass.Blacklist.BlockAsync("198.18.93.200", "surrounding range");
        await glass.Blacklist.BlockAsync("203.0.113.5", "elsewhere");

        await glass.FinishAsync(Source);

        var lift = Assert.Single(glass.Audit.Recorded, entry => entry.Action == AuditAction.IpRangeUnblocked);
        Assert.Equal("198.18.93.0/24, 198.18.93.9/32", lift.ResourceName);
        Assert.Equal("Intrusion-defence block lifted by break-glass recovery: 198.18.93.0/24, 198.18.93.9/32", lift.Details);
    }

    [Fact]
    public async Task A_lift_that_cannot_be_put_back_lists_its_guard_as_stuck_for_an_administrator()
    {
        var glass = await Glass.CreateAsync();
        glass.Audit.Reject = AuditAction.IpRangeUnblocked;
        glass.Audit.BeforeRejection = () => glass.Blacklist.BlockAsync(Source.ToString(), "raised meanwhile");
        await glass.Blacklist.BlockAsync(Source.ToString(), "probe");

        await Assert.ThrowsAsync<RecoveryIncompleteException>(() => glass.FinishAsync(Source));

        Assert.Equal(["198.18.93.9/32"], await glass.Blacklist.ListStuckGuardsAsync());
    }

    [Fact]
    public async Task A_lift_put_back_exactly_lists_no_stuck_guard()
    {
        var glass = await Glass.CreateAsync();
        glass.Audit.Reject = AuditAction.IpRangeUnblocked;
        await glass.Blacklist.BlockAsync(Source.ToString(), "probe");

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => glass.FinishAsync(Source));

        Assert.Empty(await glass.Blacklist.ListStuckGuardsAsync());
    }

    [Fact]
    public async Task An_unblocked_address_records_the_recovery_and_no_lift()
    {
        var glass = await Glass.CreateAsync();

        var finished = await glass.FinishAsync(Source);

        Assert.True(finished.SessionIssued);
        Assert.False(finished.BlockLifted);
        Assert.Equal(AuditAction.BreakGlassRecovery, Assert.Single(glass.Audit.Recorded).Action);
        Assert.False(await glass.Blacklist.IsBlockedAsync(Source));
    }

    [Fact]
    public async Task A_rejected_recovery_row_leaves_the_intrusion_block_in_place()
    {
        var glass = await Glass.CreateAsync();
        glass.Audit.Reject = AuditAction.BreakGlassRecovery;
        await glass.Blacklist.BlockAsync(Source.ToString(), "probe");
        var before = await glass.Blacklist.ListAsync();

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => glass.FinishAsync(Source));

        Assert.Equal(before, await glass.Blacklist.ListAsync());
        Assert.True(await glass.Blacklist.IsBlockedAsync(Source));
        Assert.Equal(AuditAction.LoginSucceeded, Assert.Single(glass.Attempt.Recorded).Action);
        Assert.Empty(glass.Audit.Recorded);
    }

    [Fact]
    public async Task A_rejected_lift_row_restores_every_overlapping_range_exactly_and_keeps_the_source_refused()
    {
        var glass = await Glass.CreateAsync();
        glass.Audit.Reject = AuditAction.IpRangeUnblocked;
        await glass.Blacklist.BlockAsync(Source.ToString(), "exact host");
        glass.Options.BlockSurroundingRange = true;
        await glass.Blacklist.BlockAsync("198.18.93.200", "surrounding range");
        var before = await glass.Blacklist.ListAsync();
        Assert.Equal(["198.18.93.0/24", "198.18.93.9/32"], before.Select(block => block.Network).Order());

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => glass.FinishAsync(Source));

        Assert.Equal(before, await glass.Blacklist.ListAsync());
        Assert.True(await glass.Blacklist.IsBlockedAsync(Source));
        Assert.True(await glass.Blacklist.IsBlockedAsync(IPAddress.Parse("198.18.93.7")));
        Assert.Equal([true], glass.Audit.SourceBlockedAtEachRejection);
        Assert.Equal([AuditAction.BreakGlassRecovery], glass.Audit.Recorded.Select(entry => entry.Action));
    }

    [Fact]
    public async Task A_disconnect_while_the_lift_row_is_written_does_not_cancel_its_restoration()
    {
        var glass = await Glass.CreateAsync();
        using var disconnect = new CancellationTokenSource();
        glass.Audit.Reject = AuditAction.IpRangeUnblocked;
        glass.Audit.BeforeRejection = () =>
        {
            disconnect.Cancel();
            return Task.CompletedTask;
        };
        await glass.Blacklist.BlockAsync(Source.ToString(), "probe");
        var before = await glass.Blacklist.ListAsync();

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => glass.FinishAsync(Source, disconnect.Token));

        Assert.True(disconnect.IsCancellationRequested);
        Assert.Equal(before, await glass.Blacklist.ListAsync());
        Assert.True(await glass.Blacklist.IsBlockedAsync(Source));
    }

    [Fact]
    public async Task Changes_made_while_the_lift_row_is_written_survive_its_restoration()
    {
        var glass = await Glass.CreateAsync();
        glass.Audit.Reject = AuditAction.IpRangeUnblocked;
        glass.Audit.BeforeRejection = () => glass.Blacklist.BlockAsync("203.0.113.5", "raised meanwhile");
        await glass.Blacklist.BlockAsync(Source.ToString(), "probe");
        var before = await glass.Blacklist.ListAsync();

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => glass.FinishAsync(Source));

        var after = await glass.Blacklist.ListAsync();
        Assert.Equal(Assert.Single(before), Assert.Single(after, block => block.Network == "198.18.93.9/32"));
        Assert.Equal("raised meanwhile", Assert.Single(after, block => block.Network == "203.0.113.5/32").Reason);
        Assert.True(await glass.Blacklist.IsBlockedAsync(Source));
        Assert.True(await glass.Blacklist.IsBlockedAsync(IPAddress.Parse("203.0.113.5")));
    }

    [Fact]
    public async Task A_block_that_cannot_be_put_back_as_it_was_keeps_the_source_refused_through_any_lift()
    {
        var glass = await Glass.CreateAsync();
        glass.Audit.Reject = AuditAction.IpRangeUnblocked;
        // Raised on the same range while the lift row is written: kept, so the original cannot return.
        glass.Audit.BeforeRejection = () => glass.Blacklist.BlockAsync(Source.ToString(), "raised meanwhile");
        await glass.Blacklist.BlockAsync(Source.ToString(), "probe");

        await Assert.ThrowsAsync<RecoveryIncompleteException>(() => glass.FinishAsync(Source));

        Assert.Equal("raised meanwhile", Assert.Single(await glass.Blacklist.ListAsync()).Reason);
        Assert.NotNull(await glass.Blacklist.UnblockAsync("198.18.93.9/32"));
        Assert.Empty(await glass.Blacklist.ListAsync());
        Assert.True(await glass.Blacklist.IsBlockedAsync(Source));
        Assert.Equal(AuditAction.LoginSucceeded, Assert.Single(glass.Attempt.Recorded).Action);
    }

    [Fact]
    public async Task The_guard_of_an_unsettled_lift_survives_another_break_glass_lift_of_the_same_source()
    {
        var blacklist = new InMemoryIpBlacklist(TimeProvider.System, Options.Create(new IntrusionResponseOptions()), NullLogger<InMemoryIpBlacklist>.Instance);
        await blacklist.BlockAsync(Source.ToString(), "probe");
        var unsettled = await blacklist.LiftForRecoveryAsync(Source);

        var other = await blacklist.LiftForRecoveryAsync(Source);
        await other.ReleaseAsync();

        Assert.True(unsettled.Lifted);
        Assert.False(other.Lifted);
        Assert.True(await blacklist.IsBlockedAsync(Source));
        await unsettled.ReleaseAsync();
        Assert.False(await blacklist.IsBlockedAsync(Source));
    }

    [Fact]
    public async Task A_guard_that_protected_nothing_gets_a_second_release_before_recovery_is_called_incomplete()
    {
        var account = new User { Id = Guid.NewGuid(), Username = "glass", CredentialGeneration = 1 };
        var revocation = new InMemorySessionRevocationService(NullLogger<InMemorySessionRevocationService>.Instance);
        var stamp = new SessionStamp(1, await revocation.CaptureAsync(account.Id));
        var blacklist = new UnreleasedGuardBlacklist(failedReleases: 1);
        var issuer = new SessionIssuer(new SessionAccountStore(account), revocation,
            new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System),
            new RecordingAttempt(), NullLogger<SessionIssuer>.Instance);

        var finished = await new BreakGlassSignIn(issuer, blacklist, new RecordingAudit(blacklist))
            .FinishAsync(Context(), UserDto.From(account), stamp, Source, default);

        Assert.Equal(new BreakGlassFinish(SessionIssued: true, BlockLifted: false), finished);
        Assert.Equal(2, blacklist.Releases);
    }

    [Fact]
    public async Task A_guard_that_cannot_be_released_twice_is_marked_stuck()
    {
        var account = new User { Id = Guid.NewGuid(), Username = "glass", CredentialGeneration = 1 };
        var revocation = new InMemorySessionRevocationService(NullLogger<InMemorySessionRevocationService>.Instance);
        var stamp = new SessionStamp(1, await revocation.CaptureAsync(account.Id));
        var blacklist = new UnreleasedGuardBlacklist(failedReleases: 2);
        var issuer = new SessionIssuer(new SessionAccountStore(account), revocation,
            new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System),
            new RecordingAttempt(), NullLogger<SessionIssuer>.Instance);

        await Assert.ThrowsAsync<RecoveryIncompleteException>(() => new BreakGlassSignIn(issuer, blacklist, new RecordingAudit(blacklist))
            .FinishAsync(Context(), UserDto.From(account), stamp, Source, default));

        Assert.Equal(1, blacklist.MarkedStuck);
    }

    /// <summary>A blacklist with nothing to lift whose guard fails to release <c>failedReleases</c> times.</summary>
    private sealed class UnreleasedGuardBlacklist(int failedReleases) : IIpBlacklist, IRecoveryLift
    {
        public int Releases { get; private set; }

        public int MarkedStuck { get; private set; }

        public bool Lifted => false;

        public IReadOnlyList<string> Removed => [];

        public string Guard => "the test's guard";

        public Task ReleaseAsync() => ++Releases > failedReleases ? Task.CompletedTask : throw new InvalidOperationException("Store unavailable.");

        public Task<bool> TryRestoreAsync() => throw new NotSupportedException();

        public Task<IRecoveryLift> LiftForRecoveryAsync(IPAddress address) => Task.FromResult<IRecoveryLift>(this);

        public Task<bool> IsBlockedAsync(IPAddress address, CancellationToken ct = default) => Task.FromResult(false);

        public Task BlockAsync(string sourceIp, string reason, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<BlockedRangeDto>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ILiftedIpBlock?> UnblockAsync(string network, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListStuckGuardsAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IClaimedGuard?> ClaimStuckGuardAsync(string network, CancellationToken ct = default) => throw new NotSupportedException();

        public Task MarkStuckAsync()
        {
            MarkedStuck++;
            return Task.CompletedTask;
        }
    }

    private sealed class Glass
    {
        private readonly User _account;
        private readonly SessionStamp _stamp;
        private readonly BreakGlassSignIn _signIn;

        private Glass(User account, SessionStamp stamp, InMemorySessionRevocationService revocation)
        {
            _account = account;
            _stamp = stamp;
            Blacklist = new InMemoryIpBlacklist(TimeProvider.System, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<InMemoryIpBlacklist>.Instance);
            Audit = new RecordingAudit(Blacklist);
            var issuer = new SessionIssuer(new SessionAccountStore(account), revocation,
                new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System),
                Attempt, NullLogger<SessionIssuer>.Instance);
            _signIn = new BreakGlassSignIn(issuer, Blacklist, Audit);
        }

        // Read on every block, so a test can raise blocks of both sizes over one address.
        public IntrusionResponseOptions Options { get; } = new();
        public InMemoryIpBlacklist Blacklist { get; }
        public RecordingAttempt Attempt { get; } = new();
        public RecordingAudit Audit { get; }

        /// <summary>A Top Secret account whose proof was taken under generation 1.</summary>
        public static async Task<Glass> CreateAsync(long credentialGeneration = 1)
        {
            var account = new User { Id = Guid.NewGuid(), Username = "glass", CredentialGeneration = credentialGeneration };
            var revocation = new InMemorySessionRevocationService(NullLogger<InMemorySessionRevocationService>.Instance);
            return new Glass(account, new SessionStamp(1, await revocation.CaptureAsync(account.Id)), revocation);
        }

        public Task<BreakGlassFinish> FinishAsync(IPAddress source, CancellationToken ct = default)
            => _signIn.FinishAsync(Context(), UserDto.From(_account), _stamp, source, ct);
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

    /// <summary>Records committed rows, and refuses the row of <see cref="Reject"/> as a rejected audit insert does.</summary>
    private sealed class RecordingAudit(IIpBlacklist blacklist) : IAuditSink
    {
        public List<AuditEntry> Recorded { get; } = [];
        public AuditAction? Reject { get; set; }
        public Func<Task>? BeforeRejection { get; set; }
        public List<bool> SourceBlockedAtEachWrite { get; } = [];
        public List<bool> SourceBlockedAtEachRejection { get; } = [];

        public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            var blocked = await blacklist.IsBlockedAsync(Source, CancellationToken.None);
            if (entry.Action == Reject)
            {
                SourceBlockedAtEachRejection.Add(blocked);
                if (BeforeRejection is not null)
                {
                    await BeforeRejection();
                }

                throw new AuditWriteFailedException("The audit store is unavailable.");
            }

            SourceBlockedAtEachWrite.Add(blocked);
            Recorded.Add(entry);
        }
    }
}
