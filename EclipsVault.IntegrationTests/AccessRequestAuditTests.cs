using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class AccessRequestAuditTests(VaultFixture vault)
{
    [Theory]
    [InlineData("Approve", AccessRequestStatus.Approved, AuditAction.AccessRequestApproved,
        "Access to 'it-request-secret-161' approved for it-request-161", new[] { AuditAction.AccessRequestApproved, AuditAction.SecretShared }, "161")]
    [InlineData("Reject", AccessRequestStatus.Rejected, AuditAction.AccessRequestRejected,
        "Access to 'it-request-secret-162' rejected for it-request-162", new[] { AuditAction.AccessRequestRejected }, "162")]
    public async Task A_Legacy_review_records_its_exact_audit_text(
        string action, AccessRequestStatus status, AuditAction auditAction, string expectedText, AuditAction[] expectedActions, string addressSuffix)
    {
        var reviewer = await vault.AdminClientAsync();
        var username = $"it-request-{addressSuffix}";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse($"198.51.100.{addressSuffix}"));
        var name = $"it-request-secret-{addressSuffix}";
        var details = await LegacySharing.CreateSecretAsync(reviewer, name);
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, username);
        var token = await LegacyAccessRequests.TokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAccessRequests.PostAsync(reviewer, action, token, pending.Id);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/AccessRequests", response.Headers.Location?.OriginalString);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal(expectedActions, rows.Select(row => row.Action));
        var decision = Assert.Single(rows, row => row.Action == auditAction);
        Assert.Equal("Secret", decision.ResourceType);
        Assert.Equal(pending.SecretId, decision.ResourceId);
        Assert.Equal(name, decision.ResourceName);
        Assert.Equal(expectedText, decision.Details);
        Assert.Equal(VaultFixture.AdminUsername, decision.Username);
        using var scope = vault.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.SingleAsync(request => request.Id == pending.Id);
        Assert.Equal(status, stored.Status);
        Assert.NotNull(stored.DecidedAtUtc);
        Assert.Equal(VaultFixture.AdminUsername, stored.DecidedBy);
    }

    [Fact]
    public async Task A_Legacy_withdrawal_records_its_exact_audit_text()
    {
        var reviewer = await vault.AdminClientAsync();
        var requester = await vault.SignInNewAccountAsync("it-request-163", IPAddress.Parse("198.51.100.163"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-secret-163");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-request-163");
        var token = await LegacyAccessRequests.TokenAsync(requester.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAccessRequests.PostAsync(requester.Client, "Cancel", token, pending.Id);

        Assert.Equal("/AccessRequests", response.Headers.Location?.OriginalString);
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.AccessRequestCancelled, row.Action);
        Assert.Equal("Secret", row.ResourceType);
        Assert.Equal(pending.SecretId, row.ResourceId);
        Assert.Equal("it-request-secret-163", row.ResourceName);
        Assert.Equal("Request for 'it-request-secret-163' withdrawn", row.Details);
        Assert.Equal("it-request-163", row.Username);
    }

    [Theory]
    [InlineData("Approve", "174")]
    [InlineData("Reject", "175")]
    public async Task A_posted_note_cannot_replace_the_note_already_displayed_for_a_request(string action, string addressSuffix)
    {
        var reviewer = await vault.AdminClientAsync();
        var username = $"it-request-note-{addressSuffix}";
        var requester = await vault.SignInNewAccountAsync(username, IPAddress.Parse($"198.51.100.{addressSuffix}"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, $"it-request-note-secret-{addressSuffix}");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, username);
        using (var scope = vault.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.Where(r => r.Id == pending.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.DecisionNote, "A previously stored note"));
        }
        var token = await LegacyAccessRequests.TokenAsync(reviewer);

        await LegacyAccessRequests.PostFormAsync(reviewer, action, new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = pending.Id.ToString(),
            ["ttlDays"] = "0",
            ["note"] = "A forged decision note",
        });

        var page = await requester.Client.GetStringAsync("/AccessRequests");
        Assert.Contains("A previously stored note", page);
        Assert.DoesNotContain("A forged decision note", page);
        using var check = vault.Services.CreateScope();
        var email = await check.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailLogs
            .SingleAsync(email => email.Body.Contains($"it-request-note-secret-{addressSuffix}"));
        Assert.DoesNotContain("A forged decision note", email.Body);
    }

    [Fact]
    public async Task Filing_through_Legacy_records_the_existing_access_requested_text()
    {
        var reviewer = await vault.AdminClientAsync();
        var requester = await vault.SignInNewAccountAsync("it-request-file", IPAddress.Parse("198.51.100.164"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-file-secret");
        var token = await LegacyAccessRequests.TokenAsync(requester.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAccessRequests.PostAsync(requester.Client, "Create", token, LegacySharing.SecretIdOf(details));

        Assert.Equal("/AccessRequests", response.Headers.Location?.OriginalString);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal([AuditAction.SecretMetadataViewed, AuditAction.AccessRequested], rows.Select(row => row.Action));
        var row = rows[1];
        Assert.Equal("Secret", row.ResourceType);
        Assert.Equal(LegacySharing.SecretIdOf(details), row.ResourceId);
        Assert.Equal("it-request-file-secret", row.ResourceName);
        Assert.Equal("Access to 'it-request-file-secret' requested", row.Details);
        Assert.Equal("it-request-file", row.Username);
    }

    [Fact]
    public async Task Approval_uses_the_requester_account_and_session_reviewer_even_when_the_requester_username_is_reused()
    {
        var admin = await vault.AdminClientAsync();
        var reviewer = await vault.SignInNewAccountAsync("it-request-issuer", IPAddress.Parse("198.51.100.165"));
        await vault.SignInNewAccountAsync("it-request-original", IPAddress.Parse("198.51.100.166"));
        var details = await LegacySharing.CreateSecretAsync(admin, "it-request-account-identity");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-request-original");
        using (var scope = vault.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            // A former display snapshot can be provisioned again; it must not select the recipient.
            await db.Users.Where(user => user.Id == pending.RequesterUserId)
                .ExecuteUpdateAsync(set => set.SetProperty(user => user.Username, "it-request-renamed"));
        }
        await vault.SignInNewAccountAsync("it-request-original", IPAddress.Parse("198.51.100.167"));
        var token = await LegacyAccessRequests.TokenAsync(reviewer.Client);
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyAccessRequests.PostAsync(reviewer.Client, "Approve", token, pending.Id);

        var share = await LegacySharing.ShareOfAsync(vault, details);
        Assert.Equal(pending.RequesterUserId, share.GranteeUserId);
        Assert.Equal("it-request-renamed", share.GranteeUsername);
        var issuer = await ProfileHttp.AccountAsync(vault, "it-request-issuer");
        Assert.Equal(issuer.Id, share.IssuerUserId);
        Assert.Equal("it-request-issuer", share.GrantedBy);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal([AuditAction.AccessRequestApproved, AuditAction.SecretShared], rows.Select(row => row.Action));
        Assert.Equal("Shared with 'it-request-renamed'", rows[1].Details);
        Assert.Equal("Access to 'it-request-account-identity' approved for it-request-original", rows[0].Details);
        Assert.Contains("it-request-account-identity", await reviewer.Client.GetStringAsync("/api/ui/shared-by-me"));
        using var check = vault.Services.CreateScope();
        var email = await check.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailLogs
            .SingleAsync(email => email.EventType == "AccessRequestApproved" && email.Body.Contains("it-request-account-identity"));
        Assert.Contains("was approved by it-request-issuer.", email.Body);
        Assert.DoesNotContain("Reviewer note", email.Body);
    }

    [Fact]
    public async Task Approval_reuses_an_active_share_without_a_second_share_audit_row()
    {
        var reviewer = await vault.AdminClientAsync();
        await vault.SignInNewAccountAsync("it-request-existing-168", IPAddress.Parse("198.51.100.168"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-existing-168");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-request-existing-168");
        await LegacySharing.ShareAsync(reviewer, details, "it-request-existing-168");
        var existing = await LegacySharing.ShareOfAsync(vault, details);
        var token = await LegacyAccessRequests.TokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyAccessRequests.PostAsync(reviewer, "Approve", token, pending.Id);

        Assert.Equal(existing.Id, (await LegacySharing.ShareOfAsync(vault, details)).Id);
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.AccessRequestApproved, row.Action);
    }

    [Fact]
    public async Task Approval_replaces_an_expired_share_with_one_share_audit_row()
    {
        var reviewer = await vault.AdminClientAsync();
        await vault.SignInNewAccountAsync("it-request-existing-169", IPAddress.Parse("198.51.100.169"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-existing-169");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-request-existing-169");
        await LegacySharing.ShareAsync(reviewer, details, "it-request-existing-169");
        var existing = await LegacySharing.ShareOfAsync(vault, details);
        await LegacySharing.ExpireShareAsync(vault, existing.Id);
        var token = await LegacyAccessRequests.TokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyAccessRequests.PostAsync(reviewer, "Approve", token, pending.Id);

        Assert.NotEqual(existing.Id, (await LegacySharing.ShareOfAsync(vault, details)).Id);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal([AuditAction.AccessRequestApproved, AuditAction.SecretShared], rows.Select(row => row.Action));
        Assert.Equal("Shared with 'it-request-existing-169', replacing an expired share", rows[1].Details);
    }

    [Theory]
    [InlineData("-1", "170")]
    [InlineData("3651", "171")]
    [InlineData("2147483648", "172")]
    [InlineData("mistyped", "173")]
    public async Task Invalid_Legacy_expiry_is_refused_without_deciding_or_sharing(string days, string addressSuffix)
    {
        var reviewer = await vault.AdminClientAsync();
        var username = $"it-request-invalid-{addressSuffix}";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse($"198.51.100.{addressSuffix}"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, $"it-request-invalid-{days}");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, username);
        var token = await LegacyAccessRequests.TokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);
        await LegacyAccessRequests.PostAsync(reviewer, "Approve", token, pending.Id, days);
        Assert.False(await LegacySharing.HasAnyShareAsync(vault, details));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        using var scope = vault.Services.CreateScope();
        Assert.Equal(AccessRequestStatus.Pending, (await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.SingleAsync(r => r.Id == pending.Id)).Status);
    }
}
