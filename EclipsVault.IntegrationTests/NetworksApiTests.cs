using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class NetworksApiTests(VaultFixture vault)
{
    private const string MissingNetwork = NetworksApi.Path + "/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Theory]
    [InlineData("GET", NetworksApi.Path, "198.18.114.11")]
    [InlineData("POST", NetworksApi.Path + "/trust-current-address", "198.18.114.12")]
    [InlineData("POST", NetworksApi.Path, "198.18.114.13")]
    [InlineData("DELETE", MissingNetwork, "198.18.114.14")]
    [InlineData("POST", NetworksApi.Path + "/blocks/lift", "198.18.114.15")]
    public async Task Each_endpoint_accepts_only_an_administrator_session(string method, string path, string source)
    {
        var anonymous = vault.CreateClient();
        var reader = await vault.SignInNewAccountAsync("it-net-reader-" + source.Split('.').Last(), IPAddress.Parse(source));
        var keyClient = vault.CreateClient();
        keyClient.DefaultRequestHeaders.Authorization = new("Bearer", await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-net-auth-key-" + source.Split('.').Last()));

        await UiApiFailureContractTests.AssertProblemAsync(await anonymous.SendAsync(new(new(method), path)), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await reader.Client.SendAsync(new(new(method), path)), 403, "forbidden");
        await UiApiFailureContractTests.AssertProblemAsync(await keyClient.SendAsync(new(new(method), path)), 401, "unauthenticated");
        Assert.Null(await NetworksApi.FindAsync(vault, source + "/32"));
    }

    [Theory]
    [InlineData("POST", NetworksApi.Path + "/trust-current-address")]
    [InlineData("POST", NetworksApi.Path)]
    [InlineData("DELETE", MissingNetwork)]
    [InlineData("POST", NetworksApi.Path + "/blocks/lift")]
    public async Task Every_write_requires_antiforgery(string method, string path)
    {
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await admin.SendAsync(new(new(method), path)
        { Content = JsonContent.Create(new { range = "203.0.113.41" }) }), 400, "antiforgery_rejected");

        Assert.Null(await NetworksApi.FindAsync(vault, "203.0.113.41/32"));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task The_page_lists_the_current_address_ranges_and_blocks_with_their_decoy_names()
    {
        var admin = await vault.SignInNewAccountAsync("it-net-shape", IPAddress.Parse("198.18.114.21"), ClearanceLevel.TopSecret);
        Assert.Equal(HttpStatusCode.Created, (await NetworksApi.AddAsync(admin.Client, " 203.0.113.0/24 ", "Office VPN")).StatusCode);
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync("192.0.2.21", "Honey-token 'FINANCE_DECOY_KEY' tripped");
        try
        {
            var response = await admin.Client.GetAsync(NetworksApi.Path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = (await response.Content.ReadFromJsonAsync<JsonElement>())!;
            Assert.Equal(["blocks", "configuredRanges", "currentAddress", "currentAddressTrusted", "trustedNetworks"], ProfileApi.NamesOf(body));
            Assert.Equal("198.18.114.21", body.GetProperty("currentAddress").GetString());
            Assert.False(body.GetProperty("currentAddressTrusted").GetBoolean());
            Assert.Equal(vault.Services.GetRequiredService<IOptions<AbacOptions>>().Value.TrustedIpCidrs,
                body.GetProperty("configuredRanges").EnumerateArray().Select(range => range.GetString()!).ToList());
            var network = Assert.Single(body.GetProperty("trustedNetworks").EnumerateArray(), item => item.GetProperty("range").GetString() == "203.0.113.0/24");
            Assert.Equal(["addedAtUtc", "addedBy", "label", "networkId", "range"], ProfileApi.NamesOf(network));
            var stored = await NetworksApi.FindAsync(vault, "203.0.113.0/24");
            Assert.Equal(stored!.Id, network.GetProperty("networkId").GetGuid());
            Assert.Equal("Office VPN", network.GetProperty("label").GetString());
            Assert.Equal("it-net-shape", network.GetProperty("addedBy").GetString());
            Assert.Equal(stored.CreatedAtUtc, network.GetProperty("addedAtUtc").GetDateTimeOffset());
            var block = Assert.Single(body.GetProperty("blocks").EnumerateArray(), item => item.GetProperty("range").GetString() == "192.0.2.21/32");
            Assert.Equal(["blockedAtUtc", "range", "reason"], ProfileApi.NamesOf(block));
            Assert.Equal("Honey-token 'FINANCE_DECOY_KEY' tripped", block.GetProperty("reason").GetString());
            Assert.Equal(Assert.Single(await blacklist.ListAsync(), item => item.Network == "192.0.2.21/32").BlockedAtUtc,
                block.GetProperty("blockedAtUtc").GetDateTimeOffset());
        }
        finally
        {
            await NetworksApi.ForgetAsync(vault, "203.0.113.0/24");
            await blacklist.UnblockAsync("192.0.2.21/32");
        }
    }

    [Fact]
    public async Task The_current_address_is_the_client_the_trusted_proxy_forwards()
    {
        var admin = await vault.SignInNewAccountAsync("it-net-forwarded", VaultFixture.ForwardingProxy, ClearanceLevel.TopSecret);
        using var forwarded = new HttpRequestMessage(HttpMethod.Get, NetworksApi.Path);
        forwarded.Headers.Add("X-Forwarded-For", "203.0.113.114");

        var proxied = await (await admin.Client.SendAsync(forwarded)).Content.ReadFromJsonAsync<JsonElement>();
        var direct = await admin.Client.GetFromJsonAsync<JsonElement>(NetworksApi.Path);

        Assert.Equal("203.0.113.114", proxied.GetProperty("currentAddress").GetString());
        Assert.Equal(VaultFixture.ForwardingProxy.ToString(), direct.GetProperty("currentAddress").GetString());
    }

    [Fact]
    public async Task An_address_in_a_configured_range_is_trusted_without_a_runtime_entry()
    {
        var admin = await vault.SignInNewAccountAsync("it-net-configured", IPAddress.Parse("10.114.0.21"), ClearanceLevel.TopSecret);

        var body = await admin.Client.GetFromJsonAsync<JsonElement>(NetworksApi.Path);

        Assert.Equal("10.114.0.21", body.GetProperty("currentAddress").GetString());
        Assert.True(body.GetProperty("currentAddressTrusted").GetBoolean());
        Assert.Contains("10.0.0.0/8", body.GetProperty("configuredRanges").EnumerateArray().Select(range => range.GetString()));
    }

    [Fact]
    public async Task Trusting_the_current_address_saves_its_host_range_with_one_event()
    {
        var admin = await vault.SignInNewAccountAsync("it-net-trust-current", IPAddress.Parse("198.18.114.22"), ClearanceLevel.TopSecret);
        var before = await AuditTrail.HeadAsync(vault);
        try
        {
            var trusted = await NetworksApi.TrustCurrentAddressAsync(admin.Client);

            Assert.Equal(HttpStatusCode.Created, trusted.StatusCode);
            Assert.True(trusted.Headers.CacheControl?.NoStore);
            var body = (await trusted.Content.ReadFromJsonAsync<JsonElement>())!;
            Assert.Equal(["range"], ProfileApi.NamesOf(body));
            Assert.Equal("198.18.114.22/32", body.GetProperty("range").GetString());
            var stored = await NetworksApi.FindAsync(vault, "198.18.114.22/32");
            Assert.Equal("Trusted from admin console by it-net-trust-current", stored!.Label);
            var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
            Assert.Equal((AuditAction.TrustedNetworkAdded, stored.Id, "it-net-trust-current"), (row.Action, row.ResourceId, row.Username));
            Assert.True((await admin.Client.GetFromJsonAsync<JsonElement>(NetworksApi.Path)).GetProperty("currentAddressTrusted").GetBoolean());
        }
        finally
        {
            await NetworksApi.ForgetAsync(vault, "198.18.114.22/32");
        }
    }

    [Fact]
    public async Task Trusting_an_already_trusted_current_address_is_the_conflict_and_records_nothing()
    {
        var admin = await vault.SignInNewAccountAsync("it-net-trust-twice", IPAddress.Parse("198.18.114.27"), ClearanceLevel.TopSecret);
        (await NetworksApi.TrustCurrentAddressAsync(admin.Client)).EnsureSuccessStatusCode();
        var before = await AuditTrail.HeadAsync(vault);
        try
        {
            var again = await NetworksApi.TrustCurrentAddressAsync(admin.Client);

            var problem = await ProfileApi.AssertProblemAsync(again, 409, "network_exists");
            Assert.Equal("The range '198.18.114.27/32' is already trusted.", problem.GetProperty("title").GetString());
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await NetworksApi.ForgetAsync(vault, "198.18.114.27/32");
        }
    }

    [Fact]
    public async Task Adding_a_range_saves_it_in_canonical_form_with_one_event()
    {
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);
        try
        {
            var added = await NetworksApi.AddAsync(admin, "203.0.113.7");

            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            Assert.Equal("203.0.113.7/32", (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("range").GetString());
            var stored = await NetworksApi.FindAsync(vault, "203.0.113.7/32");
            Assert.Equal(("Unlabelled", VaultFixture.AdminUsername), (stored!.Label, stored.AddedBy));
            var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
            Assert.Equal((AuditAction.TrustedNetworkAdded, stored.Id), (row.Action, row.ResourceId));
        }
        finally
        {
            await NetworksApi.ForgetAsync(vault, "203.0.113.7/32");
        }
    }

    [Theory]
    [InlineData("", "", "range", "Enter an IP address or CIDR range to trust.")]
    [InlineData("10.8.0.0/24-and-more-text-that-runs-past-sixty-four-characters-long", "", "range", "Enter an IP address or CIDR range to trust.")]
    [InlineData("not-an-address", "", "range", "'not-an-address' is not a valid IP address or CIDR range (e.g. 203.0.113.7 or 10.8.0.0/24).")]
    [InlineData("10.0.0.0/7", "", "range", "'10.0.0.0/7' is broader than /8 — refusing to trust a range that large.")]
    [InlineData("203.0.113.42", "This label is one character longer than the 128 characters that the label column stores and the Legacy form allowed to be entered", "label", "Enter an IP address or CIDR range to trust.")]
    public async Task An_invalid_range_or_label_is_refused_and_records_nothing(string range, string label, string field, string message)
    {
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        var problem = await ProfileApi.AssertProblemAsync(await NetworksApi.AddAsync(admin, range, label), 400, "validation_failed");

        Assert.Equal(message, problem.GetProperty("title").GetString());
        Assert.Equal(message, Assert.Single(problem.GetProperty("errors").GetProperty(field).EnumerateArray()).GetString());
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        using var scope = vault.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().TrustedNetworks.AnyAsync(network => network.Cidr.StartsWith("10.0.0.0/") || network.Cidr == "203.0.113.42/32"));
    }

    [Fact]
    public async Task Removing_a_range_deletes_it_with_one_event()
    {
        var admin = await vault.AdminClientAsync();
        (await NetworksApi.AddAsync(admin, "203.0.113.43", "Removed")).EnsureSuccessStatusCode();
        var stored = await NetworksApi.FindAsync(vault, "203.0.113.43/32");
        var before = await AuditTrail.HeadAsync(vault);

        var removed = await NetworksApi.RemoveAsync(admin, stored!.Id);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.True(removed.Headers.CacheControl?.NoStore);
        Assert.Null(await NetworksApi.FindAsync(vault, "203.0.113.43/32"));
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal((AuditAction.TrustedNetworkRemoved, stored.Id), (row.Action, row.ResourceId));
    }

    [Fact]
    public async Task Removing_an_unknown_range_is_not_found_and_records_nothing()
    {
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        var problem = await ProfileApi.AssertProblemAsync(await NetworksApi.RemoveAsync(admin, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")), 404, "network_not_found");

        Assert.Equal("Network entry not found.", problem.GetProperty("title").GetString());
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Lifting_a_block_removes_it_with_one_event()
    {
        var admin = await vault.AdminClientAsync();
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync("198.51.100.114", "Honey-token 'Legacy lift' tripped");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await NetworksApi.LiftAsync(admin, "198.51.100.114/32");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.114")));
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.IpRangeUnblocked, row.Action);
        Assert.Equal("198.51.100.114/32", row.ResourceName);
        Assert.Equal("TrustedNetwork", row.ResourceType);
        Assert.Equal("Intrusion-defence block lifted", row.Details);
        Assert.Equal(VaultFixture.AdminUsername, row.Username);
    }

    [Fact]
    public async Task Lifting_an_absent_block_is_not_found_and_records_nothing()
    {
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        var problem = await ProfileApi.AssertProblemAsync(await NetworksApi.LiftAsync(admin, "198.51.100.115/32"), 404, "block_not_found");

        Assert.Equal("That range is not currently blocked.", problem.GetProperty("title").GetString());
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_stale_administrator_must_prove_it_before_any_grant_and_changes_nothing()
    {
        var admin = await vault.SignInNewAccountAsync("it-net-stale", IPAddress.Parse("198.18.114.23"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.114.23"));
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync("192.0.2.23", "Honey-token 'Stale lift' tripped");
        var before = await AuditTrail.HeadAsync(vault);
        try
        {
            await AdministratorFreshnessTests.AssertStepUpAsync(await NetworksApi.TrustCurrentAddressAsync(stale.Client), "code");
            await AdministratorFreshnessTests.AssertStepUpAsync(await NetworksApi.AddAsync(stale.Client, "203.0.113.44"), "code");
            await AdministratorFreshnessTests.AssertStepUpAsync(await NetworksApi.LiftAsync(stale.Client, "192.0.2.23/32"), "code");
            // Freshness is checked before the input or the target is looked at.
            await AdministratorFreshnessTests.AssertStepUpAsync(await NetworksApi.AddAsync(stale.Client, "10.0.0.0/7"), "code");
            await AdministratorFreshnessTests.AssertStepUpAsync(await NetworksApi.LiftAsync(stale.Client, "192.0.2.24/32"), "code");

            Assert.Null(await NetworksApi.FindAsync(vault, "198.18.114.23/32"));
            Assert.Null(await NetworksApi.FindAsync(vault, "203.0.113.44/32"));
            Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("192.0.2.23")));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await blacklist.UnblockAsync("192.0.2.23/32");
        }
    }

    [Fact]
    public async Task A_stale_administrator_can_remove_a_range_without_fresh_proof()
    {
        var admin = await vault.SignInNewAccountAsync("it-net-stale-remove", IPAddress.Parse("198.18.114.24"), ClearanceLevel.TopSecret);
        (await NetworksApi.AddAsync(admin.Client, "203.0.113.45")).EnsureSuccessStatusCode();
        var stored = await NetworksApi.FindAsync(vault, "203.0.113.45/32");
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddDays(-1)), IPAddress.Parse("198.18.114.24"));

        Assert.Equal(HttpStatusCode.NoContent, (await NetworksApi.RemoveAsync(stale.Client, stored!.Id)).StatusCode);
        Assert.Null(await NetworksApi.FindAsync(vault, "203.0.113.45/32"));
    }

    [Fact]
    public async Task Fresh_proof_allows_each_previously_blocked_grant()
    {
        var admin = await vault.SignInNewAccountAsync("it-net-real-proof", IPAddress.Parse("198.18.114.25"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.114.25"));
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync("192.0.2.25", "Honey-token 'Proof lift' tripped");
        await AdministratorFreshnessTests.AssertStepUpAsync(await NetworksApi.AddAsync(stale.Client, "203.0.113.46"), "code");
        var owner = await ProfileHttp.AccountAsync(vault, "it-net-real-proof");
        try
        {
            var proof = await UiApiRequest.WriteWithAntiforgeryAsync(stale, HttpMethod.Post, "/api/ui/step-up",
                JsonContent.Create(new { code = Totp.Code(owner.TotpSecret!, Totp.CurrentStep() + 1) }));

            Assert.Equal(HttpStatusCode.NoContent, proof.StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await NetworksApi.AddAsync(stale.Client, "203.0.113.46")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await NetworksApi.TrustCurrentAddressAsync(stale.Client)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await NetworksApi.LiftAsync(stale.Client, "192.0.2.25/32")).StatusCode);
            Assert.NotNull(await NetworksApi.FindAsync(vault, "203.0.113.46/32"));
            Assert.NotNull(await NetworksApi.FindAsync(vault, "198.18.114.25/32"));
            Assert.False(await blacklist.IsBlockedAsync(IPAddress.Parse("192.0.2.25")));
        }
        finally
        {
            await NetworksApi.ForgetAsync(vault, "203.0.113.46/32");
            await NetworksApi.ForgetAsync(vault, "198.18.114.25/32");
            await blacklist.UnblockAsync("192.0.2.25/32");
        }
    }

    [Fact]
    public async Task An_administrator_without_an_authenticator_gets_sign_in_proof()
    {
        var admin = await vault.SignInNewAccountAsync("it-net-no-factor", IPAddress.Parse("198.18.114.26"), ClearanceLevel.TopSecret);
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Users.Where(user => user.Username == "it-net-no-factor")
            .ExecuteUpdateAsync(change => change.SetProperty(user => user.TotpEnabled, false));
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.114.26"));

        await AdministratorFreshnessTests.AssertStepUpAsync(await NetworksApi.TrustCurrentAddressAsync(stale.Client), "sign_in");
        await AdministratorFreshnessTests.AssertStepUpAsync(await NetworksApi.AddAsync(stale.Client, "203.0.113.47"), "sign_in");
        await AdministratorFreshnessTests.AssertStepUpAsync(await NetworksApi.LiftAsync(stale.Client, "192.0.2.26/32"), "sign_in");
    }

    [Theory]
    [InlineData("/Admin/Networks", "198.18.114.31")]
    [InlineData("/aDmIn/nEtWoRkS", "198.18.114.32")]
    public async Task The_bookmark_keeps_admin_authorization_its_return_url_and_the_Angular_document(string path, string source)
    {
        var anonymous = await vault.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal(path, System.Web.HttpUtility.ParseQueryString(anonymous.Headers.Location!.Query)["ReturnUrl"]);
        var reader = await vault.SignInNewAccountAsync("it-net-page-reader-" + source.Split('.').Last(), IPAddress.Parse(source));
        var denied = await reader.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/Denied", denied.Headers.Location?.AbsolutePath);
        var admin = await vault.AdminClientAsync();
        var document = await admin.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.True(document.Headers.CacheControl?.NoStore);
        Assert.Contains("<app-root>", await document.Content.ReadAsStringAsync());
        Assert.Contains("require-trusted-types-for 'script'", Assert.Single(document.Headers.GetValues("Content-Security-Policy")));
    }

    [Theory]
    [InlineData("POST", "/Admin/Networks", HttpStatusCode.MethodNotAllowed)]
    [InlineData("DELETE", "/aDmIn/nEtWoRkS/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("GET", "/Admin/Networks/{id}", HttpStatusCode.NotFound)]
    [InlineData("POST", "/Admin/TrustCurrentIp", HttpStatusCode.NotFound)]
    [InlineData("POST", "/aDmIn/aDdNeTwOrK", HttpStatusCode.NotFound)]
    [InlineData("POST", "/Admin/RemoveNetwork/{id}", HttpStatusCode.NotFound)]
    [InlineData("POST", "/ADMIN/UNBLOCK", HttpStatusCode.NotFound)]
    public async Task A_retired_network_handler_changes_nothing(string method, string path, HttpStatusCode expected)
    {
        var admin = await vault.AdminClientAsync();
        (await NetworksApi.AddAsync(admin, "203.0.113.48", "Retired routes")).EnsureSuccessStatusCode();
        var stored = await NetworksApi.FindAsync(vault, "203.0.113.48/32");
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync("192.0.2.48", "Honey-token 'Retired route' tripped");
        var before = await AuditTrail.HeadAsync(vault);
        try
        {
            using var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
                new HttpMethod(method), path.Replace("{id}", stored!.Id.ToString()) + $"?id={stored.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["id"] = stored.Id.ToString(),
                    ["network"] = "192.0.2.48/32",
                    ["Cidr"] = "203.0.113.49",
                    ["Label"] = "Retired",
                }));

            Assert.Equal(expected, response.StatusCode);
            Assert.NotNull(await NetworksApi.FindAsync(vault, "203.0.113.48/32"));
            Assert.Null(await NetworksApi.FindAsync(vault, "203.0.113.49/32"));
            Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("192.0.2.48")));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await NetworksApi.ForgetAsync(vault, "203.0.113.48/32");
            await blacklist.UnblockAsync("192.0.2.48/32");
        }
    }
}
