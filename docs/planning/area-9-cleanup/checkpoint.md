# Area 9 grill checkpoint

Started 2026-11-11 in worktree branch `area-9-grill` at `ab7997e` (`main` plus Area 6a, PR #147), with
Area 7's and Area 8's uncommitted records read from their worktrees. Model: Claude Opus 5.5 (high).
Survey: [survey.md](survey.md).

Lessons 1–23 apply (20: a restated headline accepts the whole recommendation; 21: GPT Sol and Thermos
before every merge; 22: accepted risks go into the ADR's Consequences; 23: cheap non-protected work to Cursor).

## Round 1 (asked 2026-11-11)

Q1 Split and order · Q2 Scope · Q3 No catch-all · Q4 MVC without Razor (F1) · Q5 Errors in the
caller's format (F5, F6) · Q6 Server pages and their stylesheet (F7) · Q7 The landing page and `/` ·
Q8 Legacy sign-out and TempData (F2) · Q9 Headers (F11) · Q10 Pinning the surface (F8) ·
Q11 #68 behaviour (F9) · Q12 Release (F10).

### Answers (2026-11-11)

"I accept everything you recommend for each Q": every round-1 recommendation stands with all its
sub-points (lesson 20).

| Ref | Accepted |
| --- | --- |
| Q1 | Two parts, own spec, branch and PR: 9a Final cleanup (protected, Claude Opus, security review) from `main` after 8a merges, not waiting for 8b; 9b #68 (ordinary, Cursor ticket, Claude fallback), built any time, merged after 9a. #69 out. ADR-0017 amended. Amended 2026-11-12 by the owner after challenge A9-1: 9a ticket 2 (Razor deletion) and the 9a merge also wait for 7b and 7c to merge. |
| Q2 | 9a deletes everything the survey lists as left (views, view models, `site.js`, `site.css`, `FlashExtensions`, Legacy Logout, the shell's `angular` flag); keeps SSO actions with attribute routes (F3), `/Profile/Avatar`, `/local-sign-out`, both APIs, fonts, favicon; ticket 1 lists any leftover from earlier Areas, and it goes. |
| Q3 | No catch-all; explicit route table with per-page rules, `/` added; unknown path → 404 not-found page behind a session (signed-out → sign-in); `/api/ui/**` Problem Details 404, `/api/v1/**` `404 { error: "not_found" }`, file-extension paths bare 404. ADR-0034. |
| Q4 | `AddControllers()` plus `AddMvcCore().AddViews()`: no Razor engine, framework antiforgery filters and TempData kept; Users notice unchanged; ticket proves `AddControllers()` alone fails first. ADR-0034. |
| Q5 | UI API unchanged; Public API `{ error }` with real status for every mapped exception, blocked `403 { error: "blocked" }`; other callers get a server error page with the real status, the middleware's fixed title, request id, link to `/`, no redirect, no exception detail (fixes F6); honey trip off the APIs still signs out and redirects to sign-in; `/Home/Error` gone; `UiApiFailureContractTests` asserts status and text and gains a Public API theory. ADR-0034. |
| Q6 | One server-page template (blocked, error, not found) and `wwwroot/css/server-page.css` with only the sign-in card rules and tokens; a test loads it from a blocked address. ADR-0034. |
| Q7 | No landing page; `/` a session page; signed-out → Login with return URL; `HomeController` deleted. ADR-0034. |
| Q8 | 9a deletes `AccountController.Logout`; 8a ticket 2 owns all three callers and all three sign-out-everywhere outcome codes. Authenticator reset uses its short-lived HttpOnly, Login-scoped one-time handoff, never a query code; a typed query shows nothing and the second load has no notice. Delete both TempData writes with the handoffs. 9a ticket 2 only proves no caller still posts `/Account/Logout` (A9-2/A9-3; reconciled with Area 8 on 2026-11-13). |
| Q9 | Trusted Types in the base CSP everywhere, `ServeIndex` override deleted; `no-store` on every `/api/` response (F11). ADR-0034. |
| Q10 | Reflection tests kept with shorter lists; endpoint-level integration test on the Production host pins anonymous, pending sign-in and administrator endpoints; every other session endpoint needs a session, not-found included; Public API endpoints are API-key-only; unknown `/api/v1/` is JSON 404 with no session challenge; `/local-sign-out` has its own HTTP check; unit test that no Razor view is compiled into Web. Amended 2026-11-12, challenge A9-4. |
| Q11 | No default in any environment; startup exits 1 with "Database:Provider is not set. Set it to 'SqlServer' or 'Postgres' (environment variable Database__Provider)."; design-time factory unchanged; e2e `compose.yml` gets `Database__Provider: SqlServer`; `run-vault` skill, README and INSTALL updated, INSTALL gains "Upgrading to 2.0"; unit test on the message; the owner adds the setting to their own `appsettings.Development.json`; release-note paragraph in the PR body. |
| Q12 | The 24 November build is the v2.0.0 candidate with 9a and 9b; nothing tagged 1.x from `main` after 9a merges; a v1.5.0, if wanted, from a commit before 9a; tagging stays with the owner. |

Records written: ADR-0034 (new); ADR-0017 amended; ADR-0014 and ADR-0018 marked as amended by
ADR-0034. F2 handed to the coordinator in `logs/area-9/coordinator-handoff.md` (this worktree).

## Round 2 (asked 2026-11-11)

Q13 Server page copy · Q14 Glossary · Q15 Test plan · Q16 9a delivery · Q17 9b delivery · Q18 Reviews ·
Q19 GPT Sol challenge.

### Answers (2026-11-11)

The owner replied "confirmed" to round 2. Taken, as in Areas 7 and 8, as accepting Q13–Q19 in full
(lesson 20) and confirming the whole decision table and delivery outline. The owner can correct this.

| Ref | Accepted |
| --- | --- |
| Q13 | Server pages keep the Legacy sign-in card look, dark, no sidebar. Error page: heading "<status> — <Legacy title>" (404 Not found, 403 Access denied, 402 Licence required, 409 Secret needs a one-time upgrade, 503 Vault unavailable (fail-closed), otherwise Something went wrong); line under it the middleware's exact title; "Request id: …"; button "Back to safety" → `/`. Not-found page: "404 — Page not found", "There is no page at this address.", same button. Blocked page text unchanged. Titles end "— EclipsVault". |
| Q14 | `CONTEXT.md`: Legacy UI redefined; Server page added. |
| Q15 | Test layers as recommended, each finding failing first; `scripts/check.sh` locally, SQL Server, the whole browser suite and the guard probes in CI. |
| Q16 | 9a on `area-9a-final-cleanup` from `main` after 8a merges: 1 errors and server pages (commits the records), 2 Razor removal and routing, 3 whole-part verification and PR. Ticket 2 starts, and the PR merges, only after 7b and 7c have merged (A9-1). |
| Q17 | 9b: one Cursor ticket on `cursor/area-9b-database-provider` from `main` at any time, spec plus one ticket, PR merged after 9a; Claude Opus on `area-9b-database-provider` if Cursor cannot. |
| Q18 | 9a: Opus 5.5 (high) implements; per ticket `security-reviewer` plus GPT Sol Standards and Spec, re-checks by the raising reviewer; per PR GPT Sol, Thermos, CI on the exact head, owner merge; closing comments with commits, CI run and counts. 9b: GPT Sol, Thermos, CI, owner merge; no security review. |
| Q19 | One GPT Sol 6.1 (xhigh) challenge of the table and drafts by the coordinator in a GPT window, as a fresh delegate, before publication. |

Records written: `CONTEXT.md` (Legacy UI, Server page).

Frontier empty. Confirmed by the owner on 2026-11-11. Decision record: [decisions.md](decisions.md).
Issue drafts and publication manifest written to `issues/` (two specs, four tickets), unpublished.

## Challenge fold-in (2026-11-12)

GPT Sol's challenge is `logs/area-6/gpt-2026-11-12/reports/area9-challenge.md` in the root checkout.
Nothing was published.

| Ref | Owner | Where it is recorded |
| --- | --- | --- |
| A9-1 | 9a ticket 2 and the 9a merge also wait for 7b and 7c. Ticket 1 may start after 8a. | ADR-0017; spec opening and Delivery; ticket 2 Blocked by; decisions Q1 and Delivery; manifest `blockedByExternal`; checkpoint Q1 and Q16. |
| A9-2 | Stale-session sign-out, and the step-up dialog found with it, go to 8a ticket 2 with the account menu. 9a only proves no caller still posts `/Account/Logout`. | `logs/area-9/coordinator-handoff.md`; spec; ticket 2; decisions Q8; manifest note. |
| A9-3 | All three sign-out-everywhere outcomes, not only the two successes, go to 8a ticket 2. 9a does not copy that fallback. | Same handoff; decisions Q8; checkpoint Q8. |
| A9-4 | Checked against `SecretsApiController`, `PublicApiControllers_AcceptOnlyAnApiKey`, ADR-0005, the session fallback policy and `LocalSignOut`. Folded in. | ADR-0034; spec Routing and Testing; ticket 2; decisions Q10 and the test plan; checkpoint Q10. |

Area 8 re-check r2 reconciliation, 2026-11-13: Q8, this checkpoint, the 9a spec and the compact
handoff now use the one-time HttpOnly Login-scoped handoff for authenticator reset, including the
typed-query negative test and second-load consumption. Three sign-out-everywhere outcomes retain
their distinct query codes. 8a ticket 2 owns the implementation and both TempData removals; 9a only
verifies there is no remaining Logout caller. No issue publication or commit occurred.

## Delivery: 9a ticket 1 (#195)

Started 2026-11-23 from `6db89e5` (`main` after PR #201) on `area-9a-final-cleanup`. Claude Opus 5.5 (high).

| Commit | Subject |
| --- | --- |
| `71d267c2` | Record the Area 9 grill: no catch-all, errors in the caller's format |
| `0789f815` | Answer every failure in its caller's format instead of redirecting |
| this one | Record the delivery of 9a ticket 1 |

The grill records were applied on top of `main`'s text: `main` already carried the grill's Master key
rotation entry, and the Area 9 paragraph of ADR-0017 follows `main`'s Area 8 paragraph.

F5, F6, F7 and F11 were each shown failing on PostgreSQL against the starting code, then fixed. Local checks:
`scripts/check.sh` (unit 1046); `scripts/check.sh integration` on PostgreSQL for `UiApiFailureContractTests`,
`AuthApiTests`, `SecretsApiTests`, `ServiceAccountKeyTests`, `DocumentSecurityHeadersTests`, `OverviewApiTests`
and `AuthenticationBoundaryTests` (187); `dotnet format`; both pending-model checks; the hook tests; the
Angular gates. CI (both databases, the browser suite, `e2e:verify-guards`) is run by the coordinator.
`security-reviewer` approved with two nits, both left for ticket 2.

Deviation: `HomeController.Error`, its view and `ErrorViewModel` stay. No failure redirects there, but
`/Home/Error?code=404` is still the suites' Legacy shell. Ticket 2 deletes them with the rest of Razor and
re-homes or drops what uses the shell: `e2e/legacy-shell.ts` and its specs, three `e2e:verify-guards` probes,
`VaultFixture.LegacyShell` and its callers. Ticket 2 also deletes two leftovers outside the spec's list,
`EnumDisplay.ClearanceSelectList` and `SensitivitySelectList`.

To restart: read this section and #195. Ticket 2 (#196) starts from this branch's head.
