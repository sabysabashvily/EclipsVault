# Area 9 Final cleanup and #68: decision record

Status: all 19 recommendations accepted (rounds 1–2) and confirmed by the owner on 2026-11-11.
Challenge A9-1 through A9-4 folded on 2026-11-12. Issue drafts are in [issues/](issues/), not yet published.

Survey and findings F1–F11: [survey.md](survey.md). Rounds: [checkpoint.md](checkpoint.md). Glossary:
`CONTEXT.md` (Legacy UI, Server page). ADRs: 0034 new; 0017 amended; 0014 and 0018 point to 0034.

## Decisions

| Ref | Decision | Agreed behaviour |
| --- | --- | --- |
| Q1 | Split | **9a Final cleanup** (protected: anonymous surface, antiforgery, errors) from `main` after 8a merges, not waiting for 8b; its Razor deletion (ticket 2) and merge also wait for 7b and 7c to merge (amended 2026-11-12, challenge A9-1); **9b Database provider** (#68, ordinary) built any time, merged after 9a. #69 stays out. |
| Q2 | 9a scope | Delete what the survey lists as left: `HomeController`, every remaining view and partial, `_ViewImports`, `_ViewStart`, `ErrorViewModel`, `EmptyState.cs`, `FlashExtensions`, Legacy `AccountController.Logout`, `site.js`, `site.css`, the conventional route, the Angular shell's `angular` flag. Keep SSO `ExternalLogin`/`ExternalCallback` at their URLs with attribute routes (F3), `/Profile/Avatar`, `/local-sign-out`, both APIs, `wwwroot/fonts`, `favicon.svg`. Any leftover from earlier Areas is listed in ticket 1 and goes. |
| Q3 | Routing (F4) | No catch-all (ADR-0034). The route table stays the list of pages, each with its rule; `/` added as a session page. Unknown path: signed in → not-found server page `404`; signed out → sign-in (unchanged); `/api/ui/**` Problem Details `404`; `/api/v1/**` `404 { "error": "not_found" }`; a path with a file extension → bare `404`, never the document. |
| Q4 | MVC (F1) | `AddControllers()` plus `AddMvcCore().AddViews()`: no Razor view engine; the framework's antiforgery filters and TempData stay. Proven first that `AddControllers()` alone fails. The Users notice keeps TempData. |
| Q5 | Errors (F5, F6) | UI API unchanged. Public API: `{ "error": "<code>" }` with the real status for every mapped exception; blocked `403 { "error": "blocked" }`. Every other caller: the error server page, written directly with the real status, the middleware's fixed title, a request id and a link to `/`; no redirect, no exception detail. A honey-token trip off the APIs still signs out and redirects to sign-in. `/Home/Error` is gone. |
| Q6 | Server pages (F7) | One template for blocked, error and not found; `wwwroot/css/server-page.css` holds only the sign-in card rules and tokens; it loads from a blocked address. |
| Q7 | `/` | No landing page; `/` needs a session; a signed-out visitor goes to Login with a return URL. `HomeController` deleted, so `HomeController.Index` and `Error` leave the anonymous list. |
| Q8 | Sign-out, TempData (F2) | 9a deletes `AccountController.Logout`. 8a ticket 2 moves every caller onto `POST /api/ui/auth/sign-out` and maps each sign-out-everywhere outcome to its own Login query code and Legacy sentence. Authenticator reset uses the short-lived HttpOnly, Login-scoped one-time handoff; a typed `?signOut=authenticator_reset` shows nothing and a second load consumes the notice. Both TempData writes go only with these handoffs. 9a ticket 2 only proves no caller still posts `/Account/Logout` (A9-2/A9-3, reconciled with Area 8 on 2026-11-13; current handoff in `logs/area-9/coordinator-handoff.md`). |
| Q9 | Headers (F11) | Trusted Types in the base CSP everywhere; the `ServeIndex` override and `AngularContentSecurityPolicy` go. `no-store` on every `/api/` response. |
| Q10 | Surface tests (F8) | Reflection tests stay with shorter lists. The endpoint-table test on the Production host pins the anonymous, pending sign-in and administrator endpoints exactly. Every other session endpoint, the page not-found fallback and `/api/ui/**` included, requires a session. Public API endpoints are API-key-only and accept no session (ADR-0005). Unknown `/api/v1/` is `404 { "error": "not_found" }` with no session challenge. `/local-sign-out` is not an endpoint; a separate HTTP check pins it. A unit test shows no Razor view is compiled into Web. Amended 2026-11-12, challenge A9-4. |
| Q11 | #68 (F9) | No default in any environment. Startup exits 1 with "Database:Provider is not set. Set it to 'SqlServer' or 'Postgres' (environment variable Database__Provider)." The design-time factory is unchanged. `e2e/compose.yml` gets `Database__Provider: SqlServer`; the `run-vault` skill, `README.md` and `docs/INSTALL.md` change, and INSTALL gains "Upgrading to 2.0". The owner adds the setting to their own `appsettings.Development.json`. The release-note paragraph goes in the PR body. |
| Q12 | Release (F10) | The 24 November build is the v2.0.0 candidate with 9a and 9b. Nothing tagged 1.x from `main` after 9a merges; a v1.5.0, if wanted, from a commit before 9a. Tagging stays with the owner. |
| Q13 | Copy | Error page heading "<status> — <title>": 404 Not found, 403 Access denied, 402 Licence required, 409 Secret needs a one-time upgrade, 503 Vault unavailable (fail-closed), otherwise Something went wrong; under it the middleware's exact title; "Request id: …"; button "Back to safety" → `/`. Not found: "404 — Page not found", "There is no page at this address.", same button. Blocked page text unchanged. Page titles end "— EclipsVault". Sign-in card look, dark, no sidebar. |
| Q14 | Glossary | Legacy UI redefined; Server page added. |
| Q15 | Tests | See [Test plan](#test-plan). |
| Q16 | 9a delivery | See [Delivery](#delivery). |
| Q17 | 9b delivery | See [Delivery](#delivery). |
| Q18 | Reviews | 9a: Claude Opus 5.5 (high) implements; per ticket `security-reviewer` plus GPT Sol 6.1 (xhigh) Standards and Spec, re-checks by the raising reviewer; per PR GPT Sol, Thermos, CI on the exact head, owner merge. 9b: GPT Sol, Thermos, CI, owner merge. |
| Q19 | Challenge | One GPT Sol 6.1 (xhigh) challenge of this table and the drafts by the coordinator, in a GPT window, as a fresh delegate, before publication. |

## Error answers by caller

| Request | Unknown path | Mapped exception | Blocked network |
| --- | --- | --- | --- |
| `/api/ui/…` | `404` Problem Details `not_found` (session required) | Problem Details, status and `code` as today | `403` Problem Details `blocked` |
| `/api/v1/…` | `404 { "error": "not_found" }` | `{ "error": "<code>" }`, same status and code as the UI API | `403 { "error": "blocked" }` |
| Anything else | Signed in: not-found page `404`; signed out: sign-in | Error page with the real status | Blocked page `403` |
| Path with a file extension | Bare `404` (signed out: sign-in) | — | Static files load before the block |

The codes and texts are the ones `GlobalExceptionMiddleware` already uses: `audit_unavailable`,
`session_store_unavailable`, `unblock_unrecorded` (503), `not_found` (404), `legacy_blob_refused`
(409), `crypto_unavailable` and `internal_error` (500), `premium_not_licensed` (402), `invalid_request` (400).

## Test plan

Each finding is first shown failing (lesson 2).

- **Unit:** no Razor view is compiled into Web; `AuthorizationSurfaceTests` lists lose `HomeController`
  and `Logout`; the Angular UI posts no `/Account/Logout`; 9b's missing-provider message.
- **Production HTTP, both databases:** the endpoint-table pin (anonymous, pending sign-in, administrator, session, and API-key-only kept apart; unknown `/api/v1/` is JSON 404 with no session challenge; `/local-sign-out` has its own HTTP check); the not-found matrix (signed in and out ×
  unknown path, `/api/ui/x`, `/api/v1/x`, `/ui/missing.js`); errors by caller (status, page text, no
  `Location`, no exception text; Public API through a test-only `/api/v1/` throw endpoint in
  `TestOnlyEndpoints.cs`); the blocked page from a blocked address with its stylesheet `200 text/css`,
  and JSON for the Public API; Trusted Types on every HTML response; `no-store` on `/api/v1`; SSO actions
  still routed; a UI API POST without a token still refused; the Users notice still works; audit-failure
  tests that expected `/Home/Error?code=503` expect the 503 page.
- **Vitest:** the shell without the `angular` flag; in-app navigation for every sidebar link and the palette.
- **Playwright** under browser health: a new spec (signed-in unknown path shows the not-found page, its
  404 console error declared with `browserHealth.expectConsoleError`; signed-out `/` lands on Login);
  specs that crossed into Legacy pages updated.
- **Commands:** `scripts/check.sh`, `scripts/check.sh integration "<filter>"` (PostgreSQL),
  `scripts/check.sh e2e <affected specs>`. CI runs SQL Server, the whole browser suite and `e2e:verify-guards`.

## Delivery

| Part | Ticket | Scope | From |
| --- | --- | --- | --- |
| 9a | 1 | Errors in the caller's format, server pages and stylesheet, Public API blocked answer, `no-store` on `/api/`; `HomeController.Error` and its view deleted; commits the records. | Q5, Q6, Q9, Q13 |
| 9a | 2 | Razor removal and routing: MVC registration (F1), SSO attribute routes (F3), no catch-all with not-found, `/` in the table, landing and Logout deleted, every view, `site.js`, `site.css`, conventional route deleted, Trusted Types global, shell `angular` flag removed, endpoint-table pin (A9-4), no-Razor test, no remaining `/Account/Logout` caller. | Q2–Q4, Q7–Q10 |
| 9a | 3 | Whole-part verification (security review, UI verification) and the PR. | Q15, Q18 |
| 9b | 1 | `Database:Provider` required; launchers and docs; PR merged after 9a. | Q11, Q17 |

9a branch `area-9a-final-cleanup` from `main` after 8a merges; ticket 2 and the merge wait for 7b
and 7c to merge. 9b branch
`cursor/area-9b-database-provider` from `main` at any time (Cursor; Claude Opus on
`area-9b-database-provider` if Cursor cannot).

Schedule risk: 9a is the last protected part and waits on 6b, 7a–7c and 8a; three tickets plus their
reviews and CI must fit before the 20–21 November verification. 9b never blocks anything.
