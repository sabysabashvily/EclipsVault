using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Networks;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;
using Xunit;

namespace EclipsVault.Tests.Networks;

/// <summary>
/// Trusting a range widens the ABAC network rule, so the validation in front of it is a security
/// boundary: these tests pin the canonical form written to the database, the refusal to trust an
/// over-broad range, and the guarantee that a rejected or duplicate range never reaches storage.
/// </summary>
public class TrustedNetworkServiceTests
{
    private sealed class FakeTrustedNetworks : ITrustedNetworkRepository
    {
        private readonly List<TrustedNetwork> _networks;

        public FakeTrustedNetworks(params TrustedNetwork[] seed) => _networks = [.. seed];

        public List<TrustedNetwork> Added { get; } = [];
        public List<TrustedNetwork> Removed { get; } = [];

        /// <summary>Another request trusts the same range between the service's check and its save.</summary>
        public bool LosesTheRace { get; init; }

        public Task<IReadOnlyList<string>> ListCidrsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(_networks.Select(n => n.Cidr).ToList());

        public Task<IReadOnlyList<TrustedNetwork>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TrustedNetwork>>(_networks);

        public Task<bool> ExistsAsync(string cidr, CancellationToken ct)
            => Task.FromResult(_networks.Any(n => n.Cidr == cidr));

        public Task AddAsync(TrustedNetwork network, CancellationToken ct)
        {
            if (LosesTheRace)
            {
                throw new NetworkExistsException();
            }

            Added.Add(network);
            _networks.Add(network);
            return Task.CompletedTask;
        }

        public Task<TrustedNetwork?> FindAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_networks.FirstOrDefault(n => n.Id == id));

