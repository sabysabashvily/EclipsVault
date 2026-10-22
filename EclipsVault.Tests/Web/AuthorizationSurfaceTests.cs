using System.Reflection;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Controllers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace EclipsVault.Tests.Web;

/// <summary>
/// Every endpoint in this vault is either behind a session or deliberately open, and which one it is
/// must never be an accident.
///
/// <para>
/// Before these tests, twelve of the twenty controllers — including the ones serving secrets, the
/// audit trail, and the personal-data export — carried no authorization attribute at all. They were
/// protected, but only by the default-deny fallback policy configured in <c>Program.cs</c>: three
/// lines, in a different file, that no controller referenced. Deleting or reordering them during a
/// refactor would have exposed all twelve to anonymous callers, and every test in this suite would
/// still have passed, because nothing anywhere asserted the relationship.
/// </para>
///
/// <para>
/// So the protection is now stated twice — <c>[Authorize]</c> on the controllers themselves, plus
/// the fallback policy as a backstop for anything new that forgets — and the anonymous surface is
/// pinned below by name. <b>A new <c>[AllowAnonymous]</c> fails this test until someone adds it to
/// the list</b>, which is the point: opening an endpoint to the internet should cost a deliberate
/// edit to a file that says so, not a one-line attribute nobody reviews.
/// </para>
/// </summary>
public class AuthorizationSurfaceTests
{
    /// <summary>
    /// The complete set of actions reachable without a session, as <c>Controller.Action</c>.
    /// Every one of them by definition runs before a session exists: the password sign-in, the
    /// passkey ceremony's two legs, the SSO redirect and its callback from the identity provider,
    /// break-glass recovery for a locked-out administrator, the landing page, and the error page.
    /// Adding a name here is a decision that an unauthenticated stranger may reach it.
    /// </summary>
    private static readonly string[] IntentionallyAnonymous =
    [
        "AccountController.ExternalCallback",
        "AccountController.ExternalLogin",
        "AccountController.Login",
        "AccountController.PasskeyLoginBegin",
        "AccountController.PasskeyLoginComplete",
        "AccountController.Recover",
        "HomeController.Error",
        "HomeController.Index",
    ];

    /// <summary>
    /// The only actions allowed to accept a principal that has passed the password factor but not a
    /// second one — the three second-factor screens themselves, each a GET/POST pair.
    /// </summary>
    private static readonly string[] MayAcceptPendingMfa =
    [
        "AccountController.EnrollTotp",
        "AccountController.RecoveryCode",
        "AccountController.Totp",
    ];

