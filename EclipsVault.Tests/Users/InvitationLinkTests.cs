using System.Security.Cryptography;
using System.Text;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.Tests.Users;

public class InvitationLinkTests
{
    [Fact]
    public void Invitation_link_is_built_solely_from_public_url_with_token_in_fragment()
    {
        var token = InvitationToken.Generate();

        Assert.True(InvitationLink.TryBuild("https://vault.example.com", token, out var link));
        Assert.Equal($"https://vault.example.com/Account/Activate#{token}", link);
    }

    [Fact]
    public void Invitation_link_normalizes_trailing_slash_and_keeps_configured_base_path()
    {
        var token = InvitationToken.Generate();

        Assert.True(InvitationLink.TryBuild("https://vault.example.com/", token, out var rootLink));
        Assert.Equal($"https://vault.example.com/Account/Activate#{token}", rootLink);

        Assert.True(InvitationLink.TryBuild("https://example.com/vault/", token, out var subpathLink));
        Assert.Equal($"https://example.com/vault/Account/Activate#{token}", subpathLink);
    }

    [Theory]
    [InlineData("http://localhost:5080")]
    [InlineData("http://127.0.0.1:5080")]
    [InlineData("http://[::1]:5080")]
    public void Loopback_http_public_url_is_accepted_for_local_development(string publicUrl)
    {
        var token = InvitationToken.Generate();

        Assert.True(InvitationLink.TryBuild(publicUrl, token, out var link));
        Assert.StartsWith(publicUrl + "/Account/Activate#", link, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/Account/Activate")]
    [InlineData("http://vault.example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pass@vault.example.com")]
    [InlineData("https://vault.example.com?next=evil")]
    [InlineData("https://vault.example.com#frag")]
    [InlineData("https://vault.example.com\r\nX-Injected: yes")]
    public void Invalid_or_non_https_public_urls_are_rejected(string? raw)
    {
        Assert.False(InvitationLink.TryParsePublicUrl(raw, out _));
        Assert.False(InvitationLink.TryBuild(raw, InvitationToken.Generate(), out _));
    }

    [Fact]
    public void Invitation_link_has_no_request_or_http_context_parameter()
    {
        var methods = typeof(InvitationLink).GetMethods();

        Assert.DoesNotContain(methods, method =>
            method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(HttpRequest)
                || parameter.ParameterType == typeof(HttpContext)
                || parameter.ParameterType == typeof(HostString)));
    }

    [Fact]
    public void Token_is_256_random_bits_and_stored_only_as_sha256_hash()
    {
        var first = InvitationToken.Generate();
        var second = InvitationToken.Generate();
        var hash = InvitationToken.Hash(first);

        Assert.NotEqual(first, second);
        Assert.Equal(43, first.Length);
        Assert.Equal(32, hash.Length);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(first)), hash);
        Assert.True(InvitationToken.Matches(first, hash));
        Assert.False(InvitationToken.Matches(second, hash));
        Assert.False(InvitationToken.Matches("", hash));
    }

    [Fact]
    public void Invitation_usability_enforces_72_hour_expiry_and_single_use()
    {
        var created = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var invitation = new AccountInvitation
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = InvitationToken.Hash(InvitationToken.Generate()),
            CreatedAtUtc = created,
            ExpiresAtUtc = created + InvitationToken.Lifetime
        };

        Assert.Equal(TimeSpan.FromHours(72), InvitationToken.Lifetime);
        Assert.True(invitation.IsUsable(created.AddHours(72).AddSeconds(-1)));
        Assert.False(invitation.IsUsable(created.AddHours(72)));

        invitation.SpentAtUtc = created.AddMinutes(5);
        Assert.False(invitation.IsUsable(created.AddMinutes(6)));
    }

    [Theory]
    [InlineData(true, "Smtp", "smtp.example.test", 587, "no-reply@example.test", "https://vault.example.test", true)]
    [InlineData(false, "Smtp", "smtp.example.test", 587, "no-reply@example.test", "https://vault.example.test", false)]
    [InlineData(true, "Log", "smtp.example.test", 587, "no-reply@example.test", "https://vault.example.test", false)]
    [InlineData(true, "Smtp", "", 587, "no-reply@example.test", "https://vault.example.test", false)]
    [InlineData(true, "Smtp", "smtp.example.test", 0, "no-reply@example.test", "https://vault.example.test", false)]
    [InlineData(true, "Smtp", "smtp.example.test", 587, "not-an-email", "https://vault.example.test", false)]
    [InlineData(true, "Smtp", "smtp.example.test", 587, "no-reply@example.test", "", false)]
    [InlineData(true, "Smtp", "smtp.example.test", 587, "no-reply@example.test", "http://vault.example.test", false)]
    public void Delivery_is_available_only_when_real_smtp_and_valid_public_url_are_both_configured(
        bool enabled, string sender, string host, int port, string from, string publicUrl, bool expected)
    {
        var emailOptions = Options.Create(new EmailOptions
        {
            Enabled = enabled,
            Sender = sender,
            FromAddress = from,
            Smtp = new SmtpOptions { Host = host, Port = port }
        });
        var delivery = new AccountInvitationDelivery(
            new SmtpEmailSender(emailOptions),
            emailOptions,
            new UserDirectoryOptions(publicUrl));

        Assert.Equal(expected, delivery.Available);
    }
}
