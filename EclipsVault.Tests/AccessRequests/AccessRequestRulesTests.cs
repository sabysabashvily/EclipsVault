using EclipsVault.Core.Application.Abac;
using EclipsVault.Core.Application.AccessRequests;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.AccessRequests;

/// <summary>
/// Whether a share could help an account open a secret in project PAYMENTS. The rule has no network or
/// time input, so the Production and Confidential rows, which an untrusted address or a closed window
/// would deny, are decided exactly like the others.
/// </summary>
public sealed class AccessRequestRulesTests
{
    [Theory]
    [InlineData(ClearanceLevel.Standard, "TEAM-B", SensitivityLevel.Internal, SecretEnvironment.Development, false, null)]
    [InlineData(ClearanceLevel.Elevated, "TEAM-B", SensitivityLevel.Confidential, SecretEnvironment.Production, false, null)]
    [InlineData(ClearanceLevel.Secret, "TEAM-B", SensitivityLevel.Secret, SecretEnvironment.Production, false, null)]
    [InlineData(ClearanceLevel.Standard, "TEAM-B", SensitivityLevel.Confidential, SecretEnvironment.Development, false, AccessRequestRefusal.ShareCannotHelp)]
    [InlineData(ClearanceLevel.Secret, "TEAM-B", SensitivityLevel.TopSecret, SecretEnvironment.Development, false, AccessRequestRefusal.ShareCannotHelp)]
    [InlineData(ClearanceLevel.Standard, "TEAM-B", SensitivityLevel.Confidential, SecretEnvironment.Development, true, AccessRequestRefusal.ShareCannotHelp)]
    [InlineData(ClearanceLevel.Standard, "PAYMENTS", SensitivityLevel.Confidential, SecretEnvironment.Development, false, AccessRequestRefusal.ShareCannotHelp)]
    [InlineData(ClearanceLevel.Elevated, "PAYMENTS", SensitivityLevel.Confidential, SecretEnvironment.Production, false, AccessRequestRefusal.AlreadyAbleToOpen)]
    [InlineData(ClearanceLevel.Elevated, "payments", SensitivityLevel.Internal, SecretEnvironment.Development, false, AccessRequestRefusal.AlreadyAbleToOpen)]
    [InlineData(ClearanceLevel.TopSecret, "TEAM-B", SensitivityLevel.TopSecret, SecretEnvironment.Production, false, AccessRequestRefusal.AlreadyAbleToOpen)]
    [InlineData(ClearanceLevel.TopSecret, "TEAM-B", SensitivityLevel.Internal, SecretEnvironment.Development, false, AccessRequestRefusal.AlreadyAbleToOpen)]
    [InlineData(ClearanceLevel.Elevated, "TEAM-B", SensitivityLevel.Confidential, SecretEnvironment.Production, true, AccessRequestRefusal.AlreadyAbleToOpen)]
    public void A_share_must_be_able_to_help_and_must_not_be_needed_already(
        ClearanceLevel clearance, string project, SensitivityLevel sensitivity, SecretEnvironment environment,
        bool holdsActiveShare, AccessRequestRefusal? expected)
    {
        var refusal = AccessRequestRules.ShareRefusal(
            new SubjectAttributes(clearance, project), new ResourceAttributes(environment, sensitivity, "PAYMENTS"), holdsActiveShare);

        Assert.Equal(expected, refusal);
    }
}
