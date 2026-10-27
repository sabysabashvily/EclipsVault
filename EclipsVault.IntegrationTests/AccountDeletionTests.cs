using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed partial class AccountDeletionTests(VaultFixture vault)
{
    /// <summary>
    /// A session carries its account's clearance and project, so a session that outlived its account
    /// would keep that account's access to secrets. Deleting the account must end its sessions.
    /// </summary>
    [Fact]
    public async Task Deleting_an_account_ends_its_sessions_so_they_can_no_longer_reveal_a_secret()
    {
        const string username = "it-deleted-reveal";
        var value = "it-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var details = await CreateSecretAsync(await vault.AdminClientAsync(), "it-deleted-reveal-secret", value);

        // A trusted address, so the reveal depends on the session alone.
        var session = await vault.SignInNewAccountAsync(username, IPAddress.Parse("10.52.0.70"));
        // The page stays open in the browser, so its form token is the one pressed again later.
        var token = VaultFixture.FormTokenOf(await session.Client.GetStringAsync(details), details);
        var before = await RevealAsync(session.Client, details, token);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(value, RevealedValue().Match(await before.Content.ReadAsStringAsync()).Groups[1].Value);
        // Taken now: a refused request clears the session cookie, and the copy below must outlive that.
        var copied = Assert.IsType<Cookie>(session.SessionCookie);

        await vault.DeleteAccountAsync(username);

        var after = await RevealAsync(session.Client, details, token);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Equal("/Account/Login", after.Headers.Location?.AbsolutePath);
        Assert.DoesNotContain(value, await after.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(session.SessionCookie);

        // Clearing the cookie is only this browser forgetting it: a copy of it is refused too.
        var replayed = vault.CreateSession(copied, IPAddress.Parse("10.52.0.70"));
        var replay = await replayed.Client.GetAsync(details);
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Equal("/Account/Login", replay.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task A_restarted_memory_store_refuses_a_previously_valid_session()
    {
        var signedIn = await vault.SignInNewAccountAsync("it-delete-surviving", IPAddress.Parse("10.52.0.71"));
        // A lost version cannot establish whether the session was revoked before the restart.
        await using var restarted = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host => host.UseEnvironment("Production"));
        using var replay = ReplayOn(restarted, signedIn.Cookies.GetCookieHeader(signedIn.Client.BaseAddress!));

        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/ui/me")).StatusCode);
    }

    private static HttpClient ReplayOn(WebApplicationFactory<Program> host, string cookies)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", cookies);
        return client;
    }

    /// <summary>Creates a secret the way an administrator does and returns its details page.</summary>
    private static async Task<string> CreateSecretAsync(HttpClient admin, string name, string value)
    {
        var form = await admin.GetStringAsync("/Secrets/Create");
        var created = await admin.PostAsync("/Secrets/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(form, "/Secrets/Create"),
            ["Name"] = name,
            ["Value"] = value,
            ["ProjectKey"] = "GLOBAL",
            ["Environment"] = "Development",
            ["Sensitivity"] = "Internal",
        }));

        return created.Headers.Location?.OriginalString is { } details && details.StartsWith("/Secrets/Details/", StringComparison.Ordinal)
            ? details
            : throw new InvalidOperationException($"Creating {name} answered {(int)created.StatusCode}, not a redirect to its details.");
    }

    /// <summary>Presses Reveal on the secret's details page, as a browser does.</summary>
    private static Task<HttpResponseMessage> RevealAsync(HttpClient client, string details, string token)
        => client.PostAsync(
            details.Replace("/Details/", "/Reveal/", StringComparison.Ordinal),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

    [GeneratedRegex("<code class=\"secret-code\" id=\"revealed-value\">([^<]+)</code>")]
    private static partial Regex RevealedValue();
}
