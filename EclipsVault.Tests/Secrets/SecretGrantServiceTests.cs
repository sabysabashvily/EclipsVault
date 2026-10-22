using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using Xunit;

namespace EclipsVault.Tests.Secrets;

/// <summary>
/// The self-scoped revoke on the "Shared by me" page must never let a caller revoke a share they
/// didn't issue. These tests pin that authorization boundary (the IDOR guard), its indistinguishable
/// failure, and that the issuer is an account, never a username a new account could be given.
/// </summary>
public class SecretGrantServiceTests
{
    private static readonly Guid AliceId = Guid.Parse("a11ce000-0000-0000-0000-000000000001");

    private sealed class FakeGrants : ISecretGrantRepository
    {
        private readonly SecretGrant? _grant;
        public int RemoveCalls { get; private set; }
        public SecretGrant? Added { get; private set; }
        public FakeGrants(SecretGrant? grant) => _grant = grant;

        public Task<SecretGrant?> FindAsync(Guid grantId, CancellationToken ct) => Task.FromResult(_grant);
        public Task<bool> RemoveAsync(Guid grantId, CancellationToken ct)
        {
            RemoveCalls++;
            return Task.FromResult(true);
        }

        public Task AddAsync(SecretGrant grant, CancellationToken ct)
        {
            Added = grant;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(Guid userId, Guid secretId, CancellationToken ct) => Task.FromResult(false);

        public Task<bool> HasActiveGrantAsync(Guid userId, Guid secretId, DateTimeOffset asOfUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SecretGrant>> ListForSecretAsync(Guid secretId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SharedSecretDto>> ListSharedWithUserAsync(Guid userId, DateTimeOffset asOfUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<OutgoingShareDto>> ListIssuedByAsync(Guid issuerUserId, DateTimeOffset asOfUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeUsers : IUserRepository
    {
        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct)
            => Task.FromResult<User?>(new User { Id = Guid.NewGuid(), Username = "carol" });

        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task SetAvatarAsync(User user, byte[] png, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveAvatarAsync(User user, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubActor : IAuditContext
    {
        public Guid? UserId => AliceId;
        public string? Username => "alice";
        public string? SourceIp => "::1";
    }

    private static SecretGrant Grant(Guid? issuerUserId, string grantedBy = "alice") => new()
    {
        Id = Guid.NewGuid(),
        SecretId = Guid.NewGuid(),
        GranteeUserId = Guid.NewGuid(),
        GranteeUsername = "carol",
        IssuerUserId = issuerUserId,
        GrantedBy = grantedBy,
        CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private static (SecretGrantService Service, FakeGrants Grants) NewService(SecretGrant? grant)
    {
        var grants = new FakeGrants(grant);
        var service = new SecretGrantService(grants, new FakeUsers(), new StubActor(), TimeProvider.System);
        return (service, grants);
    }

    [Fact]
    public async Task A_new_share_records_the_actor_as_its_issuer()
    {
        var (service, grants) = NewService(grant: null);

        await service.GrantAsync(Guid.NewGuid(), "carol", ttlDays: null, CancellationToken.None);

        var added = Assert.IsType<SecretGrant>(grants.Added);
        Assert.Equal(AliceId, added.IssuerUserId);
        Assert.Equal("alice", added.GrantedBy);
    }

    [Fact]
    public async Task Revokes_when_the_caller_issued_the_share()
    {
        var (service, grants) = NewService(Grant(issuerUserId: AliceId));

        Assert.True(await service.RevokeIssuedAsync(Guid.NewGuid(), AliceId, CancellationToken.None));
        Assert.Equal(1, grants.RemoveCalls);
    }

    [Fact]
    public async Task Refuses_to_revoke_a_share_issued_by_another_account()
    {
        var (service, grants) = NewService(Grant(issuerUserId: Guid.NewGuid(), grantedBy: "bob"));

        // alice must not be able to revoke a share bob issued, even with its real id.
        Assert.False(await service.RevokeIssuedAsync(Guid.NewGuid(), AliceId, CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }

    /// <summary>
    /// A deleted account's username can be given to a new account. The share still names the old
    /// account as its issuer, so the new one must not be able to revoke it.
    /// </summary>
    [Fact]
    public async Task Refuses_to_revoke_a_share_issued_by_a_deleted_account_with_the_callers_username()
    {
        var (service, grants) = NewService(Grant(issuerUserId: Guid.NewGuid(), grantedBy: "alice"));

        Assert.False(await service.RevokeIssuedAsync(Guid.NewGuid(), AliceId, CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }

    [Fact]
    public async Task Refuses_to_revoke_a_share_with_no_known_issuer()
    {
        var (service, grants) = NewService(Grant(issuerUserId: null, grantedBy: "alice"));

        Assert.False(await service.RevokeIssuedAsync(Guid.NewGuid(), AliceId, CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }

    [Fact]
    public async Task Returns_false_for_a_missing_grant_without_removing_anything()
    {
        var (service, grants) = NewService(grant: null);

        Assert.False(await service.RevokeIssuedAsync(Guid.NewGuid(), AliceId, CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }

    [Fact]
    public async Task Revokes_a_grant_that_belongs_to_the_secret()
    {
        var grant = Grant(issuerUserId: AliceId);
        var (service, grants) = NewService(grant);

        Assert.True(await service.RevokeAsync(grant.Id, grant.SecretId, CancellationToken.None));
        Assert.Equal(1, grants.RemoveCalls);
    }

    /// <summary>
    /// The owner-side revoke is reached from a secret's sharing panel, where the caller has been
    /// authorized against <em>that secret</em> and nothing else. Accepting a grant id belonging to a
    /// different secret would turn "may share one secret" into "may revoke any grant in the vault",
    /// including on secrets in projects the caller cannot see or enumerate.
    /// </summary>
    [Fact]
    public async Task Refuses_a_grant_that_belongs_to_a_different_secret()
    {
        var grant = Grant(issuerUserId: AliceId);
        var (service, grants) = NewService(grant);

        var someOtherSecret = Guid.NewGuid();
        Assert.NotEqual(grant.SecretId, someOtherSecret);

        Assert.False(await service.RevokeAsync(grant.Id, someOtherSecret, CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }

    [Fact]
    public async Task Returns_false_for_a_missing_grant_on_the_owner_side_too()
    {
        var (service, grants) = NewService(grant: null);

        Assert.False(await service.RevokeAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(0, grants.RemoveCalls);
    }
}
