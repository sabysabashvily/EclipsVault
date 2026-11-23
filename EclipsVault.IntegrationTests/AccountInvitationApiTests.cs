using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Notifications;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class InvitationFixture : IAsyncLifetime
{
    public const string ConfiguredPublicUrl = "https://vault.example.test";

    public VaultFixture Vault { get; } = new()
    {
        Mailbox = new SmtpCapture(),
        PublicUrl = ConfiguredPublicUrl,
        TrustIdpMfa = true
    };

    public Task InitializeAsync() => Vault.InitializeAsync();
    public Task DisposeAsync() => Vault.DisposeAsync();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InvitationCollection : ICollectionFixture<InvitationFixture>
{
    public const string Name = "account invitations";
}

[Collection(InvitationCollection.Name)]
public sealed class AccountInvitationApiTests(InvitationFixture fixture)
{
    private const string Missing = "/api/ui/users/11111111-1111-1111-1111-111111111111";
    private const string InvitedPassword = "It-invited-refused-password!";
    private const string InvitedTotpSecret = "JBSWY3DPEHPK3PXP";

    private VaultFixture Vault => fixture.Vault;

    [Fact]
    public async Task New_user_metadata_reports_invitations_available_when_smtp_and_public_url_are_configured()
    {
        var admin = await Vault.AdminClientAsync();
        var response = await admin.GetAsync("/api/ui/users/new");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("invitationsAvailable").GetBoolean());
    }

    [Fact]
    public async Task Users_list_and_details_say_resend_can_deliver_when_smtp_and_public_url_are_configured()
    {
        var admin = await Vault.AdminClientAsync();
        var administrator = await ProfileHttp.AccountAsync(Vault, "vault-admin");

        using var list = JsonDocument.Parse(await (await admin.GetAsync("/api/ui/users")).Content.ReadAsStringAsync());
        using var details = JsonDocument.Parse(await (await admin.GetAsync($"/api/ui/users/{administrator.Id}")).Content.ReadAsStringAsync());

        Assert.True(list.RootElement.GetProperty("invitationsAvailable").GetBoolean());
        Assert.True(details.RootElement.GetProperty("invitationsAvailable").GetBoolean());
    }

    [Theory]
    [InlineData("POST", Missing + "/invitation", "198.18.120.11")]
    [InlineData("POST", Missing + "/invitation-address", "198.18.120.12")]
    public async Task Invitation_endpoints_accept_only_an_administrator_session(string method, string path, string source)
    {
        var anonymous = Vault.CreateClient();
        var reader = await Vault.SignInNewAccountAsync("it-inv-reader-" + source.Split('.').Last(), IPAddress.Parse(source));
        var apiKey = Vault.CreateClient();
        apiKey.DefaultRequestHeaders.Authorization = new("Bearer",
            await Vault.IssueApiKeyAsync(await Vault.AdminClientAsync(), "it-inv-key-" + source.Split('.').Last()));

        await UiApiFailureContractTests.AssertProblemAsync(await anonymous.SendAsync(new(new(method), path)), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await reader.Client.SendAsync(new(new(method), path)), 403, "forbidden");
        await UiApiFailureContractTests.AssertProblemAsync(await apiKey.SendAsync(new(new(method), path)), 401, "unauthenticated");
    }

    [Theory]
    [InlineData(Missing + "/invitation")]
    [InlineData(Missing + "/invitation-address")]
    public async Task Invitation_writes_require_antiforgery_and_fresh_proof_before_lookup(string path)
    {
        var admin = await Vault.AdminClientAsync();
        await UiApiFailureContractTests.AssertProblemAsync(
            await admin.PostAsJsonAsync(path, new { email = "fresh@example.test" }), 400, "antiforgery_rejected");

        var staleUsername = "it-inv-stale-" + path.Split('/').Last();
        var freshAdmin = await Vault.SignInNewAccountAsync(staleUsername,
            IPAddress.Parse(path.EndsWith("invitation", StringComparison.Ordinal) ? "198.18.120.13" : "198.18.120.14"),
            ClearanceLevel.TopSecret);
        try
        {
            var stale = Vault.CreateSession(
                SessionApi.WithStrongAuthenticationAt(Vault, freshAdmin, DateTimeOffset.UtcNow.AddMinutes(-11)),
                IPAddress.Parse("198.18.120.15"));
            var response = await UiApiRequest.WriteWithAntiforgeryAsync(stale, HttpMethod.Post, path,
                JsonContent.Create(new { email = "fresh@example.test" }));
            await AdministratorFreshnessTests.AssertStepUpAsync(response, "code");
        }
        finally
        {
            await Vault.DeleteAccountAsync(staleUsername);
        }
    }

    [Fact]
    public async Task Resend_replaces_the_token_builds_the_link_only_from_public_url_ignores_request_address_and_keeps_token_out_of_records()
    {
        var admin = await Vault.AdminClientAsync();
        var invited = await SeedInvitedAccountAsync("it-inv-resend", "stored.person@example.test", displayName: "Ada Lovelace");
        var oldHash = InvitationToken.Hash("previous-live-token");
        using (var setup = Vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            db.AccountInvitations.Add(new AccountInvitation
            {
                Id = Guid.NewGuid(),
                UserId = invited.Id,
                TokenHash = oldHash,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                PendingTotpSecret = "STALESEED123456"
            });
            await db.SaveChangesAsync();
        }

        var before = await AuditTrail.HeadAsync(Vault);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ui/users/{invited.Id}/invitation")
        {
            Content = JsonContent.Create(new { email = "attacker@example.test" })
        };
        request.Headers.Add("RequestVerificationToken", await UiApiRequest.AntiforgeryTokenAsync(admin));
        request.Headers.Add("X-Forwarded-Host", "attacker.example");
        var response = await admin.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["expiresAtUtc", "invitation"], ProfileApi.NamesOf(body.RootElement));
        Assert.Equal("sent", body.RootElement.GetProperty("invitation").GetString());

        var mail = await Vault.Mailbox!.LatestInvitationForAsync("stored.person@example.test");
        Assert.Equal("You're invited to EclipsVault", mail.Subject);
        Assert.StartsWith($"{InvitationFixture.ConfiguredPublicUrl}/Account/Activate#", mail.Link, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker.example", mail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello Ada Lovelace, an administrator created an EclipsVault account for you with the username 'it-inv-resend'.", mail.Body, StringComparison.Ordinal);
        Assert.Equal(0, await Vault.Mailbox.CountMessagesForAsync("attacker@example.test"));

        using (var verify = Vault.Services.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var stored = await db.AccountInvitations.AsNoTracking().SingleAsync(row => row.UserId == invited.Id);
            Assert.False(oldHash.SequenceEqual(stored.TokenHash));
            Assert.True(InvitationToken.Matches(mail.Token, stored.TokenHash));
            Assert.Null(stored.SpentAtUtc);
            Assert.Null(stored.PendingTotpSecret);
        }

        var rows = await AuditTrail.RowsAfterAsync(Vault, before);
        var audit = Assert.Single(rows, row => row.Action == AuditAction.InvitationSent);
        Assert.Equal(invited.Id, audit.ResourceId);
        Assert.Contains("stored.person@example.test", audit.Details!, StringComparison.Ordinal);
        await ProofDisclosure.AssertNotDisclosedAsync(Vault, before, invited.Id, $"/api/ui/users/{invited.Id}/invitation", mail.Token, mail.Link);
        await ProofDisclosure.AssertMailNotRecordedAsync(Vault, "stored.person@example.test", mail.Token, mail.Link);
    }

    [Fact]
    public async Task Create_commits_first_and_reports_not_delivered_when_smtp_fails()
    {
        var admin = await Vault.AdminClientAsync();
        var options = Vault.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
        var originalPort = options.Smtp.Port;
        var before = await AuditTrail.HeadAsync(Vault);
        options.Smtp.Port = 1;
        try
        {
            var response = await UserAccounts.CreateAsync(admin, "it-inv-create-smtp-fail", email: "create.smtp.fail@example.test");

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("not_delivered", body.RootElement.GetProperty("invitation").GetString());
        }
        finally
        {
            options.Smtp.Port = originalPort;
        }

        var created = await ProfileHttp.AccountAsync(Vault, "it-inv-create-smtp-fail");
        using var verify = Vault.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        Assert.NotNull(await db.AccountInvitations.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == created.Id));
        Assert.Contains(await AuditTrail.RowsAfterAsync(Vault, before), row => row.Action == AuditAction.UserCreated);
    }

    [Fact]
    public async Task Resend_commits_first_and_reports_not_delivered_when_smtp_fails()
    {
        var admin = await Vault.AdminClientAsync();
        var invited = await SeedInvitedAccountAsync("it-inv-smtp-fail", "smtp.fail@example.test");
        var options = Vault.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
        var originalPort = options.Smtp.Port;
        var before = await AuditTrail.HeadAsync(Vault);
        options.Smtp.Port = 1;
        try
        {
            var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
                HttpMethod.Post, $"/api/ui/users/{invited.Id}/invitation");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("not_delivered", body.RootElement.GetProperty("invitation").GetString());
        }
        finally
        {
            options.Smtp.Port = originalPort;
        }

        using var verify = Vault.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        Assert.NotNull(await db.AccountInvitations.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == invited.Id));
        Assert.Contains(await AuditTrail.RowsAfterAsync(Vault, before), row => row.Action == AuditAction.InvitationSent);
    }

    /// <summary>
    /// The Users list and the user page say whether an account awaits activation and until when its link
    /// works. A link that can no longer activate the account, here one spent by an address replacement,
    /// has no expiry to show.
    /// </summary>
    [Fact]
    public async Task Users_list_and_details_report_the_activation_state_and_the_usable_links_expiry()
    {
        var admin = await Vault.AdminClientAsync();
        var invited = await SeedInvitedAccountAsync("it-inv-state", "it-inv-state@example.test");
        var resend = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            HttpMethod.Post, $"/api/ui/users/{invited.Id}/invitation");
        var expiresAtUtc = (await resend.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiresAtUtc").GetDateTimeOffset();

        var (listed, details) = await StateAsync(admin, invited.Id);
        Assert.True(listed.GetProperty("awaitingActivation").GetBoolean());
        Assert.Equal(expiresAtUtc, listed.GetProperty("invitationExpiresAtUtc").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1));
        Assert.True(details.GetProperty("awaitingActivation").GetBoolean());
        Assert.Equal(expiresAtUtc, details.GetProperty("invitationExpiresAtUtc").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1));
        Assert.Equal("it-inv-state@example.test", details.GetProperty("email").GetString());

        (await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            HttpMethod.Post, $"/api/ui/users/{invited.Id}/invitation-address",
            JsonContent.Create(new { email = "it-inv-state-new@example.test" }))).EnsureSuccessStatusCode();

        (listed, details) = await StateAsync(admin, invited.Id);
        Assert.True(listed.GetProperty("awaitingActivation").GetBoolean());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("invitationExpiresAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, details.GetProperty("invitationExpiresAtUtc").ValueKind);
        Assert.Equal("it-inv-state-new@example.test", details.GetProperty("email").GetString());

        var administrator = await ProfileHttp.AccountAsync(Vault, "vault-admin");
        (listed, details) = await StateAsync(admin, administrator.Id);
        Assert.False(listed.GetProperty("awaitingActivation").GetBoolean());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("invitationExpiresAtUtc").ValueKind);
        Assert.False(details.GetProperty("awaitingActivation").GetBoolean());
    }

    private static async Task<(JsonElement Listed, JsonElement Details)> StateAsync(HttpClient admin, Guid userId)
    {
        var users = await admin.GetFromJsonAsync<JsonElement>("/api/ui/users");
        var listed = users.GetProperty("users").EnumerateArray().Single(user => user.GetProperty("userId").GetGuid() == userId);
        return (listed, await admin.GetFromJsonAsync<JsonElement>($"/api/ui/users/{userId}"));
    }

    [Fact]
    public async Task Replacing_invitation_address_spends_the_current_token_audits_the_new_address_sends_no_mail_and_resend_uses_it()
    {
        var admin = await Vault.AdminClientAsync();
        var invited = await SeedInvitedAccountAsync("it-inv-replace", "old.typo@example.test");
        (await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            HttpMethod.Post, $"/api/ui/users/{invited.Id}/invitation")).EnsureSuccessStatusCode();
        using (var setup = Vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var row = await db.AccountInvitations.SingleAsync(i => i.UserId == invited.Id);
            row.PendingTotpSecret = "INSPECTEDSEED123";
            await db.SaveChangesAsync();
        }

        var oldMailCount = await Vault.Mailbox!.CountMessagesForAsync("old.typo@example.test");
        var before = await AuditTrail.HeadAsync(Vault);
        var replaceResponse = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            HttpMethod.Post, $"/api/ui/users/{invited.Id}/invitation-address",
            JsonContent.Create(new { email = "  corrected.person@example.test  " }));

        Assert.Equal(HttpStatusCode.OK, replaceResponse.StatusCode);
        Assert.True(replaceResponse.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await replaceResponse.Content.ReadAsStringAsync());
        Assert.Equal(["email"], ProfileApi.NamesOf(body.RootElement));
        Assert.Equal("corrected.person@example.test", body.RootElement.GetProperty("email").GetString());

        Assert.Equal(oldMailCount, await Vault.Mailbox.CountMessagesForAsync("old.typo@example.test"));
        Assert.Equal(0, await Vault.Mailbox.CountMessagesForAsync("corrected.person@example.test"));

        using (var verify = Vault.Services.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var storedUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == invited.Id);
            var storedInvitation = await db.AccountInvitations.AsNoTracking().SingleAsync(i => i.UserId == invited.Id);
            Assert.Equal("corrected.person@example.test", storedUser.Email);
            Assert.NotNull(storedInvitation.SpentAtUtc);
            Assert.Null(storedInvitation.PendingTotpSecret);
        }

        var audit = Assert.Single(await AuditTrail.RowsAfterAsync(Vault, before), row => row.Action == AuditAction.InvitationAddressReplaced);
        Assert.Contains("corrected.person@example.test", audit.Details!, StringComparison.Ordinal);

        // Resend now delivers to the replaced address.
        var resendResponse = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            HttpMethod.Post, $"/api/ui/users/{invited.Id}/invitation");
        Assert.Equal(HttpStatusCode.OK, resendResponse.StatusCode);
        var delivered = await Vault.Mailbox.LatestInvitationForAsync("corrected.person@example.test");
        Assert.StartsWith($"{InvitationFixture.ConfiguredPublicUrl}/Account/Activate#", delivered.Link, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invitation")]
    [InlineData("invitation-address")]
    public async Task Invitation_endpoints_refuse_an_activated_account_with_already_active(string endpoint)
    {
        var admin = await Vault.AdminClientAsync();
        var active = await ProfileHttp.AccountAsync(Vault, VaultFixture.AdminUsername);

        var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            HttpMethod.Post, $"/api/ui/users/{active.Id}/{endpoint}", JsonContent.Create(new { email = "other@example.test" }));

        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "already_active");
    }

    [Fact]
    public async Task Replacing_the_invitation_address_with_a_taken_email_answers_email_taken_without_naming_its_owner()
    {
        var admin = await Vault.AdminClientAsync();
        var owner = await ProfileHttp.AccountAsync(Vault, VaultFixture.AdminUsername);
        var invited = await SeedInvitedAccountAsync("it-inv-taken", "taken.target@example.test");

        var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            HttpMethod.Post, $"/api/ui/users/{invited.Id}/invitation-address",
            JsonContent.Create(new { email = owner.Email.ToUpperInvariant() }));

        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "email_taken");
        Assert.DoesNotContain(owner.Username, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replacing_the_invitation_address_with_an_invalid_email_names_the_field()
    {
        var admin = await Vault.AdminClientAsync();
        var invited = await SeedInvitedAccountAsync("it-inv-invalid", "invalid.target@example.test");

        var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            HttpMethod.Post, $"/api/ui/users/{invited.Id}/invitation-address",
            JsonContent.Create(new { email = "not-an-email" }));

        await UserFormsApiTests.AssertFieldProblemAsync(response, "email", "Enter a valid email address.");
    }

    [Theory]
    [InlineData("invitation")]
    [InlineData("invitation-address")]
    public async Task Invitation_endpoints_answer_user_not_found_for_a_missing_account(string endpoint)
    {
        var admin = await Vault.AdminClientAsync();

        var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            HttpMethod.Post, $"{Missing}/{endpoint}", JsonContent.Create(new { email = "missing@example.test" }));

        await UiApiFailureContractTests.AssertProblemAsync(response, 404, "user_not_found");
    }

    [Fact]
    public async Task An_invited_accounts_password_is_refused_as_a_wrong_password_and_recorded_as_awaiting_activation()
    {
        var invited = await SeedInvitedAccountAsync("it-inv-refused-password", "refused.password@example.test",
            password: InvitedPassword, totpEnabled: true, totpSecret: InvitedTotpSecret);
        var before = await AuditTrail.HeadAsync(Vault);
        var browser = Vault.CreateSession(source: IPAddress.Parse("198.18.120.21"));

        var response = await AuthApi.PostAsync(browser, "/api/ui/auth/password", await AuthApi.AnonymousTokenAsync(browser),
            new { username = invited.Username, password = InvitedPassword });

        var problem = await ProfileApi.AssertProblemAsync(response, 400, "sign_in_failed");
        Assert.Equal("Invalid username or password.", problem.GetProperty("title").GetString());
        var loginFailed = Assert.Single(await AuditTrail.RowsAfterAsync(Vault, before),
            row => row.Action == AuditAction.LoginFailed && row.ResourceId == invited.Id);
        Assert.Equal("Account awaiting activation", loginFailed.Details);
    }

    [Theory]
    [InlineData("totp", "198.18.120.25")]
    [InlineData("recovery-code", "198.18.120.26")]
    [InlineData("enrollment", "198.18.120.27")]
    [InlineData("enrollment/confirm", "198.18.120.28")]
    public async Task A_pending_sign_in_of_an_invited_account_expires_even_when_its_stamp_still_matches(string step, string source)
    {
        var invited = await SeedInvitedAccountAsync($"it-inv-pending-{step.Replace('/', '-')}", $"pending.{step.Replace('/', '.')}@example.test",
            password: InvitedPassword, totpEnabled: true, totpSecret: InvitedTotpSecret);
        var browser = Vault.CreateSession(source: IPAddress.Parse(source));
        AttachPendingCookie(browser, invited, await MatchingStampAsync(invited));

        var response = await AuthApi.PostAsync(browser, $"/api/ui/auth/{step}", await AuthApi.AnonymousTokenAsync(browser),
            new { code = Totp.Code(InvitedTotpSecret, Totp.CurrentStep()) });

        await ProfileApi.AssertProblemAsync(response, 401, "sign_in_expired");
    }

    [Fact]
    public async Task Final_session_issuance_refuses_an_invited_account_even_when_its_stamp_still_matches()
    {
        var invited = await SeedInvitedAccountAsync("it-inv-refused-issuance", "refused.issuance@example.test",
            password: InvitedPassword, totpEnabled: true, totpSecret: InvitedTotpSecret);
        var stamp = await MatchingStampAsync(invited);
        var before = await AuditTrail.HeadAsync(Vault);

        using (var scope = Vault.Services.CreateScope())
        {
            var issuer = scope.ServiceProvider.GetRequiredService<SessionIssuer>();
            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
            Assert.False(await issuer.SignInAsync(httpContext, UserDto.From(invited), stamp,
                new SignInAudit(AuditAction.LoginSucceeded, "Single sign-on"), default));
        }

        var rows = await ProfileHttp.EventsAfterAsync(Vault, before, invited.Id);
        Assert.Contains(AuditAction.SignInRefused, rows);
        Assert.DoesNotContain(AuditAction.LoginSucceeded, rows);
    }

    [Fact]
    public async Task Single_sign_on_refuses_an_invited_account_as_not_activated()
    {
        var invited = await SeedInvitedAccountAsync("it-inv-refused-sso", "refused.sso@example.test",
            password: InvitedPassword, totpEnabled: true, totpSecret: InvitedTotpSecret);
        var before = await AuditTrail.HeadAsync(Vault);

        var response = await PostIdpAssertion.InvokeAsync(Vault, invited.Email, "198.18.120.23");

        Assert.Equal("/Account/Login?sso=refused", response.Headers.Location?.OriginalString);
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(Vault, before), row => row.Action == AuditAction.SsoSignInRefused);
        Assert.Contains("NotActivated", row.Details!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Break_glass_refuses_an_invited_top_secret_account()
    {
        var invited = await SeedInvitedAccountAsync("it-inv-refused-recover", "refused.recover@example.test",
            clearance: ClearanceLevel.TopSecret, password: InvitedPassword, totpEnabled: true, totpSecret: InvitedTotpSecret);
        var browser = Vault.CreateSession(source: IPAddress.Parse("198.18.120.24"));

        var response = await AuthApi.PostAsync(browser, "/api/ui/auth/recover", await AuthApi.AnonymousTokenAsync(browser, "/Account/Recover"),
            new { username = invited.Username, password = InvitedPassword, code = Totp.Code(InvitedTotpSecret, Totp.CurrentStep()) });

        await ProfileApi.AssertProblemAsync(response, 400, "recovery_failed");
        Assert.Null(browser.SessionCookie);
    }

    /// <summary>The stamp a sign-in begun now would carry: the account's credential generation and revocation version.</summary>
    private async Task<SessionStamp> MatchingStampAsync(User invited)
        => new(invited.CredentialGeneration,
            await Vault.Services.GetRequiredService<ISessionRevocationService>().CaptureAsync(invited.Id, default));

    [Fact]
    public async Task Invited_accounts_are_excluded_from_last_administrator_and_refuse_reset_mfa_while_other_administration_works()
    {
        var admin = await Vault.AdminClientAsync();
        var vaultAdmin = await ProfileHttp.AccountAsync(Vault, VaultFixture.AdminUsername);
        var invitedAdmin = await SeedInvitedAccountAsync("it-inv-last-admin", "invited.admin@example.test", clearance: ClearanceLevel.TopSecret);

        // vault-admin is the sole active TopSecret administrator; the invited TopSecret account does not count.
        Assert.Equal(0, await CountOtherActiveAdminsAsync(vaultAdmin.Id));
        var demoteVaultAdmin = await UserAccounts.EditRoleAsync(admin, vaultAdmin.Id, ClearanceLevel.Secret, "GLOBAL");
        await UiApiFailureContractTests.AssertProblemAsync(demoteVaultAdmin, 409, "last_administrator");

        // Reset MFA on an invited account is refused with 409 not_activated.
        var beforeReset = await AuditTrail.HeadAsync(Vault);
        await UiApiFailureContractTests.AssertProblemAsync(await UserActions.ResetMfaAsync(admin, invitedAdmin.Id), 409, "not_activated");
        Assert.Empty(await AuditTrail.RowsAfterAsync(Vault, beforeReset));

        // Role change, disable, enable, force sign-out and delete all work on invited accounts.
        Assert.Equal(HttpStatusCode.NoContent, (await UserAccounts.EditRoleAsync(admin, invitedAdmin.Id, ClearanceLevel.Secret, "OPS")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserActions.SetEnabledAsync(admin, invitedAdmin.Id, false)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserActions.SetEnabledAsync(admin, invitedAdmin.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserActions.ForceLogoutAsync(admin, invitedAdmin.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserActions.DeleteAsync(admin, invitedAdmin.Id)).StatusCode);
    }

    private async Task<int> CountOtherActiveAdminsAsync(Guid vaultAdminId)
    {
        using var scope = Vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.Users.CountAsync(u => u.Id != vaultAdminId
            && u.Clearance == ClearanceLevel.TopSecret
            && !u.IsDisabled
            && !u.AwaitingActivation);
    }

    private async Task<User> SeedInvitedAccountAsync(
        string username,
        string email,
        string displayName = "Invited Person",
        ClearanceLevel clearance = ClearanceLevel.Standard,
        string? password = null,
        bool totpEnabled = false,
        string? totpSecret = null)
    {
        using var scope = Vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var hash = password is null
            ? new PasswordHashResult([], [])
            : hasher.Hash(password);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = displayName,
            Email = email,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            Clearance = clearance,
            ProjectKey = "GLOBAL",
            TotpEnabled = totpEnabled,
            TotpSecret = totpSecret,
            AwaitingActivation = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private void AttachPendingCookie(VaultSession browser, User user, SessionStamp stamp)
    {
        var options = Vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthSchemes.MfaPending);
        var principal = VaultClaimsFactory.CreatePendingMfaPrincipal(UserDto.From(user), stamp);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(6),
        }, AuthSchemes.MfaPending);
        browser.Cookies.Add(browser.Client.BaseAddress!, new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/")
        {
            Secure = true,
            HttpOnly = true
        });
    }
}

