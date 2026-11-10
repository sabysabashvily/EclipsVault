using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.KeyManagement;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Encryption keys over HTTP, on the vault that loads a retired master key: the UI API's two endpoints
/// and the page's routes. Each test starts by rotating, so the only pending rows are its own.
/// </summary>
[Collection(KekRotationCollection.Name)]
public sealed class EncryptionApiTests(KekRotationFixture fixture)
{
    private const string StatusPath = "/api/ui/encryption";
    private const string RotatePath = StatusPath + "/rotate";

    private readonly VaultFixture _vault = fixture.Vault;

    private string RetiredKekId => SecretsUnderMasterKey.IdOf(fixture.RetiredMasterKey);

    private string CurrentKekId => _vault.Services.GetRequiredService<IKekProvider>().CurrentKekId;

    [Theory]
    [InlineData("GET", StatusPath, "198.18.116.11")]
    [InlineData("POST", RotatePath, "198.18.116.12")]
    public async Task Each_endpoint_accepts_only_an_administrator_session(string method, string path, string source)
    {
        await RotateAsync();
        var pending = await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "refused-" + source.Split('.').Last(), "value");
        var anonymous = _vault.CreateClient();
        var reader = await _vault.SignInNewAccountAsync("it-enc-reader-" + source.Split('.').Last(), IPAddress.Parse(source));
        var key = _vault.CreateClient();
        key.DefaultRequestHeaders.Authorization = new("Bearer", await _vault.IssueApiKeyAsync(await _vault.AdminClientAsync(), "it-enc-key-" + source.Split('.').Last()));

