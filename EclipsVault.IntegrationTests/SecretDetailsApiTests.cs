using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A secret's page in the UI API: who may do what to it, what each answer looks like, and what each
/// records. A trusted address is in 10.0.0.0/8; only a trusted address opens a Confidential secret, and
/// a Standard clearance opens nothing above Internal, so the off-network caller is Elevated. Each row signs in an account of its own from
/// its own address, so no row spends another's sign-in budget, and a decoy's row has a range of its own
/// to be blocked.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SecretDetailsApiTests(VaultFixture vault)
{
    private const string Elsewhere = "IT-DETAILS-ELSEWHERE";

    [Theory]
    [InlineData("get", "member", 200, null, "10.172.0.1")]
    [InlineData("get", "administrator", 200, null, null)]
    [InlineData("get", "recipient", 200, null, "10.172.0.2")]
    [InlineData("get", "other-project", 403, "access_denied", "10.172.0.3")]
    [InlineData("get", "under-cleared", 403, "access_denied", "10.172.0.4")]
    [InlineData("get", "off-network", 403, "access_denied", "198.18.172.1")]
    [InlineData("get", "decoy", 404, "not_found", "198.18.180.1")]
    [InlineData("reveal", "member", 200, null, "10.172.0.11")]
    [InlineData("reveal", "administrator", 200, null, null)]
    [InlineData("reveal", "recipient", 200, null, "10.172.0.12")]
    [InlineData("reveal", "other-project", 403, "access_denied", "10.172.0.13")]
    [InlineData("reveal", "under-cleared", 403, "access_denied", "10.172.0.14")]
    [InlineData("reveal", "off-network", 403, "access_denied", "198.18.172.2")]
    [InlineData("reveal", "decoy", 404, "not_found", "198.18.181.1")]
    [InlineData("reveal-version", "member", 200, null, "10.172.0.21")]
    [InlineData("reveal-version", "administrator", 200, null, null)]
    [InlineData("reveal-version", "recipient", 200, null, "10.172.0.22")]
    [InlineData("reveal-version", "other-project", 403, "access_denied", "10.172.0.23")]
    [InlineData("reveal-version", "under-cleared", 403, "access_denied", "10.172.0.24")]
    [InlineData("reveal-version", "off-network", 403, "access_denied", "198.18.172.3")]
    [InlineData("reveal-version", "decoy", 404, "not_found", "198.18.182.1")]
    [InlineData("rotate", "member", 204, null, "10.172.0.31")]
    [InlineData("rotate", "administrator", 204, null, null)]
    [InlineData("rotate", "recipient", 403, "not_permitted", "10.172.0.32")]
    [InlineData("rotate", "other-project", 403, "access_denied", "10.172.0.33")]
    [InlineData("rotate", "under-cleared", 403, "access_denied", "10.172.0.34")]
    [InlineData("rotate", "off-network", 403, "access_denied", "198.18.172.4")]
    [InlineData("rotate", "decoy", 404, "not_found", "198.18.183.1")]
    [InlineData("rotate-managed", "member", 409, "not_managed", "10.172.0.41")]
    [InlineData("rotate-managed", "administrator", 409, "not_managed", null)]
    [InlineData("rotate-managed", "recipient", 403, "not_permitted", "10.172.0.42")]
    [InlineData("rotate-managed", "other-project", 403, "access_denied", "10.172.0.43")]
    [InlineData("rotate-managed", "under-cleared", 403, "access_denied", "10.172.0.44")]
    [InlineData("rotate-managed", "off-network", 403, "access_denied", "198.18.172.5")]
    [InlineData("rotate-managed", "decoy", 404, "not_found", "198.18.184.1")]
    [InlineData("restore", "member", 204, null, "10.172.0.51")]
    [InlineData("restore", "administrator", 204, null, null)]
    [InlineData("restore", "recipient", 403, "not_permitted", "10.172.0.52")]
    [InlineData("restore", "other-project", 403, "access_denied", "10.172.0.53")]
    [InlineData("restore", "under-cleared", 403, "access_denied", "10.172.0.54")]
    [InlineData("restore", "off-network", 403, "access_denied", "198.18.172.6")]
    [InlineData("restore", "decoy", 404, "not_found", "198.18.185.1")]
    [InlineData("share", "member", 204, null, "10.172.0.61")]
    [InlineData("share", "administrator", 204, null, null)]
    [InlineData("share", "recipient", 403, "not_permitted", "10.172.0.62")]
    [InlineData("share", "other-project", 403, "access_denied", "10.172.0.63")]
    [InlineData("share", "under-cleared", 403, "access_denied", "10.172.0.64")]
    [InlineData("share", "off-network", 403, "access_denied", "198.18.172.7")]
    [InlineData("share", "decoy", 404, "not_found", "198.18.186.1")]
    [InlineData("revoke", "member", 204, null, "10.172.0.71")]
    [InlineData("revoke", "administrator", 204, null, null)]
    [InlineData("revoke", "recipient", 403, "not_permitted", "10.172.0.72")]
    [InlineData("revoke", "other-project", 403, "access_denied", "10.172.0.73")]
    [InlineData("revoke", "under-cleared", 403, "access_denied", "10.172.0.74")]
    [InlineData("revoke", "off-network", 403, "access_denied", "198.18.172.8")]
    [InlineData("revoke", "decoy", 404, "not_found", "198.18.187.1")]
    [InlineData("delete", "member", 204, null, "10.172.0.81")]
    [InlineData("delete", "administrator", 204, null, null)]
    [InlineData("delete", "recipient", 403, "not_permitted", "10.172.0.82")]
    [InlineData("delete", "other-project", 403, "access_denied", "10.172.0.83")]
    [InlineData("delete", "under-cleared", 403, "access_denied", "10.172.0.84")]
    [InlineData("delete", "off-network", 403, "access_denied", "198.18.172.9")]
    [InlineData("delete", "decoy", 404, "not_found", "198.18.188.1")]
    public async Task Each_endpoint_answers_each_caller_and_records_a_refusal_as_one_denial(
        string endpoint, string caller, int status, string? code, string? address)
    {
        var name = $"it-details-{endpoint}-{caller}";
        var admin = await vault.AdminClientAsync();
        var sensitivity = caller switch
        {
            "under-cleared" => "Secret",
            "off-network" => "Confidential",
            _ => "Internal",
        };
        var details = await SecretSharing.CreateSecretAsync(admin, name, value: "it-details-first-value", sensitivity: sensitivity);
        // The administrator's address is untrusted, so it can archive a version only of an Internal secret.
        // The other rows are refused before any version is looked up, so an unknown id serves them.
        var version = Guid.NewGuid();
        if (sensitivity == "Internal")
        {
            SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateAsync(admin, details, "it-details-live-value"));
            version = await SecretDetailsApi.NewestVersionAsync(vault, details);
        }

        await UserAccounts.CreateAsync(admin, $"{name}-holder", project: Elsewhere);
        await SecretSharing.InsertShareAsync(vault, details, $"{name}-holder");
        var holderShare = await SecretSharing.ShareOfAsync(vault, details);
        var client = caller == "administrator"
            ? admin
            : (await vault.SignInNewAccountAsync(name, IPAddress.Parse(address!),
                caller == "off-network" ? ClearanceLevel.Elevated : ClearanceLevel.Standard,
                project: caller is "recipient" or "other-project" ? Elsewhere : "GLOBAL")).Client;
        if (caller == "recipient")
        {
            await SecretSharing.InsertShareAsync(vault, details, name);
        }

        if (caller == "decoy")
        {
            await SecretSharing.MakeDecoyAsync(vault, details);
        }

        await UserAccounts.CreateAsync(admin, $"{name}-new", project: Elsewhere);
        var before = await AuditTrail.HeadAsync(vault);

        var response = endpoint switch
        {
            "get" => await SecretDetailsApi.GetAsync(client, details),
            "reveal" => await SecretDetailsApi.RevealAsync(client, details),
            "reveal-version" => await SecretDetailsApi.RevealVersionAsync(client, details, version),
            "rotate" => await SecretDetailsApi.RotateAsync(client, details, "it-details-new-value"),
            "rotate-managed" => await SecretDetailsApi.RotateUpstreamAsync(client, details),
            "restore" => await SecretDetailsApi.RestoreAsync(client, details, version),
            "share" => await SecretDetailsApi.ShareAsync(client, details, $"{name}-new"),
            "revoke" => await SecretDetailsApi.RevokeAsync(client, details, holderShare.Id),
            "delete" => await SecretDetailsApi.DeleteAsync(client, details),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
        };

        Assert.Equal(status, (int)response.StatusCode);
        if (code is not null)
        {
            await SecretsApi.AssertProblemAsync(response, status, code);
        }

        var denials = (await AuditTrail.RowsAfterAsync(vault, before)).Count(row => row.Action == AuditAction.SecretAccessDenied);
        Assert.Equal(code is "access_denied" or "not_permitted" ? 1 : 0, denials);
    }

    [Fact]
    public async Task The_page_answers_its_shape_with_versions_and_shares_and_records_one_view()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-details-shape", value: "it-details-first-value", environment: "Staging");
        await vault.SignInNewAccountAsync("it-details-shape-recipient", IPAddress.Parse("10.172.1.1"), project: Elsewhere);
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateAsync(admin, details, "it-details-live-value"));
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.ShareAsync(admin, details, "it-details-shape-recipient", ttlDays: 3));
        var version = await SecretDetailsApi.NewestVersionAsync(vault, details);
        var share = await SecretSharing.ShareOfAsync(vault, details);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretDetailsApi.GetAsync(admin, details);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ["secretId", "name", "project", "environment", "sensitivity", "sensitivityLabel", "algorithm", "createdAtUtc", "updatedAtUtc",
             "expiresAtUtc", "managed", "versions", "canChange", "canShare", "shares", "stepUpMaxAgeMinutes"],
            page.EnumerateObject().Select(member => member.Name));
        Assert.Equal(
            (SecretSharing.SecretIdOf(details).ToString(), "it-details-shape", "GLOBAL", "Staging", 1, "Internal", true, true, 10),
            (page.GetProperty("secretId").GetString(), page.GetProperty("name").GetString(), page.GetProperty("project").GetString(),
             page.GetProperty("environment").GetString(), page.GetProperty("sensitivity").GetInt32(), page.GetProperty("sensitivityLabel").GetString(),
             page.GetProperty("canChange").GetBoolean(), page.GetProperty("canShare").GetBoolean(), page.GetProperty("stepUpMaxAgeMinutes").GetInt32()));
        Assert.Equal(JsonValueKind.Null, page.GetProperty("managed").ValueKind);
        var archived = Assert.Single(page.GetProperty("versions").EnumerateArray());
        Assert.Equal((version.ToString(), 1, VaultFixture.AdminUsername),
            (archived.GetProperty("versionId").GetString(), archived.GetProperty("versionNumber").GetInt32(), archived.GetProperty("archivedBy").GetString()));
        var listed = Assert.Single(page.GetProperty("shares").EnumerateArray());
        Assert.Equal((share.Id.ToString(), "it-details-shape-recipient", VaultFixture.AdminUsername),
            (listed.GetProperty("shareId").GetString(), listed.GetProperty("recipient").GetString(), listed.GetProperty("issuer").GetString()));
        Assert.Equal([AuditAction.SecretMetadataViewed], await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
    }

    [Fact]
    public async Task A_recipients_page_offers_no_change_and_lists_no_shares()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-details-recipient-page");
        var recipient = await vault.SignInNewAccountAsync("it-details-recipient-page", IPAddress.Parse("10.172.1.2"), project: Elsewhere);
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.ShareAsync(admin, details, "it-details-recipient-page"));

        var page = await (await SecretDetailsApi.GetAsync(recipient.Client, details)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal((false, false, JsonValueKind.Null),
            (page.GetProperty("canChange").GetBoolean(), page.GetProperty("canShare").GetBoolean(), page.GetProperty("shares").ValueKind));
    }

    [Fact]
    public async Task A_managed_secrets_page_names_its_login_and_the_unstarted_evaluation()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-details-managed-page");
        await SecretDetailsApi.MakeManagedAsync(vault, details, "it_details_login");

        var page = await (await SecretDetailsApi.GetAsync(admin, details)).Content.ReadFromJsonAsync<JsonElement>();

        var managed = page.GetProperty("managed");
        Assert.Equal(("it_details_login", false, true, JsonValueKind.Null),
            (managed.GetProperty("principal").GetString(), managed.GetProperty("licensed").GetBoolean(),
             managed.GetProperty("evaluationKnown").GetBoolean(), managed.GetProperty("evaluationDaysRemaining").ValueKind));
    }

    [Fact]
    public async Task A_reveal_answers_the_value_labelled_and_is_never_stored()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-details-reveal-shape", value: "it-details-first-value");
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateAsync(admin, details, "it-details-live-value"));
        var version = await SecretDetailsApi.NewestVersionAsync(vault, details);

        var current = await SecretDetailsApi.RevealAsync(admin, details);
        var archived = await SecretDetailsApi.RevealVersionAsync(admin, details, version);

        Assert.All([current, archived], response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        });
        Assert.Equal("""{"label":"current value","value":"it-details-live-value"}""", await current.Content.ReadAsStringAsync());
        Assert.Equal("""{"label":"version 1","value":"it-details-first-value"}""", await archived.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_stale_session_revealing_a_version_of_a_Secret_secret_is_asked_to_step_up_and_records_nothing()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-details-version-step-up", sensitivity: "Secret");
        var member = await vault.SignInNewAccountAsync("it-details-version-step-up", IPAddress.Parse("10.172.1.3"), ClearanceLevel.Secret);
        // The member's fresh session on a trusted address archives the version the stale one asks for.
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateAsync(member.Client, details, "it-details-live-value"));
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, member, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("10.172.1.3"));
        var version = await SecretDetailsApi.NewestVersionAsync(vault, details);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretDetailsApi.RevealVersionAsync(stale.Client, details, version);

        var problem = await SecretsApi.AssertProblemAsync(response, 403, "step_up_required");
        Assert.Equal(("Confirm it's you before revealing this secret.", "code"),
            (problem.GetProperty("title").GetString(), problem.GetProperty("proof").GetString()));
        Assert.Empty(await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
    }

    [Fact]
    public async Task A_refusal_names_the_policys_reasons()
    {
        var details = await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-details-reasons", sensitivity: "Secret");
        var member = await vault.SignInNewAccountAsync("it-details-reasons", IPAddress.Parse("10.172.1.4"));

        var problem = await SecretsApi.AssertProblemAsync(await SecretDetailsApi.GetAsync(member.Client, details), 403, "access_denied");

        Assert.Equal(["Clearance 'Standard' is below required sensitivity 'Secret'."],
            problem.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()));
    }

    [Fact]
    public async Task A_decoys_answer_is_the_same_as_an_unknown_ids_and_trips_its_trap()
    {
        var details = await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-details-decoy-answer");
        await SecretSharing.MakeDecoyAsync(vault, details);
        var probe = await vault.SignInNewAccountAsync("it-details-decoy-probe", IPAddress.Parse("10.172.1.5"));
        var intruder = await vault.SignInNewAccountAsync("it-details-decoy-intruder", IPAddress.Parse("198.18.189.1"));
        var unknown = await SecretDetailsApi.GetAsync(probe.Client, $"/Secrets/Details/{Guid.NewGuid()}");
        var before = await AuditTrail.HeadAsync(vault);

        var decoy = await SecretDetailsApi.GetAsync(intruder.Client, details);

        Assert.Equal((unknown.StatusCode, await unknown.Content.ReadAsStringAsync()), (decoy.StatusCode, await decoy.Content.ReadAsStringAsync()));
        Assert.Equal(
            unknown.Headers.Concat(unknown.Content.Headers).Select(header => header.Key).Where(key => key != "Date").Order(),
            decoy.Headers.Concat(decoy.Content.Headers).Select(header => header.Key).Where(key => key != "Date").Order());
        Assert.Contains(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.HoneyTokenTripped);
    }

    [Theory]
    [InlineData("POST", "reveal")]
    [InlineData("POST", "rotate")]
    [InlineData("POST", "rotate-managed")]
    [InlineData("POST", "shares")]
    [InlineData("DELETE", "")]
    public async Task A_write_without_the_antiforgery_token_is_refused_and_changes_nothing(string method, string action)
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, $"it-details-forgery-{method}-{action}".TrimEnd('-'));
        var before = await AuditTrail.HeadAsync(vault);

        var response = await admin.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"{SecretDetailsApi.PathOf(details)}/{action}".TrimEnd('/'))
        {
            Content = method == "POST" ? JsonContent.Create(new { newValue = "forged", recipient = "forged" }) : null,
        });

        await UiApiFailureContractTests.AssertProblemAsync(response, 400, "antiforgery_rejected");
        Assert.Empty(await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
    }

    [Fact]
    public async Task The_page_refuses_a_working_api_key()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-details-api-key");
        var client = vault.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await vault.IssueApiKeyAsync(admin, "it-details-api-key"));

        await UiApiFailureContractTests.AssertProblemAsync(await SecretDetailsApi.GetAsync(client, details), 401, "unauthenticated");
    }

    [Fact]
    public async Task A_rotation_with_no_new_value_is_refused_with_the_Legacy_sentence()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-details-rotate-empty");

        var problem = await SecretsApi.AssertProblemAsync(await SecretDetailsApi.RotateAsync(admin, details, ""), 400, "validation_failed");

        Assert.Equal("Enter the new value to rotate this secret.", problem.GetProperty("title").GetString());
        Assert.Equal(["newValue"], problem.GetProperty("errors").EnumerateObject().Select(field => field.Name));
    }

    [Fact]
    public async Task Sharing_with_someone_who_already_has_a_share_is_refused()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-details-share-twice");
        await UserAccounts.CreateAsync(admin, "it-details-share-twice", project: Elsewhere);
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.ShareAsync(admin, details, "it-details-share-twice"));

        var problem = await SecretsApi.AssertProblemAsync(await SecretDetailsApi.ShareAsync(admin, details, "it-details-share-twice"), 409, "share_exists");

        Assert.Equal("'it-details-share-twice' already has a grant on this secret.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Revoking_a_share_that_no_longer_exists_says_so()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-details-revoke-gone");

        var problem = await SecretsApi.AssertProblemAsync(await SecretDetailsApi.RevokeAsync(admin, details, Guid.NewGuid()), 404, "share_not_found");

        Assert.Equal("That share no longer exists.", problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("reveal-version", "not_found")]
    [InlineData("restore", "not_found")]
    [InlineData("revoke", "share_not_found")]
    public async Task Another_secrets_version_or_share_answers_as_missing_and_changes_nothing(string endpoint, string code)
    {
        var admin = await vault.AdminClientAsync();
        var name = $"it-details-foreign-{endpoint}";
        var details = await SecretSharing.CreateSecretAsync(admin, name, value: "it-details-own-value");
        var other = await SecretSharing.CreateSecretAsync(admin, $"{name}-other", value: "it-details-other-first");
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateAsync(admin, other, "it-details-other-live"));
        await UserAccounts.CreateAsync(admin, name, project: Elsewhere);
        await SecretSharing.InsertShareAsync(vault, other, name);
        var otherVersion = await SecretDetailsApi.NewestVersionAsync(vault, other);
        var otherShare = await SecretSharing.ShareOfAsync(vault, other);
        var before = await AuditTrail.HeadAsync(vault);

        var response = endpoint switch
        {
            "reveal-version" => await SecretDetailsApi.RevealVersionAsync(admin, details, otherVersion),
            "restore" => await SecretDetailsApi.RestoreAsync(admin, details, otherVersion),
            "revoke" => await SecretDetailsApi.RevokeAsync(admin, details, otherShare.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
        };

        await SecretsApi.AssertProblemAsync(response, 404, code);
        Assert.DoesNotContain("it-details-other", await response.Content.ReadAsStringAsync());
        Assert.Empty(await SecretDetailsApi.ActionsAfterAsync(vault, before, other));
        Assert.Empty(await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
        Assert.Equal("it-details-own-value", await UpstreamRotationTests.StoredValueAsync(vault, details));
        Assert.Equal("it-details-other-live", await UpstreamRotationTests.StoredValueAsync(vault, other));
        Assert.Equal(otherShare.Id, (await SecretSharing.ShareOfAsync(vault, other)).Id);
    }

    [Fact]
    public async Task A_rotation_that_read_before_another_committed_is_told_the_secret_changed()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-details-secret-changed", value: "it-details-first-value");
        await UiApiRequest.AntiforgeryTokenAsync(admin);

        Task<HttpResponseMessage> loser;
        await using (var held = await HeldAuditChain.TakeAsync(vault))
        {
            loser = SecretDetailsApi.RotateAsync(admin, details, "it-details-loser-value");
            await held.WaitForQueuedWritersAsync(1);
            await RotateInAsync(held, details, "it-details-winner-value");
            await held.CommitAsync();
        }

        var problem = await SecretsApi.AssertProblemAsync(await loser, 409, "secret_changed");
        Assert.Equal("This secret changed while you were working on it. Reload and try again.", problem.GetProperty("title").GetString());
        Assert.Equal("it-details-winner-value", await UpstreamRotationTests.StoredValueAsync(vault, details));
    }

    private static Task RotateInAsync(HeldAuditChain held, string details, string value)
        => held.Services.GetRequiredService<ISecretService>().RotateAsync(SecretSharing.SecretIdOf(details), value, null, null, default);
}