/// <summary>A vault with neither email delivery nor a public address, so it cannot invite anyone.</summary>
public sealed class UnconfiguredInvitationsFixture : IAsyncLifetime
{
    public VaultFixture Vault { get; } = new() { Mailbox = null, PublicUrl = null };
    public Task InitializeAsync() => Vault.InitializeAsync();
    public Task DisposeAsync() => Vault.DisposeAsync();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UnconfiguredInvitationsCollection : ICollectionFixture<UnconfiguredInvitationsFixture>
{
    public const string Name = "unconfigured invitations";
}

[Collection(UnconfiguredInvitationsCollection.Name)]
public sealed class AccountInvitationUnavailableTests(UnconfiguredInvitationsFixture fixture)
{
    private VaultFixture Vault => fixture.Vault;

    [Fact]
    public async Task Creating_an_account_is_refused_with_invitations_unavailable_and_the_form_says_so()
    {
        var admin = await Vault.AdminClientAsync();
        using (var form = JsonDocument.Parse(await (await admin.GetAsync("/api/ui/users/new")).Content.ReadAsStringAsync()))
        {
            Assert.False(form.RootElement.GetProperty("invitationsAvailable").GetBoolean());
        }

        var refused = await UserAccounts.CreateAsync(admin, "it-inv-unavail-create");
        await UiApiFailureContractTests.AssertProblemAsync(refused, 409, "invitations_unavailable");
        using var scope = Vault.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Users
            .AnyAsync(user => user.Username == "it-inv-unavail-create"));
    }

