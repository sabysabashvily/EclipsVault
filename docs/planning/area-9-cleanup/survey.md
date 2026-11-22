# Area 9 survey

Read on 2026-11-11 at `ab7997e` (`main` plus Area 6a), with Area 7's and Area 8's uncommitted grill
records read from their worktrees. Findings are deductions from reading the code; each ticket proves
its finding failing first (lesson 2).

## What is left for 9a after Areas 6b–8a

| Piece | Removed by |
| --- | --- |
| `AuditController`, `Views/Audit/Index` | 6b |
| `HomeController.Index` signed-in branch, `Views/Home/Dashboard`, `DashboardViewModel`; `SecretsController.Index`, `Search`, `Create`, their views; `site.js` palette moved to the UI API | 7a |
| Rest of `SecretsController`, `Views/Secrets/Details`, Legacy `AccessRequestsController`, `AccountController.Denied`, `Views/Account/Denied` | 7b |
| `DynamicSecretsController`, `Views/DynamicSecrets/Index` | 7c |
| `AccountController` Login, Totp, RecoveryCode, EnrollTotp, Recover, PasskeyLogin*; their five views and models; `site.js` passkey sign-in | 8a |
| **Left for 9a:** `HomeController` (landing branch and `Error`), `Views/Home/Index`, `Views/Shared/` (`Error`, `_Layout`, `_AuthLayout`, `_Flash`, `_LicenseBanner`, `_ThemeToggle`, `_EmptyState`), `_ViewImports`, `_ViewStart`, `ErrorViewModel`, `EmptyState.cs`, `FlashExtensions`, Legacy `AccountController.Logout`, `wwwroot/js/site.js`, `wwwroot/css/site.css`, `AddControllersWithViews`, the conventional route, the Trusted Types split in `SecurityHeadersMiddleware`, and the Angular shell's `angular` flag on sidebar links (every link is Angular by then) | 9a |

Stays: `AccountController.ExternalLogin` and `ExternalCallback` (SSO, server actions), `AvatarController`
(`GET /Profile/Avatar`), `UiApiDocumentController` (`/openapi/ui.json`, Development only), `LocalSignOut`
middleware (`/local-sign-out`), `LicenseNudgeState` (now feeds `/api/ui/me`), `AuditDisplay`.
Static files the Angular UI does not own but uses: `wwwroot/fonts/` (its stylesheet loads them,
`OFL.txt` is in the image notices) and `wwwroot/favicon.svg` (its `index.html` links it).

The Legacy licence banner is already replaced: `/api/ui/me` carries `licenseNotice` and the Angular
shell shows it to administrators. Only the partial is left to delete.

## Error path per caller today

| Caller | Today |
| --- | --- |
| UI API (`/api/ui/`) | Problem Details with a stable `code`; unknown path `404 not_found` behind a session |
| Public API (`/api/v1/`) | `SecretsApiController.Get` catches five exception types and answers `{ error }` JSON. Anything else (a failure in `List`, `DomainException`, `PremiumFeatureNotLicensedException`, an unhandled one) is **redirected with 302 to `/Home/Error`**, an HTML page. A blocked network gets the **HTML** blocked page. |
| Page navigation | 302 to `/Home/Error?code=<status>`; the page sets that status and shows fixed text per code |
| SSO actions, `/Profile/Avatar` | Same redirect |
| Static files | Missing file: no endpoint matches, so the fallback policy sends a signed-out caller to sign-in and a signed-in one gets a bare 404 |
| `/local-sign-out` | Its own JSON answers |

## Anonymous surface today

Controller actions (pinned by `AuthorizationSurfaceTests`, reflection over actions only):
`AccountController` ExternalCallback, ExternalLogin, Login, PasskeyLoginBegin, PasskeyLoginComplete,
Recover; `HomeController` Error, Index. Terminal middleware: `/local-sign-out`. Before authentication:
static files and the blocked page. After 8a: the `/api/ui/auth/` calls, and the Angular documents for
`/Account/Login` and `/Account/Recover` (8b adds `/Account/Activate`) through the route table.

## Findings

