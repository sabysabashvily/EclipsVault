using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Models.Ui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Secrets list, the palettes' search and New secret. A trusted address is in 10.0.0.0/8;
/// 100.164.0.0/16 is not. Each account signs in from its own address, so no test spends another's
/// sign-in budget.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SecretsApiTests(VaultFixture vault)
{
    [Theory]
    [InlineData("/api/ui/secrets", "it-secrets-list-key")]
    [InlineData("/api/ui/secrets/search?q=it", "it-secrets-search-key")]
    public async Task The_reads_refuse_anonymous_and_working_api_key_callers(string path, string serviceAccount)
    {
        var client = vault.CreateClient();
        await UiApiFailureContractTests.AssertProblemAsync(await client.GetAsync(path), 401, "unauthenticated");
        var key = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), serviceAccount);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/secrets")).StatusCode);

        await UiApiFailureContractTests.AssertProblemAsync(await client.GetAsync(path), 401, "unauthenticated");
    }

    [Fact]
    public async Task The_list_and_the_search_answer_their_shapes_and_write_no_audit_row()
    {
        const string project = "IT-SECRETS-SHAPE";
        var secret = await OverviewSecrets.CreateAsync(vault, "it-secrets-shape", project, sensitivity: "Confidential",
            environment: "Staging", expiresInDays: 3);
        var member = await vault.SignInNewAccountAsync("it-secrets-shape", IPAddress.Parse("10.164.0.1"), ClearanceLevel.Elevated, project);
        var before = await AuditTrail.HeadAsync(vault);

        var list = await member.Client.GetAsync("/api/ui/secrets");
        var search = await member.Client.GetAsync("/api/ui/secrets/search?q=it-secrets-sh");

        Assert.True(list.Headers.CacheControl?.NoStore);
        Assert.True(search.Headers.CacheControl?.NoStore);
        using var listed = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(["secrets"], ProfileApi.NamesOf(listed.RootElement));
        var row = Assert.Single(listed.RootElement.GetProperty("secrets").EnumerateArray());
        Assert.Equal(["createdAtUtc", "environment", "expiresAtUtc", "name", "project", "secretId", "sensitivity", "sensitivityLabel"],
            ProfileApi.NamesOf(row));
        Assert.Equal((secret.Id, "it-secrets-shape", project, "Staging", 2, "Confidential"),
            (row.GetProperty("secretId").GetGuid(), row.GetProperty("name").GetString(), row.GetProperty("project").GetString(),
             row.GetProperty("environment").GetString(), row.GetProperty("sensitivity").GetInt32(), row.GetProperty("sensitivityLabel").GetString()));
        Assert.Equal(secret.ExpiresAtUtc!.Value, row.GetProperty("expiresAtUtc").GetDateTimeOffset(), TimeSpan.FromMilliseconds(1));
        using var found = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        var hit = Assert.Single(found.RootElement.EnumerateArray());
        Assert.Equal(["environment", "id", "name", "project", "sensitivity", "sensitivityName", "url"], ProfileApi.NamesOf(hit));
        Assert.Equal((secret.Id, "it-secrets-shape", project, "Staging", 2, "Confidential", $"/Secrets/Details/{secret.Id}"),
            (hit.GetProperty("id").GetGuid(), hit.GetProperty("name").GetString(), hit.GetProperty("project").GetString(),
             hit.GetProperty("environment").GetString(), hit.GetProperty("sensitivity").GetInt32(),
             hit.GetProperty("sensitivityName").GetString(), hit.GetProperty("url").GetString()));
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), audit => audit.Username == "it-secrets-shape");
    }

    [Fact]
    public async Task The_list_and_the_search_leave_out_another_projects_an_under_cleared_and_a_decoy_secret()
    {
        const string project = "IT-SECRETS-FILTER";
        await OverviewSecrets.CreateAsync(vault, "it-secrets-filter-visible", project);
        await OverviewSecrets.CreateAsync(vault, "it-secrets-filter-other-project", "IT-SECRETS-ELSEWHERE");
        await OverviewSecrets.CreateAsync(vault, "it-secrets-filter-under-cleared", project, sensitivity: "Secret");
        await OverviewSecrets.CreateAsync(vault, "it-secrets-filter-decoy", project, decoy: true);
        var member = await vault.SignInNewAccountAsync("it-secrets-filter", IPAddress.Parse("10.164.0.2"), ClearanceLevel.Elevated, project);

        var list = await member.Client.GetFromJsonAsync<SecretsResponse>("/api/ui/secrets");
        var search = await member.Client.GetFromJsonAsync<SecretSearchResultResponse[]>("/api/ui/secrets/search?q=it-secrets-filter");

        Assert.Equal(["it-secrets-filter-visible"], list!.Secrets.Select(secret => secret.Name));
        Assert.Equal(["it-secrets-filter-visible"], search!.Select(secret => secret.Name));
    }

    [Fact]
    public async Task The_list_and_the_search_leave_out_a_secret_that_needs_a_trusted_network_off_the_network()
    {
        const string project = "IT-SECRETS-NETWORK";
        await OverviewSecrets.CreateAsync(vault, "it-secrets-network-internal", project);
        await OverviewSecrets.CreateAsync(vault, "it-secrets-network-confidential", project, sensitivity: "Confidential");
        var member = await vault.SignInNewAccountAsync("it-secrets-network", IPAddress.Parse("100.164.0.1"), ClearanceLevel.Elevated, project);

        var list = await member.Client.GetFromJsonAsync<SecretsResponse>("/api/ui/secrets");
        var search = await member.Client.GetFromJsonAsync<SecretSearchResultResponse[]>("/api/ui/secrets/search?q=it-secrets-network");

        Assert.Equal(["it-secrets-network-internal"], list!.Secrets.Select(secret => secret.Name));
        Assert.Equal(["it-secrets-network-internal"], search!.Select(secret => secret.Name));
    }

    [Theory]
    [InlineData("i", "it-secrets-short-letter")]
    [InlineData(" i ", "it-secrets-short-padded")]
    [InlineData("", "it-secrets-short-empty")]
    public async Task A_search_shorter_than_two_characters_finds_nothing(string query, string listable)
    {
        var admin = await vault.AdminClientAsync();
        await OverviewSecrets.CreateAsync(vault, listable, "GLOBAL");

        var found = await admin.GetFromJsonAsync<SecretSearchResultResponse[]>($"/api/ui/secrets/search?q={Uri.EscapeDataString(query)}");

        Assert.Empty(found!);
    }

    [Fact]
    public async Task Creating_stores_one_secret_with_one_SecretCreated_row_and_sets_the_Legacy_success_message()
    {
        var member = await vault.SignInNewAccountAsync("it-secrets-create", IPAddress.Parse("10.164.0.3"), project: "IT-SECRETS-CREATE");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretsApi.CreateAsync(member.Client, "it-secrets-created", "it-created-value", " it-secrets-create ",
            "Staging", "Internal", ttlDays: 30);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var created = await response.Content.ReadFromJsonAsync<CreatedSecretResponse>();
        Assert.Equal($"/Secrets/Details/{created!.SecretId}", response.Headers.Location?.OriginalString);
        var stored = await StoredAsync("it-secrets-created");
        Assert.Equal((created.SecretId, "IT-SECRETS-CREATE", SecretEnvironment.Staging, SensitivityLevel.Internal),
            (stored!.Id, stored.ProjectKey, stored.Environment, stored.Sensitivity));
        Assert.Equal(TimeSpan.FromDays(30), stored.ExpiresAtUtc!.Value - stored.CreatedAtUtc);
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), audit => audit.Username == "it-secrets-create");
        Assert.Equal((AuditAction.SecretCreated, created.SecretId), (row.Action, row.ResourceId));
    }

    [Theory]
    [InlineData("it-secrets-taken-elsewhere", false, "it-secrets-taken-elsewhere-member", "10.164.0.5")]
    [InlineData("it-secrets-taken-by-decoy", true, "it-secrets-taken-by-decoy-member", "10.164.0.4")]
    public async Task A_name_another_project_or_a_decoy_holds_is_taken_and_stores_nothing(
        string name, bool decoy, string username, string source)
    {
        await OverviewSecrets.CreateAsync(vault, name, "IT-SECRETS-HOLDER", decoy: decoy);
        var member = await vault.SignInNewAccountAsync(username, IPAddress.Parse(source), project: "IT-SECRETS-TAKER");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretsApi.CreateAsync(member.Client, name, project: "IT-SECRETS-TAKER");

        var problem = await SecretsApi.AssertProblemAsync(response, 409, "name_taken");
        Assert.Equal("A secret with that name already exists.", problem.GetProperty("title").GetString());
        Assert.Equal("IT-SECRETS-HOLDER", (await StoredAsync(name))!.ProjectKey);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), audit => audit.Username == username);
    }

    [Theory]
    [InlineData("it-secrets-denied-project", "10.164.0.6", "it-secrets-denied-project-secret", "IT-SECRETS-NOT-MINE", "Internal",
        "Subject project 'IT-SECRETS-DENIED' does not permit creating a secret in project 'IT-SECRETS-NOT-MINE'.")]
    [InlineData("it-secrets-denied-clearance", "10.164.0.7", "it-secrets-denied-clearance-secret", "IT-SECRETS-DENIED", "Confidential",
        "Clearance 'Standard' is below the requested sensitivity 'Confidential'.")]
    public async Task Creating_in_another_project_or_above_clearance_is_refused_with_the_policys_reasons(
        string username, string source, string name, string project, string sensitivity, string reason)
    {
        var member = await vault.SignInNewAccountAsync(username, IPAddress.Parse(source), project: "IT-SECRETS-DENIED");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretsApi.CreateAsync(member.Client, name, project: project, sensitivity: sensitivity);

        var problem = await SecretsApi.AssertProblemAsync(response, 403, "creation_denied");
        Assert.Equal([reason], problem.GetProperty("reasons").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Null(await StoredAsync(name));
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), audit => audit.Username == username);
    }

    [Fact]
    public async Task Creating_without_the_antiforgery_token_is_refused_and_stores_nothing()
    {
        var admin = await vault.AdminClientAsync();

        var response = await admin.PostAsync(SecretsApi.Path, SecretsApi.CreateContent("it-secrets-forged"));

        await UiApiFailureContractTests.AssertProblemAsync(response, 400, "antiforgery_rejected");
        Assert.Null(await StoredAsync("it-secrets-forged"));
    }

    [Theory]
    [InlineData("ab", "name", "The field Name must be a string with a minimum length of 3 and a maximum length of 128.",
        """{ "name": "ab", "value": "v", "project": "GLOBAL", "environment": "Development", "sensitivity": 1, "ttlDays": 0 }""")]
    [InlineData("has space", "name", "Use letters, digits, underscores, hyphens or dots.",
        """{ "name": "has space", "value": "v", "project": "GLOBAL", "environment": "Development", "sensitivity": 1, "ttlDays": 0 }""")]
    [InlineData("it-secrets-no-value", "value", "The Secret value field is required.",
        """{ "name": "it-secrets-no-value", "value": "", "project": "GLOBAL", "environment": "Development", "sensitivity": 1, "ttlDays": 0 }""")]
    [InlineData("it-secrets-long-project", "project", "The field Project key must be a string with a maximum length of 64.",
        """{ "name": "it-secrets-long-project", "value": "v", "project": "PPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPPP", "environment": "Development", "sensitivity": 1, "ttlDays": 0 }""")]
    [InlineData("it-secrets-bad-environment", "environment", "This field is missing or invalid.",
        """{ "name": "it-secrets-bad-environment", "value": "v", "project": "GLOBAL", "environment": "1", "sensitivity": 1, "ttlDays": 0 }""")]
    [InlineData("it-secrets-bad-sensitivity", "sensitivity", "This field is missing or invalid.",
        """{ "name": "it-secrets-bad-sensitivity", "value": "v", "project": "GLOBAL", "environment": "Development", "sensitivity": 9, "ttlDays": 0 }""")]
    [InlineData("it-secrets-bad-ttl", "ttlDays", "The field TTL in days (0 = never expires) must be between 0 and 3650.",
        """{ "name": "it-secrets-bad-ttl", "value": "v", "project": "GLOBAL", "environment": "Development", "sensitivity": 1, "ttlDays": 3651 }""")]
    [InlineData("it-secrets-empty-ttl", "ttlDays", "The value '' is invalid.",
        """{ "name": "it-secrets-empty-ttl", "value": "v", "project": "GLOBAL", "environment": "Development", "sensitivity": 1, "ttlDays": null }""")]
    public async Task An_invalid_field_is_refused_in_the_Legacy_forms_words_and_nothing_is_stored(
        string name, string field, string message, string body)
    {
        var admin = await vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);

        var response = await UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, SecretsApi.Path,
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        var problem = await SecretsApi.AssertProblemAsync(response, 400, "validation_failed");
        var error = Assert.Single(problem.GetProperty("errors").EnumerateObject());
        Assert.Equal((field, message), (error.Name, Assert.Single(error.Value.EnumerateArray()).GetString()));
        Assert.Null(await StoredAsync(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task A_missing_or_unreadable_create_body_is_refused_as_validation_failed(string body)
    {
        var admin = await vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);

        var response = await UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, SecretsApi.Path,
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        var problem = await SecretsApi.AssertProblemAsync(response, 400, "validation_failed");
        Assert.Equal("The request could not be read.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_unreadable_body_names_its_field_without_quoting_what_was_sent()
    {
        var admin = await vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);

        var response = await UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, SecretsApi.Path,
            new StringContent("""{ "name": "it-secrets-unreadable", "value": "not-a-number-secret", "project": "GLOBAL", "environment": "Development", "sensitivity": 1, "ttlDays": "not-a-number-secret" }""",
                System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("not-a-number-secret", await response.Content.ReadAsStringAsync());
        Assert.Null(await StoredAsync("it-secrets-unreadable"));
    }

    [Fact]
    public async Task A_value_of_8192_characters_is_stored_and_one_more_is_refused()
    {
        var admin = await vault.AdminClientAsync();

        Assert.Equal(HttpStatusCode.Created, (await SecretsApi.CreateAsync(admin, "it-secrets-longest-value", new string('v', 8192))).StatusCode);
        await SecretsApi.AssertProblemAsync(await SecretsApi.CreateAsync(admin, "it-secrets-too-long-value", new string('v', 8193)), 400, "validation_failed");
        Assert.Null(await StoredAsync("it-secrets-too-long-value"));
    }

    /// <summary>
    /// Another request can take the name between the service's check and the insert; the unique index
    /// then refuses it, and that refusal is a taken name, not an audit failure.
    /// </summary>
    [Fact]
    public async Task An_insert_that_loses_the_name_to_another_is_a_taken_name()
    {
        await OverviewSecrets.CreateAsync(vault, "it-secrets-race", "GLOBAL");
        using var scope = vault.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISecretRepository>();

        await Assert.ThrowsAsync<SecretNameTakenException>(() => repository.AddAsync(new Secret
        {
            Id = Guid.NewGuid(),
            Name = "it-secrets-race",
            ProjectKey = "GLOBAL",
            Environment = SecretEnvironment.Development,
            Sensitivity = SensitivityLevel.Internal,
            Ciphertext = [1],
            WrappedDek = [1],
            KekId = "it-race",
            Algorithm = "it-race",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        }, CancellationToken.None));
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets
            .CountAsync(secret => secret.Name == "it-secrets-race"));
    }

    private async Task<Secret?> StoredAsync(string name)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.AsNoTracking()
            .SingleOrDefaultAsync(secret => secret.Name == name);
    }
}

/// <summary>Production secrets open only inside the production window, so outside it the list and the search leave them out.</summary>
[Collection(ClosedProductionWindowCollection.Name)]
public sealed class SecretsProductionWindowTests(ClosedProductionWindowFixture fixture)
{
    [Fact]
    public async Task The_list_and_the_search_leave_out_a_production_secret_while_the_window_is_closed()
    {
        var vault = fixture.Vault;
        const string project = "IT-SECRETS-WINDOW";
        await OverviewSecrets.CreateAsync(vault, "it-secrets-window-production", project, environment: "Production");
        await OverviewSecrets.CreateAsync(vault, "it-secrets-window-development", project);
        var member = await vault.SignInNewAccountAsync("it-secrets-window", IPAddress.Parse("10.164.0.8"), project: project);

        var list = await member.Client.GetFromJsonAsync<SecretsResponse>("/api/ui/secrets");
        var search = await member.Client.GetFromJsonAsync<SecretSearchResultResponse[]>("/api/ui/secrets/search?q=it-secrets-window");

        Assert.Equal(["it-secrets-window-development"], list!.Secrets.Select(secret => secret.Name));
        Assert.Equal(["it-secrets-window-development"], search!.Select(secret => secret.Name));
    }
}
