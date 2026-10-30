using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Distributed;

/// <summary>
/// The revocation contract that both the in-process and Redis stores must honour: a session is valid
/// while the version it captured is the account's current one, signing out everywhere replaces the
/// version, and a version never comes back, not even after the store loses its state.
/// </summary>
public class InMemorySessionRevocationServiceTests
{
    private static InMemorySessionRevocationService NewService()
        => new(NullLogger<InMemorySessionRevocationService>.Instance);

    [Fact]
    public async Task An_account_that_never_signed_in_has_no_current_version()
        => Assert.Null(await NewService().FindAsync(Guid.NewGuid()));

    [Fact]
    public async Task A_captured_version_stays_current_until_the_account_signs_out_everywhere()
    {
        var service = NewService();
        var account = Guid.NewGuid();

        var captured = await service.CaptureAsync(account);

        Assert.Equal(captured, await service.CaptureAsync(account));
        Assert.Equal(captured, await service.FindAsync(account));
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_the_captured_version()
    {
        var service = NewService();
        var account = Guid.NewGuid();
        var captured = await service.CaptureAsync(account);

        await service.RevokeAsync(account);

        Assert.NotEqual(captured, await service.FindAsync(account));
    }

    [Fact]
    public async Task A_session_captured_between_two_sign_outs_in_the_same_instant_ends_with_the_second()
    {
        var service = NewService();
        var account = Guid.NewGuid();
        await service.RevokeAsync(account);
        var between = await service.CaptureAsync(account);

        await service.RevokeAsync(account);

        Assert.NotEqual(between, await service.FindAsync(account));
    }

    [Fact]
    public async Task A_session_captured_after_signing_out_everywhere_stays_current()
    {
        var service = NewService();
        var account = Guid.NewGuid();
        await service.RevokeAsync(account);

        var afterwards = await service.CaptureAsync(account);

        Assert.Equal(afterwards, await service.FindAsync(account));
    }

    [Fact]
    public async Task Concurrent_first_sign_ins_capture_one_version()
    {
        var service = NewService();
        var account = Guid.NewGuid();

        var captured = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => service.CaptureAsync(account))));

        Assert.Single(captured.Distinct());
        Assert.Equal(captured[0], await service.FindAsync(account));
    }

    [Fact]
    public async Task Concurrent_sign_outs_leave_no_earlier_version_current()
    {
        var service = NewService();
        var account = Guid.NewGuid();
        var captured = await service.CaptureAsync(account);

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => service.RevokeAsync(account))));

        var current = await service.FindAsync(account);
        Assert.NotNull(current);
        Assert.NotEqual(captured, current);
    }

    [Fact]
    public async Task Signing_out_everywhere_leaves_other_accounts_current()
    {
        var service = NewService();
        var revoked = Guid.NewGuid();
        var other = Guid.NewGuid();
        var otherVersion = await service.CaptureAsync(other);

        await service.RevokeAsync(revoked);

        Assert.Equal(otherVersion, await service.FindAsync(other));
    }

    [Fact]
    public async Task A_restarted_store_has_no_version_and_never_issues_the_lost_one_again()
    {
        var account = Guid.NewGuid();
        var lost = await NewService().CaptureAsync(account);
        var restarted = NewService();

        Assert.Null(await restarted.FindAsync(account));
        Assert.NotEqual(lost, await restarted.CaptureAsync(account));
    }
}
