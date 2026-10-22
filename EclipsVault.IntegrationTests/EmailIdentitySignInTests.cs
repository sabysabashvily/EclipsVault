using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Which account an email address signs in to. Accounts whose username is shaped like an email come
/// only from custom provisioning: the administrator's form refuses such usernames, so these tests
/// save their accounts directly.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class EmailIdentitySignInTests(VaultFixture vault)
{
    [Fact]
    public async Task Single_sign_on_selects_the_account_whose_email_matches_in_another_letter_case_and_white_space()
    {
        var account = await ProvisionAsync("it-email-sso-case", "It.Email.Sso.Case@Example.Test");
        var head = await AuditTrail.HeadAsync(vault);

        var response = await CallBackFromIdentityProviderAsync(" it.email.sso.case@EXAMPLE.test ", "198.51.100.230");

        // This vault does not trust the provider's second factor, so the account enrolls its own.
        Assert.Equal("/Account/EnrollTotp", response.Headers.Location?.OriginalString);
        var linked = Assert.Single(await AuditTrail.RowsAfterAsync(vault, head), row => row.Action == AuditAction.SsoIdentityLinked);
        Assert.Equal(account.Id, linked.UserId);
        Assert.Equal("it-email-sso-case", linked.Username);
    }

    [Fact]
    public async Task Single_sign_on_selects_the_account_with_the_email_not_the_account_with_that_username()
    {
        await ProvisionAsync("it-email-sso-alias@example.test", "it-email-sso-alias-named@example.test");
        var addressed = await ProvisionAsync("it-email-sso-alias-addressed", "IT-Email-SSO-Alias@Example.Test");
        var head = await AuditTrail.HeadAsync(vault);

        await CallBackFromIdentityProviderAsync("it-email-sso-alias@example.test", "198.51.100.231");

        var linked = Assert.Single(await AuditTrail.RowsAfterAsync(vault, head), row => row.Action == AuditAction.SsoIdentityLinked);
        Assert.Equal(addressed.Id, linked.UserId);
        Assert.Equal("it-email-sso-alias-addressed", linked.Username);
    }

    [Fact]
    public async Task Single_sign_on_refuses_an_address_that_is_only_an_accounts_username()
    {
        await ProvisionAsync("it-email-sso-name-only@example.test", "it-email-sso-name-only-named@example.test");
        var head = await AuditTrail.HeadAsync(vault);

        var response = await CallBackFromIdentityProviderAsync("it-email-sso-name-only@example.test", "198.51.100.232");

        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
        var refused = Assert.Single(await AuditTrail.RowsAfterAsync(vault, head), row => row.Action == AuditAction.SsoSignInRefused);
        Assert.Null(refused.UserId);
        Assert.Equal("it-email-sso-name-only@example.test", refused.Username);
    }

    [Fact]
    public async Task Single_sign_on_refuses_an_address_spelled_with_a_dotless_i_for_the_account_spelled_with_a_dotted_i()
    {
        await ProvisionAsync("it-email-sso-dotted", "it-email-sso-admin@example.test");
        var head = await AuditTrail.HeadAsync(vault);

        var response = await CallBackFromIdentityProviderAsync("it-email-sso-adm\u0131n@example.test", "198.51.100.236");

        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, head), row => row.Action == AuditAction.SsoIdentityLinked);
    }

    [Fact]
    public async Task Password_sign_in_refuses_an_identifier_that_is_one_accounts_username_and_anothers_email()
    {
        const string password = "It-email-alias-shared-password-1";
        await ProvisionAsync("it-email-login-alias@example.test", "it-email-login-alias-named@example.test", password);
        await ProvisionAsync("it-email-login-alias-addressed", "it-email-login-alias@example.test", password);
        var session = vault.CreateSession(source: IPAddress.Parse("198.51.100.233"));
        var head = await AuditTrail.HeadAsync(vault);

        var response = await session.Client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "it-email-login-alias@example.test",
            ["Password"] = password,
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(await session.Client.GetStringAsync("/Account/Login"), "/Account/Login"),
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid username or password.", await response.Content.ReadAsStringAsync());
        var failed = Assert.Single(await AuditTrail.RowsAfterAsync(vault, head), row => row.Action == AuditAction.LoginFailed);
        Assert.Null(failed.UserId);
        Assert.Equal("Unknown username or email", failed.Details);
    }

    [Fact]
    public async Task Named_passkey_sign_in_offers_the_passkey_of_the_account_with_that_username()
    {
        var account = await ProvisionAsync("it-email-passkey-named", "it-email-passkey-named@example.test");
        await RegisterPasskeyAsync(account.Id);

        var options = await BeginPasskeySignInAsync("it-email-passkey-named", "198.51.100.234");

        Assert.Single(options.GetProperty("allowCredentials").EnumerateArray());
    }

    [Fact]
    public async Task Named_passkey_sign_in_offers_no_passkey_for_an_identifier_that_is_one_accounts_username_and_anothers_email()
    {
        var named = await ProvisionAsync("it-email-passkey-alias@example.test", "it-email-passkey-alias-named@example.test");
        await ProvisionAsync("it-email-passkey-alias-addressed", "it-email-passkey-alias@example.test");
        await RegisterPasskeyAsync(named.Id);

        var options = await BeginPasskeySignInAsync("it-email-passkey-alias@example.test", "198.51.100.235");

        Assert.Empty(options.GetProperty("allowCredentials").EnumerateArray());
    }

    [Fact]
    public async Task Sharing_with_an_identifier_that_is_one_accounts_username_and_anothers_email_shares_with_nobody()
    {
        await ProvisionAsync("it-email-share-alias@example.test", "it-email-share-alias-named@example.test");
        await ProvisionAsync("it-email-share-alias-addressed", "it-email-share-alias@example.test");
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-email-share-alias-secret");

        await LegacySharing.ShareAsync(admin, details, "it-email-share-alias@example.test");

        using var scope = vault.Services.CreateScope();
        var secretId = LegacySharing.SecretIdOf(details);
        Assert.False(await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SecretGrants
            .AnyAsync(share => share.SecretId == secretId));
    }

    [Fact]
    public async Task Provisioning_skips_generated_addresses_reserved_by_legacy_usernames()
    {
        await ProvisionAsync("identity.aliasreserve.1@eclipsvault.local", "it-email-reserved-named@example.test");
        using var scope = vault.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserAdminService>();

        var created = await users.CreateAsync(new CreateUserRequest(
            "it-email-provision-safe", "Identity", "Aliasreserve", "It-email-provision-unique-password-1",
            ClearanceLevel.Standard, "GLOBAL"), default);

        Assert.Equal("identity.aliasreserve.2@eclipsvault.local", created.Email);
    }

    /// <summary>A passkey on file for the account. Asking for sign-in options never verifies its key.</summary>
    private async Task RegisterPasskeyAsync(Guid accountId)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        db.Add(new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            UserId = accountId,
            CredentialId = RandomNumberGenerator.GetBytes(16),
            PublicKey = [1],
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> BeginPasskeySignInAsync(string username, string source)
    {
        var session = vault.CreateSession(source: IPAddress.Parse(source));
        var login = await session.Client.GetStringAsync("/Account/Login");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/PasskeyLoginBegin")
        {
            Content = JsonContent.Create(new { username }),
        };
        request.Headers.Add("RequestVerificationToken", VaultFixture.FormTokenOf(login, "/Account/Login"));

        var response = await session.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Saves an account as custom provisioning would, bypassing the administrator's form and its
    /// username rules.
    /// </summary>
    private async Task<User> ProvisionAsync(string username, string email, string? password = null)
    {
        using var scope = vault.Services.CreateScope();
        var hashed = scope.ServiceProvider.GetRequiredService<IPasswordHasher>()
            .Hash(password ?? "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        var account = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = username,
            Email = email,
            PasswordHash = hashed.Hash,
            PasswordSalt = hashed.Salt,
            ProjectKey = "GLOBAL",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(account, default);
        return account;
    }

    /// <summary>
    /// Arrives at the callback as if the identity provider had just verified <paramref name="email"/>.
    /// This begins after the provider's assertion, with the real correlation cookie scheme and its
    /// protector: it covers how the vault matches an account, not how it validates the provider's tokens.
    /// </summary>
    private async Task<HttpResponseMessage> CallBackFromIdentityProviderAsync(string email, string source)
    {
        var callback = vault.CreateSession(source: IPAddress.Parse(source));
        var options = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthSchemes.OidcCorrelation);
        var assertion = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("iss", "https://idp.example.test/realms/integration"),
            new Claim("sub", "it-email-" + Guid.NewGuid()),
            new Claim("email", email),
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
        return response;
    }
}
