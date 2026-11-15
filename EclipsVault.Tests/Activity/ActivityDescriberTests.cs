using EclipsVault.Core.Application.Activity;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Activity;

/// <summary>
/// The pure audit-action → activity-entry mapping that drives the personal feed. Every action
/// must produce a readable title (no raw enum names leak to the user), and the security-sensitive
/// ones must carry the right category and severity so they stand out.
/// </summary>
public class ActivityDescriberTests
{
    public static IEnumerable<object[]> AllActions()
        => Enum.GetValues<AuditAction>().Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(AllActions))]
    public void Every_action_has_a_non_empty_title(AuditAction action)
    {
        var description = ActivityDescriber.Describe(action);
        Assert.False(string.IsNullOrWhiteSpace(description.Title));
    }

    [Theory]
    [InlineData(AuditAction.LoginSucceeded, ActivityCategory.Authentication, ActivitySeverity.Routine)]
    [InlineData(AuditAction.PasskeyLogin, ActivityCategory.Authentication, ActivitySeverity.Routine)]
    [InlineData(AuditAction.SecretRevealed, ActivityCategory.Secrets, ActivitySeverity.Notable)]
    [InlineData(AuditAction.SecretShared, ActivityCategory.Sharing, ActivitySeverity.Notable)]
    [InlineData(AuditAction.PasswordChanged, ActivityCategory.Account, ActivitySeverity.Notable)]
    [InlineData(AuditAction.HoneyTokenTripped, ActivityCategory.Security, ActivitySeverity.Critical)]
    [InlineData(AuditAction.AccountLockedOut, ActivityCategory.Security, ActivitySeverity.Critical)]
    [InlineData(AuditAction.KekRotated, ActivityCategory.Administration, ActivitySeverity.Critical)]
    public void Actions_map_to_the_expected_category_and_severity(
        AuditAction action, ActivityCategory category, ActivitySeverity severity)
    {
        var description = ActivityDescriber.Describe(action);
        Assert.Equal(category, description.Category);
        Assert.Equal(severity, description.Severity);
    }

    [Theory]
    [InlineData(AuditAction.AuditLogViewed, "Viewed the audit log")]
    [InlineData(AuditAction.AuditChainVerified, "Verified the audit chain")]
    public void Access_to_the_audit_trail_is_routine_administration(AuditAction action, string title)
    {
        Assert.Equal(
            new ActivityDescription(ActivityCategory.Administration, title, ActivitySeverity.Routine),
            ActivityDescriber.Describe(action));
    }

    [Fact]
    public void Every_critical_entry_is_a_security_or_admin_concern()
    {
        // A "critical" entry is the loudest signal in the feed; it should only ever come from a
        // security event or a high-impact administrative one — never an everyday action.
        foreach (var action in Enum.GetValues<AuditAction>())
        {
            var d = ActivityDescriber.Describe(action);
            if (d.Severity == ActivitySeverity.Critical)
            {
                Assert.Contains(d.Category, new[] { ActivityCategory.Security, ActivityCategory.Administration });
            }
        }
    }

    [Fact]
    public void An_unmapped_action_degrades_to_a_humanised_other_entry()
    {
        // A value outside the defined enum (a future action not yet mapped) must not throw or
        // surface a bare "12345" — it falls back to the Other bucket with a readable title.
        var unmapped = (AuditAction)99999;
        var description = ActivityDescriber.Describe(unmapped);

        Assert.Equal(ActivityCategory.Other, description.Category);
        Assert.Equal(ActivitySeverity.Routine, description.Severity);
        Assert.False(string.IsNullOrWhiteSpace(description.Title));
    }

    [Fact]
    public void Describes_the_unlicensed_premium_feature_action()
    {
        var description = ActivityDescriber.Describe(AuditAction.LicenseFeatureUnlicensed);

        Assert.Equal(
            new ActivityDescription(
                ActivityCategory.Administration,
                "Used a premium feature without a license",
                ActivitySeverity.Notable),
            description);
    }
}