    [Fact]
    public void EveryAction_IsEitherBehindAuthorizeOrDeliberatelyAnonymous()
    {
        var unprotected = Actions()
            .Where(action => !IsAnonymous(action) && !IsAuthorized(action))
            .Select(Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(unprotected.Count == 0,
            "These actions are reachable without [Authorize] and without being declared anonymous. " +
            "They would be relying entirely on the fallback policy in Program.cs:\n  " +
            string.Join("\n  ", unprotected));
    }

    [Fact]
    public void AnonymousSurface_IsExactlyTheSetWeIntended()
    {
        var actual = Actions()
            .Where(IsAnonymous)
            .Select(Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var expected = IntentionallyAnonymous.OrderBy(name => name, StringComparer.Ordinal).ToList();

        var added = actual.Except(expected, StringComparer.Ordinal).ToList();
        var removed = expected.Except(actual, StringComparer.Ordinal).ToList();

        Assert.True(added.Count == 0,
            "These actions were opened to anonymous callers without being listed as intentional:\n  " +
            string.Join("\n  ", added) +
            "\n\nIf that is deliberate, add them to IntentionallyAnonymous and say why in review.");

        Assert.True(removed.Count == 0,
            "These actions are listed as intentionally anonymous but no longer are. Remove them from " +
            "IntentionallyAnonymous so the list keeps meaning something:\n  " + string.Join("\n  ", removed));
    }

    /// <summary>
    /// The tests above pin that every action carries <c>[Authorize]</c>, but not <em>which</em>
    /// authorization — and they pass just as happily for a weak one. Moving a protected action to
    /// <c>[Authorize(AuthenticationSchemes = AuthSchemes.MfaPending)]</c> would look like hardening in
    /// a diff while actually admitting a half-authenticated caller: someone who has typed a correct
    /// password and nothing else. That principal carries only a name and an id
    /// (<c>VaultClaimsFactory.CreatePendingMfaPrincipal</c>), so it holds no clearance and no project,
    /// and the six-minute pending cookie is a separate scheme from the session precisely so it cannot
    /// reach the vault. This pins the boundary by name: the second-factor screens may accept it, and
    /// nothing else may.
    /// </summary>
    [Fact]
    public void OnlyTheSecondFactorScreens_AcceptAHalfAuthenticatedPrincipal()
    {
        var actual = Actions()
            .Where(DeclaresPendingMfaScheme)
            .Select(Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var expected = MayAcceptPendingMfa.OrderBy(name => name, StringComparer.Ordinal).ToList();

        var added = actual.Except(expected, StringComparer.Ordinal).ToList();
        var removed = expected.Except(actual, StringComparer.Ordinal).ToList();

        Assert.True(added.Count == 0,
            "These actions accept the pre-MFA scheme, so a caller who has only typed a password can " +
            "reach them:\n  " + string.Join("\n  ", added) +
            "\n\nIf that is genuinely intended, add them to MayAcceptPendingMfa and justify it in review.");

        Assert.True(removed.Count == 0,
            "These are listed as accepting the pre-MFA scheme but no longer do. Remove them from " +
            "MayAcceptPendingMfa so the list keeps meaning something:\n  " + string.Join("\n  ", removed));
    }

    /// <summary>
    /// The backstop itself: <c>VaultController</c> carries the requirement that ten controllers
    /// inherit, so losing it would quietly unprotect all of them at once.
    /// </summary>
    [Fact]
    public void VaultControllerBase_CarriesAuthorize()
    {
        var baseController = ControllerTypes()
            .Single(type => type.Name == "VaultController");

        Assert.NotEmpty(baseController.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
    }

    /// <summary>
    /// The UI API and the Public API must never accept each other's credentials (ADR-0005). The UI
    /// API is driven by a session, so it gets the session's CSRF rules; the Public API is driven by an
    /// API key, with the key's scopes. An endpoint accepting both would apply each set of rules only
    /// to some of its callers. The scheme is named explicitly on every UI API controller, rather than
    /// left to the default, so that changing the default scheme in <c>Program.cs</c> cannot quietly
    /// let a key in.
    /// </summary>
    [Fact]
    public void UiApiControllers_AcceptOnlyTheSession_AndNoneOfTheirActionsIsAnonymous()
    {
        var uiApi = ControllersUnder("api/ui/");
        Assert.NotEmpty(uiApi);

        var wrongScheme = uiApi
            .Where(type => !DeclaresExactly(type, CookieAuthenticationDefaults.AuthenticationScheme))
            .Select(type => type.Name)
            .ToList();
        Assert.True(wrongScheme.Count == 0,
            "These UI API controllers do not declare the session scheme, and only it, on their [Authorize]:\n  " +
            string.Join("\n  ", wrongScheme));

        var anonymous = Actions()
            .Where(action => uiApi.Contains(action.DeclaringType!))
            .Where(action => IsAnonymous(action) || DeclaresAnyScheme(action))
            .Select(Name)
            .ToList();
        Assert.True(anonymous.Count == 0,
            "These UI API actions are anonymous or override the controller's scheme:\n  " +
            string.Join("\n  ", anonymous));
    }

    /// <summary>The other half of ADR-0005: the Public API takes an API key and never a session.</summary>
    [Fact]
    public void PublicApiControllers_AcceptOnlyAnApiKey()
    {
        var publicApi = ControllersUnder("api/v1/");
        Assert.NotEmpty(publicApi);

        var wrongScheme = publicApi
            .Where(type => !DeclaresExactly(type, AuthSchemes.ApiKey))
            .Select(type => type.Name)
            .ToList();
        Assert.True(wrongScheme.Count == 0,
            "These Public API controllers do not declare the API-key scheme, and only it, on their [Authorize]:\n  " +
            string.Join("\n  ", wrongScheme));

        // An action naming a scheme adds to the controller's, so a session on one action is enough.
        var overriding = Actions()
            .Where(action => publicApi.Contains(action.DeclaringType!))
            .Where(DeclaresAnyScheme)
            .Select(Name)
            .ToList();
        Assert.True(overriding.Count == 0,
            "These Public API actions add an authentication scheme to the controller's API key:\n  " +
            string.Join("\n  ", overriding));
    }

    /// <summary>
    /// <c>/api/ui/me</c> offers each Area by evaluating the policy <see cref="UiAreas"/> names for it
    /// (ADR-0012). That table restates what the Area's controller enforces, so this pins the two
    /// together: an Area must name a policy its controller carries, and the controller may carry no
    /// other policy than <c>Session</c>. Otherwise the sidebar could offer a page that refuses the
    /// user, or a controller could gain a stricter policy the Area list never hears about.
    /// </summary>
    [Fact]
    public void EveryUiArea_NamesThePolicyItsControllerEnforces()
    {
        var controllers = new Dictionary<string, Type>
        {
            ["overview"] = typeof(HomeController),
            ["secrets"] = typeof(SecretsController),
            ["dynamic-secrets"] = typeof(DynamicSecretsController),
            ["shared-with-me"] = typeof(SecretsController),
            ["shared-by-me"] = typeof(EclipsVault.Web.Controllers.Ui.SharedByMeController),
            ["access"] = typeof(EclipsVault.Web.Controllers.Ui.AccessController),
            ["access-requests"] = typeof(AccessRequestsController),
            ["security"] = typeof(EclipsVault.Web.Controllers.Ui.SecurityController),
            ["sign-ins"] = typeof(EclipsVault.Web.Controllers.Ui.SignInsController),
            ["sessions"] = typeof(EclipsVault.Web.Controllers.Ui.SessionsController),
            ["activity"] = typeof(EclipsVault.Web.Controllers.Ui.ActivityController),
            ["data"] = typeof(EclipsVault.Web.Controllers.Ui.DataController),
            ["profile"] = typeof(ProfileController),
            ["users"] = typeof(AdminController),
            ["networks"] = typeof(AdminController),
            ["service-accounts"] = typeof(ServiceAccountsController),
            ["audit"] = typeof(AuditController),
            ["notifications"] = typeof(NotificationsController),
            ["encryption"] = typeof(EncryptionController),
            ["license"] = typeof(LicenseController),
        };

        Assert.Equal(controllers.Keys.Order(StringComparer.Ordinal),
            UiAreas.All.Select(area => area.Key).Order(StringComparer.Ordinal));

        foreach (var (key, policy) in UiAreas.All)
        {
            var enforced = controllers[key].GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Select(attribute => attribute.Policy)
                .OfType<string>()
                .ToList();
            Assert.True(enforced.Contains(policy) && enforced.All(name => name == VaultPolicies.Session || name == policy),
                $"Area '{key}' is offered by '{policy}', but {controllers[key].Name} enforces: {string.Join(", ", enforced)}");
        }
    }

    private static string Name(MethodInfo action)
        => $"{action.DeclaringType!.Name}.{action.Name}";

    private static bool IsAnonymous(MethodInfo action)
        => action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
           || action.DeclaringType!.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

    /// <summary>
    /// Whether any <c>[Authorize]</c> on the action or its controller names the pre-MFA scheme.
    /// <c>AuthenticationSchemes</c> is a comma-separated string, so it is split rather than compared.
    /// </summary>
    private static bool DeclaresPendingMfaScheme(MethodInfo action)
        => action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Select(attribute => attribute.AuthenticationSchemes)
            .Where(schemes => !string.IsNullOrWhiteSpace(schemes))
            .SelectMany(schemes => schemes!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(scheme => string.Equals(scheme, AuthSchemes.MfaPending, StringComparison.Ordinal));

    /// <summary>
    /// Controllers with any route template starting with <paramref name="prefix"/>, whether it sits on
    /// the controller or on one of its actions, so a controller routed only per action is still found.
    /// </summary>
    private static List<Type> ControllersUnder(string prefix)
        => ControllerTypes()
            .Where(type => !type.IsAbstract)
            .Where(type => type.GetCustomAttributes<RouteAttribute>(inherit: true).Select(route => route.Template)
                .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(method => method.GetCustomAttributes(inherit: true).OfType<IRouteTemplateProvider>())
                    .Select(route => route.Template))
                .Any(template => template is not null
                    && template.TrimStart('/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>
    /// Whether the controller's <c>[Authorize]</c> attributes name exactly one scheme between them,
    /// and it is <paramref name="scheme"/>. An attribute naming no scheme would fall back to the
    /// default one, so it counts as a mismatch.
    /// </summary>
    private static bool DeclaresExactly(Type controller, string scheme)
    {
        var attributes = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        return attributes.Count > 0
            && attributes.All(attribute => SchemesOf(attribute).SequenceEqual([scheme], StringComparer.Ordinal));
    }

    private static bool DeclaresAnyScheme(MethodInfo action)
        => action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(attribute => SchemesOf(attribute).Length > 0);

    private static string[] SchemesOf(AuthorizeAttribute attribute)
        => (attribute.AuthenticationSchemes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsAuthorized(MethodInfo action)
        => action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
           || action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

    private static IEnumerable<MethodInfo> Actions()
        => ControllerTypes()
            .Where(type => !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName)
            .Where(method => !method.GetCustomAttributes<NonActionAttribute>(inherit: true).Any());

    private static IEnumerable<Type> ControllerTypes()
        => typeof(VaultController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .Where(type => type != typeof(Controller) && type != typeof(ControllerBase));
}
