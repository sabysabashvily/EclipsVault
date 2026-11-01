using EclipsVault.Core.Domain.Enums;
using Xunit;
using static EclipsVault.IntegrationTests.PendingDenialReasonsCleanup;

namespace EclipsVault.IntegrationTests;

public sealed class SqlServerPendingDenialReasonsCleanupTests(SqlServerPendingDenialReasonsCleanup database)
    : IClassFixture<SqlServerPendingDenialReasonsCleanup>
{
    [Fact]
    public async Task A_pending_request_loses_the_denial_text_its_requester_wrote()
        => Assert.Null(await database.DeniedReasonsOfAsync(AccessRequestStatus.Pending));

    [Theory]
    [InlineData(AccessRequestStatus.Approved)]
    [InlineData(AccessRequestStatus.Rejected)]
    [InlineData(AccessRequestStatus.Cancelled)]
    public async Task A_decided_request_keeps_the_denial_text_its_reviewer_saw(AccessRequestStatus status)
        => Assert.Equal(RequesterWrittenText, await database.DeniedReasonsOfAsync(status));
}

public sealed class PostgresPendingDenialReasonsCleanupTests(PostgresPendingDenialReasonsCleanup database)
    : IClassFixture<PostgresPendingDenialReasonsCleanup>
{
    [Fact]
    public async Task A_pending_request_loses_the_denial_text_its_requester_wrote()
        => Assert.Null(await database.DeniedReasonsOfAsync(AccessRequestStatus.Pending));

    [Theory]
    [InlineData(AccessRequestStatus.Approved)]
    [InlineData(AccessRequestStatus.Rejected)]
    [InlineData(AccessRequestStatus.Cancelled)]
    public async Task A_decided_request_keeps_the_denial_text_its_reviewer_saw(AccessRequestStatus status)
        => Assert.Equal(RequesterWrittenText, await database.DeniedReasonsOfAsync(status));
}
