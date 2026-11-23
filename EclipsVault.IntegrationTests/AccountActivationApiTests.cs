using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Creating an account by invitation and activating it (ADR-0033): the administrator enters the
/// person's mailbox and never holds a credential; only the emailed token reaches the authenticator seed.
/// </summary>
[Collection(InvitationCollection.Name)]
public sealed class AccountActivationApiTests(InvitationFixture fixture)
{
    private VaultFixture Vault => fixture.Vault;

    /// <summary>The account's email is the mailbox the administrator entered, never one the vault made up.</summary>
    [Fact]
    public async Task Create_keeps_the_entered_mailbox_as_the_account_email()
    {
        var admin = await Vault.AdminClientAsync();
        const string mailbox = "grace.hopper@example.test";

        var response = await UserAccounts.CreateAsync(admin, JsonContent.Create(new
        {
            firstName = "Grace",
            lastName = "Hopper",
            username = "it-act-f7",
            email = mailbox,
            clearance = (int)ClearanceLevel.Standard,
            project = "GLOBAL"
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(mailbox, body.GetProperty("email").GetString());
        var saved = await ProfileHttp.AccountAsync(Vault, "it-act-f7");
        Assert.Equal(mailbox, saved.Email);
        Assert.True(saved.AwaitingActivation);
        var (_, _, _, token) = await Vault.Mailbox!.LatestInvitationForAsync(mailbox);
        Assert.False(string.IsNullOrEmpty(token));
    }

    /// <summary>
    /// Whoever creates the account must not be able to pass its password stage and copy the seed the
    /// owner will later enroll. A password the administrator slips into the create body is not a credential.
    /// </summary>
    [Fact]
    public async Task The_administrator_cannot_reach_a_new_accounts_authenticator_seed()
    {
        var admin = await Vault.AdminClientAsync();
        const string chosenByAdministrator = "Administrator-chosen-credential-180";

        var created = await UserAccounts.CreateAsync(admin, JsonContent.Create(new
        {
            firstName = "Seed",
            lastName = "Copier",
            username = "it-act-f3",
            email = "seed.copier@example.test",
            password = chosenByAdministrator,
            clearance = (int)ClearanceLevel.Standard,
            project = "GLOBAL"
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var copier = Vault.CreateSession(source: IPAddress.Parse("198.18.121.3")).Client;
        var passwordStage = await UiApiRequest.SendWithTokenAsync(copier, await SignInDocument.TokenAsync(copier), HttpMethod.Post,
            "/api/ui/auth/password", JsonContent.Create(new { username = "it-act-f3", password = chosenByAdministrator }));
        await UiApiFailureContractTests.AssertProblemAsync(passwordStage, 400, "sign_in_failed");

        var enrollment = await UiApiRequest.SendWithTokenAsync(copier, await SignInDocument.TokenAsync(copier), HttpMethod.Post,
            "/api/ui/auth/enrollment");
        await UiApiFailureContractTests.AssertProblemAsync(enrollment, 401, "sign_in_expired");
    }

    [Fact]
    public async Task Inspecting_shows_one_seed_and_spends_nothing_then_completing_activates_and_the_person_signs_in()
    {
        var token = await InviteAsync("it-act-flow");
        var visitor = ActivationApi.Visitor(Vault, "198.18.180.1");

        var first = await ActivationApi.InspectAsync(visitor, token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl?.NoStore);
        using var shown = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal(["displayName", "expiresAtUtc", "qrCodeDataUri", "secretBase32", "username"], ProfileApi.NamesOf(shown.RootElement));
        Assert.Equal("it-act-flow", shown.RootElement.GetProperty("username").GetString());
        Assert.Equal("Integration Test", shown.RootElement.GetProperty("displayName").GetString());
        Assert.StartsWith("data:image/png;base64,", shown.RootElement.GetProperty("qrCodeDataUri").GetString(), StringComparison.Ordinal);
        var secret = shown.RootElement.GetProperty("secretBase32").GetString()!;
        using (var again = JsonDocument.Parse(await (await ActivationApi.InspectAsync(visitor, token)).Content.ReadAsStringAsync()))
        {
            Assert.Equal(secret, again.RootElement.GetProperty("secretBase32").GetString());
        }
        var account = await ProfileHttp.AccountAsync(Vault, "it-act-flow");
        var inspected = await InvitationOfAsync(account.Id);
        Assert.Null(inspected.SpentAtUtc);
        Assert.Equal(secret, inspected.PendingTotpSecret);
        Assert.Equal(inspected.ExpiresAtUtc, shown.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset());
        var head = await AuditTrail.HeadAsync(Vault);

        var code = ActivationApi.FirstCode(secret);
        var completed = await ActivationApi.CompleteAsync(visitor, token, Password, code);

        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        var activated = await ProfileHttp.AccountAsync(Vault, "it-act-flow");
        Assert.False(activated.AwaitingActivation);
        Assert.True(activated.TotpEnabled);
        Assert.Equal(secret, activated.TotpSecret);
        Assert.Equal(account.CredentialGeneration + 1, activated.CredentialGeneration);
        var spent = await InvitationOfAsync(account.Id);
        Assert.NotNull(spent.SpentAtUtc);
        Assert.Null(spent.PendingTotpSecret);
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(Vault, head));
        Assert.Equal(AuditAction.AccountActivated, row.Action);
        Assert.Equal(account.Id, row.UserId);
        await ProofDisclosure.AssertNotDisclosedAsync(Vault, head, account.Id, "/api/ui/auth/activation/complete", token, Password, secret);
        await UiApiFailureContractTests.AssertProblemAsync(await ActivationApi.InspectAsync(visitor, token), 404, "invitation_invalid");

        var person = Vault.CreateSession(source: IPAddress.Parse("198.18.180.2")).Client;
        var password = await UiApiRequest.SendWithTokenAsync(person, await SignInDocument.TokenAsync(person), HttpMethod.Post,
            "/api/ui/auth/password", JsonContent.Create(new { username = "it-act-flow", password = Password }));
        Assert.Equal("totp", (await password.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("next").GetString());
        var signedIn = await UiApiRequest.SendWithTokenAsync(person, SignInDocument.TokenOf(password), HttpMethod.Post,
            "/api/ui/auth/totp", JsonContent.Create(new { code = Totp.Code(secret, Totp.StepStillAccepted(DateTimeOffset.UtcNow, activated.LastTotpStep)) }));
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task A_wrong_code_or_a_refused_password_keeps_the_invitation_usable()
    {
        var token = await InviteAsync("it-act-refused");
        var visitor = ActivationApi.Visitor(Vault, "198.18.180.3");
        var secret = await ActivationApi.SecretAsync(visitor, token);
        var account = await ProfileHttp.AccountAsync(Vault, "it-act-refused");
        var head = await AuditTrail.HeadAsync(Vault);

        await UiApiFailureContractTests.AssertProblemAsync(
            await ActivationApi.CompleteAsync(visitor, token, Password, Totp.Code(secret, Totp.CurrentStep() + 10)), 400, "code_invalid");
        var shortPassword = await ActivationApi.CompleteAsync(visitor, token, "short", ActivationApi.FirstCode(secret));
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
        using (var refusal = JsonDocument.Parse(await shortPassword.Content.ReadAsStringAsync()))
        {
            Assert.Equal("validation_failed", refusal.RootElement.GetProperty("code").GetString());
            Assert.Equal("Your new password must be at least 12 characters long.",
                refusal.RootElement.GetProperty("errors").GetProperty("password")[0].GetString());
        }

        Assert.Empty(await AuditTrail.RowsAfterAsync(Vault, head));
        var unchanged = await ProfileHttp.AccountAsync(Vault, "it-act-refused");
        Assert.True(unchanged.AwaitingActivation);
        Assert.Equal(account.CredentialGeneration, unchanged.CredentialGeneration);
        var invitation = await InvitationOfAsync(account.Id);
        Assert.Null(invitation.SpentAtUtc);
        Assert.Equal(secret, invitation.PendingTotpSecret);
        Assert.Equal(HttpStatusCode.NoContent,
            (await ActivationApi.CompleteAsync(visitor, token, Password, ActivationApi.FirstCode(secret))).StatusCode);
    }

    [Fact]
    public async Task Unknown_expired_disabled_used_replaced_and_readdressed_tokens_get_one_identical_404()
    {
        // Each case comes from an address of its own, so no case spends another's sign-in budget.
        var answers = new List<string>();
        var nextAddress = 10;
        async Task AssertInvalidAsync(string token)
        {
            var visitor = ActivationApi.Visitor(Vault, $"198.18.180.{nextAddress++}");
            foreach (var response in new[]
            {
                await ActivationApi.InspectAsync(visitor, token),
                await ActivationApi.CompleteAsync(visitor, token, Password, "123456"),
            })
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                answers.Add(await response.Content.ReadAsStringAsync());
            }
        }

        await AssertInvalidAsync(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'));

        var expired = await InviteAsync("it-act-expired");
        await using (var scope = Vault.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var id = (await ProfileHttp.AccountAsync(Vault, "it-act-expired")).Id;
            await db.AccountInvitations.Where(invitation => invitation.UserId == id)
                .ExecuteUpdateAsync(set => set.SetProperty(invitation => invitation.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        await AssertInvalidAsync(expired);

        var disabled = await InviteAsync("it-act-disabled");
        await using (var scope = Vault.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Users.Where(user => user.Username == "it-act-disabled")
                .ExecuteUpdateAsync(set => set.SetProperty(user => user.IsDisabled, true));
        }
        await AssertInvalidAsync(disabled);

        var used = await InviteAsync("it-act-used");
        var activator = ActivationApi.Visitor(Vault, "198.18.180.4");
        var usedSecret = await ActivationApi.SecretAsync(activator, used);
        Assert.Equal(HttpStatusCode.NoContent,
            (await ActivationApi.CompleteAsync(activator, used, Password, ActivationApi.FirstCode(usedSecret))).StatusCode);
        await AssertInvalidAsync(used);

        var replaced = await InviteAsync("it-act-replaced");
        var replacedId = (await ProfileHttp.AccountAsync(Vault, "it-act-replaced")).Id;
        var admin = await Vault.AdminClientAsync();
        (await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), HttpMethod.Post,
            $"/api/ui/users/{replacedId}/invitation")).EnsureSuccessStatusCode();
        await AssertInvalidAsync(replaced);

        var readdressed = await InviteAsync("it-act-readdressed");
        var readdressedId = (await ProfileHttp.AccountAsync(Vault, "it-act-readdressed")).Id;
        admin = await Vault.AdminClientAsync();
        (await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), HttpMethod.Post,
            $"/api/ui/users/{readdressedId}/invitation-address",
            JsonContent.Create(new { email = "it-act-readdressed.right@example.test" }))).EnsureSuccessStatusCode();
        await AssertInvalidAsync(readdressed);

        Assert.Single(answers.Distinct());
        using var problem = JsonDocument.Parse(answers[0]);
        Assert.Equal("invitation_invalid", problem.RootElement.GetProperty("code").GetString());
    }

    private const string Password = "It-activation-chosen-password-180";

    /// <summary>Invites a new account through the administrator's create and returns its emailed token.</summary>
    private async Task<string> InviteAsync(string username)
    {
        (await UserAccounts.CreateAsync(await Vault.AdminClientAsync(), username)).EnsureSuccessStatusCode();
        return (await Vault.Mailbox!.LatestInvitationForAsync(UserAccounts.EmailOf(username))).Token;
    }

    private async Task<EclipsVault.Core.Domain.Entities.AccountInvitation> InvitationOfAsync(Guid accountId)
    {
        await using var scope = Vault.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccountInvitations.AsNoTracking()
            .SingleAsync(invitation => invitation.UserId == accountId);
    }
}

/// <summary>The two anonymous activation calls, as the activation page makes them.</summary>
internal static class ActivationApi
{
    /// <summary>A visitor with no session, from an address of its own so its calls spend no other test's budget.</summary>
    public static HttpClient Visitor(VaultFixture vault, string address) => vault.CreateSession(source: IPAddress.Parse(address)).Client;

    public static async Task<HttpResponseMessage> InspectAsync(HttpClient visitor, string token)
        => await UiApiRequest.SendWithTokenAsync(visitor, await SignInDocument.TokenAsync(visitor, "/Account/Activate"), HttpMethod.Post,
            "/api/ui/auth/activation", JsonContent.Create(new { token }));

    public static async Task<string> SecretAsync(HttpClient visitor, string token)
    {
        var response = await InspectAsync(visitor, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secretBase32").GetString()!;
    }

    public static async Task<HttpResponseMessage> CompleteAsync(HttpClient visitor, string token, string password, string code)
        => await UiApiRequest.SendWithTokenAsync(visitor, await SignInDocument.TokenAsync(visitor, "/Account/Activate"), HttpMethod.Post,
            "/api/ui/auth/activation/complete", JsonContent.Create(new { token, password, code }));

    /// <summary>
    /// A first code for a newly shown authenticator. Nothing is spent yet, and the vault accepts the current step
    /// for one step either side, so the code still works if the step ends before the vault reads it.
    /// </summary>
    public static string FirstCode(string secret) => Totp.Code(secret, Totp.CurrentStep());
}