        public Task RemoveAsync(TrustedNetwork network, CancellationToken ct)
        {
            Removed.Add(network);
            _networks.Remove(network);
            return Task.CompletedTask;
        }
    }

    private sealed class StubActor : IAuditContext
    {
        public Guid? UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Username => "vault-admin";
        public string? SourceIp => "203.0.113.7";
    }

    private static TrustedNetworkService Build(FakeTrustedNetworks repository)
        => new(repository, new StubActor(), TimeProvider.System);

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7/32")]
    [InlineData("  203.0.113.7  ", "203.0.113.7/32")]
    [InlineData("10.8.0.0/24", "10.8.0.0/24")]
    [InlineData("2001:db8::1", "2001:db8::1/128")]
    public async Task AddAsync_stores_the_canonical_cidr(string input, string expected)
    {
        var repository = new FakeTrustedNetworks();

        var dto = await Build(repository).AddAsync(input, "VPN egress", CancellationToken.None);

        Assert.Equal(expected, dto.Cidr);
        Assert.Equal(expected, Assert.Single(repository.Added).Cidr);
    }

    [Fact]
    public async Task AddAsync_collapses_an_ipv4_mapped_ipv6_address_to_its_ipv4_form()
    {
        var repository = new FakeTrustedNetworks();

        var dto = await Build(repository).AddAsync("::ffff:10.0.0.5", "Mapped", CancellationToken.None);

        Assert.Equal("10.0.0.5/32", dto.Cidr);
    }

    [Theory]
    [InlineData("10.0.0.0/4")]
    [InlineData("0.0.0.0/0")]
    public async Task AddAsync_refuses_a_range_broader_than_slash_8(string cidr)
    {
        var repository = new FakeTrustedNetworks();

        var ex = await Assert.ThrowsAsync<NetworkInputException>(
            () => Build(repository).AddAsync(cidr, "Too wide", CancellationToken.None));

        Assert.Contains("broader than /8", ex.Message);
        Assert.Equal("range", ex.Field);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task AddAsync_accepts_a_slash_8_exactly()
    {
        var repository = new FakeTrustedNetworks();

        var dto = await Build(repository).AddAsync("10.0.0.0/8", "Corp", CancellationToken.None);

        Assert.Equal("10.0.0.0/8", dto.Cidr);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("999.1.1.1")]
    [InlineData("")]
    public async Task AddAsync_rejects_unparseable_input_without_persisting(string input)
    {
        var repository = new FakeTrustedNetworks();

        var ex = await Assert.ThrowsAsync<NetworkInputException>(
            () => Build(repository).AddAsync(input, "Junk", CancellationToken.None));

        Assert.Equal("range", ex.Field);

        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task AddAsync_rejects_a_duplicate_range_by_its_canonical_form()
    {
        // Stored as /32 — re-adding the bare address must collide, not create a second row.
        var repository = new FakeTrustedNetworks(new TrustedNetwork { Id = Guid.NewGuid(), Cidr = "203.0.113.7/32" });

        var ex = await Assert.ThrowsAsync<NetworkExistsException>(
            () => Build(repository).AddAsync("203.0.113.7", "Duplicate", CancellationToken.None));

        Assert.Equal("The range '203.0.113.7/32' is already trusted.", ex.Message);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task AddAsync_reports_a_lost_race_as_the_same_conflict_naming_the_range()
    {
        var repository = new FakeTrustedNetworks { LosesTheRace = true };

        var ex = await Assert.ThrowsAsync<NetworkExistsException>(
            () => Build(repository).AddAsync("203.0.113.7", "Racing", CancellationToken.None));

        Assert.Equal("The range '203.0.113.7/32' is already trusted.", ex.Message);
    }

    [Theory]
    [InlineData("   ", "", "range")]
    [InlineData("10.8.0.0/24 with a description long enough to pass the sixty-four limit", "", "range")]
    [InlineData("203.0.113.7", "This label is one character longer than the 128 characters that the label column stores and the Legacy form allowed to be entered", "label")]
    public async Task AddAsync_refuses_the_Legacy_form_limits_with_the_form_message(string range, string label, string field)
    {
        var repository = new FakeTrustedNetworks();

        var ex = await Assert.ThrowsAsync<NetworkInputException>(
            () => Build(repository).AddAsync(range, label, CancellationToken.None));

        Assert.Equal(("Enter an IP address or CIDR range to trust.", field), (ex.Message, ex.Field));
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task AddAsync_records_the_acting_admin_and_defaults_a_blank_label()
    {
        var repository = new FakeTrustedNetworks();

        var dto = await Build(repository).AddAsync("203.0.113.7", "   ", CancellationToken.None);

        Assert.Equal("vault-admin", dto.AddedBy);
        Assert.Equal("Unlabelled", dto.Label);
    }

    [Fact]
    public async Task AddAsync_trims_the_label()
    {
        var repository = new FakeTrustedNetworks();

        var dto = await Build(repository).AddAsync("203.0.113.7", "  VPN egress  ", CancellationToken.None);

        Assert.Equal("VPN egress", dto.Label);
    }

    [Fact]
    public async Task RemoveAsync_removes_a_known_range()
    {
        var entity = new TrustedNetwork { Id = Guid.NewGuid(), Cidr = "10.8.0.0/24" };
        var repository = new FakeTrustedNetworks(entity);

        Assert.True(await Build(repository).RemoveAsync(entity.Id, CancellationToken.None));
        Assert.Same(entity, Assert.Single(repository.Removed));
    }

    [Fact]
    public async Task RemoveAsync_reports_a_miss_without_touching_storage()
    {
        var repository = new FakeTrustedNetworks();

        Assert.False(await Build(repository).RemoveAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(repository.Removed);
    }

    [Fact]
    public async Task IsTrustedAsync_matches_an_address_inside_a_trusted_range()
    {
        var repository = new FakeTrustedNetworks(new TrustedNetwork { Id = Guid.NewGuid(), Cidr = "10.8.0.0/24" });
        var service = Build(repository);

        Assert.True(await service.IsTrustedAsync(IPAddress.Parse("10.8.0.42"), CancellationToken.None));
        Assert.False(await service.IsTrustedAsync(IPAddress.Parse("10.9.0.42"), CancellationToken.None));
    }

    [Fact]
    public async Task IsTrustedAsync_matches_an_ipv4_mapped_ipv6_client_against_an_ipv4_range()
    {
        var repository = new FakeTrustedNetworks(new TrustedNetwork { Id = Guid.NewGuid(), Cidr = "10.8.0.0/24" });

        Assert.True(await Build(repository).IsTrustedAsync(IPAddress.Parse("10.8.0.42").MapToIPv6(), CancellationToken.None));
    }

    [Fact]
    public async Task IsTrustedAsync_is_false_when_nothing_is_trusted()
        => Assert.False(await Build(new FakeTrustedNetworks()).IsTrustedAsync(IPAddress.Parse("10.8.0.42"), CancellationToken.None));
}
