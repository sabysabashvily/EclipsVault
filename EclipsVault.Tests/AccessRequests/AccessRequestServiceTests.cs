using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.AccessRequests;
using EclipsVault.Core.Application.Notifications;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Notifications;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.AccessRequests;

public sealed class AccessRequestServiceTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(3651)]
    [InlineData(int.MaxValue)]
    public async Task Expiry_outside_zero_to_3650_days_is_refused_in_Core(int days)
    {
        var service = new AccessRequestService(requests: new MissingRequests(), grants: null!, secrets: null!, users: null!,
            committer: null!, actor: null!, notifications: null!, clock: TimeProvider.System);
        await Assert.ThrowsAsync<SharingException>(() => service.ApproveAsync(Guid.NewGuid(), days, default));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(3650, 3650.0)]
    public async Task Expiry_at_each_bound_is_committed_with_the_expected_share_lifetime(int days, double? expectedDays)
    {
        await using var setup = await ApprovalSetup.CreateAsync();
        Assert.True(await setup.Service.ApproveAsync(setup.Request.Id, days, default));
        var share = await setup.Context.SecretGrants.SingleAsync();
        Assert.Equal(expectedDays, (share.ExpiresAtUtc - share.CreatedAtUtc)?.TotalDays);
    }

    [Fact]
    public async Task Approval_with_an_active_share_decides_the_request_without_creating_a_second_share()
    {
        await using var setup = await ApprovalSetup.CreateAsync();
        var active = new SecretGrant
        {
            Id = Guid.NewGuid(),
            SecretId = setup.Request.SecretId,
            GranteeUserId = setup.Request.RequesterUserId,
            GranteeUsername = "requester",
            GrantedBy = "previous-issuer",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        setup.Context.SecretGrants.Add(active);
        await setup.Context.SaveChangesAsync();

        Assert.True(await setup.Service.ApproveAsync(setup.Request.Id, 7, default));

        Assert.Equal(AccessRequestStatus.Approved, setup.Request.Status);
        Assert.Equal(active.Id, (await setup.Context.SecretGrants.SingleAsync()).Id);
        Assert.Equal("previous-issuer", (await setup.Context.SecretGrants.SingleAsync()).GrantedBy);
    }

    private sealed class ApprovalSetup(SqliteConnection connection, EclipsVaultDbContext context, AccessRequest request) : IAsyncDisposable
    {
        public EclipsVaultDbContext Context { get; } = context;
        public AccessRequest Request { get; } = request;
        public AccessRequestService Service { get; } = new(new AccessRequestRepository(context), new SecretGrantRepository(context),
            new SecretRepository(context), new UserRepository(context, TimeProvider.System), new AccessRequestCommitter(context),
            new Reviewer(), new NotificationService(new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance),
                new EmailLogRepository(context), new UserRepository(context, TimeProvider.System), new NotificationOptions(false), TimeProvider.System), TimeProvider.System);

        public static async Task<ApprovalSetup> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var context = new EclipsVaultDbContext(new DbContextOptionsBuilder<EclipsVaultDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var user = new User { Id = Guid.NewGuid(), Username = "requester", Email = "requester@example.test" };
            var secret = new Secret { Id = Guid.NewGuid(), Name = "deployment-key" };
            var request = new AccessRequest
            {
                Id = Guid.NewGuid(),
                SecretId = secret.Id,
                SecretName = secret.Name,
                RequesterUserId = user.Id,
                RequesterUsername = user.Username
            };
            context.AddRange(user, secret, request);
            await context.SaveChangesAsync();
            return new ApprovalSetup(connection, context, request);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Reviewer : IAuditContext
    {
        public Guid? UserId => Guid.Parse("a11ce000-0000-0000-0000-000000000001");
        public string? Username => "reviewer";
        public string? SourceIp => null;
    }

    private sealed class MissingRequests : IAccessRequestRepository
    {
        public Task<AccessRequest?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<AccessRequest?>(null);
        public Task AddAsync(AccessRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(AccessRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HasPendingAsync(Guid secretId, Guid requesterUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccessRequest>> ListByRequesterAsync(Guid requesterUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccessRequest>> ListPendingAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccessRequest>> ListPendingForProjectAsync(string projectKey, CancellationToken ct) => throw new NotSupportedException();
    }
}