    [Fact]
    public async Task Users_list_and_details_say_resend_cannot_deliver_when_mail_or_public_url_is_not_configured()
    {
        var admin = await Vault.AdminClientAsync();
        var administrator = await ProfileHttp.AccountAsync(Vault, "vault-admin");

        using var list = JsonDocument.Parse(await (await admin.GetAsync("/api/ui/users")).Content.ReadAsStringAsync());
        using var details = JsonDocument.Parse(await (await admin.GetAsync($"/api/ui/users/{administrator.Id}")).Content.ReadAsStringAsync());

        Assert.False(list.RootElement.GetProperty("invitationsAvailable").GetBoolean());
        Assert.False(details.RootElement.GetProperty("invitationsAvailable").GetBoolean());
    }

    [Fact]
    public async Task Resend_refuses_with_invitations_unavailable_when_mail_or_public_url_is_not_configured()
    {
        var admin = await Vault.AdminClientAsync();
        var invited = new User
        {
            Id = Guid.NewGuid(),
            Username = "it-inv-unavail",
            DisplayName = "Unavailable Target",
            Email = "unavailable.target@example.test",
            PasswordHash = [],
            PasswordSalt = [],
            Clearance = ClearanceLevel.Standard,
            ProjectKey = "GLOBAL",
            AwaitingActivation = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        using (var setup = Vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            db.Users.Add(invited);
            await db.SaveChangesAsync();
        }

        var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            HttpMethod.Post, $"/api/ui/users/{invited.Id}/invitation");
        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "invitations_unavailable");
    }
}
