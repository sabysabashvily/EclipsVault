using EclipsVault.Core.Application.Activity;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Activity;

/// <summary>
/// The activity service composes the actor-scoped reader into a paged, projected feed. These
/// tests pin the paging arithmetic (skip/take, the +1 look-ahead for HasMore), the input
/// clamping, and that every row is mapped through the describer.
/// </summary>
public class ActivityServiceTests
{
    private static readonly Guid User = Guid.NewGuid();

    /// <summary>Records the last ListForActorAsync call and returns a canned page.</summary>
    private sealed class FakeReader : IAuditLogReader
    {
        private readonly IReadOnlyList<AuditEntryDto> _rows;
        public bool WasQueried { get; private set; }
        public int LastSkip { get; private set; } = -1;
        public int LastTake { get; private set; } = -1;
        public Guid LastActor { get; private set; }

        public FakeReader(IReadOnlyList<AuditEntryDto> rows) => _rows = rows;

        public Task<IReadOnlyList<AuditEntryDto>> ListForActorAsync(Guid actorUserId, int skip, int take, CancellationToken ct)
        {
            WasQueried = true;
            LastActor = actorUserId;
            LastSkip = skip;
            LastTake = take;
            // Return at most `take` rows, like a real Skip/Take would.
            return Task.FromResult<IReadOnlyList<AuditEntryDto>>(_rows.Take(take).ToList());
        }

        public Task<IReadOnlyList<AuditEntryDto>> ListRecentAsync(int count, string? username, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditEntryDto>> ListForActorByActionsAsync(
            Guid actorUserId, IReadOnlyCollection<AuditAction> actions, int take, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<int> CountCriticalSinceAsync(DateTimeOffset sinceUtc, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<AuditIntegrityReport> VerifyIntegrityAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }

    private static AuditEntryDto Row(AuditAction action, string? resource = null, string ip = "203.0.113.5")
        => new(Guid.NewGuid(), DateTimeOffset.UtcNow, "alice", ip, action, "Secret", resource, null, false);

    private static IReadOnlyList<AuditEntryDto> Rows(int count)
        => Enumerable.Range(0, count).Select(_ => Row(AuditAction.LoginSucceeded)).ToList();

    [Fact]
    public async Task Empty_user_returns_an_empty_feed_without_querying()
    {
        var reader = new FakeReader(Rows(5));
        var feed = await new ActivityService(reader).GetForUserAsync(Guid.Empty, 1, 25, CancellationToken.None);

        Assert.Empty(feed.Items);
        Assert.False(feed.HasMore);
        Assert.False(reader.WasQueried);
    }

    [Fact]
    public async Task It_queries_by_the_requested_actor()
    {
        var reader = new FakeReader(Rows(1));
        await new ActivityService(reader).GetForUserAsync(User, 1, 10, CancellationToken.None);

        Assert.Equal(User, reader.LastActor);
    }

    [Fact]
    public async Task Skip_and_take_follow_the_page_and_size()
    {
        var reader = new FakeReader(Rows(50));
        await new ActivityService(reader).GetForUserAsync(User, page: 3, pageSize: 10, CancellationToken.None);

        Assert.Equal(20, reader.LastSkip);   // (3 - 1) * 10
        Assert.Equal(11, reader.LastTake);   // pageSize + 1 look-ahead
    }

    [Theory]
    [InlineData(int.MaxValue)]   // (page - 1) * 25 wraps to 2,147,483,598 in 32 bits
    [InlineData(100_000_000)]    // (page - 1) * 25 wraps to a negative offset in 32 bits
    public async Task A_huge_page_number_skips_past_every_row_instead_of_wrapping(int page)
    {
        var reader = new FakeReader([]);
        var feed = await new ActivityService(reader).GetForUserAsync(User, page, ActivityService.DefaultPageSize, CancellationToken.None);

        Assert.Equal(int.MaxValue, reader.LastSkip);
        Assert.Equal(page, feed.Page);
        Assert.Empty(feed.Items);
        Assert.True(feed.HasPrevious);
    }

    [Fact]
    public async Task A_full_look_ahead_row_sets_HasMore_and_is_trimmed_off()
    {
        // pageSize 2 with 3 available rows: the service asks for 3 (2+1), gets 3, shows 2, flags more.
        var reader = new FakeReader(Rows(3));
        var feed = await new ActivityService(reader).GetForUserAsync(User, page: 1, pageSize: 2, CancellationToken.None);

        Assert.Equal(2, feed.Items.Count);
        Assert.True(feed.HasMore);
    }

    [Fact]
    public async Task A_short_page_clears_HasMore()
    {
        var reader = new FakeReader(Rows(1));
        var feed = await new ActivityService(reader).GetForUserAsync(User, page: 1, pageSize: 2, CancellationToken.None);

        Assert.Single(feed.Items);
        Assert.False(feed.HasMore);
    }

    [Fact]
    public async Task Non_positive_page_and_size_are_clamped_to_defaults()
    {
        var reader = new FakeReader(Rows(10));
        var feed = await new ActivityService(reader).GetForUserAsync(User, page: 0, pageSize: 0, CancellationToken.None);

        Assert.Equal(1, feed.Page);
        Assert.Equal(ActivityService.DefaultPageSize, feed.PageSize);
        Assert.Equal(0, reader.LastSkip);
        Assert.Equal(ActivityService.DefaultPageSize + 1, reader.LastTake);
    }

    [Fact]
    public async Task An_oversized_page_is_capped_at_the_maximum()
    {
        var reader = new FakeReader(Rows(1));
        var feed = await new ActivityService(reader).GetForUserAsync(User, page: 1, pageSize: 10_000, CancellationToken.None);

        Assert.Equal(ActivityService.MaxPageSize, feed.PageSize);
        Assert.Equal(ActivityService.MaxPageSize + 1, reader.LastTake);
    }

    [Fact]
    public async Task Rows_are_projected_through_the_describer()
    {
        var reader = new FakeReader([Row(AuditAction.SecretRevealed, resource: "prod/db-password", ip: "198.51.100.9")]);
        var feed = await new ActivityService(reader).GetForUserAsync(User, 1, 25, CancellationToken.None);

        var item = Assert.Single(feed.Items);
        var expected = ActivityDescriber.Describe(AuditAction.SecretRevealed);
        Assert.Equal(expected.Title, item.Title);
        Assert.Equal(ActivityCategory.Secrets, item.Category);
        Assert.Equal(ActivitySeverity.Notable, item.Severity);
        Assert.Equal("prod/db-password", item.ResourceName);
        Assert.Equal("198.51.100.9", item.SourceIp);
    }
}
