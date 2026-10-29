using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using EclipsVault.Core.Application.Activity;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class SelfServiceScopingTests(VaultFixture vault)
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
        var target = await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.51.100.111"));
        var before = await OwnActivityAsync(AccountIdOf(target));
        var admin = await vault.AdminClientAsync();

        var reset = await PostFormAsync(admin, $"/Admin/ResetTotp/{AccountIdOf(target):D}", new(), "/Admin/Users");
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        // The reset ends the target's sessions, so its own feed is read as its next session would see it.
        Assert.Equal(before, await OwnActivityAsync(AccountIdOf(target)));
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
        var target = await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.51.100.112"));
        await FailSignInAsync(username, "198.51.100.113");
        var admin = (await vault.SignInNewAccountAsync("it-scope-unlock-admin", IPAddress.Parse("198.51.100.114"),
            ClearanceLevel.TopSecret)).Client;
        var adminHistory = await admin.GetStringAsync("/api/ui/sign-ins");

        var response = await PostFormAsync(admin, $"/Admin/Unlock/{AccountIdOf(target):D}", new(), "/Admin/Users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(adminHistory, await admin.GetStringAsync("/api/ui/sign-ins"));
        var activityBody = await target.Client.GetStringAsync("/api/ui/activity");
        var historyBody = await target.Client.GetStringAsync("/api/ui/sign-ins");
        Assert.DoesNotContain("198.51.100.114", activityBody);
        Assert.DoesNotContain("198.51.100.114", historyBody);
        Assert.Contains("Your account was unlocked", activityBody);
        // The global audit retains the actual source; self-service projections hide it.
        Assert.Contains("198.51.100.114", await admin.GetStringAsync("/Audit"));
        using var history = JsonDocument.Parse(historyBody);
        AssertSummary(history.RootElement, 1, 1, 1, 2, needsAttention: true, lastFailureKind: JsonValueKind.String);
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
        var codes = await ProfileApi.RecoverySetAsync(b.ProfileOwner!);
        // Three fresh sessions use B's second address. Six Account POSTs fit its credential budget;
        // separate recovery codes prove each session without replaying an authenticator step.
        var firstDevice = await SignInWithRecoveryCodeAsync(bUsername, password, codes[0], bOtherAddress);
        Assert.NotNull(firstDevice.SessionCookie);
        var secondDevice = await SignInWithRecoveryCodeAsync(bUsername, password, codes[1], bOtherAddress);
        Assert.NotNull(secondDevice.SessionCookie);
        var thirdDevice = await SignInWithRecoveryCodeAsync(bUsername, password, codes[2], bOtherAddress);
        Assert.NotNull(thirdDevice.SessionCookie);

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
                 "Signed in", "Signed in with a recovery code", "Generated new recovery codes", "Verified your account for a security change", "A sign-in attempt failed",
                 "Signed in", "Set up your authenticator"],
                [bOtherAddress, bOtherAddress, bOtherAddress, bOtherAddress, bOtherAddress, bOtherAddress,
                 bAddress, bAddress, bFailedAddress, bAddress, bAddress]);
        }
        using (var history = JsonDocument.Parse(aResponses[1]))
        {
            AssertSummary(history.RootElement, 1, 0, 0, 1, needsAttention: false, lastFailureKind: JsonValueKind.Null);
            var signIn = Assert.Single(history.RootElement.GetProperty("events").EnumerateArray());
            Assert.Equal(aAddress, signIn.GetProperty("sourceIp").GetString());
            Assert.Equal("Signed in", signIn.GetProperty("title").GetString());
            Assert.Equal("FirstSeen", signIn.GetProperty("locationFlag").GetString());
        }
        using (var history = JsonDocument.Parse(bResponses[1]))
        {
            AssertSummary(history.RootElement, 7, 1, 1, 3, needsAttention: true, lastFailureKind: JsonValueKind.String);
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
        Assert.All(adminResponses, body =>
        {
            Assert.DoesNotContain(aAddress, body);
            Assert.DoesNotContain(bAddress, body);
            Assert.DoesNotContain(bOtherAddress, body);
            Assert.DoesNotContain(bFailedAddress, body);
        });
        // The bootstrap administrator has one enrolled authenticator and one session. Creating
        // other accounts and B using recovery codes must widen neither history nor posture.
        using (var history = JsonDocument.Parse(adminResponses[1]))
        {
            AssertSummary(history.RootElement, 1, 0, 0, 1, needsAttention: false, lastFailureKind: JsonValueKind.Null);
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
        var updated = await ProfileHttp.UpdateDisplayNameAsync(old, "Former account");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var oldResponses = await ReadEndpointsAsync(old.Client);
        Assert.Contains("Updated your profile", oldResponses[0]);
        Assert.Contains(oldFailedAddress, oldResponses[1]);

        await vault.DeleteAccountAsync(username);
        // A fresh password and enrollment create a distinct account session. Its history must be
        // scoped to its new account identifier, even though its username is reused.
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
            AssertSummary(history.RootElement, 1, 0, 0, 1, needsAttention: false, lastFailureKind: JsonValueKind.Null);
            Assert.Equal(newAddress, Assert.Single(history.RootElement.GetProperty("events").EnumerateArray())
                .GetProperty("sourceIp").GetString());
        }
        Assert.All(newResponses, body =>
        {
            Assert.DoesNotContain(oldAddress, body);
            Assert.DoesNotContain(oldFailedAddress, body);
            Assert.DoesNotContain("Updated your profile", body);
            Assert.DoesNotContain("sign-in attempt failed", body, StringComparison.OrdinalIgnoreCase);
        });
        await AssertInjectedParametersIgnoredAsync(replacement.Client, newResponses, AccountIdOf(old), username);
    }

    [Fact]
    public async Task Attempts_against_unknown_usernames_appear_in_no_accounts_activity_or_sign_in_history()
    {
        var a = await vault.SignInNewAccountAsync("it-scope-unknown-a", IPAddress.Parse("198.51.100.88"));
        var b = await vault.SignInNewAccountAsync("it-scope-unknown-b", IPAddress.Parse("198.51.100.89"));
        var admin = await vault.AdminClientAsync();
        var beforeA = await ReadEndpointsAsync(a.Client);
        var beforeB = await ReadEndpointsAsync(b.Client);
        var beforeAdmin = await ReadEndpointsAsync(admin);

        const string unknown = "it-scope-does-not-exist";
        const string source = "198.51.100.90";
        await FailSignInAsync(unknown, source);
        // The global Audit log proves the fail-closed write occurred, without inspecting a DB row.
        var audit = await admin.GetStringAsync("/Audit");
        Assert.Contains(unknown, audit);
        Assert.Contains(source, audit);

        var afterA = await ReadEndpointsAsync(a.Client);
        Assert.Equal(beforeA, afterA);
        Assert.All(afterA, body =>
        {
            Assert.DoesNotContain(unknown, body);
            Assert.DoesNotContain(source, body);
        });
        var afterB = await ReadEndpointsAsync(b.Client);
        Assert.Equal(beforeB, afterB);
        Assert.All(afterB, body =>
        {
            Assert.DoesNotContain(unknown, body);
            Assert.DoesNotContain(source, body);
        });
        var afterAdmin = await ReadEndpointsAsync(admin);
        Assert.Equal(beforeAdmin, afterAdmin);
        Assert.All(afterAdmin, body =>
        {
            Assert.DoesNotContain(unknown, body);
            Assert.DoesNotContain(source, body);
        });
    }

    [Fact]
    public async Task An_unknown_username_attempt_with_an_existing_session_appears_in_no_accounts_history()
    {
        var a = await vault.SignInNewAccountAsync("it-scope-authenticated-unknown-a", IPAddress.Parse("198.51.100.96"));
        var b = await vault.SignInNewAccountAsync("it-scope-authenticated-unknown-b", IPAddress.Parse("198.51.100.98"));
        var admin = await vault.AdminClientAsync();
        var beforeA = await ReadEndpointsAsync(a.Client);
        var beforeB = await ReadEndpointsAsync(b.Client);
        var beforeAdmin = await ReadEndpointsAsync(admin);

        const string unknown = "it-scope-authenticated-does-not-exist";
        const string source = "198.51.100.97";
        var signedIn = vault.CreateSession(Assert.IsType<Cookie>(a.SessionCookie), IPAddress.Parse(source));
        // Login's GET redirects a signed-in caller. A token from Secrets is valid for their
        // current identity and proves this unsafe request genuinely carries A's session.
        var response = await PostFormAsync(signedIn.Client, "/Account/Login", new()
        {
            ["Username"] = unknown,
            ["Password"] = "An-intentionally-incorrect-password-57!",
        }, "/Secrets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid username or password.", await response.Content.ReadAsStringAsync());
        Assert.NotNull(signedIn.SessionCookie);
        var audit = await admin.GetStringAsync("/Audit");
        Assert.Contains(unknown, audit);
        Assert.Contains(source, audit);

        var afterA = await ReadEndpointsAsync(a.Client);
        Assert.All(afterA, body => Assert.DoesNotContain(source, body));
        Assert.Equal(beforeA, afterA);
        var afterB = await ReadEndpointsAsync(b.Client);
        Assert.All(afterB, body => Assert.DoesNotContain(source, body));
        Assert.Equal(beforeB, afterB);
        var afterAdmin = await ReadEndpointsAsync(admin);
        Assert.All(afterAdmin, body => Assert.DoesNotContain(source, body));
        Assert.Equal(beforeAdmin, afterAdmin);
    }

    [Fact]
    public async Task An_unknown_sso_identity_with_an_existing_session_appears_in_no_accounts_history()
    {
        var a = await vault.SignInNewAccountAsync("it-scope-sso-unknown-a", IPAddress.Parse("198.51.100.99"));
        var b = await vault.SignInNewAccountAsync("it-scope-sso-unknown-b", IPAddress.Parse("198.51.100.100"));
        var admin = await vault.AdminClientAsync();
        var beforeA = await ReadEndpointsAsync(a.Client);
        var beforeB = await ReadEndpointsAsync(b.Client);
        var beforeAdmin = await ReadEndpointsAsync(admin);

        const string unknown = "it-scope-idp-only@example.test";
        const string source = "198.51.100.101";
        var callback = vault.CreateSession(Assert.IsType<Cookie>(a.SessionCookie), IPAddress.Parse(source));
        var options = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthSchemes.OidcCorrelation);
        // The seam begins AFTER OIDC verification. Use the real production cookie scheme and
        // protector for its IdP assertion, alongside A's independently issued vault session.
        // This exercises account matching and audit attribution, not the IdP's token validation.
        var assertion = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("iss", "https://idp.example.test/realms/integration"),
            new Claim("sub", "it-scope-idp-only"),
            new Claim("email", unknown),
            new Claim("email_verified", "true"),
        ], AuthSchemes.Oidc));
        var ticket = new AuthenticationTicket(assertion, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10),
        }, AuthSchemes.OidcCorrelation);
        var cookieName = Assert.IsType<string>(options.Cookie.Name);
        callback.Cookies.Add(callback.Client.BaseAddress!, new Cookie(cookieName, options.TicketDataFormat.Protect(ticket), "/")
        {
            Secure = true,
            HttpOnly = true,
        });

        var response = await callback.Client.GetAsync("/Account/ExternalCallback");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
        Assert.Null(callback.Cookies.GetCookies(callback.Client.BaseAddress!)[cookieName]);
        Assert.NotNull(callback.SessionCookie);
        var audit = await admin.GetStringAsync("/Audit");
        Assert.Contains(unknown, audit);
        Assert.Contains(source, audit);
        var afterA = await ReadEndpointsAsync(a.Client);
        Assert.All(afterA, body =>
        {
            Assert.DoesNotContain(unknown, body);
            Assert.DoesNotContain(source, body);
        });
        Assert.Equal(beforeA, afterA);
        var afterB = await ReadEndpointsAsync(b.Client);
        Assert.All(afterB, body =>
        {
            Assert.DoesNotContain(unknown, body);
            Assert.DoesNotContain(source, body);
        });
        Assert.Equal(beforeB, afterB);
        var afterAdmin = await ReadEndpointsAsync(admin);
        Assert.All(afterAdmin, body =>
        {
            Assert.DoesNotContain(unknown, body);
            Assert.DoesNotContain(source, body);
        });
        Assert.Equal(beforeAdmin, afterAdmin);
    }

    [Fact]
    public async Task Reading_all_three_endpoints_adds_no_activity_for_the_caller()
    {
        var session = await vault.SignInNewAccountAsync("it-scope-read-only", IPAddress.Parse("198.51.100.110"));
        var before = await session.Client.GetStringAsync("/api/ui/activity");
        using (var activity = JsonDocument.Parse(before))
        {
            Assert.False(activity.RootElement.GetProperty("hasMore").GetBoolean());
        }

        await ReadEndpointsAsync(session.Client);

        Assert.Equal(before, await session.Client.GetStringAsync("/api/ui/activity"));
    }

    private async Task<string[]> OwnActivityAsync(Guid accountId)
    {
        using var request = vault.Services.CreateScope();
        var feed = await request.ServiceProvider.GetRequiredService<IActivityService>()
            .GetForUserAsync(accountId, 1, ActivityService.DefaultPageSize, default);
        return feed.Items.Select(item => $"{item.TimestampUtc:O} {item.Title} {item.SourceIp}").ToArray();
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

    private static void AssertSummary(JsonElement history, int successes, int failures, int suspicious, int locations, bool needsAttention, JsonValueKind lastFailureKind)
    {
        var summary = history.GetProperty("summary");
        Assert.Equal(successes, summary.GetProperty("successCount").GetInt32());
        Assert.Equal(failures, summary.GetProperty("failedCount").GetInt32());
        Assert.Equal(suspicious, summary.GetProperty("suspiciousCount").GetInt32());
        Assert.Equal(locations, summary.GetProperty("distinctLocations").GetInt32());
        Assert.Equal(needsAttention, summary.GetProperty("needsAttention").GetBoolean());
        Assert.Equal(JsonValueKind.String, summary.GetProperty("lastSuccessUtc").ValueKind);
        Assert.Equal(lastFailureKind, summary.GetProperty("lastFailedUtc").ValueKind);
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
        return [await client.GetStringAsync(Endpoints[0]), await client.GetStringAsync(Endpoints[1]), await client.GetStringAsync(Endpoints[2])];
    }

    private static async Task AssertInjectedParametersIgnoredAsync(HttpClient client, string[] expected, Guid otherId, string otherUsername)
    {
        var query = $"?userId={otherId:D}&username={Uri.EscapeDataString(otherUsername)}&pageSize=100&actorUserId={otherId:D}";
        Assert.Equal(expected[0], await client.GetStringAsync(Endpoints[0] + query));
        Assert.Equal(expected[1], await client.GetStringAsync(Endpoints[1] + query));
        Assert.Equal(expected[2], await client.GetStringAsync(Endpoints[2] + query));
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

}
