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
        var details = await SecretSharing.CreateSecretAsync(admin, $"it-change-shared-{change}", value: "it-change-first-value");
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateAsync(admin, details, "it-change-stored-value"));
        await SecretDetailsApi.MakeManagedAsync(vault, details, "it_change_no_target");
        var recipient = await vault.SignInNewAccountAsync($"it-change-recipient-{change}", IPAddress.Parse(address), project: "IT-CHANGE-ELSEWHERE");
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.ShareAsync(admin, details, $"it-change-recipient-{change}"));
        var before = await AuditTrail.HeadAsync(vault);

        var response = await PressAsync(change, recipient.Client, details);

        var problem = await SecretsApi.AssertProblemAsync(response, 403, "not_permitted");
        Assert.Equal("Only an administrator or a member of this secret's project can change it.", problem.GetProperty("title").GetString());
        var denial = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.ResourceId == SecretSharing.SecretIdOf(details));
        Assert.Equal((AuditAction.SecretAccessDenied, $"it-change-recipient-{change}", false), (denial.Action, denial.Username, denial.IsCritical));
        Assert.Contains(NotPermittedReason, denial.Details);
        Assert.Equal("it-change-stored-value", await UpstreamRotationTests.StoredValueAsync(vault, details));
        Assert.Equal(1, await SecretDetailsApi.VersionCountAsync(vault, details));
    }

    [Theory]
    [InlineData("member", "rotate", "10.168.0.11", "it-change-new-value")]
    [InlineData("member", "restore", "10.168.0.12", "it-change-first-value")]
    [InlineData("administrator", "rotate", null, "it-change-new-value")]
    [InlineData("administrator", "restore", null, "it-change-first-value")]
    public async Task A_project_members_and_an_administrators_new_value_is_stored(string role, string change, string? address, string storedAfter)
    {
        var (changer, details) = await SecretWithAVersionAsync($"it-change-{role}-{change}", address);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await PressAsync(change, changer, details);

        SecretDetailsApi.AssertNoContent(response);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.SecretAccessDenied);
        Assert.Equal(storedAfter, await UpstreamRotationTests.StoredValueAsync(vault, details));
    }

    [Theory]
    [InlineData("member", "10.168.0.13")]
    [InlineData("administrator", null)]
    public async Task A_project_members_and_an_administrators_delete_removes_the_secret(string role, string? address)
    {
        var (changer, details) = await SecretWithAVersionAsync($"it-change-{role}-delete", address);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretDetailsApi.DeleteAsync(changer, details);

        SecretDetailsApi.AssertNoContent(response);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.SecretAccessDenied);
        Assert.False(await SecretDetailsApi.ExistsAsync(vault, details));
    }

    /// <summary>
    /// A GLOBAL secret with one archived version, and the client that changes it: the administrator's when
    /// <paramref name="address"/> is null, otherwise a new GLOBAL member's signed in from that address.
    /// </summary>
    private async Task<(HttpClient Changer, string Details)> SecretWithAVersionAsync(string name, string? address)
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, name, value: "it-change-first-value");
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateAsync(admin, details, "it-change-stored-value"));
        var changer = address is null ? admin : (await vault.SignInNewAccountAsync(name, IPAddress.Parse(address))).Client;
        return (changer, details);
    }

    private async Task<HttpResponseMessage> PressAsync(string change, HttpClient client, string details) => change switch
    {
        "rotate" => await SecretDetailsApi.RotateAsync(client, details, "it-change-new-value"),
        "rotate-upstream" => await SecretDetailsApi.RotateUpstreamAsync(client, details),
        "restore" => await SecretDetailsApi.RestoreAsync(client, details, await SecretDetailsApi.NewestVersionAsync(vault, details)),
        "delete" => await SecretDetailsApi.DeleteAsync(client, details),
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };
}
