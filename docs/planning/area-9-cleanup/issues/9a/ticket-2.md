## Parent

#SPEC

## What to build

Razor goes and the route table stays explicit (ADR-0034):

- `AddControllersWithViews` becomes `AddControllers()` plus `AddMvcCore().AddViews()`, keeping the antiforgery filters and TempData (F1).
- `ExternalLogin` and `ExternalCallback` get attribute routes at their current URLs (F3); the conventional route goes.
- `/` joins the route table as a session page. A path the table does not name: signed in → the not-found server page with `404`; signed out → sign-in; `/api/v1/**` → `404 { "error": "not_found" }` with no session challenge; a path with a file extension → bare `404` (F4).
- Delete `HomeController`, `Views/`, `EmptyState.cs`, `FlashExtensions`, `AccountController.Logout`, `site.js`, `site.css`, `AngularContentSecurityPolicy` and ticket 1's leftovers. Trusted Types joins the base Content Security Policy.
- The Angular shell drops the `angular` flag; every sidebar and palette link navigates in-app.
- Prove no application code still posts `/Account/Logout`. 8a ticket 2 moves the account menu, `StaleSessionSignOut` and the step-up dialog onto `POST /api/ui/auth/sign-out`, and maps Profile's sign-out notices. This ticket does not move them.
- An endpoint-table test (F8, A9-4) and a no-Razor test. The table pins anonymous, pending sign-in, administrator, session and API-key-only endpoints separately. Unknown `/api/v1/` answers `404 { "error": "not_found" }` with no session challenge. `/local-sign-out` keeps its own HTTP check.

## Acceptance criteria

- [ ] F1 is shown failing with `AddControllers()` alone (a UI API POST and the Users notice fail), then fixed; F3, F4 and F8 are each shown failing, then fixed.
- [ ] The endpoint-table test on the Production host pins the anonymous endpoints, the pending sign-in endpoints and the administrator pages exactly. Every other session endpoint, the page not-found fallback and `/api/ui/**` included, requires a session. Public API endpoints accept an API key and no session. Unknown `/api/v1/x` answers `404 { "error": "not_found" }` with no session challenge. `/local-sign-out` still clears the cookie for a caller with no session.
- [ ] No application code posts `/Account/Logout`.
- [ ] The not-found matrix (signed in and out × unknown path, `/api/ui/x`, `/api/v1/x`, `/ui/missing.js`) answers as the spec says, on both databases.
- [ ] No Razor view is compiled into Web; no `.cshtml` remains.
- [ ] Every HTML response carries Trusted Types; a UI API POST without a token is still refused; SSO is still routed.
- [ ] Vitest for the shell and palette; a new browser spec for the not-found page and signed-out `/`; specs that crossed into Legacy pages updated.
- [ ] `scripts/check.sh`, `scripts/check.sh integration "<filter>"` and `scripts/check.sh e2e <affected specs>` pass.

## Blocked by

- #T1
- #SPEC7B and #SPEC7C merged: they move Secrets and Dynamic secrets off the views this ticket deletes.
