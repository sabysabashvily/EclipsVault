using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed partial class SelfServiceScopingTests(VaultFixture vault)
{
    private static readonly string[] Endpoints = ["/api/ui/activity", "/api/ui/sign-ins", "/api/ui/security"];

    [Fact]
    public async Task Activity_contains_only_actions_from_the_callers_own_addresses()
    {
        const string address = "198.51.100.80";
        var session = await vault.SignInNewAccountAsync("it-scope-own-actions", IPAddress.Parse(address));

        using var activity = JsonDocument.Parse(await session.Client.GetStringAsync("/api/ui/activity"));

        Assert.All(activity.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal(address, item.GetProperty("sourceIp").GetString()));
    }

    [Fact]
    public async Task Creating_resetting_and_deleting_an_account_are_the_administrators_own_activity()
    {
        const string username = "it-scope-admin-actions";
        var target = await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.51.100.92"));
        var before = await target.Client.GetStringAsync("/api/ui/activity");
        var admin = await vault.AdminClientAsync();

        var reset = await PostFormAsync(admin, $"/Admin/ResetTotp/{AccountIdOf(target):D}", new(), "/Admin/Users");
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Equal(before, await target.Client.GetStringAsync("/api/ui/activity"));
        await vault.DeleteAccountAsync(username);

        using var activity = JsonDocument.Parse(await admin.GetStringAsync("/api/ui/activity"));
        var actions = activity.RootElement.GetProperty("items").EnumerateArray().Take(3).ToArray();
        Assert.Equal(["Deleted a user", "Reset a user's MFA", "Created a user"],
            actions.Select(item => item.GetProperty("title").GetString()));
        Assert.All(actions, item => Assert.Equal("internal", item.GetProperty("sourceIp").GetString()));
    }

    [Fact]
    public async Task An_account_unlock_stays_in_the_affected_accounts_sign_in_history()
    {
        const string username = "it-scope-unlocked";
        var target = await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.51.100.93"));
        await FailSignInAsync(username, "198.51.100.94");
        var admin = (await vault.SignInNewAccountAsync("it-scope-unlock-admin", IPAddress.Parse("198.51.100.95"),
            ClearanceLevel.TopSecret)).Client;
        var adminHistory = await admin.GetStringAsync("/api/ui/sign-ins");

        var response = await PostFormAsync(admin, $"/Admin/Unlock/{AccountIdOf(target):D}", new(), "/Admin/Users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(adminHistory, await admin.GetStringAsync("/api/ui/sign-ins"));
        var activityBody = await target.Client.GetStringAsync("/api/ui/activity");
        var historyBody = await target.Client.GetStringAsync("/api/ui/sign-ins");
        Assert.DoesNotContain("198.51.100.95", activityBody);
        Assert.DoesNotContain("198.51.100.95", historyBody);
        Assert.Contains("Your account was unlocked", activityBody);
        // The global audit retains the actual source; self-service projections hide it.
        Assert.Contains("198.51.100.95", await admin.GetStringAsync("/Audit"));
        using var history = JsonDocument.Parse(historyBody);
        AssertSummary(history.RootElement, 1, 1, 1, 2);
        var unlocked = Assert.Single(history.RootElement.GetProperty("events").EnumerateArray(),
            item => item.GetProperty("title").GetString() == "Account unlocked");
        Assert.Equal("Info", unlocked.GetProperty("outcome").GetString());
        Assert.Equal("System", unlocked.GetProperty("method").GetString());
        Assert.Equal("internal", unlocked.GetProperty("sourceIp").GetString());
    }

    [Fact]
    public async Task Each_endpoint_keeps_distinct_accounts_and_the_administrator_scoped_to_their_own_data_despite_injected_parameters()
    {
        const string aAddress = "198.51.100.81";
        const string bAddress = "198.51.100.82";
        const string bOtherAddress = "198.51.100.83";
        const string bFailedAddress = "198.51.100.84";
        const string bUsername = "it-scope-b";
        var a = await vault.SignInNewAccountAsync("it-scope-a", IPAddress.Parse(aAddress));
        var password = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var b = await vault.SignInNewAccountAsync(bUsername, IPAddress.Parse(bAddress), password: password);
        await FailSignInAsync(bUsername, bFailedAddress);
        var codes = await GenerateRecoveryCodesAsync(b.Client);
        for (var i = 0; i < 3; i++)
        {
            // Three fresh sessions, all from B's second successful address. Six Account POSTs,
            // below the source address's eleven-attempt budget; no TOTP step is replayed.
            var device = await SignInWithRecoveryCodeAsync(bUsername, password, codes[i], bOtherAddress);
            Assert.NotNull(device.SessionCookie);
        }

        var aResponses = await ReadEndpointsAsync(a.Client);
        var bResponses = await ReadEndpointsAsync(b.Client);
        using (var activity = JsonDocument.Parse(aResponses[0]))
        {
            AssertActivity(activity.RootElement, 1,
                ["Signed in", "Set up your authenticator"], [aAddress, aAddress]);
        }
        using (var activity = JsonDocument.Parse(bResponses[0]))
        {
            AssertActivity(activity.RootElement, 6,
                ["Signed in", "Signed in with a recovery code", "Signed in", "Signed in with a recovery code",
                 "Signed in", "Signed in with a recovery code", "Generated new recovery codes", "A sign-in attempt failed",
                 "Signed in", "Set up your authenticator"],
                [bOtherAddress, bOtherAddress, bOtherAddress, bOtherAddress, bOtherAddress, bOtherAddress,
                 bAddress, bFailedAddress, bAddress, bAddress]);
        }
        using (var history = JsonDocument.Parse(aResponses[1]))
        {
            AssertSummary(history.RootElement, 1, 0, 0, 1);
            var signIn = Assert.Single(history.RootElement.GetProperty("events").EnumerateArray());
            Assert.Equal(aAddress, signIn.GetProperty("sourceIp").GetString());
            Assert.Equal("Signed in", signIn.GetProperty("title").GetString());
            Assert.Equal("FirstSeen", signIn.GetProperty("locationFlag").GetString());
        }
        using (var history = JsonDocument.Parse(bResponses[1]))
        {
            AssertSummary(history.RootElement, 7, 1, 1, 3);
            var events = history.RootElement.GetProperty("events").EnumerateArray().ToArray();
            Assert.Equal(8, events.Length);
            Assert.Equal(3, events.Count(e => e.GetProperty("title").GetString() == "Signed in with a recovery code"));
            Assert.Equal(6, events.Count(e => e.GetProperty("sourceIp").GetString() == bOtherAddress));
            var failed = Assert.Single(events, e => e.GetProperty("outcome").GetString() == "Failed");
            Assert.Equal("Sign-in attempt failed", failed.GetProperty("title").GetString());
            Assert.Equal(bFailedAddress, failed.GetProperty("sourceIp").GetString());
            Assert.Equal("Unfamiliar", failed.GetProperty("locationFlag").GetString());
        }
        using (var checkup = JsonDocument.Parse(aResponses[2]))
        {
            AssertCheckup(checkup.RootElement, 65, "Fair", 1, 1, 2,
                "You have no backup codes left.", "Only this device is signed in.");
        }
        using (var checkup = JsonDocument.Parse(bResponses[2]))
        {
            AssertCheckup(checkup.RootElement, 83, "Good", 0, 2, 2,
                "You have 7 one-time backup codes.", "4 devices are signed in to your account.");
            Assert.Equal("Add a passkey for sign-in that can't be phished, even if your password leaks.",
                checkup.RootElement.GetProperty("nextStep").GetString());
        }

        var admin = await vault.AdminClientAsync();
        var adminResponses = await ReadEndpointsAsync(admin);
        foreach (var body in adminResponses)
        {
            foreach (var address in new[] { aAddress, bAddress, bOtherAddress, bFailedAddress })
            {
                Assert.DoesNotContain(address, body);
            }
        }
        // The bootstrap administrator has one enrolled authenticator and one session. Creating
        // other accounts and B using recovery codes must widen neither history nor posture.
        using (var history = JsonDocument.Parse(adminResponses[1]))
        {
            AssertSummary(history.RootElement, 1, 0, 0, 1);
            Assert.Equal("internal", Assert.Single(history.RootElement.GetProperty("events").EnumerateArray())
                .GetProperty("sourceIp").GetString());
        }
        using (var checkup = JsonDocument.Parse(adminResponses[2]))
        {
            AssertCheckup(checkup.RootElement, 65, "Fair", 1, 1, 2,
                "You have no backup codes left.", "Only this device is signed in.");
        }
        Assert.All(aResponses.Concat(bResponses), body => Assert.DoesNotContain("\"internal\"", body));
        Assert.All(aResponses, body =>
        {
            Assert.DoesNotContain(bAddress, body);
            Assert.DoesNotContain(bOtherAddress, body);
            Assert.DoesNotContain(bFailedAddress, body);
        });
        Assert.All(bResponses, body => Assert.DoesNotContain(aAddress, body));

        await AssertInjectedParametersIgnoredAsync(a.Client, aResponses, AccountIdOf(b), bUsername);
        await AssertInjectedParametersIgnoredAsync(b.Client, bResponses, AccountIdOf(a), "it-scope-a");
        await AssertInjectedParametersIgnoredAsync(admin, adminResponses, AccountIdOf(b), bUsername);
    }

    [Fact]
    public async Task Reusing_a_deleted_username_does_not_inherit_the_old_accounts_activity_or_sign_ins()
    {
        const string username = "it-scope-reused";
        const string oldAddress = "198.51.100.85";
        const string oldFailedAddress = "198.51.100.86";
        const string newAddress = "198.51.100.87";
        var old = await vault.SignInNewAccountAsync(username, IPAddress.Parse(oldAddress));
        await FailSignInAsync(username, oldFailedAddress);
        var updated = await PostFormAsync(old.Client, "/Profile/Update", new()
        {
            ["DisplayName"] = "Former account",
            ["Email"] = "former@example.test",
        }, "/Profile");
        Assert.Equal(HttpStatusCode.Redirect, updated.StatusCode);
        var oldResponses = await ReadEndpointsAsync(old.Client);
        Assert.Contains("Updated your profile", oldResponses[0]);
        Assert.Contains(oldFailedAddress, oldResponses[1]);

        await vault.DeleteAccountAsync(username);
        // A fresh password and enrollment create a NEW session. The old session's revocation is
        // intentionally irrelevant: the account-deletion fix on main is not on this branch.
        var replacement = await vault.SignInNewAccountAsync(username, IPAddress.Parse(newAddress));
        Assert.NotEqual(AccountIdOf(old), AccountIdOf(replacement));
        var newResponses = await ReadEndpointsAsync(replacement.Client);
        using (var activity = JsonDocument.Parse(newResponses[0]))
        {
            AssertActivity(activity.RootElement, 1,
                ["Signed in", "Set up your authenticator"], [newAddress, newAddress]);
        }
        using (var history = JsonDocument.Parse(newResponses[1]))
        {
            AssertSummary(history.RootElement, 1, 0, 0, 1);
            Assert.Equal(newAddress, Assert.Single(history.RootElement.GetProperty("events").EnumerateArray())
                .GetProperty("sourceIp").GetString());
        }
        foreach (var body in newResponses)
        {
            Assert.DoesNotContain(oldAddress, body);
            Assert.DoesNotContain(oldFailedAddress, body);
            Assert.DoesNotContain("Updated your profile", body);
            Assert.DoesNotContain("sign-in attempt failed", body, StringComparison.OrdinalIgnoreCase);
        }
        await AssertInjectedParametersIgnoredAsync(replacement.Client, newResponses, AccountIdOf(old), username);
    }

    [Fact]
    public async Task Attempts_against_unknown_usernames_appear_in_no_accounts_activity_or_sign_in_history()
    {
        var a = await vault.SignInNewAccountAsync("it-scope-unknown-a", IPAddress.Parse("198.51.100.88"));
        var b = await vault.SignInNewAccountAsync("it-scope-unknown-b", IPAddress.Parse("198.51.100.89"));
        var clients = new[] { a.Client, b.Client, await vault.AdminClientAsync() };
        var before = new List<string[]>();
        foreach (var client in clients) before.Add(await ReadEndpointsAsync(client));

        const string unknown = "it-scope-does-not-exist";
        const string source = "198.51.100.90";
        await FailSignInAsync(unknown, source);
        // The global Audit log proves the fail-closed write occurred, without inspecting a DB row.
        var audit = await clients[2].GetStringAsync("/Audit");
        Assert.Contains(unknown, audit);
        Assert.Contains(source, audit);

        for (var i = 0; i < clients.Length; i++)
        {
            var after = await ReadEndpointsAsync(clients[i]);
            Assert.Equal(before[i], after);
            Assert.All(after, body =>
            {
                Assert.DoesNotContain(unknown, body);
                Assert.DoesNotContain(source, body);
            });
        }
    }

    [Fact]
    public async Task An_unknown_username_attempt_with_an_existing_session_appears_in_no_accounts_history()
    {
        var a = await vault.SignInNewAccountAsync("it-scope-authenticated-unknown-a", IPAddress.Parse("198.51.100.96"));
        var b = await vault.SignInNewAccountAsync("it-scope-authenticated-unknown-b", IPAddress.Parse("198.51.100.98"));
        var clients = new[] { a.Client, b.Client, await vault.AdminClientAsync() };
        var before = new List<string[]>();
        foreach (var client in clients) before.Add(await ReadEndpointsAsync(client));

        const string unknown = "it-scope-authenticated-does-not-exist";
        const string source = "198.51.100.97";
        var signedIn = vault.CreateSession(Assert.IsType<Cookie>(a.SessionCookie), IPAddress.Parse(source));
        // Login's GET redirects a signed-in caller. A token from Profile is valid for their
        // current identity and proves this unsafe request genuinely carries A's session.
        var response = await PostFormAsync(signedIn.Client, "/Account/Login", new()
        {
            ["Username"] = unknown,
            ["Password"] = "An-intentionally-incorrect-password-57!",
        }, "/Profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid username or password.", await response.Content.ReadAsStringAsync());
        Assert.NotNull(signedIn.SessionCookie);
        var audit = await clients[2].GetStringAsync("/Audit");
        Assert.Contains(unknown, audit);
        Assert.Contains(source, audit);

        for (var i = 0; i < clients.Length; i++)
        {
            var after = await ReadEndpointsAsync(clients[i]);
            Assert.All(after, body => Assert.DoesNotContain(source, body));
            Assert.Equal(before[i], after);
        }
    }

    [Fact]
    public async Task Reading_all_three_endpoints_adds_no_activity_for_the_caller()
    {
        var session = await vault.SignInNewAccountAsync("it-scope-read-only", IPAddress.Parse("198.51.100.91"));
        var before = await session.Client.GetStringAsync("/api/ui/activity");
        using (var activity = JsonDocument.Parse(before))
        {
            Assert.False(activity.RootElement.GetProperty("hasMore").GetBoolean());
        }

        await ReadEndpointsAsync(session.Client);

        Assert.Equal(before, await session.Client.GetStringAsync("/api/ui/activity"));
    }

    private static void AssertActivity(JsonElement activity, int notableCount, string[] titles, string[] sources)
    {
        Assert.Equal(1, activity.GetProperty("page").GetInt32());
        Assert.Equal(25, activity.GetProperty("pageSize").GetInt32());
        Assert.False(activity.GetProperty("hasMore").GetBoolean());
        Assert.False(activity.GetProperty("hasPrevious").GetBoolean());
        Assert.Equal(notableCount, activity.GetProperty("notableCount").GetInt32());
        var items = activity.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(titles, items.Select(item => item.GetProperty("title").GetString()));
        Assert.Equal(sources, items.Select(item => item.GetProperty("sourceIp").GetString()));
    }

    private static void AssertSummary(JsonElement history, int successes, int failures, int suspicious, int locations)
    {
        var summary = history.GetProperty("summary");
        Assert.Equal(successes, summary.GetProperty("successCount").GetInt32());
        Assert.Equal(failures, summary.GetProperty("failedCount").GetInt32());
        Assert.Equal(suspicious, summary.GetProperty("suspiciousCount").GetInt32());
        Assert.Equal(locations, summary.GetProperty("distinctLocations").GetInt32());
        Assert.Equal(suspicious > 0, summary.GetProperty("needsAttention").GetBoolean());
        Assert.Equal(JsonValueKind.String, summary.GetProperty("lastSuccessUtc").ValueKind);
        Assert.Equal(failures == 0 ? JsonValueKind.Null : JsonValueKind.String, summary.GetProperty("lastFailedUtc").ValueKind);
    }

    private static void AssertCheckup(JsonElement checkup, int score, string grade, int needed, int recommended, int secured,
        string backupDetail, string devicesDetail)
    {
        Assert.Equal(score, checkup.GetProperty("score").GetInt32());
        Assert.Equal(grade, checkup.GetProperty("grade").GetString());
        Assert.Equal(needed, checkup.GetProperty("actionNeededCount").GetInt32());
        Assert.Equal(recommended, checkup.GetProperty("recommendedCount").GetInt32());
        Assert.Equal(secured, checkup.GetProperty("passCount").GetInt32());
        var checks = checkup.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Equal(4, checks.Length);
        Assert.Equal(backupDetail, Assert.Single(checks, c => c.GetProperty("kind").GetString() == "BackupCodes").GetProperty("detail").GetString());
        Assert.Equal(devicesDetail, Assert.Single(checks, c => c.GetProperty("kind").GetString() == "SignedInDevices").GetProperty("detail").GetString());
        Assert.Equal("An authenticator app is protecting your sign-in.",
            Assert.Single(checks, c => c.GetProperty("kind").GetString() == "TwoStepSignIn").GetProperty("detail").GetString());
        Assert.Equal("You haven't added a passkey yet.",
            Assert.Single(checks, c => c.GetProperty("kind").GetString() == "Passkey").GetProperty("detail").GetString());
    }

    private static async Task<string[]> ReadEndpointsAsync(HttpClient client)
    {
        var bodies = new List<string>();
        foreach (var endpoint in Endpoints) bodies.Add(await client.GetStringAsync(endpoint));
        return bodies.ToArray();
    }

    private static async Task AssertInjectedParametersIgnoredAsync(HttpClient client, string[] expected, Guid otherId, string otherUsername)
    {
        var query = $"?userId={otherId:D}&username={Uri.EscapeDataString(otherUsername)}&pageSize=100&actorUserId={otherId:D}";
        for (var i = 0; i < Endpoints.Length; i++)
        {
            Assert.Equal(expected[i], await client.GetStringAsync(Endpoints[i] + query));
        }
    }

    private async Task FailSignInAsync(string username, string source)
    {
        var anonymous = vault.CreateSession(source: IPAddress.Parse(source));
        var response = await PostFormAsync(anonymous.Client, "/Account/Login", new()
        {
            ["Username"] = username,
            ["Password"] = "An-intentionally-incorrect-password-57!",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid username or password.", await response.Content.ReadAsStringAsync());
        Assert.Null(anonymous.SessionCookie);
    }

    private static async Task<string[]> GenerateRecoveryCodesAsync(HttpClient client)
    {
        var response = await PostFormAsync(client, "/Profile/GenerateRecoveryCodes", new(), "/Profile");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Profile/RecoveryCodes", response.Headers.Location?.OriginalString);
        var page = await client.GetStringAsync("/Profile/RecoveryCodes");
        var codes = RecoveryCode().Matches(page).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToArray();
        Assert.Equal(10, codes.Length);
        return codes;
    }

    private async Task<VaultSession> SignInWithRecoveryCodeAsync(string username, string password, string code, string source)
    {
        var session = vault.CreateSession(source: IPAddress.Parse(source));
        var passwordStep = await PostFormAsync(session.Client, "/Account/Login", new() { ["Username"] = username, ["Password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, passwordStep.StatusCode);
        Assert.Equal("/Account/Totp", passwordStep.Headers.Location?.OriginalString);
        var secondFactor = await PostFormAsync(session.Client, "/Account/RecoveryCode", new() { ["Code"] = code });
        Assert.Equal(HttpStatusCode.Redirect, secondFactor.StatusCode);
        Assert.Equal("/Secrets", secondFactor.Headers.Location?.OriginalString);
        return session;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields, string? formPage = null)
    {
        var page = formPage ?? path;
        fields["__RequestVerificationToken"] = VaultFixture.FormTokenOf(await client.GetStringAsync(page), page);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private Guid AccountIdOf(VaultSession session)
    {
        var format = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;
        var ticket = Assert.IsType<AuthenticationTicket>(format.Unprotect(Assert.IsType<Cookie>(session.SessionCookie).Value));
        var accountId = ticket.Principal.GetUserId();
        Assert.NotEqual(Guid.Empty, accountId);
        return accountId;
    }

    [GeneratedRegex("<li class=\"mono\">([^<]+)</li>")]
    private static partial Regex RecoveryCode();
}
