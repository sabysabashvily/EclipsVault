using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Notifications;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class MailboxFixture : IAsyncLifetime
{
    public VaultFixture Vault { get; } = new() { Mailbox = new SmtpCapture(), TrustIdpMfa = true };
    public Task InitializeAsync() => Vault.InitializeAsync();
    public Task DisposeAsync() => Vault.DisposeAsync();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MailboxCollection : ICollectionFixture<MailboxFixture>
{
    public const string Name = "real SMTP mailbox";
}

[Collection(MailboxCollection.Name)]
public sealed class MailboxTests(MailboxFixture fixture)
{
    private VaultFixture Vault => fixture.Vault;

    [Fact]
    public async Task Delivery_keeps_the_old_alias_until_confirmation_rotates_the_session_and_notifies_the_old_address()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-activate", "100.68.88.1");
        var copied = Vault.CreateSession(owner.Browser.SessionCookie);
        var operation = await VerifiedAsync(owner, "Mailbox.Activate@Example.Test");
        var head = await AuditTrail.HeadAsync(Vault);
        var sent = await RequestAsync(owner, operation);
        Assert.True(sent.Headers.CacheControl?.NoStore);
        var pending = await sent.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["address", "expiresAtUtc", "operationId"], ProfileApi.NamesOf(pending));
        Assert.Equal("Mailbox.Activate@Example.Test", pending.GetProperty("address").GetString());
        Assert.Equal(owner.Stored.Email, (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).Email);
        Assert.Equal(owner.Stored.CredentialGeneration, (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).CredentialGeneration);
        Assert.NotNull((await ProfileApi.OperationAsync(Vault, operation)).VerifiedAtUtc);
        var code = await Vault.Mailbox!.CodeForAsync("Mailbox.Activate@Example.Test");
        Assert.Equal(32, code.Length);
        Assert.NotEqual(System.Text.Encoding.UTF8.GetBytes(code), (await ProfileApi.OperationAsync(Vault, operation)).MailboxCodeHash);

        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(owner, operation, code)).StatusCode);

        var active = await ProfileHttp.AccountAsync(Vault, owner.Stored.Username);
        Assert.Equal("Mailbox.Activate@Example.Test", active.Email);
        Assert.Equal(owner.Stored.CredentialGeneration + 1, active.CredentialGeneration);
        Assert.NotEqual(copied.SessionCookie, owner.Browser.SessionCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.Browser.Client.GetAsync("/api/ui/me")).StatusCode);
        await ProfileApi.AssertNoticeAsync(Vault, owner, "ChangeEmail");
        await ProofDisclosure.AssertMailNotRecordedAsync(Vault, owner.Stored.Email, code);
        Assert.Equal([AuditAction.EmailChangeRequested, AuditAction.EmailChanged], await ProfileHttp.EventsAfterAsync(Vault, head, owner.Id));
        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, code), 404, "operation_not_found");
        await ProofDisclosure.AssertNotDisclosedAsync(Vault, head, owner.Id, "/api/ui/profile/email/confirm", code, owner.Password);
    }

    [Fact]
    public async Task A_stale_account_grant_can_be_verified_again_for_the_same_pending_target()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-reverify", "100.68.88.2");
        var recovery = await ProfileApi.RecoverySetAsync(owner);
        var operation = await VerifiedAsync(owner, "mailbox.reverify@example.test");
        await RequestAsync(owner, operation);
        var code = await Vault.Mailbox!.CodeForAsync("mailbox.reverify@example.test");
        await ProfileApi.ExpireGrantAsync(Vault, operation);
        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, code), 409, "verification_expired");

        Assert.Equal(HttpStatusCode.OK, (await ProfileApi.VerifyAsync(owner.Browser, operation,
            "PasswordAndRecoveryCode", owner.Password, recovery[0])).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(owner, operation, code)).StatusCode);
        Assert.Equal("mailbox.reverify@example.test", (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).Email);
    }

    [Fact]
    public async Task Five_mailbox_mistakes_retire_the_code_and_grant_without_locking_sign_in()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-mistakes", "100.68.88.3");
        var operation = await VerifiedAsync(owner, "mailbox.mistakes@example.test");
        await RequestAsync(owner, operation);
        var code = await Vault.Mailbox!.CodeForAsync("mailbox.mistakes@example.test");
        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, "wrong-1"), 400, "email_code_invalid");
        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, "wrong-2"), 400, "email_code_invalid");
        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, "wrong-3"), 400, "email_code_invalid");
        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, "wrong-4"), 400, "email_code_invalid");
        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, "wrong-5"), 400, "email_code_invalid");
        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, code), 409, "email_code_expired");
        var pending = await ProfileApi.OperationAsync(Vault, operation);
        Assert.Null(pending.MailboxCodeHash);
        Assert.Null(pending.VerifiedAtUtc);
        var account = await ProfileHttp.AccountAsync(Vault, owner.Stored.Username);
        Assert.Equal(0, account.FailedAccessCount);
        Assert.Null(account.LockedUntilUtc);
        Assert.Equal(owner.Stored.Email, account.Email);
    }

    [Fact]
    public async Task Expiry_retires_both_proofs_without_changing_the_alias()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-expiry", "100.68.88.4");
        var operation = await VerifiedAsync(owner, "mailbox.expiry@example.test");
        await RequestAsync(owner, operation);
        var code = await Vault.Mailbox!.CodeForAsync("mailbox.expiry@example.test");
        using var scope = Vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().ProfileOperations.Where(row => row.Id == operation)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.MailboxExpiresAtUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));

        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, code), 409, "email_code_expired");

        Assert.Null((await ProfileApi.OperationAsync(Vault, operation)).VerifiedAtUtc);
        Assert.Equal(owner.Stored.Email, (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).Email);
    }

    [Fact]
    public async Task Cancellation_removes_the_pending_code_and_its_activation_authority()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-cancel", "100.68.88.5");
        var operation = await VerifiedAsync(owner, "mailbox.cancel@example.test");
        await RequestAsync(owner, operation);
        var code = await Vault.Mailbox!.CodeForAsync("mailbox.cancel@example.test");

        Assert.Equal(HttpStatusCode.NoContent, (await ProfileApi.SendAsync(owner.Browser, HttpMethod.Delete,
            "/api/ui/profile/email/pending", new { operationId = operation })).StatusCode);

        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, code), 404, "operation_not_found");
        Assert.Equal(owner.Stored.Email, (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).Email);
    }

    [Fact]
    public async Task Another_session_cannot_request_or_activate_the_frozen_target()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-binding", "100.68.88.6");
        var recovery = await ProfileApi.RecoverySetAsync(owner);
        var other = await ProfileApi.SecondSessionAsync(Vault, owner, "100.68.88.7");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ChangeEmail", newEmail = "mailbox.binding@example.test" });
        Assert.Equal(HttpStatusCode.OK, (await ProfileApi.VerifyAsync(owner.Browser, operation,
            "PasswordAndRecoveryCode", owner.Password, recovery[0])).StatusCode);
        await RequestAsync(owner, operation);
        var code = await Vault.Mailbox!.CodeForAsync("mailbox.binding@example.test");

        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(other, HttpMethod.Post,
            "/api/ui/profile/email/request", new { operationId = operation }), 404, "operation_not_found");
        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(other, HttpMethod.Post,
            "/api/ui/profile/email/confirm", new { operationId = operation, code }), 404, "operation_not_found");
        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/email/confirm", new { operationId = operation, code, newEmail = "other@example.test" }), 400, "validation_failed");
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(owner, operation, code)).StatusCode);
    }

    [Fact]
    public async Task A_canonical_conflict_at_activation_identifies_no_other_account()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-conflict", "100.68.88.8");
        var operation = await VerifiedAsync(owner, "mailbox.conflict@example.test");
        await RequestAsync(owner, operation);
        var code = await Vault.Mailbox!.CodeForAsync("mailbox.conflict@example.test");
        using var scope = Vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        db.Users.Add(EmailIdentityDatabases.NewAccount("it-mailbox-conflict-taker", "MAILBOX.CONFLICT@EXAMPLE.TEST"));
        await db.SaveChangesAsync();

        var problem = await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, code), 409, "email_conflict");

        Assert.DoesNotContain("taker", problem.ToString(), StringComparison.Ordinal);
        Assert.Equal(owner.Stored.Email, (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).Email);
        Assert.NotNull((await ProfileApi.OperationAsync(Vault, operation)).VerifiedAtUtc);
    }

    [Fact]
    public async Task Send_requests_need_account_proof_and_share_a_minute_budget_with_resends()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-limits", "100.68.88.9");
        var operation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ChangeEmail", newEmail = "mailbox.limits@example.test" });
        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/email/request", new { operationId = operation }), 409, "verification_required");
        Assert.Equal(HttpStatusCode.OK, (await ProfileApi.VerifyAsync(owner.Browser, operation,
            "PasswordAndAuthenticatorCode", owner.Password, Totp.NextCode(owner.TotpSecret))).StatusCode);
        await RequestAsync(owner, operation);

        await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/email/resend", new { operationId = operation }), 429, "throttled");
    }

    [Fact]
    public async Task A_real_transport_failure_preserves_the_old_alias_and_clears_activation_authority()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-failure", "100.68.88.10");
        var operation = await VerifiedAsync(owner, "mailbox.failure@example.test");
        var options = Vault.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
        var originalPort = options.Smtp.Port;
        var head = await AuditTrail.HeadAsync(Vault);
        options.Smtp.Port = 1;
        try
        {
            await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
                "/api/ui/profile/email/request", new { operationId = operation }), 409, "mail_unavailable");
        }
        finally
        {
            options.Smtp.Port = originalPort;
        }

        var pending = await ProfileApi.OperationAsync(Vault, operation);
        Assert.False(pending.MailboxDelivered);
        Assert.Null(pending.VerifiedAtUtc);
        Assert.Equal(owner.Stored.Email, (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).Email);
        await ProofDisclosure.AssertNotDisclosedAsync(Vault, head, owner.Id, "/api/ui/profile/email/request", owner.Password);
        using var scope = Vault.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailLogs
            .Where(email => email.ToAddress == "mailbox.failure@example.test").ToArrayAsync());
    }

    [Theory]
    [InlineData(false, "Smtp", "100.68.88.12")]
    [InlineData(true, "Log", "100.68.88.11")]
    public async Task A_disabled_or_log_transport_makes_email_read_only_with_an_explanation(bool enabled, string sender, string source)
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-readonly-" + sender, source);
        var options = Vault.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
        options.Enabled = enabled;
        options.Sender = sender;
        try
        {
            var page = await owner.Browser.Client.GetFromJsonAsync<JsonElement>("/api/ui/profile");
            var action = page.GetProperty("changes").GetProperty("changeEmail");
            Assert.False(action.GetProperty("available").GetBoolean());
            Assert.Equal("mail_unavailable", action.GetProperty("unavailableReason").GetString());
            await ProfileApi.AssertProblemAsync(await ProfileApi.PrepareAsync(owner.Browser,
                new { kind = "ChangeEmail", newEmail = "mailbox.readonly@example.test" }), 409, "mail_unavailable");
        }
        finally
        {
            options.Enabled = true;
            options.Sender = "Smtp";
        }
    }

    [Fact]
    public async Task Deliberate_resend_rotates_the_code_and_preserves_the_original_frozen_address()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-resend", "100.68.88.13");
        var operation = await VerifiedAsync(owner, "mailbox.resend@example.test");
        await RequestAsync(owner, operation);
        var oldCode = await Vault.Mailbox!.CodeForAsync("mailbox.resend@example.test");
        await AgeSendsAsync(owner.Id);

        var resent = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/email/resend", new { operationId = operation });

        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        var newCode = await Vault.Mailbox.CodeForAsync("mailbox.resend@example.test");
        Assert.NotEqual(oldCode, newCode);
        await ProfileApi.AssertProblemAsync(await ConfirmAsync(owner, operation, oldCode), 400, "email_code_invalid");
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(owner, operation, newCode)).StatusCode);
        Assert.Equal("mailbox.resend@example.test", (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).Email);
    }

    [Fact]
    public async Task Concurrent_resends_share_one_send_reservation_and_only_the_delivered_code_can_activate()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-resend-race", "100.68.88.14");
        var operation = await VerifiedAsync(owner, "mailbox.resend.race@example.test");
        await RequestAsync(owner, operation);
        var oldCode = await Vault.Mailbox!.CodeForAsync("mailbox.resend.race@example.test");
        await AgeSendsAsync(owner.Id);

        var results = await Task.WhenAll(
            ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/email/resend", new { operationId = operation }),
            ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/email/resend", new { operationId = operation }));

        Assert.Single(results, result => result.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, result => result.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Conflict);
        var newCode = await Vault.Mailbox.CodeForAsync("mailbox.resend.race@example.test");
        Assert.NotEqual(oldCode, newCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(owner, operation, newCode)).StatusCode);
    }

    [Fact]
    public async Task Single_sign_on_resolves_only_the_active_address_after_mailbox_activation()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-sso", "100.68.88.15");
        var operation = await VerifiedAsync(owner, "mailbox.sso@example.test");
        await RequestAsync(owner, operation);
        Assert.Equal("/Account/Login", (await PostIdpAssertion.InvokeAsync(Vault, "mailbox.sso@example.test", "100.68.88.16")).Headers.Location?.OriginalString);
        var code = await Vault.Mailbox!.CodeForAsync("mailbox.sso@example.test");
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(owner, operation, code)).StatusCode);
        var head = await AuditTrail.HeadAsync(Vault);

        Assert.Equal("/Account/Login", (await PostIdpAssertion.InvokeAsync(Vault, owner.Stored.Email, "100.68.88.17")).Headers.Location?.OriginalString);
        Assert.Equal("/Account/Totp", (await PostIdpAssertion.InvokeAsync(Vault, "MAILBOX.SSO@EXAMPLE.TEST", "100.68.88.18")).Headers.Location?.OriginalString);

        Assert.Equal(owner.Id, Assert.Single(await AuditTrail.RowsAfterAsync(Vault, head), row => row.Action == AuditAction.SsoIdentityLinked).UserId);
    }

    [Fact]
    public async Task A_trusted_SSO_session_without_local_proof_cannot_prepare_an_email_change()
    {
        using var scope = Vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var account = EmailIdentityDatabases.NewAccount("it-mailbox-sso-no-proof", "mailbox.sso.noproof@example.test");
        db.Users.Add(account);
        await db.SaveChangesAsync();
        var browser = Vault.CreateSession(source: IPAddress.Parse("100.68.88.19"));
        var callback = await PostIdpAssertion.InvokeAsync(Vault, account.Email, browser, providerMfa: true);
        Assert.Equal("/Dashboard", callback.Headers.Location?.OriginalString);
        var page = await browser.Client.GetFromJsonAsync<JsonElement>("/api/ui/profile");
        Assert.False(page.GetProperty("changes").GetProperty("changeEmail").GetProperty("available").GetBoolean());
        Assert.Empty(page.GetProperty("verificationMethods").EnumerateArray());

        await ProfileApi.AssertProblemAsync(await ProfileApi.PrepareAsync(browser,
            new { kind = "ChangeEmail", newEmail = "mailbox.sso.forbidden@example.test" }), 409, "method_unavailable");
    }

    [Fact]
    public async Task Failed_change_notices_cannot_restore_the_old_alias_or_copied_session()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-notice-failure", "100.68.88.20");
        var copied = Vault.CreateSession(owner.Browser.SessionCookie);
        var operation = await VerifiedAsync(owner, "mailbox.notice.failure@example.test");
        await RequestAsync(owner, operation);
        var code = await Vault.Mailbox!.CodeForAsync("mailbox.notice.failure@example.test");
        var options = Vault.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
        var originalPort = options.Smtp.Port;
        options.Smtp.Port = 1;
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(owner, operation, code)).StatusCode);
        }
        finally
        {
            options.Smtp.Port = originalPort;
        }

        Assert.Equal("mailbox.notice.failure@example.test", (await ProfileHttp.AccountAsync(Vault, owner.Stored.Username)).Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
        using var scope = Vault.Services.CreateScope();
        var notice = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailLogs
            .SingleAsync(email => email.ToAddress == owner.Stored.Email && email.EventType == "ChangeEmail");
        Assert.Equal(EmailDeliveryStatus.Failed, notice.Status);
        Assert.Equal("Mail delivery failed.", notice.Error);
        Assert.DoesNotContain(code, notice.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Real_SMTP_failure_diagnostics_never_record_the_verification_code_or_body()
    {
        var owner = await ProfileAccount.CreateAsync(Vault, "it-mailbox-diagnostic-failure", "100.68.88.21");
        var operation = await VerifiedAsync(owner, "mailbox.diagnostics@example.test");
        var head = await AuditTrail.HeadAsync(Vault);
        var options = Vault.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
        var originalPort = options.Smtp.Port;
        options.Smtp.Port = 1;
        try
        {
            var delivery = Vault.Services.GetRequiredService<IEmailVerificationDelivery>();
            Assert.False(await delivery.TrySendAsync("mailbox.diagnostics@example.test", "Failed-Mailbox-Code-Disclosure-Sentinel", default));
            await ProfileApi.AssertProblemAsync(await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
                "/api/ui/profile/email/request", new { operationId = operation }), 409, "mail_unavailable");
        }
        finally
        {
            options.Smtp.Port = originalPort;
        }

        await ProofDisclosure.AssertNotDisclosedAsync(Vault, head, owner.Id, "/api/ui/profile/email/request",
            "Failed-Mailbox-Code-Disclosure-Sentinel", "Enter this code on the Profile page where you requested the change:");
        await ProofDisclosure.AssertMailNotRecordedAsync(Vault, "mailbox.diagnostics@example.test",
            "Failed-Mailbox-Code-Disclosure-Sentinel", "Enter this code on the Profile page where you requested the change:");
    }

    private async Task AgeSendsAsync(Guid accountId)
    {
        using var scope = Vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailVerificationSends.Where(send => send.AccountId == accountId)
            .ExecuteUpdateAsync(set => set.SetProperty(send => send.SentAtUtc, DateTimeOffset.UtcNow.AddMinutes(-2)));
    }

    private static Task<Guid> VerifiedAsync(ProfileAccount owner, string address)
        => ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "ChangeEmail", newEmail = address });

    private static async Task<HttpResponseMessage> RequestAsync(ProfileAccount owner, Guid operation)
    {
        var response = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/email/request", new { operationId = operation });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private static Task<HttpResponseMessage> ConfirmAsync(ProfileAccount owner, Guid operation, string code)
        => ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/email/confirm", new { operationId = operation, code });
}