        await UiApiFailureContractTests.AssertProblemAsync(await anonymous.SendAsync(new(new(method), path)), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await reader.Client.SendAsync(new(new(method), path)), 403, "forbidden");
        await UiApiFailureContractTests.AssertProblemAsync(await key.SendAsync(new(new(method), path)), 401, "unauthenticated");
        Assert.Equal(RetiredKekId, await KekIdOfSecretAsync(pending));
    }

    [Fact]
    public async Task Rotating_without_an_antiforgery_token_is_refused_and_moves_nothing()
    {
        await RotateAsync();
        var pending = await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "unverified-request", "value");
        var admin = await _vault.AdminClientAsync();

        await UiApiFailureContractTests.AssertProblemAsync(await admin.PostAsync(RotatePath, null), 400, "antiforgery_rejected");

        Assert.Equal(RetiredKekId, await KekIdOfSecretAsync(pending));
    }

    [Fact]
    public async Task The_status_lists_each_key_and_counts_movable_rows_apart_from_rows_on_keys_that_are_not_loaded()
    {
        await RotateAsync();
        var missingMasterKey = SecretsUnderMasterKey.NewMasterKey();
        var stranded = await SecretsUnderMasterKey.CreateAsync(_vault, missingMasterKey, "status-under-missing-key", "value");
        try
        {
            var replaced = await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "status-replaced", "first value");
            await SecretsUnderMasterKey.ReplaceValueAsync(_vault, fixture.RetiredMasterKey, replaced, "second value");
            await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "status-pending", "value");

            var response = await (await _vault.AdminClientAsync()).GetAsync(StatusPath);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["awaitingRotation", "currentKekId", "keys", "loadedKekIds", "onMissingKeys"], ProfileApi.NamesOf(body));
            Assert.Equal(CurrentKekId, body.GetProperty("currentKekId").GetString());
            Assert.Equal([CurrentKekId, RetiredKekId], body.GetProperty("loadedKekIds").EnumerateArray().Select(id => id.GetString()!).ToArray());
            Assert.Equal(3, body.GetProperty("awaitingRotation").GetInt32());
            Assert.Equal(1, body.GetProperty("onMissingKeys").GetInt32());
            var keys = body.GetProperty("keys").EnumerateArray().ToArray();
            Assert.Equal(["kekId", "secrets", "status", "versions"], ProfileApi.NamesOf(keys[0]));
            Assert.Equal(
                [(CurrentKekId, "current"), (RetiredKekId, "retired"), (SecretsUnderMasterKey.IdOf(missingMasterKey), "not_loaded")],
                keys.Select(key => (key.GetProperty("kekId").GetString()!, key.GetProperty("status").GetString()!)));
            Assert.Equal((2, 1), (keys[1].GetProperty("secrets").GetInt32(), keys[1].GetProperty("versions").GetInt32()));
            Assert.Equal((1, 0), (keys[2].GetProperty("secrets").GetInt32(), keys[2].GetProperty("versions").GetInt32()));
        }
        finally
        {
            await DeleteAsync(stranded);
        }
    }

    [Fact]
    public async Task The_status_names_each_key_by_its_fingerprint_and_never_answers_a_master_key()
    {
        var response = await (await _vault.AdminClientAsync()).GetAsync(StatusPath);

        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(fixture.RetiredMasterKey, text);
        var keys = JsonDocument.Parse(text).RootElement.GetProperty("keys").EnumerateArray().ToArray();
        Assert.Contains(keys, key => key.GetProperty("kekId").GetString() == RetiredKekId);
        Assert.All(keys, key => Assert.Matches("^kek-[0-9a-f]{8}$", key.GetProperty("kekId").GetString()));
    }

    [Fact]
    public async Task Rotating_moves_the_pending_rows_and_answers_their_counts_and_the_rows_left_on_missing_keys()
    {
        await RotateAsync();
        var missingMasterKey = SecretsUnderMasterKey.NewMasterKey();
        var stranded = await SecretsUnderMasterKey.CreateAsync(_vault, missingMasterKey, "rotate-under-missing-key", "value");
        try
        {
            var replaced = await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "rotated-from-the-ui-api", "first value");
            await SecretsUnderMasterKey.ReplaceValueAsync(_vault, fixture.RetiredMasterKey, replaced, "second value");
            var admin = await _vault.AdminClientAsync();
            var before = await AuditTrail.HeadAsync(_vault);

            var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), HttpMethod.Post, RotatePath);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(["currentKekId", "onMissingKeys", "secretsRewrapped", "versionsRewrapped"], ProfileApi.NamesOf(body));
            Assert.Equal(CurrentKekId, body.GetProperty("currentKekId").GetString());
            Assert.Equal(1, body.GetProperty("secretsRewrapped").GetInt32());
            Assert.Equal(1, body.GetProperty("versionsRewrapped").GetInt32());
            Assert.Equal(1, body.GetProperty("onMissingKeys").GetInt32());
            Assert.Equal(CurrentKekId, await KekIdOfSecretAsync(replaced));
            var rotations = (await AuditTrail.RowsAfterAsync(_vault, before)).Where(row => row.Action == AuditAction.KekRotated).ToArray();
            Assert.Equal([VaultFixture.AdminUsername, VaultFixture.AdminUsername], rotations.Select(row => row.Username));
            var status = await admin.GetFromJsonAsync<JsonElement>(StatusPath);
            Assert.Equal(0, status.GetProperty("awaitingRotation").GetInt32());
            Assert.Equal(1, status.GetProperty("onMissingKeys").GetInt32());
        }
        finally
        {
            await DeleteAsync(stranded);
        }
    }

    [Fact]
    public async Task A_rotation_asked_for_while_another_runs_answers_rotation_running_and_moves_nothing()
    {
        await RotateAsync();
        await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "rotated-by-the-first-pass", "value");
        var admin = await _vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);
        var before = await AuditTrail.HeadAsync(_vault);

        Task<KekRotationResult> first;
        HttpResponseMessage second;
        await using (var held = await HeldAuditChain.TakeAsync(_vault))
        {
            first = RotateAsync();
            await held.WaitForQueuedWritersAsync(1);

            second = await UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, RotatePath);
        }

        await UiApiFailureContractTests.AssertProblemAsync(second, 409, "rotation_running");
        Assert.Equal("Another rotation is already running. Try again when it finishes.",
            (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(new KekRotationResult(CurrentKekId, 1, 0, 0), await first);
        Assert.Single(await AuditTrail.RowsAfterAsync(_vault, before), row => row.Action == AuditAction.KekRotated);
    }

    [Theory]
    [InlineData("/Encryption", "198.18.116.21")]
    [InlineData("/Encryption/Index", "198.18.116.22")]
    [InlineData("/eNcRyPtIoN/iNdEx", "198.18.116.23")]
    public async Task Each_bookmark_keeps_its_return_url_and_serves_Angular_only_to_administrators(string path, string source)
    {
        var anonymous = await _vault.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal(path, System.Web.HttpUtility.ParseQueryString(anonymous.Headers.Location!.Query)["ReturnUrl"]);
        var reader = await _vault.SignInNewAccountAsync("it-enc-page-" + source.Split('.').Last(), IPAddress.Parse(source));
        var denied = await reader.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/Denied", denied.Headers.Location?.AbsolutePath);
        var document = await (await _vault.AdminClientAsync()).GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.True(document.Headers.CacheControl?.NoStore);
        Assert.Contains("<app-root>", await document.Content.ReadAsStringAsync());
        Assert.Contains("require-trusted-types-for 'script'", Assert.Single(document.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task The_Legacy_sidebar_link_keeps_its_destination()
    {
        var html = await (await _vault.AdminClientAsync()).GetStringAsync("/Secrets");
        var link = Assert.Single(Regex.Matches(html, "<a\\b[^>]*>[\\s\\S]*?</a>")
            .Select(match => match.Value), anchor => Regex.Replace(anchor, "<[^>]*>", "").Trim() == "Encryption keys");

        Assert.Contains("href=\"/Encryption\"", link);
    }

    [Theory]
    [InlineData("POST", "/Encryption")]
    [InlineData("PUT", "/eNcRyPtIoN/")]
    [InlineData("POST", "/Encryption/Index")]
    [InlineData("DELETE", "/ENCRYPTION/INDEX")]
    public async Task The_page_paths_refuse_every_method_but_GET_and_HEAD(string method, string path)
    {
        var response = await SendAsAdministratorAsync(method, path);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/Encryption/Rotate")]
    [InlineData("POST", "/eNcRyPtIoN/rOtAtE")]
    [InlineData("GET", "/Encryption/Rotate")]
    [InlineData("GET", "/Encryption/Index/11111111-1111-1111-1111-111111111111")]
    public async Task The_Legacy_rotate_action_and_deeper_paths_are_not_found(string method, string path)
    {
        var response = await SendAsAdministratorAsync(method, path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsAdministratorAsync(string method, string path)
    {
        var admin = await _vault.AdminClientAsync();
        return await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), new HttpMethod(method), path);
    }

    private async Task<KekRotationResult> RotateAsync()
    {
        using var scope = _vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IKekRotationService>().RotateAsync(default);
    }

    private async Task DeleteAsync(Guid secretId)
    {
        using var scope = _vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISecretService>().DeleteAsync(secretId, default);
    }

    private async Task<string> KekIdOfSecretAsync(Guid secretId)
    {
        using var scope = _vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets
            .Where(secret => secret.Id == secretId).Select(secret => secret.KekId).SingleAsync();
    }
}
