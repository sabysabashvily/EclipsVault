This spec is **Area 9 part 9a, Final cleanup** (ADR-0017 amendment, ADR-0018, ADR-0034). It starts from `main` after part 8a (#SPEC8A) merges; it does not wait for 8b. Ticket 2, which deletes the Razor views, and this part's merge also wait for parts 7b (#SPEC7B) and 7c (#SPEC7C) to merge, because Secrets and Dynamic secrets render Razor until then (owner, 2026-11-12). Part 9b (#SPEC9B) requires `Database:Provider` and merges after this part. The owner accepted all 19 grill decisions and both delivery outlines, and confirmed them on 2026-11-11.

## Problem Statement

When Areas 6–8 have moved, the Legacy UI still leaves behind the landing page, the error page, the Razor layouts, `site.js`, `site.css`, a Legacy sign-out action, MVC view support and the conventional route (ADR-0018). ADR-0001 is met only when no Razor view remains.

The grill's survey found these flaws, deductions from reading the code at `ab7997e`; each ticket proves its finding with a failing test first.

- **`AddControllers()` alone breaks every controller (F1).** The antiforgery filters behind the global `AutoValidateAntiforgeryTokenAttribute` and `ExternalLogin`'s `[ValidateAntiForgeryToken]`, and `ITempDataDictionaryFactory`, come with MVC's view features. The UI API uses TempData (`UsersController`'s notice).
- **Removing the conventional route unroutes SSO (F3).** `ExternalLogin` and `ExternalCallback` have no attribute routes.
- **A catch-all would serve the app for paths that are not pages (F4)**, with `200`, and would drop the server's per-page rules (administrator only, anonymous sign-in pages, pending sign-in cookie).
- **Public API failures outside five caught types redirect to an HTML page, and a blocked network gets HTML (F5).**
- **The error page blames the audit trail for every 503 (F6).**
- **The blocked page links `/css/site.css`**, which this part deletes (F7).
- **The anonymous-surface test sees only controller actions (F8)**, not route-table pages or fallbacks.
- **Public API answers are not `no-store` (F11).**
- **Two Profile notices reach no one after 8a (F2)** if 8a has not moved them off TempData. 8a ticket 2 maps all three sign-out-everywhere outcomes to their fixed query codes. Authenticator reset uses its short-lived HttpOnly, Login-scoped one-time handoff; a typed query cannot claim the notice and a second load consumes it. The same ticket deletes both TempData writes (A9-3). This part only checks that no caller of `AccountController.Logout` remains (A9-2).

## Solution

The route table stays the explicit list of pages; a path it does not name is an honest not-found. Every failure answers in its caller's format without a redirect. The vault writes only three server pages itself: access blocked, error and not found. Razor goes; MVC's view features stay. No landing page: `/` needs a session.

## User Stories

1. As a signed-out visitor to `/`, I am sent to Login and come back to the Overview after signing in.
2. As a signed-in user who mistypes an address, I see "404 — Page not found" with a way back, not an empty page.
3. As a Public API client, every failure is JSON in the `{ error }` shape with the right status, never a redirect to HTML.
4. As anyone whose request fails outside the APIs, I see the real status and a sentence that says what failed, never exception details.
5. As someone on a blocked network, the access-blocked page still looks right and still offers break-glass recovery.
6. As an operator, SSO sign-in still works at its URLs.
7. As a security reviewer, a test lists every endpoint reachable without a session, and no Razor view can ship.

## Implementation Decisions

### Scope (Q2, Q7, Q8)

Deleted: `HomeController`, `Views/` (every view, partial, `_ViewImports`, `_ViewStart`), `ErrorViewModel`, `EmptyState.cs`, `FlashExtensions`, `AccountController.Logout`, `wwwroot/js/site.js`, `wwwroot/css/site.css`, the conventional route, `AngularContentSecurityPolicy`, the Angular shell's `angular` flag on sidebar links (and `data-command-angular`). Anything an earlier Area left is listed in ticket 1 and goes. Kept: `ExternalLogin`, `ExternalCallback` (now attribute-routed at `/Account/ExternalLogin` and `/Account/ExternalCallback`), `GET /Profile/Avatar`, `/local-sign-out`, `/openapi/ui.json` (Development), the UI API, the Public API, `wwwroot/fonts/`, `wwwroot/favicon.svg`, `LicenseNudgeState`, `AuditDisplay`. 8a ticket 2 moves every caller of `POST /Account/Logout` (challenge A9-2) and maps Profile's sign-out notices (F2, A9-3). Ticket 2 proves no caller still posts `/Account/Logout`.

### Routing (Q3, ADR-0034)

No catch-all. `/` joins the route table as a session page. A path the table does not name: signed in → the not-found server page with `404`; signed out → sign-in, as today. `/api/ui/**` keeps its Problem Details `404`; `/api/v1/**` answers `404 { "error": "not_found" }` and does not challenge with the session cookie; a path with a file extension answers a bare `404`, never the Angular document.

### MVC registration (Q4)

`AddControllers()` plus `AddMvcCore().AddViews()`, with the existing antiforgery filters. No Razor view engine. `UsersController`'s notice keeps TempData.

### Errors (Q5, Q13)

| Request | Mapped exception | Blocked network |
| --- | --- | --- |
| `/api/ui/…` | Problem Details, unchanged | `403` Problem Details `blocked`, unchanged |
| `/api/v1/…` | `{ "error": "<code>" }`, the real status | `403 { "error": "blocked" }` |
| anything else | error server page, the real status, no redirect | blocked server page, `403` |

A honey-token trip outside the APIs still signs out and redirects to sign-in. Error page: heading "<status> — <title>" (404 Not found, 403 Access denied, 402 Licence required, 409 Secret needs a one-time upgrade, 503 Vault unavailable (fail-closed), otherwise Something went wrong); below it the middleware's exact title for the failure; "Request id: …"; button "Back to safety" → `/`. Not found: "404 — Page not found", "There is no page at this address.", same button. Page titles end "— EclipsVault". No exception detail anywhere.

### Server pages (Q6)

One template for blocked, error and not found: the Legacy sign-in card, dark, no sidebar, no script. They link `wwwroot/css/server-page.css`, which holds only the card rules, tokens and `@font-face` from `site.css`. The blocked page's text is unchanged.

### Headers (Q9)

Trusted Types (`require-trusted-types-for 'script'; trusted-types angular angular#bundler`) joins the base Content Security Policy for every response; `ServeIndex` stops overriding it. Every `/api/` response is `no-store`.

## Testing Decisions

- **Unit:** no Razor view compiled into Web (no `RazorCompiledItemAttribute`); `AuthorizationSurfaceTests` lists without `HomeController` and `Logout`.
- **Endpoint table (F8, A9-4):** an integration test on the Production host lists every endpoint and pins the anonymous ones, the pending sign-in ones and the administrator pages exactly. Every other session endpoint requires a session, the page not-found fallback and `/api/ui/**` included. Public API endpoints are pinned as API-key-only and accept no session (ADR-0005). Unknown `/api/v1/` is the JSON 404 above, with no session challenge. `/local-sign-out` is not an endpoint; a separate HTTP check pins that a caller with no session still clears the cookie.
- **Logout callers (A9-2):** a test fails while the Angular UI still posts `/Account/Logout`. 8a ticket 2 moves the account menu, `StaleSessionSignOut` and the step-up dialog. This part does not.
- **Production HTTP on both databases:** the not-found matrix; errors by caller with no `Location` header and no exception text (`UiApiFailureContractTests`, plus a test-only `/api/v1/` throw endpoint in `TestOnlyEndpoints.cs`); the blocked page from a blocked address with its stylesheet `200 text/css`, and JSON for the Public API; Trusted Types on every HTML response; `no-store` on `/api/v1`; SSO actions routed; a UI API POST without a token refused; the Users notice; audit-failure tests that expected `/Home/Error?code=503`.
- **Vitest:** the shell and palette without the `angular` flag.
- **Playwright** under browser health: a new spec for the not-found page (its 404 console error declared) and signed-out `/`; specs that crossed into Legacy pages updated.
- Locally `scripts/check.sh`, `scripts/check.sh integration "<filter>"` (PostgreSQL), `scripts/check.sh e2e <specs>`; CI runs SQL Server, the whole browser suite and `e2e:verify-guards`.

## Delivery

Branch `area-9a-final-cleanup` from `main` after 8a merges, three sequential tickets, one PR. Ticket 2 starts, and the PR merges, only after 7b and 7c have merged; rebase onto that `main` first. Claude Opus 5.5 (high) implements. The `security-reviewer` agent reviews every ticket and the whole part; Standards and Spec reviews go to GPT Sol 6.1 at xhigh; fixes are re-checked by the reviewer that raised them. The PR gets GPT Sol, Thermos, CI on its exact head and the owner's merge.

## Out of Scope

- `Database:Provider` (#SPEC9B), the reference deployment (#69), cutting v2.0.0 (owner).
- Any redesign; an anonymous landing page.

## Records

`docs/planning/area-9-cleanup/`; `CONTEXT.md` (Legacy UI, Server page); ADR-0034; ADRs 0014, 0017 and 0018 amended.
