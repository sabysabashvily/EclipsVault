using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed partial class SharedByMeTests(VaultFixture vault)
{
    /// <summary>
    /// Deleting an account leaves the shares it issued in place, and its username can then be given to
    /// a new account. A username must never be the key to someone else's shares.
    /// </summary>
    [Fact]
    public async Task A_new_account_with_a_deleted_issuers_username_cannot_see_or_revoke_its_shares()
    {
        const string issuerUsername = "it-share-reused-issuer";
        const string recipientUsername = "it-share-reused-recipient";
        const string secretName = "it-share-reused-secret";
        var details = await CreateSecretAsync(await vault.AdminClientAsync(), secretName);
        // Outside the secret's project, so only the share lets the recipient open it.
        var recipient = await vault.SignInNewAccountAsync(recipientUsername, IPAddress.Parse("198.51.100.120"), project: "SHARE-TEST");
        Assert.StartsWith("/Account/Denied?", (await recipient.Client.GetAsync(details)).Headers.Location?.OriginalString);

        var issuer = await vault.SignInNewAccountAsync(issuerUsername, IPAddress.Parse("198.51.100.121"));
        await ShareAsync(issuer.Client, details, recipientUsername);
        var issuersList = WebUtility.HtmlDecode(await issuer.Client.GetStringAsync("/Secrets/SharedByMe"));
        Assert.Contains(secretName, issuersList);
        var shareId = RevokeShareField().Match(issuersList).Groups[1].Value;

        await vault.DeleteAccountAsync(issuerUsername);
        var replacement = await vault.SignInNewAccountAsync(issuerUsername, IPAddress.Parse("198.51.100.122"));

        var replacementsList = WebUtility.HtmlDecode(await replacement.Client.GetStringAsync("/Secrets/SharedByMe"));
        Assert.Contains("You haven't shared anything", replacementsList);
        Assert.DoesNotContain(secretName, replacementsList);
        Assert.DoesNotContain(recipientUsername, replacementsList);

        var revoke = await replacement.Client.PostAsync("/Secrets/RevokeSharedByMe", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(replacementsList, "/Secrets/SharedByMe"),
            ["grantId"] = shareId,
        }));
        Assert.Equal("/Secrets/SharedByMe", revoke.Headers.Location?.OriginalString);
        Assert.Contains("That grant no longer exists, or it wasn't yours to revoke.",
            WebUtility.HtmlDecode(await replacement.Client.GetStringAsync("/Secrets/SharedByMe")));

        Assert.Equal(HttpStatusCode.OK, (await recipient.Client.GetAsync(details)).StatusCode);
        Assert.Contains(secretName, await recipient.Client.GetStringAsync("/Secrets/SharedWithMe"));
    }

    /// <summary>Creates an Internal secret in the GLOBAL project the way an administrator does, and returns its details page.</summary>
    private static async Task<string> CreateSecretAsync(HttpClient admin, string name)
    {
        var form = await admin.GetStringAsync("/Secrets/Create");
        var created = await admin.PostAsync("/Secrets/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(form, "/Secrets/Create"),
            ["Name"] = name,
            ["Value"] = "it-shared-value",
            ["ProjectKey"] = "GLOBAL",
            ["Environment"] = "Development",
            ["Sensitivity"] = "Internal",
        }));

        return created.Headers.Location?.OriginalString is { } details && details.StartsWith("/Secrets/Details/", StringComparison.Ordinal)
            ? details
            : throw new InvalidOperationException($"Creating {name} answered {(int)created.StatusCode}, not a redirect to its details.");
    }

    /// <summary>Shares the secret from its details page, with no expiry, as a browser does.</summary>
    private static async Task ShareAsync(HttpClient issuer, string details, string recipientUsername)
    {
        var page = await issuer.GetStringAsync(details);
        var shared = await issuer.PostAsync("/Secrets/Grant", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, details),
            ["SecretId"] = details["/Secrets/Details/".Length..],
            ["GranteeUsernameOrEmail"] = recipientUsername,
            ["TtlDays"] = "0",
        }));
        if (shared.Headers.Location?.OriginalString != details)
        {
            throw new InvalidOperationException($"Sharing answered {(int)shared.StatusCode}, not a redirect to the secret's details.");
        }
    }

    [GeneratedRegex("<input type=\"hidden\" name=\"grantId\" value=\"([0-9a-fA-F-]{36})\"")]
    private static partial Regex RevokeShareField();
}
