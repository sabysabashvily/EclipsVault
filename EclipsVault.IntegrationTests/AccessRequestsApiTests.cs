using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Access requests page's UI API: its page data, the answers its decisions give, and the Denied
/// page's form, which files a request and sends the browser to the page with the outcome. Each account
/// signs in from its own address in 198.18.101.0/24, so sign-in budgets never meet.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AccessRequestsApiTests(VaultFixture vault)
{
    private const string AnyRequest = "11111111-1111-1111-1111-111111111111";

    [Theory]
    [InlineData("GET", AccessRequestsApi.Path)]
    [InlineData("POST", $"{AccessRequestsApi.Path}/{AnyRequest}/approve")]
    [InlineData("POST", $"{AccessRequestsApi.Path}/{AnyRequest}/reject")]
    [InlineData("POST", $"{AccessRequestsApi.Path}/{AnyRequest}/withdraw")]
    public async Task Each_endpoint_refuses_anonymous_and_working_api_key_callers(string method, string path)
    {
        var client = vault.CreateClient();
        await UiApiFailureContractTests.AssertProblemAsync(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 401, "unauthenticated");
        var key = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), $"it-arapi-key-{path.Split('/')[^1]}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/secrets")).StatusCode);

        await UiApiFailureContractTests.AssertProblemAsync(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 401, "unauthenticated");
    }

    [Fact]
    public async Task A_reviewer_gets_the_pinned_queue_shape_with_the_servers_denial_reasons()
    {
        var admin = await vault.AdminClientAsync();
        var requester = await vault.SignInNewAccountAsync("it-arapi-queue", IPAddress.Parse("198.18.101.1"), ClearanceLevel.Elevated, "IT-ARAPI");
        var secretId = LegacySharing.SecretIdOf(await LegacySharing.CreateSecretAsync(admin, "it-arapi-queue-secret"));
        await LegacyAccessRequests.FileAsync(requester.Client, await LegacyAccessRequests.TokenAsync(requester.Client), secretId, "Release on Friday");
        var filed = await StoredRequestAsync(secretId);

        var response = await admin.GetAsync(AccessRequestsApi.Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["mine", "reviewScope", "toReview"], ProfileApi.NamesOf(document.RootElement));
        Assert.Equal("all", document.RootElement.GetProperty("reviewScope").GetString());
        var listed = Assert.Single(document.RootElement.GetProperty("toReview").EnumerateArray(),
            request => request.GetProperty("requestId").GetGuid() == filed.Id);
        Assert.Equal(["deniedBecause", "project", "reason", "requestId", "requestedAtUtc", "requester", "secretName"], ProfileApi.NamesOf(listed));
        Assert.Equal("it-arapi-queue-secret", listed.GetProperty("secretName").GetString());
        Assert.Equal("GLOBAL", listed.GetProperty("project").GetString());
        Assert.Equal("it-arapi-queue", listed.GetProperty("requester").GetString());
        Assert.Equal("Release on Friday", listed.GetProperty("reason").GetString());
        Assert.Equal("Subject project 'IT-ARAPI' does not match resource project 'GLOBAL'.", listed.GetProperty("deniedBecause").GetString());
        Assert.Equal(filed.CreatedAtUtc.UtcDateTime, listed.GetProperty("requestedAtUtc").GetDateTime());
    }

    [Fact]
    public async Task A_requester_gets_the_pinned_shape_of_their_requests_newest_first()
    {
        var admin = await vault.AdminClientAsync();
        var requester = await vault.SignInNewAccountAsync("it-arapi-mine", IPAddress.Parse("198.18.101.2"), ClearanceLevel.Elevated, "IT-ARAPI-MINE");
        var older = await LegacyAccessRequests.InsertPendingAsync(vault, await LegacySharing.CreateSecretAsync(admin, "it-arapi-mine-pending"), "it-arapi-mine");
        var newer = await LegacyAccessRequests.InsertPendingAsync(vault, await LegacySharing.CreateSecretAsync(admin, "it-arapi-mine-rejected"), "it-arapi-mine");
        var decidedAt = new DateTimeOffset(2026, 10, 30, 8, 15, 0, TimeSpan.Zero);
        using (var scope = vault.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.Where(r => r.Id == newer.Id)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, AccessRequestStatus.Rejected)
                    .SetProperty(r => r.DecidedBy, "vault-admin")
                    .SetProperty(r => r.DecidedAtUtc, decidedAt)
                    .SetProperty(r => r.DecisionNote, "A stored note"));
        }

        using var document = JsonDocument.Parse(await requester.Client.GetStringAsync(AccessRequestsApi.Path));

        Assert.Equal("project", document.RootElement.GetProperty("reviewScope").GetString());
        Assert.Empty(document.RootElement.GetProperty("toReview").EnumerateArray());
        var mine = document.RootElement.GetProperty("mine").EnumerateArray().ToArray();
        Assert.Equal(2, mine.Length);
        Assert.All(mine, request => Assert.Equal(
            ["decidedAtUtc", "decidedBy", "decisionNote", "project", "requestId", "requestedAtUtc", "secretName", "status"], ProfileApi.NamesOf(request)));
        Assert.Equal(newer.Id, mine[0].GetProperty("requestId").GetGuid());
        Assert.Equal("it-arapi-mine-rejected", mine[0].GetProperty("secretName").GetString());
        Assert.Equal("GLOBAL", mine[0].GetProperty("project").GetString());
        Assert.Equal("Rejected", mine[0].GetProperty("status").GetString());
        Assert.Equal(newer.CreatedAtUtc.UtcDateTime, mine[0].GetProperty("requestedAtUtc").GetDateTime(), TimeSpan.FromMilliseconds(1));
        Assert.Equal("vault-admin", mine[0].GetProperty("decidedBy").GetString());
        Assert.Equal(decidedAt.UtcDateTime, mine[0].GetProperty("decidedAtUtc").GetDateTime());
        Assert.Equal("A stored note", mine[0].GetProperty("decisionNote").GetString());
        Assert.Equal(older.Id, mine[1].GetProperty("requestId").GetGuid());
        Assert.Equal("it-arapi-mine-pending", mine[1].GetProperty("secretName").GetString());
        Assert.Equal("Pending", mine[1].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, mine[1].GetProperty("decidedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, mine[1].GetProperty("decidedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, mine[1].GetProperty("decisionNote").ValueKind);
    }

    [Fact]
    public async Task A_request_inserted_without_denial_reasons_reaches_the_queue_with_none()
    {
        var admin = await vault.AdminClientAsync();
        await vault.SignInNewAccountAsync("it-arapi-no-reasons", IPAddress.Parse("198.18.101.3"), ClearanceLevel.Elevated, "IT-ARAPI");
        var pending = await LegacyAccessRequests.InsertPendingAsync(vault, await LegacySharing.CreateSecretAsync(admin, "it-arapi-no-reasons"), "it-arapi-no-reasons");

        var page = await AccessRequestsApi.PageAsync(admin);

        var listed = Assert.Single(page.GetProperty("toReview").EnumerateArray(), request => request.GetProperty("requestId").GetGuid() == pending.Id);
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("deniedBecause").ValueKind);
    }

    [Fact]
    public async Task Each_requester_lists_only_their_own_requests_despite_injected_parameters()
    {
        var admin = await vault.AdminClientAsync();
        var first = await vault.SignInNewAccountAsync("it-arapi-scope-a", IPAddress.Parse("198.18.101.4"), ClearanceLevel.Elevated, "IT-ARAPI-SCOPE");
        var second = await vault.SignInNewAccountAsync("it-arapi-scope-b", IPAddress.Parse("198.18.101.5"), ClearanceLevel.Elevated, "IT-ARAPI-SCOPE");
        var firsts = await LegacyAccessRequests.InsertPendingAsync(vault, await LegacySharing.CreateSecretAsync(admin, "it-arapi-scope-a"), "it-arapi-scope-a");
        var seconds = await LegacyAccessRequests.InsertPendingAsync(vault, await LegacySharing.CreateSecretAsync(admin, "it-arapi-scope-b"), "it-arapi-scope-b");
        var claimingSecond = $"?userId={seconds.RequesterUserId}&requesterUserId={seconds.RequesterUserId}&username=it-arapi-scope-b";

        var firstPage = await first.Client.GetFromJsonAsync<JsonElement>(AccessRequestsApi.Path + claimingSecond);
        var secondPage = await second.Client.GetFromJsonAsync<JsonElement>(AccessRequestsApi.Path);

        Assert.Equal(firsts.Id, Assert.Single(firstPage.GetProperty("mine").EnumerateArray()).GetProperty("requestId").GetGuid());
        Assert.Equal(seconds.Id, Assert.Single(secondPage.GetProperty("mine").EnumerateArray()).GetProperty("requestId").GetGuid());
    }

    [Fact]
    public async Task Approval_with_an_expiry_issues_a_share_that_expires_after_that_many_days()
    {
        var admin = await vault.AdminClientAsync();
        await vault.SignInNewAccountAsync("it-arapi-expiry", IPAddress.Parse("198.18.101.6"), ClearanceLevel.Elevated, "IT-ARAPI");
        var details = await LegacySharing.CreateSecretAsync(admin, "it-arapi-expiry");
        var pending = await LegacyAccessRequests.InsertPendingAsync(vault, details, "it-arapi-expiry");

        var response = await AccessRequestsApi.ApproveAsync(admin, pending.Id, expiresInDays: 7);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        var share = await LegacySharing.ShareOfAsync(vault, details);
        Assert.Equal(share.CreatedAtUtc.AddDays(7), share.ExpiresAtUtc);
    }

    [Fact]
    public async Task A_share_saved_first_by_another_request_answers_share_exists()
    {
        var response = await (await vault.AdminClientAsync()).GetAsync("/api/ui/test-only/access-request-share-exists");

        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "share_exists");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("They already have a share on this secret.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_requester_deleted_during_approval_makes_the_request_ineligible()
    {
        var response = await (await vault.AdminClientAsync()).GetAsync("/api/ui/test-only/access-request-requester-gone");

        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "request_ineligible");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("That requester account no longer exists.", problem.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(AccessRequestStatus.Approved, "reject", "198.18.101.10")]
    [InlineData(AccessRequestStatus.Rejected, "approve", "198.18.101.11")]
    [InlineData(AccessRequestStatus.Cancelled, "approve", "198.18.101.12")]
    [InlineData(AccessRequestStatus.Approved, "withdraw", "198.18.101.13")]
    public async Task A_request_no_longer_pending_refuses_every_later_decision(AccessRequestStatus decided, string decision, string requesterAddress)
    {
        var admin = await vault.AdminClientAsync();
        var username = $"it-arapi-decided-{decided}-{decision}".ToLowerInvariant();
        var requester = await vault.SignInNewAccountAsync(username, IPAddress.Parse(requesterAddress), ClearanceLevel.Elevated, "IT-ARAPI");
        var pending = await LegacyAccessRequests.InsertPendingAsync(vault, await LegacySharing.CreateSecretAsync(admin, username), username);
        using (var scope = vault.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.Where(r => r.Id == pending.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, decided).SetProperty(r => r.DecidedAtUtc, DateTimeOffset.UtcNow));
        }
        var caller = decision == "withdraw" ? requester.Client : admin;
        var token = await UiApiRequest.AntiforgeryTokenAsync(caller);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.DecideAsync(caller, token, pending.Id, decision,
            decision == "approve" ? JsonContent.Create(new { expiresInDays = 0 }) : null);

        var problem = await ProfileApi.AssertProblemAsync(response, 409, "request_not_pending");
        Assert.Equal("That request is no longer pending.", problem.GetProperty("title").GetString());
        Assert.Equal(decided, await AccessRequestsApi.StatusOfAsync(vault, pending.Id));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Theory]
    [InlineData("approve", "198.18.101.30")]
    [InlineData("reject", "198.18.101.31")]
    [InlineData("withdraw", "198.18.101.32")]
    public async Task A_decision_without_the_antiforgery_header_changes_nothing(string decision, string requesterAddress)
    {
        var admin = await vault.AdminClientAsync();
        var username = $"it-arapi-csrf-{decision}";
        var requester = await vault.SignInNewAccountAsync(username, IPAddress.Parse(requesterAddress), ClearanceLevel.Elevated, "IT-ARAPI");
        var pending = await LegacyAccessRequests.InsertPendingAsync(vault, await LegacySharing.CreateSecretAsync(admin, username), username);
        var caller = decision == "withdraw" ? requester.Client : admin;
        var before = await AuditTrail.HeadAsync(vault);

        var response = await caller.PostAsync($"{AccessRequestsApi.Path}/{pending.Id}/{decision}", JsonContent.Create(new { expiresInDays = 0 }));

        await UiApiFailureContractTests.AssertProblemAsync(response, 400, "antiforgery_rejected");
        Assert.Equal(AccessRequestStatus.Pending, await AccessRequestsApi.StatusOfAsync(vault, pending.Id));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_deleted_accounts_cookie_can_neither_read_nor_decide()
    {
        var source = IPAddress.Parse("198.18.101.40");
        var session = await vault.SignInNewAccountAsync("it-arapi-deleted", source);
        var cookie = Assert.IsType<Cookie>(session.SessionCookie);
        await vault.DeleteAccountAsync("it-arapi-deleted");
        var before = await AuditTrail.HeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await session.Client.GetAsync(AccessRequestsApi.Path), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(
            await vault.CreateSession(cookie, source).Client.PostAsync($"{AccessRequestsApi.Path}/{AnyRequest}/withdraw", null), 401, "unauthenticated");
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_cookie_without_its_account_id_can_neither_read_nor_decide()
    {
        var source = IPAddress.Parse("198.18.101.41");
        var session = await vault.SignInNewAccountAsync("it-arapi-no-id", source);
        var cookie = SessionApi.WithoutAccountId(vault, session);
        var before = await AuditTrail.HeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await vault.CreateSession(cookie, source).Client.GetAsync(AccessRequestsApi.Path), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(
            await vault.CreateSession(cookie, source).Client.PostAsync($"{AccessRequestsApi.Path}/{AnyRequest}/withdraw", null), 401, "unauthenticated");
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Filing_a_request_sends_the_browser_to_the_page_saying_it_was_filed()
    {
        var requester = await vault.SignInNewAccountAsync("it-arapi-filed", IPAddress.Parse("198.18.101.42"), ClearanceLevel.Elevated, "IT-ARAPI");
        var secretId = LegacySharing.SecretIdOf(await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-arapi-filed"));

        var response = await LegacyAccessRequests.FileAsync(requester.Client, await LegacyAccessRequests.TokenAsync(requester.Client), secretId);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/AccessRequests?filed=1", response.Headers.Location?.OriginalString);
        Assert.Equal(AccessRequestStatus.Pending, (await StoredRequestAsync(secretId)).Status);
    }

    [Fact]
    public async Task Filing_without_a_reason_sends_the_browser_to_the_page_saying_why()
    {
        var requester = await vault.SignInNewAccountAsync("it-arapi-reasonless", IPAddress.Parse("198.18.101.43"), ClearanceLevel.Elevated, "IT-ARAPI");
        var secretId = LegacySharing.SecretIdOf(await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-arapi-reasonless"));
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAccessRequests.FileAsync(requester.Client, await LegacyAccessRequests.TokenAsync(requester.Client), secretId, reason: " ab ");

        Assert.Equal("/AccessRequests?refused=reason_too_short", response.Headers.Location?.OriginalString);
        Assert.False(await HasRequestAsync(secretId));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Filing_for_a_secret_that_no_longer_exists_sends_the_browser_to_the_page_saying_so()
    {
        var requester = await vault.SignInNewAccountAsync("it-arapi-gone", IPAddress.Parse("198.18.101.44"), ClearanceLevel.Elevated, "IT-ARAPI");
        var secretId = LegacySharing.SecretIdOf(await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-arapi-gone"));
        using (var scope = vault.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            (await db.Secrets.SingleAsync(secret => secret.Id == secretId)).Shred(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var response = await LegacyAccessRequests.FileAsync(requester.Client, await LegacyAccessRequests.TokenAsync(requester.Client), secretId);

        Assert.Equal("/AccessRequests?refused=secret_not_found", response.Headers.Location?.OriginalString);
        Assert.False(await HasRequestAsync(secretId));
    }

    private async Task<AccessRequest> StoredRequestAsync(Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.AsNoTracking()
            .SingleAsync(request => request.SecretId == secretId);
    }

    private async Task<bool> HasRequestAsync(Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.AnyAsync(request => request.SecretId == secretId);
    }
}
