using EclipsVault.Core.Application.Abac;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Abac;

/// <summary>
/// The ABAC decision matrix. This is the single most security-critical pure function in the
/// system — the same engine gates both interactive users and API keys — so every rule and its
/// two documented exceptions (TopSecret bypass, explicit grant) are pinned here.
/// </summary>
public class SecretAccessPolicyTests
{
    private static RequestContext Context(bool window = true, bool trusted = true, bool granted = false) =>
        new(DateTimeOffset.UnixEpoch, "10.0.0.1", window, trusted, granted);

    private static ResourceAttributes Resource(
        SecretEnvironment env = SecretEnvironment.Development,
        SensitivityLevel sensitivity = SensitivityLevel.Internal,
        string project = "PHOENIX") => new(env, sensitivity, project);

    [Fact]
    public void Allows_when_every_rule_is_satisfied()
    {
        var subject = new SubjectAttributes(ClearanceLevel.Secret, "PHOENIX");
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(), Context());
        Assert.True(decision.IsAllowed);
        Assert.Empty(decision.DenialReasons);
    }

    [Fact]
    public void Denies_when_clearance_is_below_sensitivity()
    {
        var subject = new SubjectAttributes(ClearanceLevel.Standard, "PHOENIX");
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(sensitivity: SensitivityLevel.TopSecret), Context());
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void Denies_on_project_mismatch_without_grant_or_topsecret()
    {
        var subject = new SubjectAttributes(ClearanceLevel.Secret, "ORION");
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(project: "PHOENIX"), Context());
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void TopSecret_clearance_crosses_the_project_boundary()
    {
        var subject = new SubjectAttributes(ClearanceLevel.TopSecret, "ORION");
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(project: "PHOENIX"), Context());
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Explicit_grant_crosses_the_project_boundary()
    {
        var subject = new SubjectAttributes(ClearanceLevel.Secret, "ORION");
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(project: "PHOENIX"), Context(granted: true));
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Grant_does_not_waive_the_clearance_rule()
    {
        // A cross-project grant lifts only the project rule — clearance must still dominate.
        var subject = new SubjectAttributes(ClearanceLevel.Standard, "ORION");
        var decision = SecretAccessPolicy.Evaluate(
            subject, Resource(sensitivity: SensitivityLevel.Secret, project: "PHOENIX"), Context(granted: true));
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void A_grant_can_only_widen_access_never_narrow_it()
    {
        // Monotonicity of the grant, pinned across the whole attribute matrix. The enumeration
        // fast-path in SecretAccessHandler leans on exactly this: when a row already resolves to
        // "allow" without a grant, it skips the per-secret grant database lookup — because a grant
        // can only ever *lift* the project rule, never introduce a denial. Were that false, skipping
        // the lookup would hand back a secret a grant was supposed to keep hidden. So: for every
        // combination where the ungranted decision allows, the granted decision must allow too.
        foreach (var clearance in Enum.GetValues<ClearanceLevel>())
            foreach (var sensitivity in Enum.GetValues<SensitivityLevel>())
                foreach (var env in Enum.GetValues<SecretEnvironment>())
                    foreach (var window in new[] { true, false })
                        foreach (var trusted in new[] { true, false })
                            foreach (var sameProject in new[] { true, false })
                            {
                                var subject = new SubjectAttributes(clearance, "PHOENIX");
                                var resource = Resource(env: env, sensitivity: sensitivity, project: sameProject ? "PHOENIX" : "ORION");

                                if (!SecretAccessPolicy.Evaluate(subject, resource, Context(window, trusted, granted: false)).IsAllowed)
                                {
                                    continue;
                                }

                                var granted = SecretAccessPolicy.Evaluate(subject, resource, Context(window, trusted, granted: true));
                                Assert.True(granted.IsAllowed,
                                    $"a grant flipped an allow to a deny at clearance={clearance}, sensitivity={sensitivity}, " +
                                    $"env={env}, window={window}, trusted={trusted}, sameProject={sameProject}");
                            }
    }

    [Fact]
    public void Denies_production_access_outside_the_time_window()
    {
        var subject = new SubjectAttributes(ClearanceLevel.Secret, "PHOENIX");
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(env: SecretEnvironment.Production), Context(window: false));
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void Denies_confidential_or_higher_from_an_untrusted_network()
    {
        var subject = new SubjectAttributes(ClearanceLevel.Secret, "PHOENIX");
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(sensitivity: SensitivityLevel.Confidential), Context(trusted: false));
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void TopSecret_clearance_is_still_bound_by_the_production_window()
    {
        // The point that surprises admins: clearance dominates *sensitivity*, but the environmental
        // rules (time window, network trust) apply to everyone — TopSecret is not a super-bypass.
        var subject = new SubjectAttributes(ClearanceLevel.TopSecret, "GLOBAL");
        var decision = SecretAccessPolicy.Evaluate(
            subject,
            Resource(env: SecretEnvironment.Production, sensitivity: SensitivityLevel.TopSecret, project: "GLOBAL"),
            Context(window: false));
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void TopSecret_clearance_is_still_bound_by_the_trusted_network_rule()
    {
        var subject = new SubjectAttributes(ClearanceLevel.TopSecret, "GLOBAL");
        var decision = SecretAccessPolicy.Evaluate(
            subject,
            Resource(sensitivity: SensitivityLevel.TopSecret, project: "GLOBAL"),
            Context(trusted: false));
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void Metadata_only_key_cannot_read_a_value_even_when_otherwise_allowed()
    {
        var subject = new SubjectAttributes(ClearanceLevel.Secret, "PHOENIX");
        var scope = new ApiKeyScope(ProjectScope: null, MetadataOnly: true);
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(), Context(), scope);
        Assert.False(decision.IsAllowed);
    }

    // ---- enumeration runs the same rules ------------------------------------------------

    [Fact]
    public void Metadata_only_key_may_still_enumerate()
    {
        // The one rule enumeration drops, because dropping it is what "metadata-only" means.
        var subject = new SubjectAttributes(ClearanceLevel.Secret, "PHOENIX");
        var scope = new ApiKeyScope(ProjectScope: null, MetadataOnly: true);
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(), Context(), scope, AccessKind.Enumerate);
        Assert.True(decision.IsAllowed);
    }

    [Theory]
    [InlineData(AccessKind.Enumerate)]
    [InlineData(AccessKind.Read)]
    public void Clearance_below_sensitivity_hides_the_secret_as_firmly_as_it_blocks_the_read(AccessKind kind)
    {
        // Knowing 'Production_AWS_Root_Key' exists is worth having; the name says what it is worth.
        var subject = new SubjectAttributes(ClearanceLevel.Standard, "PHOENIX");
        var decision = SecretAccessPolicy.Evaluate(
            subject, Resource(sensitivity: SensitivityLevel.TopSecret), Context(), scope: null, kind);
        Assert.False(decision.IsAllowed);
    }

    [Theory]
    [InlineData(AccessKind.Enumerate)]
    [InlineData(AccessKind.Read)]
    public void Another_projects_secret_is_not_enumerable_either(AccessKind kind)
    {
        var subject = new SubjectAttributes(ClearanceLevel.Secret, "ORION");
        var decision = SecretAccessPolicy.Evaluate(
            subject, Resource(project: "PHOENIX"), Context(), scope: null, kind);
        Assert.False(decision.IsAllowed);
    }

    [Theory]
    [InlineData(AccessKind.Enumerate)]
    [InlineData(AccessKind.Read)]
    public void A_project_scoped_key_enumerates_only_its_own_project(AccessKind kind)
    {
        var subject = new SubjectAttributes(ClearanceLevel.TopSecret, "GLOBAL");
        var scope = new ApiKeyScope(ProjectScope: "ORION", MetadataOnly: false);
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(project: "PHOENIX"), Context(), scope, kind);
        Assert.False(decision.IsAllowed);
    }

    [Theory]
    [InlineData(AccessKind.Enumerate)]
    [InlineData(AccessKind.Read)]
    public void An_untrusted_network_hides_confidential_secrets_rather_than_only_blocking_them(AccessKind kind)
    {
        var subject = new SubjectAttributes(ClearanceLevel.Secret, "PHOENIX");
        var decision = SecretAccessPolicy.Evaluate(
            subject, Resource(sensitivity: SensitivityLevel.Confidential), Context(trusted: false), scope: null, kind);
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void Read_is_the_default_so_a_caller_that_forgets_to_say_gets_the_strict_answer()
    {
        var subject = new SubjectAttributes(ClearanceLevel.Secret, "PHOENIX");
        var scope = new ApiKeyScope(ProjectScope: null, MetadataOnly: true);
        Assert.False(SecretAccessPolicy.Evaluate(subject, Resource(), Context(), scope).IsAllowed);
    }

    [Fact]
    public void Project_scoped_key_is_denied_outside_its_project_even_for_topsecret()
    {
        var subject = new SubjectAttributes(ClearanceLevel.TopSecret, "GLOBAL");
        var scope = new ApiKeyScope(ProjectScope: "ORION", MetadataOnly: false);
        var decision = SecretAccessPolicy.Evaluate(subject, Resource(project: "PHOENIX"), Context(), scope);
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void A_denial_reports_every_failing_rule_at_once()
    {
        // Low clearance + wrong project + out of window + untrusted network → four reasons.
        var subject = new SubjectAttributes(ClearanceLevel.Standard, "ORION");
        var decision = SecretAccessPolicy.Evaluate(
            subject,
            Resource(env: SecretEnvironment.Production, sensitivity: SensitivityLevel.TopSecret, project: "PHOENIX"),
            Context(window: false, trusted: false));
        Assert.False(decision.IsAllowed);
        Assert.Equal(4, decision.DenialReasons.Count);
    }
}