/// <summary>A Production secret opens only inside the production window, so outside it every endpoint refuses.</summary>
[Collection(ClosedProductionWindowCollection.Name)]
public sealed class SecretDetailsProductionWindowTests(ClosedProductionWindowFixture fixture)
{
    private const string WindowReason = "Production secrets are outside the permitted access time window.";

    [Theory]
    [InlineData("get", "10.172.3.1")]
    [InlineData("reveal", "10.172.3.2")]
    [InlineData("rotate", "10.172.3.3")]
    [InlineData("share", "10.172.3.4")]
    [InlineData("delete", "10.172.3.5")]
    public async Task Outside_the_window_a_production_secret_is_refused_and_recorded_once(string endpoint, string address)
    {
        var vault = fixture.Vault;
        var name = $"it-details-window-{endpoint}";
        var details = await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), name, value: "it-details-window-value", environment: "Production");
        var member = await vault.SignInNewAccountAsync(name, IPAddress.Parse(address));
        var before = await AuditTrail.HeadAsync(vault);

        var response = endpoint switch
        {
            "get" => await SecretDetailsApi.GetAsync(member.Client, details),
            "reveal" => await SecretDetailsApi.RevealAsync(member.Client, details),
            "rotate" => await SecretDetailsApi.RotateAsync(member.Client, details, "it-details-window-new-value"),
            "share" => await SecretDetailsApi.ShareAsync(member.Client, details, VaultFixture.AdminUsername),
            "delete" => await SecretDetailsApi.DeleteAsync(member.Client, details),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
        };

        var problem = await SecretsApi.AssertProblemAsync(response, 403, "access_denied");
        Assert.Equal([WindowReason], problem.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()));
        Assert.Equal([AuditAction.SecretAccessDenied], await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
        Assert.Equal("it-details-window-value", await UpstreamRotationTests.StoredValueAsync(vault, details));
    }
}
