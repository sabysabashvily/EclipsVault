using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using EclipsVault.Core.Application.Abac;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class MyAccessTests(VaultFixture vault)
{
    [Theory]
    [InlineData(ClearanceLevel.TopSecret, "10.52.0.1", true)]
    [InlineData(ClearanceLevel.TopSecret, "198.51.100.52", false)]
    [InlineData(ClearanceLevel.Standard, "10.52.0.2", true)]
    [InlineData(ClearanceLevel.Standard, "198.51.100.53", false)]
    public async Task The_grid_matches_the_rule_engine_for_the_callers_own_project(
        ClearanceLevel clearance, string source, bool trusted)
    {
        const string project = "ACCESS-TEST";
        var session = await vault.SignInNewAccountAsync($"it-access-{source}", IPAddress.Parse(source), clearance, project);

        // Caller-supplied subject attributes must have no effect, even for an administrator.
        var before = DateTimeOffset.UtcNow;
        var response = await session.Client.GetAsync("/api/ui/access?project=OTHER&clearance=4&userId=someone-else");
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var access = document.RootElement;
        Assert.Equal(clearance == ClearanceLevel.TopSecret ? "Top Secret" : "Standard", access.GetProperty("clearance").GetString());
        Assert.Equal(project, access.GetProperty("project").GetString());
        Assert.Equal(source, access.GetProperty("sourceIp").GetString());
        Assert.Equal(trusted, access.GetProperty("isTrustedNetwork").GetBoolean());
        Assert.Equal(6, access.GetProperty("windowStartHour").GetInt32());
        Assert.Equal(22, access.GetProperty("windowEndHour").GetInt32());
        Assert.Equal("UTC", access.GetProperty("windowZone").GetString());
        Assert.Equal(clearance == ClearanceLevel.TopSecret, access.GetProperty("crossesProjects").GetBoolean());
        Assert.Equal(["Development", "Staging", "Production"],
            access.GetProperty("environments").EnumerateArray().Select(e => e.GetString()!).ToArray());

        var rows = access.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal([4, 3, 2, 1], rows.Select(row => row.GetProperty("sensitivityRank").GetInt32()).ToArray());
        Assert.Equal(["Top Secret", "Secret", "Confidential", "Internal"],
            rows.Select(row => row.GetProperty("sensitivity").GetString()!).ToArray());
        var context = new RequestContext(DateTimeOffset.UtcNow, source,
            access.GetProperty("isProductionWindowOpen").GetBoolean(), trusted);
        Assert.Contains(context.IsWithinProductionWindow,
            new[] { before.Hour >= 6 && before.Hour < 22, after.Hour >= 6 && after.Hour < 22 });
        foreach (var row in rows)
        {
            var sensitivity = (SensitivityLevel)row.GetProperty("sensitivityRank").GetInt32();
            var cells = row.GetProperty("cells").EnumerateArray().ToArray();
            Assert.Equal(["Development", "Staging", "Production"], cells.Select(cell => cell.GetProperty("environment").GetString()!).ToArray());
            foreach (var cell in cells)
            {
                var environment = Enum.Parse<SecretEnvironment>(cell.GetProperty("environment").GetString()!);
                var decision = SecretAccessPolicy.Evaluate(new SubjectAttributes(clearance, project),
                    new ResourceAttributes(environment, sensitivity, project), context);
                Assert.Equal(decision.IsAllowed, cell.GetProperty("allowed").GetBoolean());
                // Independent display expectations pin the Legacy UI's first-denial wording/order.
                var reason = (int)clearance < (int)sensitivity ? "Clearance too low"
                    : environment == SecretEnvironment.Production && !context.IsWithinProductionWindow ? "Outside access hours"
                    : sensitivity >= SensitivityLevel.Confidential && !trusted ? "Untrusted network"
                    : null;
                Assert.Equal(reason, cell.GetProperty("reason").GetString());
            }
        }

        // Pin the complete wire shape: neither trusted ranges nor Core objects may leak into it.
        Assert.Equal(new[] { "clearance", "clearanceRank", "crossesProjects", "environments", "isProductionWindowOpen", "isTrustedNetwork", "project", "rows", "sourceIp", "windowEndHour", "windowStartHour", "windowZone" },
            access.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Access_refuses_both_anonymous_requests_and_a_working_api_key()
    {
        var client = vault.CreateClient();
        await UiApiFailureContractTests.AssertProblemAsync(await client.GetAsync("/api/ui/access"), 401, "unauthenticated");
        var key = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-access-api-key");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/secrets")).StatusCode);
        await UiApiFailureContractTests.AssertProblemAsync(await client.GetAsync("/api/ui/access"), 401, "unauthenticated");
    }

    [Theory]
    [InlineData(VaultClaimTypes.Clearance, null, "198.51.100.54")]
    [InlineData(VaultClaimTypes.Project, null, "198.51.100.55")]
    [InlineData(VaultClaimTypes.Clearance, "999", "198.51.100.56")]
    [InlineData(VaultClaimTypes.Project, "", "198.51.100.57")]
    public async Task Missing_or_invalid_vault_attributes_require_protected_sign_out(string claimType, string? value, string address)
    {
        var source = IPAddress.Parse(address);
        var original = await vault.SignInNewAccountAsync($"it-access-stale-{claimType.Replace(":", "-")}-{value ?? "missing"}",
            source);
        var cookie = Assert.IsType<Cookie>(original.SessionCookie);
        // No alternate authentication scheme: alter only the attributes of a real, protected session
        // ticket, modelling a cookie issued before those claims existed. Keep its revocation metadata.
        var format = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;
        var ticket = Assert.IsType<AuthenticationTicket>(format.Unprotect(cookie.Value));
        var identity = Assert.IsType<ClaimsIdentity>(ticket.Principal.Identity);
        foreach (var claim in identity.FindAll(claimType).ToArray()) identity.RemoveClaim(claim);
        if (value is not null) identity.AddClaim(new Claim(claimType, value));
        var stale = vault.CreateSession(new Cookie(cookie.Name, format.Protect(ticket)), source);

        foreach (var path in new[] { "/api/ui/access", "/api/ui/me" })
        {
            var response = await stale.Client.GetAsync(path);
            await UiApiFailureContractTests.AssertProblemAsync(response, 401, "stale_session");
            Assert.Null(response.Headers.Location);
        }

        // A fresh antiforgery token is available even when /me could not return an identity.
        var xsrf = Assert.IsType<Cookie>(stale.Cookies.GetCookies(stale.Client.BaseAddress!)["XSRF-TOKEN"]);
        await UiApiFailureContractTests.AssertProblemAsync(await stale.Client.PostAsync("/api/ui/auth/sign-out", null), 400, "antiforgery_rejected");
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/ui/auth/sign-out");
        logout.Headers.Add("RequestVerificationToken", xsrf.Value);
        Assert.Equal(HttpStatusCode.NoContent, (await stale.Client.SendAsync(logout)).StatusCode);
        Assert.Null(stale.SessionCookie);
        // Signing out revokes the session on the server too, including the unmodified cookie.
        await UiApiFailureContractTests.AssertProblemAsync(await original.Client.GetAsync("/api/ui/access"), 401, "unauthenticated");
    }
}
