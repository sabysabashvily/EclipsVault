using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The wire contract of the three read-only self-service endpoints (My activity, Sign-in history,
/// Security checkup): session only, no-store, an exact JSON shape, the stale-session answer, and
/// My activity's paging over real audit rows.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SelfServiceApiTests(VaultFixture vault)
{
    private static readonly string[] Endpoints = ["/api/ui/activity", "/api/ui/sign-ins", "/api/ui/security"];

    [Fact]
    public async Task Each_endpoint_refuses_both_anonymous_requests_and_a_working_api_key()
    {
        var client = vault.CreateClient();
        foreach (var path in Endpoints)
        {
            await UiApiFailureContractTests.AssertProblemAsync(await client.GetAsync(path), 401, "unauthenticated");
        }

        var key = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-self-service-api-key");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/secrets")).StatusCode);
        foreach (var path in Endpoints)
        {
            await UiApiFailureContractTests.AssertProblemAsync(await client.GetAsync(path), 401, "unauthenticated");
        }
    }

    [Fact]
    public async Task Activity_answers_the_callers_own_page_in_exactly_its_contract()
    {
        var started = DateTime.UtcNow;
        var session = await vault.SignInNewAccountAsync("it-activity-contract", IPAddress.Parse("198.51.100.60"));

        var response = await session.Client.GetAsync("/api/ui/activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var activity = document.RootElement;
        Assert.Equal(["hasMore", "hasPrevious", "items", "notableCount", "page", "pageSize"], NamesOf(activity));
        Assert.Equal(1, activity.GetProperty("page").GetInt32());
        Assert.Equal(25, activity.GetProperty("pageSize").GetInt32());
        Assert.False(activity.GetProperty("hasMore").GetBoolean());
        Assert.False(activity.GetProperty("hasPrevious").GetBoolean());
        Assert.Equal(1, activity.GetProperty("notableCount").GetInt32());

        // Newest first: the first sign-in signs in after enrolling an authenticator.
        // Creating the account belongs to the administrator's own activity.
        var items = activity.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.Equal(
            ["category", "resourceName", "severity", "sourceIp", "timestampUtc", "title"], NamesOf(item)));
        Assert.Equal(["Signed in", "Set up your authenticator"], items.Select(item => item.GetProperty("title").GetString()));
        Assert.Equal(["Authentication", "Account"], items.Select(item => item.GetProperty("category").GetString()));
        Assert.Equal(["Routine", "Notable"], items.Select(item => item.GetProperty("severity").GetString()));
        Assert.All(items, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("resourceName").ValueKind));
        Assert.Equal(["198.51.100.60", "198.51.100.60"], items.Select(item => item.GetProperty("sourceIp").GetString()));
        Assert.All(items, item => AssertUtcSince(started, item.GetProperty("timestampUtc")));
    }

    [Fact]
    public async Task Sign_ins_answers_the_callers_own_history_in_exactly_its_contract()
    {
        var started = DateTime.UtcNow;
        var session = await vault.SignInNewAccountAsync("it-sign-ins-contract", IPAddress.Parse("198.51.100.61"));

        var response = await session.Client.GetAsync("/api/ui/sign-ins");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var history = document.RootElement;
        Assert.Equal(["events", "summary"], NamesOf(history));

        var summary = history.GetProperty("summary");
        Assert.Equal(
            ["distinctLocations", "failedCount", "lastFailedUtc", "lastSuccessUtc", "needsAttention", "successCount", "suspiciousCount"],
            NamesOf(summary));
        Assert.Equal(1, summary.GetProperty("successCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("failedCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("suspiciousCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("distinctLocations").GetInt32());
        AssertUtcSince(started, summary.GetProperty("lastSuccessUtc"));
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("lastFailedUtc").ValueKind);
        Assert.False(summary.GetProperty("needsAttention").GetBoolean());

        var signIn = Assert.Single(history.GetProperty("events").EnumerateArray().ToArray());
        Assert.Equal(["locationFlag", "method", "outcome", "sourceIp", "timestampUtc", "title"], NamesOf(signIn));
        Assert.Equal("Success", signIn.GetProperty("outcome").GetString());
        Assert.Equal("Password", signIn.GetProperty("method").GetString());
        Assert.Equal("Signed in", signIn.GetProperty("title").GetString());
        Assert.Equal("198.51.100.61", signIn.GetProperty("sourceIp").GetString());
        Assert.Equal("FirstSeen", signIn.GetProperty("locationFlag").GetString());
        Assert.Equal(summary.GetProperty("lastSuccessUtc").GetString(), signIn.GetProperty("timestampUtc").GetString());
    }

    [Fact]
    public async Task Security_answers_the_callers_own_checkup_in_exactly_its_contract()
    {
        var session = await vault.SignInNewAccountAsync("it-security-contract", IPAddress.Parse("198.51.100.62"));

        var response = await session.Client.GetAsync("/api/ui/security");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checkup = document.RootElement;
        Assert.Equal(["actionNeededCount", "checks", "grade", "nextStep", "passCount", "recommendedCount", "score"], NamesOf(checkup));

        // A new account: an authenticator, no backup codes, no passkey, one device.
        Assert.Equal(65, checkup.GetProperty("score").GetInt32());
        Assert.Equal("Fair", checkup.GetProperty("grade").GetString());
        Assert.Equal(1, checkup.GetProperty("actionNeededCount").GetInt32());
        Assert.Equal(1, checkup.GetProperty("recommendedCount").GetInt32());
        Assert.Equal(2, checkup.GetProperty("passCount").GetInt32());
        Assert.Equal("Generate a fresh set so you can get in if you ever lose your authenticator.",
            checkup.GetProperty("nextStep").GetString());

        var checks = checkup.GetProperty("checks").EnumerateArray().ToArray();
        Assert.All(checks, check => Assert.Equal(["detail", "fix", "kind", "recommendation", "status", "title"], NamesOf(check)));
        Assert.Equal(["BackupCodes", "Passkey", "TwoStepSignIn", "SignedInDevices"], checks.Select(check => check.GetProperty("kind").GetString()));
        Assert.Equal(["ActionNeeded", "Recommended", "Pass", "Pass"], checks.Select(check => check.GetProperty("status").GetString()));
        Assert.Equal(["BackupCodes", "Profile", "None", "SignedInDevices"], checks.Select(check => check.GetProperty("fix").GetString()));
        Assert.Equal(["Backup codes", "Passkey", "Two-step sign-in", "Signed-in devices"], checks.Select(check => check.GetProperty("title").GetString()));
        Assert.Equal(
            ["You have no backup codes left.", "You haven't added a passkey yet.",
             "An authenticator app is protecting your sign-in.", "Only this device is signed in."],
            checks.Select(check => check.GetProperty("detail").GetString()));
        Assert.Equal(
            ["Generate a fresh set so you can get in if you ever lose your authenticator.",
             "Add a passkey for sign-in that can't be phished, even if your password leaks.",
             "You're all set. Keep your authenticator app backed up.",
             "Check this list whenever you sign in somewhere new."],
            checks.Select(check => check.GetProperty("recommendation").GetString()));
    }

    /// <summary>
    /// The endpoints refuse a session without an account id the vault issued. The cookie handler
    /// rejects a ticket whose account id is missing, does not parse, or names no existing enabled
    /// account, before any endpoint runs, so each answers <c>unauthenticated</c> and clears the cookie.
    /// </summary>
    [Theory]
    [InlineData(null, "198.51.100.63")]
    [InlineData("not-an-account-id", "198.51.100.64")]
    [InlineData("00000000-0000-0000-0000-000000000000", "198.51.100.65")]
    public async Task A_session_without_a_usable_account_id_is_refused_before_any_read(string? accountId, string address)
    {
        var source = IPAddress.Parse(address);
        var original = await vault.SignInNewAccountAsync($"it-self-service-stale-{accountId ?? "missing"}", source);
        var tampered = Tamper(original, identity =>
        {
            foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).ToArray()) identity.RemoveClaim(claim);
            if (accountId is not null) identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, accountId));
        });

        foreach (var path in Endpoints)
        {
            // A fresh client per endpoint, so a cookie cleared by one refusal cannot explain the next.
            var stale = vault.CreateSession(tampered, source);
            var response = await stale.Client.GetAsync(path);
            await UiApiFailureContractTests.AssertProblemAsync(response, 401, "unauthenticated");
            Assert.Null(response.Headers.Location);
            Assert.Null(stale.SessionCookie);
        }
    }

    /// <summary>
    /// Deleting an account ends its sessions, so the cookie is refused before the endpoint runs: the
    /// security checkup answers <c>unauthenticated</c> and clears the cookie.
    /// </summary>
    [Fact]
    public async Task Security_checkup_for_a_deleted_account_requires_signing_in_again()
    {
        const string username = "it-security-deleted";
        var session = await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.51.100.66"));
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/ui/security")).StatusCode);

        await vault.DeleteAccountAsync(username);
        var response = await session.Client.GetAsync("/api/ui/security");

        await UiApiFailureContractTests.AssertProblemAsync(response, 401, "unauthenticated");
        Assert.Null(response.Headers.Location);
        Assert.Null(session.SessionCookie);
    }

    [Fact]
    public async Task Activity_pages_25_rows_at_a_time_newest_first_without_repeating_or_skipping_rows_that_share_an_instant()
    {
        const string username = "it-activity-paging";
        var session = await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.51.100.67"));
        using var before = JsonDocument.Parse(await session.Client.GetStringAsync("/api/ui/activity"));
        var ownRows = before.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetRawText()).ToArray();
        Assert.Equal(2, ownRows.Length);

        // Thirty newer rows, all at one instant, so the page boundary falls inside the tie.
        var names = await WriteRowsAtOneInstantAsync(AccountIdOf(session), username, 30);
        var newestFirst = Enumerable.Reverse(names).ToArray();

        using var page1 = JsonDocument.Parse(await session.Client.GetStringAsync("/api/ui/activity?page=1"));
        using var page2 = JsonDocument.Parse(await session.Client.GetStringAsync("/api/ui/activity?page=2"));
        var first = page1.RootElement;
        var second = page2.RootElement;

        Assert.Equal(1, first.GetProperty("page").GetInt32());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.False(first.GetProperty("hasPrevious").GetBoolean());
        Assert.Equal(12, first.GetProperty("notableCount").GetInt32());
        var firstItems = first.GetProperty("items").EnumerateArray().ToArray();
        // Within the instant, the later write is the newer row.
        Assert.Equal(newestFirst.Take(25), firstItems.Select(item => item.GetProperty("resourceName").GetString()!));
        Assert.Single(firstItems.Select(item => item.GetProperty("timestampUtc").GetString()).Distinct());

        Assert.Equal(2, second.GetProperty("page").GetInt32());
        Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.True(second.GetProperty("hasPrevious").GetBoolean());
        Assert.Equal(4, second.GetProperty("notableCount").GetInt32());
        var secondItems = second.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(newestFirst.Skip(25), secondItems.Take(5).Select(item => item.GetProperty("resourceName").GetString()!));
        Assert.Equal(ownRows, secondItems.Skip(5).Select(item => item.GetRawText()));

        // Every row exactly once across the two pages.
        var all = firstItems.Concat(secondItems).Select(item => item.GetRawText()).ToArray();
        Assert.Equal(32, all.Length);
        Assert.Equal(32, all.Distinct().Count());
    }

    [Fact]
    public async Task Activity_reads_an_unusable_page_as_page_1_and_one_past_the_end_as_empty()
    {
        var session = await vault.SignInNewAccountAsync("it-activity-page-values", IPAddress.Parse("198.51.100.68"));
        var pageOne = await session.Client.GetStringAsync("/api/ui/activity?page=1");
        using (var document = JsonDocument.Parse(pageOne))
        {
            Assert.Equal(2, document.RootElement.GetProperty("items").GetArrayLength());
        }

        foreach (var query in new[] { "", "?page=", "?page=0", "?page=-3", "?page=abc", "?page=2.5" })
        {
            var response = await session.Client.GetAsync("/api/ui/activity" + query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(pageOne, await response.Content.ReadAsStringAsync());
        }

        // 100000000 is a page whose offset wraps negative in 32-bit arithmetic.
        foreach (var page in new[] { 2, 3, 100_000_000, int.MaxValue })
        {
            var response = await session.Client.GetAsync($"/api/ui/activity?page={page.ToString(CultureInfo.InvariantCulture)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var activity = document.RootElement;
            Assert.Equal(page, activity.GetProperty("page").GetInt32());
            Assert.Equal(0, activity.GetProperty("items").GetArrayLength());
            Assert.False(activity.GetProperty("hasMore").GetBoolean());
            Assert.True(activity.GetProperty("hasPrevious").GetBoolean());
            Assert.Equal(0, activity.GetProperty("notableCount").GetInt32());
        }
    }

    private static string[] NamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static void AssertUtcSince(DateTime started, JsonElement timestamp)
    {
        var text = timestamp.GetString()!;
        Assert.EndsWith("Z", text);
        var value = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.InRange(value, started.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }

    /// <summary>The account id in a real session ticket, as the vault issued it.</summary>
    private Guid AccountIdOf(VaultSession session)
    {
        var ticket = Assert.IsType<AuthenticationTicket>(TicketFormat().Unprotect(Assert.IsType<Cookie>(session.SessionCookie).Value));
        return ticket.Principal.GetUserId();
    }

    /// <summary>
    /// A copy of a real, protected session ticket with its claims altered, keeping its revocation
    /// metadata: no alternate authentication scheme is involved.
    /// </summary>
    private Cookie Tamper(VaultSession session, Action<ClaimsIdentity> change)
    {
        var cookie = Assert.IsType<Cookie>(session.SessionCookie);
        var format = TicketFormat();
        var ticket = Assert.IsType<AuthenticationTicket>(format.Unprotect(cookie.Value));
        change(Assert.IsType<ClaimsIdentity>(ticket.Principal.Identity));
        return new Cookie(cookie.Name, format.Protect(ticket));
    }

    private ISecureDataFormat<AuthenticationTicket> TicketFormat()
        => vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;

    /// <summary>
    /// Writes audit rows owned by the account through the vault's own fail-closed sink, with every
    /// dependency from the vault's services except a clock stopped at one instant.
    /// </summary>
    private async Task<string[]> WriteRowsAtOneInstantAsync(Guid accountId, string username, int count)
    {
        using var scope = vault.Services.CreateScope();
        var sink = ActivatorUtilities.CreateInstance<AuditSink>(scope.ServiceProvider, new StoppedClock(DateTimeOffset.UtcNow));
        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            names[i] = $"it-paging-{i:D2}";
            await sink.WriteAsync(new AuditEntry
            {
                // Alternating, so each page's notable count is known: revealing is notable, viewing is routine.
                Action = i % 2 == 0 ? AuditAction.SecretRevealed : AuditAction.SecretMetadataViewed,
                ResourceType = "Secret",
                ResourceName = names[i],
                ActorUserId = accountId,
                ActorUsername = username,
            }, CancellationToken.None);
        }

        return names;
    }

    private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