| Ref | Severity | Finding |
| --- | --- | --- |
| F1 | High (trap) | **`AddControllers()` alone breaks every controller.** The antiforgery filters behind the global `AutoValidateAntiforgeryTokenAttribute` and `ExternalLogin`'s `[ValidateAntiForgeryToken]`, and `ITempDataDictionaryFactory`, are registered by MVC's view features, which `AddControllers` does not add. The UI API uses TempData: `UsersController` keeps its notice in it, and Profile's sign-out everywhere and authenticator reset write a flash for Login. |
| F2 | Medium (8a) | **Two Profile notices reach no one after 8a.** A successful sign-out everywhere and an authenticator reset hand Login a one-shot message through TempData ("Your authenticator was reset. Sign in to set up a new one."). The Angular Login cannot read TempData, and Area 8's records do not mention these two messages. |
| F3 | Medium | **Removing the conventional route unroutes SSO.** `ExternalLogin` and `ExternalCallback` have no attribute routes; without `{controller}/{action}` both return 404. |
| F4 | Medium | **A literal catch-all serves the app for paths that are not pages.** A typo, `/api/v1/unknown` or a missing `/ui/*.js` bundle would get `index.html` with 200; the Angular router has no `**` route, so a signed-in user sees an empty shell, and a missing script answers HTML. A single catch-all also loses the per-path rules: `AdminOnly` on administrator pages, anonymous Login and Recover, the pending-sign-in cookie on the second-factor pages. |
| F5 | Medium | **Public API failures outside five caught types redirect to HTML** (see the table above), and a blocked network gets HTML, not the Public API's `{ error }` shape. |
| F6 | Low | **The Error page blames the audit trail for every 503**, although `session_store_unavailable` and `unblock_unrecorded` are 503 too. |
| F7 | Low | **The blocked page links `/css/site.css`**, which 9a deletes. The Angular stylesheet's name is hashed (`outputHashing: all`), so it has no stable URL to link instead. |
| F8 | Medium (test gap) | **The anonymous-surface test cannot see endpoints that are not controller actions**: route-table documents, `MapFallback`, a catch-all. An anonymous catch-all, or a mistyped rule in 8a's or 8b's route table, would pass every test. |
| F9 | Low (#68) | **Launchers rely on the SQL Server default:** the browser suite's `e2e/compose.yml` sets no `Database__Provider`; `docs/INSTALL.md`, `README.md` and the `run-vault` skill say an unset provider means SQL Server. The owner's untracked `appsettings.Development.json` has no `Provider` key (only the key was counted; no value was read). |
| F10 | Process | **v1.5.0 is untagged.** If 9a or 9b merges before it is tagged, a 1.x release cut from `main` would carry v2.0's breaking changes. |
| F11 | Low | **Public API answers are not `no-store`.** A Public API read returns a decrypted value as JSON; the header rule covers HTML and the UI API only (ADR-0013). The key is usually sent as `X-Api-Key`, which does not stop a shared cache the way `Authorization` does. |

## CSP, Trusted Types and `no-store` (ADR-0013)

The base policy stays. Trusted Types is still set only by `AngularUiRoutes.ServeIndex`, because Legacy
`site.js` writes `innerHTML`; once `site.js` is gone the base policy can carry it everywhere and the
per-document override goes. Server-rendered pages left (blocked, error) carry no script. Browser specs
that assert a Legacy page lacks Trusted Types change with it.

## CI (ADR-0019)

No job is added or removed. Specs that cross into Legacy pages (`sidebar.ts`, `sign-in.spec.ts`,
`palette.ts` and others) are updated by the Areas that remove those pages and by 9a. Nothing yet pins
ADR-0001's "no `.cshtml` remains".

## #68 and #69

`DependencyInjection.AddEclipsVaultInfrastructure` falls back to SQL Server when `Database:Provider` is
unset; an unknown value already fails. `EclipsVaultDbContextFactory` selects by
`ECLIPSVAULT_DESIGN_PROVIDER` and is out of scope. Integration fixtures set the provider. #69
(reference deployment) is "later" by the owner's decision and stays out.
