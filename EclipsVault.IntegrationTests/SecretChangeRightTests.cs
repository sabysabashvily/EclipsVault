using System.Net;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Rotating, restoring and deleting a secret need the change right: an administrator, or a member of the
/// secret's project who passes the access check. A share lets its recipient open the secret and nothing
/// more. A trusted address is in 10.0.0.0/8, and each account signs in from its own address.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SecretChangeRightTests(VaultFixture vault)
{
    private const string NotPermittedReason = "A share grants read access only.";

    [Theory]
    [InlineData("rotate", "10.168.0.1")]
    [InlineData("rotate-upstream", "10.168.0.2")]
    [InlineData("restore", "10.168.0.3")]
    [InlineData("delete", "10.168.0.4")]
    public async Task A_share_recipients_change_is_refused_recorded_and_changes_nothing(string change, string address)
    {
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, $"it-change-shared-{change}", value: "it-change-first-value");
        var adminToken = await LegacySecretActions.FormTokenAsync(admin, details);
        Assert.Equal(HttpStatusCode.Redirect, (await LegacySecretActions.RotateAsync(admin, details, adminToken, "it-change-stored-value")).StatusCode);
        await LegacySecretActions.MakeManagedAsync(vault, details, "it_change_no_target");
        var recipient = await vault.SignInNewAccountAsync($"it-change-recipient-{change}", IPAddress.Parse(address), project: "IT-CHANGE-ELSEWHERE");
        Assert.Equal(HttpStatusCode.Redirect, (await LegacySharing.ShareAsync(admin, details, $"it-change-recipient-{change}")).StatusCode);
        var token = await LegacySecretActions.FormTokenAsync(recipient.Client, details);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await PressAsync(change, recipient.Client, details, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Denied", response.Headers.Location?.AbsolutePath);
        var denial = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.ResourceId == LegacySharing.SecretIdOf(details));
        Assert.Equal((AuditAction.SecretAccessDenied, $"it-change-recipient-{change}", false), (denial.Action, denial.Username, denial.IsCritical));
        Assert.Contains(NotPermittedReason, denial.Details);
        Assert.Equal("it-change-stored-value", await UpstreamRotationTests.StoredValueAsync(vault, details));
        Assert.Equal(1, await LegacySecretActions.VersionCountAsync(vault, details));
    }

    [Theory]
    [InlineData("member", "rotate", "10.168.0.11", "it-change-new-value")]
    [InlineData("member", "restore", "10.168.0.12", "it-change-first-value")]
    [InlineData("administrator", "rotate", null, "it-change-new-value")]
    [InlineData("administrator", "restore", null, "it-change-first-value")]
    public async Task A_project_members_and_an_administrators_new_value_is_stored(string role, string change, string? address, string storedAfter)
    {
        var (changer, details, token) = await SecretWithAVersionAsync($"it-change-{role}-{change}", address);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await PressAsync(change, changer, details, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.SecretAccessDenied);
        Assert.Equal(storedAfter, await UpstreamRotationTests.StoredValueAsync(vault, details));
    }

    [Theory]
    [InlineData("member", "10.168.0.13")]
    [InlineData("administrator", null)]
    public async Task A_project_members_and_an_administrators_delete_removes_the_secret(string role, string? address)
    {
        var (changer, details, token) = await SecretWithAVersionAsync($"it-change-{role}-delete", address);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacySecretActions.DeleteAsync(changer, details, token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.SecretAccessDenied);
        Assert.False(await LegacySecretActions.ExistsAsync(vault, details));
    }

    /// <summary>
    /// A GLOBAL secret with one archived version, and the client that changes it: the administrator's when
    /// <paramref name="address"/> is null, otherwise a new GLOBAL member's signed in from that address.
    /// </summary>
    private async Task<(HttpClient Changer, string Details, string Token)> SecretWithAVersionAsync(string name, string? address)
    {
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, name, value: "it-change-first-value");
        var adminToken = await LegacySecretActions.FormTokenAsync(admin, details);
        Assert.Equal(HttpStatusCode.Redirect, (await LegacySecretActions.RotateAsync(admin, details, adminToken, "it-change-stored-value")).StatusCode);
        var changer = address is null ? admin : (await vault.SignInNewAccountAsync(name, IPAddress.Parse(address))).Client;
        return (changer, details, await LegacySecretActions.FormTokenAsync(changer, details));
    }

    private async Task<HttpResponseMessage> PressAsync(string change, HttpClient client, string details, string token) => change switch
    {
        "rotate" => await LegacySecretActions.RotateAsync(client, details, token, "it-change-new-value"),
        "rotate-upstream" => await LegacySecretActions.RotateUpstreamAsync(client, details, token),
        "restore" => await LegacySecretActions.RestoreAsync(client, details, token, await LegacySecretActions.NewestVersionAsync(vault, details)),
        "delete" => await LegacySecretActions.DeleteAsync(client, details, token),
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };
}
