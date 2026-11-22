# Area 8 grill checkpoint

Started 2026-11-11 in worktree branch `area-8-grill` at `ab7997e` (`main` plus Area 6a, PR #147), with
Area 7's uncommitted records read from its worktree. Model: Claude Opus 5.5 (high). Survey: [survey.md](survey.md).

Lessons 1–23 apply (20: a restated headline accepts the whole recommendation; 21: GPT Sol and Thermos
before every merge; 22: accepted risks go into the ADR's Consequences; 23: cheap non-protected work to Cursor).

## Round 1 (asked 2026-11-11)

Q1 Split and order · Q2 Scope and disposition · Q3 Provisioning (#117) · Q4 Anonymous surface and the
blocked network · Q5 Enumeration (F4, F5) · Q6 Where sign-in lands (F1, F2) · Q7 Sign-out (F6) ·
Q8 The five leftover methods · Q9 Audit rows and #93 · Q10 Messages across a navigation.

### Answers (2026-11-11)

"I accept everything you recommend for each Q": every round-1 recommendation stands with all its
sub-points (lesson 20).

| Ref | Accepted |
| --- | --- |
| Q1 | Two parts, own spec, branch and PR: 8a Sign-in (ceremonies on the UI API, F2, F4–F6, leftover methods) from `main` after 7a merges, may run beside 7b and 7c; 8b Provisioning (#117) from `main` after 8a merges. Area 9 needs only 8a. ADR-0017 amended. |
| Q2 | Angular at the same URLs: Login, Totp, RecoveryCode, EnrollTotp, Recover; calls under `/api/ui/auth/`, passkey calls included. SSO `ExternalLogin`/`ExternalCallback` stay server actions at their URLs (Login submits a real form POST). Legacy `/Account/Logout` stays for the Error page's layout until Area 9; Angular uses `POST /api/ui/auth/sign-out`, one implementation. Blocked page stays server HTML; landing and Error wait for Area 9. Six Account views, their models and `site.js` passkey sign-in deleted. |
| Q3 | Invitations (option A): invited accounts refuse every sign-in method, SSO included, until activated; the link never passes through the outbox, logs or audit details; opening it changes nothing, only the page's POST activates. ADR-0033. Details in round 2. |
| Q4 | Anonymous Login and Recover (signed-in Login redirects server-side); Totp, RecoveryCode, EnrollTotp only with the pending cookie; sign-in pages never call `/api/ui/me`; anonymous documents set the token, identity changes reissue it; blocked network reaches only the Recover page and `POST /api/ui/auth/recover`; allowlist tests name each auth call; throttle on every unsafe auth call except sign-out. ADR-0032. |
| Q5 | Hash before the lockout check (F5); discoverable-only passkey sign-in, no username, Profile registration `residentKey: "required"` (F4); lockout DoS accepted in ADR-0032. |
| Q6 | Literal defaults: `/Secrets` after every sign-in method, `/` after break-glass; server chooses with the unchanged guard; Angular does a full page load. F1 handed to the coordinator for 7a ticket 2. |
| Q7 | Revoke, then clear; on failed revoke the cookie is cleared and `503 sign_out_incomplete` (ADR-0021's sentence, `?signOut=sign_out_incomplete` on Login); still no audit row. |
| Q8 | Remove all five methods; tests move to the verified services or direct seeding; unit fakes drop them; `SignInRefusalAuditTests` keeps every assertion. |
| Q9 | Rows and order exactly as #127; the UI API reuses `SessionIssuer` and `BreakGlassSignIn`; refusal and break-glass tests ported with the same assertions; AuditFailureFixture covers every auth call. |
| Q10 | Recovery-code notice (with codes left) and break-glass outcome shown on their own pages with Continue; restarted sign-in is a `409` that returns to Login in-app with the message; SSO failures as `?sso=failed|refused`. |

Records written: ADR-0032 (new), ADR-0017 amended. F1 noted for the coordinator in `logs/area-8/coordinator-handoff.md`.

## Round 2 (asked 2026-11-11)

New finding F9: an invitation link built from the request's host could point at an attacker's site
(reset-link poisoning). Q11 8a UI API · Q12 8a presentation · Q13 Invitation address and link base (F7,
F9) · Q14 Lifetime, resend, delivery, audit · Q15 Activation page · Q16 Invited accounts elsewhere ·
Q17 No mail · Q18 Never-used existing accounts · Q19 8b texts · Q20 Glossary · Q21 Test plan.

### Answers (2026-11-11)

"I accept everything you recommend for each Q": every round-2 recommendation stands with all its
sub-points (lesson 20). The owner asked that testing use the scripts: confirmed below (Q21).

| Ref | Accepted |
| --- | --- |
| Q11 | 8a UI API as listed in the decision record; Problem Details codes `sign_in_failed`, `code_invalid`, `passkey_failed`, `recovery_failed`, `validation_failed`, `401 sign_in_expired`, `409 sign_in_restarted`, `throttled`, `audit_unavailable`, `sign_out_incomplete`; no 401 for a wrong credential; enrollment is a POST; return URL judged only by the server; SSO a real form POST with the token in a hidden field. |
| Q12 | Exact copies of the five pages except: passkey button ignores the username; recovery-code notice with codes left and Continue; break-glass outcome with "Continue to the vault"; Login messages in StatusLine; QR stays a server PNG data URI. |
| Q13 | New user takes the person's email (unique, generic `409 email_taken`); generated addresses and `Identity:EmailDomain` retired, README updated; link base only from required `Identity:PublicUrl`; token in the fragment of `/Account/Activate`; admin-typed address risk in ADR-0033. |
| Q14 | 256-bit token, SHA-256 at rest, single use, 72 h; Resend replaces it; create and resend need fresh proof; `409 already_active`; unrecorded SMTP; commit first, then deliver, `invitation: "not_delivered"`; rows `UserCreated` (with address), `InvitationSent`, `AccountActivated`, fail-closed. |
| Q15 | `/Account/Activate` anonymous Angular; inspect POST (seed generated once, nothing spent) and complete POST (password, authenticator, token, generation, `AccountActivated` in one transaction); then `/Account/Login?activated=1`; one `404 invitation_invalid`; `400` keeps the invitation; throttled, no lockout. |
| Q16 | Invited accounts fail every sign-in route (`LoginFailed` "Account awaiting activation", SSO `NotActivated`); not counted as enabled administrators; reset `409 not_activated`; role, disable, delete, force sign-out work; Users list shows Invited / Invitation expired. |
| Q17 | No SMTP or no `Identity:PublicUrl` → New user and Resend unavailable with an explanation; no fallback. |
| Q18 | Migration in both sets: no authenticator, no passkey and no `LoginSucceeded`/`PasskeyLogin` row → awaiting activation; the trail is only read. |
| Q19 | Texts as recommended (form hint, two outcomes, email subject and body, Overview subtitle). |
| Q20 | `CONTEXT.md`: Invitation, Account activation. |
| Q21 | Test layers as recommended. Local verification uses `scripts/check.sh` per ticket: `scripts/check.sh` (build, unit), `scripts/check.sh integration "<filter>"` (affected HTTP tests on PostgreSQL), `scripts/check.sh e2e <spec>...` (affected specs). CI runs SQL Server, the whole browser suite and the guard probes. |

Records written: ADR-0033 (new), `CONTEXT.md` Invitation and Account activation.

## Round 3 (asked 2026-11-11)

Q22 8a delivery · Q23 8b delivery · Q24 Remaining texts · Q25 Browser test through a real IdP ·
Q26 Reviews · Q27 GPT Sol challenge.

### Answers (2026-11-11)

The owner replied "confirmed" to round 3. Taken, as in Area 7, as accepting Q22–Q27 in full (lesson 20)
and confirming the whole decision table and delivery outline.

| Ref | Accepted |
| --- | --- |
| Q22 | 8a on `area-8a-sign-in` from `main` after 7a merges: 1 sign-in rules on the server (commits the records), 2 sign-in pages, 3 break-glass, then the Q25 browser spec, then verify and PR. Numbered in the drafts as 1–5 with the Q25 spec as ticket 4. |
| Q23 | 8b on `area-8b-provisioning` from `main` after 8a merges: 1 invited accounts and invitations, 2 activation, 3 pages, 4 verify and PR. |
| Q24 | Activation page, Resend, Users status and mail-unavailable texts as recommended. |
| Q25 | Playwright spec through a digest-pinned Keycloak in the e2e stack (IdP MFA → `/Secrets`; vault factor → Angular Totp; no account → one message). Test infrastructure: a Cursor ticket from 8a's pushed HEAD after ticket 2; ticket 5 does it if Cursor cannot. |
| Q26 | Opus 5.5 (high) implements all protected tickets; per ticket `security-reviewer` plus GPT Sol Standards and Spec, re-checks by the raising reviewer; per PR GPT Sol, Thermos, CI on the exact head, owner merge; Cursor only for Q25 and browser reruns; closing comments with commits, CI run and `scripts/check.sh` counts. |
| Q27 | One GPT Sol 6.1 (xhigh) challenge of the table and drafts by the coordinator in a GPT window, as a fresh delegate, before publication. |

Frontier empty. Confirmed by the owner on 2026-11-11. Decision record: [decisions.md](decisions.md).
Issue drafts and publication manifest written to `issues/` (two specs, nine tickets), unpublished.

## Coordinator note, 11 Nov 2026

Area 9 finding F2 (Profile sign-out-everywhere and authenticator-reset messages carried to the Angular Login by query code) was folded into 8a ticket 2 by the coordinator, as the Area 9 grill and the owner accepted.

## Q27 fold, 12 Nov 2026

Checked against `ab7997e`. Folded A8-2, A8-3 (as the helper's real per-row outcomes, not new compensation), A8-4, A8-6, A8-7, A8-8, A9-2 and A9-3. A8-1 and A8-5 were open. ADR-0017 was not changed. Nothing was published. The authenticator-reset half of the earlier F2 fold was corrected: a query string cannot claim that notice.

The owner answered on 2026-11-12. A8-1 is accepted as recommended. A8-5 stays in Area 9's records; ADR-0017's Area 9 paragraph is not edited here. A8-3 stays with no compensation and one test per row. Area 9's handoff adds the step-up dialog as the third `/Account/Logout` caller; 8a ticket 2 moves that caller with the account menu and `StaleSessionSignOut`. The three sign-out-everywhere sentences keep their own query codes. The authenticator-reset sentence keeps its own handoff, which a typed query cannot claim, and both TempData writes are deleted.

Owner change to A8-1, 2026-11-12: replacing the invitation address spends the current token in the same
transaction as `InvitationAddressReplaced`. The address is replaced because it was wrong, so the link
already sent may have reached someone else and could activate the account, possibly an administrator,
for up to 72 hours. Replacement still sends no mail; the administrator resends to the new address.
Updated Q14 and its texts in the decision record, ADR-0033, the 8b spec, 8b ticket 1 (behaviour and an
HTTP test that the old link answers `404 invitation_invalid` after replacement, spent in the same
transaction as the row) and 8b ticket 3's confirmation. Nothing was published.

## GPT re-check r2, 13 November: HOLD

Report: `logs/area-6/gpt-2026-11-12/reports/area8-recheck-r2.md` in the coordinator's checkout.
A8-1/2/4/5/7/8 are closed in design or owner-accepted as reported. A8-3 remains open specifically
for the unrecorded block lift. The reviewer withdrew the demand to retire an already-audited session.
Changing the prior no-compensation choice is pending an owner decision; no implementation is changed.

Routine corrections applied by the coordinator: ticket 1 keeps the real database proof that address,
token spending and audit commit atomically. Ticket 2 owns the literal old-link HTTP 404 proof because
it introduces the activation endpoints. No assertion or shipping gate is removed. Area 9's Q8,
checkpoint, spec and handoff now match Area 8's one-time authenticator-reset handoff and typed-query
negative test; only the sign-out-everywhere outcomes use query codes. These edits need the fresh
raising-reviewer re-check after A8-3 is resolved. Nothing is published or committed.

Owner revision to A8-3, 2026-11-13: "Allow exact block restoration (Recommended)". Keep the
already-audited session; restore exact removed ranges on failed `IpRangeUnblocked`, with a guard
that keeps the source blocked when restoration cannot be confirmed. The coordinator folded this
into Q9, the 8a Audit/contract/test sections, ticket 1 (the shared server helper and selective failure
tests), ticket 3 (failure UI) and ADR-0032. The guard precedes removal, is shared when the blacklist
is shared, and cannot expire into an unrecorded grant. A later protected request proves refusal.
This supersedes the earlier no-compensation choice for block lifting only. The records remain
uncommitted and unpublished, awaiting the fresh GPT re-check. No protected code was written here.

## Delivery: 8a ticket 1 (#174)

Branch `area-8a-sign-in` from `fb39fc2` (main after 7a). Commits: `07df782` records the grill (this
folder, ADR-0032, ADR-0033, ADR-0017's Area 8 paragraph after Area 7's, `CONTEXT.md`); `0504d65` serves
sign-in through `/api/ui/auth` (`AuthController`, `PendingSignInController`), fixes F2, F4, F5 and F6,
hardens break-glass block lifting with a source guard and exact restoration, and removes the five
leftover Area 3 methods; this section is the last commit.

Checks on `0504d65`: `scripts/check.sh` (restore, Release build with 0 warnings, 957 unit tests);
`scripts/check.sh integration "<filter>"` on PostgreSQL, 304 passed (filter in the ticket's evidence,
`logs/ticket-174/integration-filter.txt`); `dotnet format --verify-no-changes`; both pending-model
checks (no model change, no migration); hook tests; Angular lint, format, 578 Vitest tests, build checks,
`check:api-types`, build, `npm audit --audit-level=high`. F2, F5, F6 failed first in unit tests and F4 in
integration tests on the starting commit. Both full integration suites and the browser suite run in CI.

Reviews: `security-reviewer` (Opus 5.5 high) on the whole diff and six re-checks; every finding fixed or
answered, the last re-check clean. Left for the owner: a Networks action to clear a stuck break-glass
guard (README has the manual steps), and whether `IpRangeUnblocked` should list the ranges lifted.

Restart: Angular pages come next (ticket 2, #175) on these calls. The anonymous sign-in pages' request
token still comes from the Legacy forms in the tests (`AuthApi.AnonymousTokenAsync`); ticket 2 moves it to
the Angular documents. Ticket 3 narrows the `/Account/Recover` exemption when it deletes the Legacy POST.

CI run 44137964907 on `df3fc54`: e2e passed; integration failed the same four tests on both databases
(1340/1344). Three still began a named passkey sign-in, and the trusted-SSO mailbox test still expected
`/Dashboard`. Frontend and security-scan failed on an unpatched picomatch advisory, which is not this ticket.

`4372346` keeps those proofs on the usernameless flow. A password change or a sign-out everywhere that
lands after the assertion has identified the account still refuses the proof and leaves the counter
unspent. A username on begin lists no credential. SSO lands on `/Secrets`, and that session still cannot
prepare an email change. On that commit, `scripts/check.sh` passed (957 unit tests) and
`scripts/check.sh integration` on PostgreSQL passed 343, with `CredentialGenerationTests`,
`EmailIdentitySignInTests` and `MailboxTests` added to the filter. `dotnet format --verify-no-changes`
was clean. No production code changed, and no model change. This paragraph is the last commit.

Accepted on 18 November. CI run 44191638627 on `811add2` passed every required check: build-test,
integration on SqlServer and Postgres, e2e (the full browser suite and `e2e:verify-guards`), image, sbom,
migrations and agent-guardrails. Frontend and security-scan failed only on the unpatched picomatch
advisory GHSA-fc5q-39xc-vjpf, under the owner's merge exception (#188). Security re-check 8 found no
defect in a passkey challenge begun before a password change or a sign-out everywhere and completed
after it (`logs/ticket-174/security-review-8.md`).

## Delivery: 8a ticket 2 (#175)

Commits on `7b85cd5`: `3f789ae` serves Login, Totp, RecoveryCode and EnrollTotp as Angular pages at their
Legacy URLs. Login is anonymous and sends a signed-in visit on to `SignInTarget.Choose`; the
second-factor pages need the pending sign-in. Each document issues the request token, sends `no-store`
and carries the Angular CSP. The Legacy sign-in actions, views and passkey script are gone: a signed-in POST
gets 405 (PasskeyLogin* 404) and anyone else the default-deny redirect to Login. Notices no longer ride TempData. Sign-out everywhere and an own unrecorded
force logout reach Login as `?signOut=` codes, SSO failures as `?sso=`, and an authenticator reset as a
one-time cookie that only a GET to Login takes. The coordinator's `13afec7` merges main (`1c55064`, the
redesign). `7b0b7e1`, `8a5dae1` and `b1971e1` fix the tests CI found (below); this section is the last commit.

Checks on `3f789ae`: `scripts/check.sh` (restore, Release build, 957 unit tests); both pending-model
checks (no model change); Angular lint, Prettier, 628 Vitest tests, build checks, `check:api-types`,
build, `npm audit --audit-level=high`. On `13afec7` the coordinator ran lint (with e2e tsc) and build.
Not run locally on that commit: the PostgreSQL integration filter (the Docker lock was held by #173). No
browser spec ran locally (owner's rule).

Reviews: `security-reviewer` (Opus 5.5 high) on the whole diff from `7b85cd5` and one re-check
(`logs/ticket-175/review-ledger.md`). Three nits fixed. Left for the owner and the verify ticket: the
Angular CSP keeps `form-action 'self'`, which may block the SSO form's redirect to an identity provider
on another origin, as it did on Legacy; SSO's browser path has no e2e.

Kept signatures: `VaultFixture`, `SignInProof` (adds `SendPasskeyAsync`) and `e2e/account-sign-in.ts`.
Renamed: `LegacyPasskeyCeremonyTests` to `PasskeyCeremonyTests`.

Restart: ticket 3 deletes the Legacy `/Account/Recover` POST and narrows its exemption. Three guard probes in
`browser-health.probe.ts` (two inline-script, one uncaught exception) need a Legacy page and run on
`/Account/Recover`; move them when that page becomes Angular.

CI run 44623642298 on `13afec7` failed 26 integration tests on both databases and the mailbox browser
spec. The Angular enrollment page starts enrollment with a POST, which ADR-0032's throttle meters, so a
first sign-in spends three of its address's 11 requests per 120 seconds where Legacy spent two. That is
by design. `7b0b7e1` gives such sign-ins their own address in the integration tests and clears the run's
budget in the mailbox, profile and service-accounts specs. It also corrects three of this ticket's
expectations: anonymous and pending callers get the default-deny redirect from a retired POST,
`validation_failed` carries `errors`, and Login carries the Angular policy. On `7b0b7e1`,
`scripts/check.sh integration` on PostgreSQL passed 571 tests across 32 classes, and
`dotnet format --verify-no-changes` was clean.

CI run 44631918797 on `7b0b7e1`: integration green on both databases; e2e failed one test,
service-accounts.spec.ts:217. The Angular enrollment page confirms with a fetch, where Legacy posted
a form, so the spec's next navigation could cancel it before the session cookie arrived. `8a5dae1`
makes `AuthenticatorEnrollment.confirm` wait for that response.

CI run 44918388291 on `8a5dae1`: the browser suite passed (396); `e2e:verify-guards` failed because
Angular's ErrorHandler caught the uncaught-exception probe's throw on the now-Angular Login. `b1971e1`
runs that probe on `/Account/Recover`, a Legacy page, with the inline-script probes.

CI run 44923165874 on `b1971e1`: every job green (migrations, build-test, integration on SQL Server
and PostgreSQL, frontend, e2e with `e2e:verify-guards`, security-scan, image, sbom, agent-guardrails;
commit-subjects skipped on a push). The coordinator accepted #175 on that run.

## Delivery: 8a ticket 3 (#176)

Commits on `88ab36c` (the checkpoint commit recording #175's green run): `5befceb` serves `/Account/Recover`
from the Angular UI to anyone, with an anonymous request token and `no-store`; its only call is
`POST /api/ui/auth/recover`. The page shows the outcome with "Continue to the vault" to `/`, and shows
`recovery_failed`, `recovery_incomplete` and `audit_unavailable` in the vault's own sentence without
claiming access restored. The Legacy Recover actions, view and `RecoverViewModel` are gone. The IP-block
exemption is exactly GET/HEAD `/Account/Recover` and POST `/api/ui/auth/recover`; the retired form's POST,
`/Account/Recover/` and anything under it are blocked. The last commit holds the review fixes (two
`SignInPagesTests` for a signed-in and a pending visitor's Recover token; `SignInTarget.AfterBreakGlass`
removed) and this section.

Guard probes: the two inline-script probes and the uncaught-exception probe now open the Legacy shell's
`/Home/Error?code=404` through `e2e/legacy-shell.ts`, byte-identical to part 7b's file in PR #193, and
declare its 404 with `expectLegacyShell`. What each proves is unchanged; the driver still expects all 8.

New browser spec `e2e/break-glass.spec.ts`, its own Playwright project after `chromium` and before `users`:
it creates a Top Secret account, enrolls it, blocks the run's own browser address in the run's Redis
(`e2e/break-glass.ts`), opens `/`, follows "Vault administrator? Recover access", recovers with the
password and a real TOTP code, and continues to the Overview. Its `finally` clears the block and guard.

Checks on `5befceb`: `scripts/check.sh` (957 unit); `scripts/check.sh integration` on PostgreSQL for
AuthApiTests, SignInPagesTests, ProfileAuditTests, SignInRefusalAuditTests, AuthAuditFailureTests,
RedisRecoveryLiftTests, SignInReturnUrlTests and OverviewApiTests (164 passed); `dotnet format`; both
pending-model checks (no model change); hook tests (6 passed); Angular lint with e2e tsc, Prettier,
642 Vitest, 15 build checks, `check:api-types`, build, `npm audit --audit-level=high`. After the fixes:
`scripts/check.sh` (957 unit), `dotnet format --verify-no-changes`, and `SignInPagesTests` on PostgreSQL
(29 passed). No browser spec ran locally (owner's rule).

Reviews: `security-reviewer` (Opus 5.5 high) on `88ab36c..5befceb` and one re-check (Opus 5.5 high via
Antigravity after the Claude API rate limit; `logs/ticket-176/review-ledger.md`). No important findings;
N2 and N3 fixed and verified. Left for #178: N1, recover's `audit_unavailable` uses the global sentence
even when the session row committed (owner decision); P1 (pre-existing), anonymous route-table pages are
not pinned by `AuthorizationSurfaceTests`; R1 (optional, non-blocking), no negative control asserting
`antiforgery_rejected` on `/api/ui/auth/recover`.

CI: run 44931436200 on `5befceb` had 9/10 jobs green (migrations, build-test, integration on SQL Server
and PostgreSQL, frontend, security-scan, image, sbom, agent-guardrails) with `e2e` still running when
this review-fix commit was cut; full CI on this head is pending coordinator push.

Restart: ticket 4 (#177) starts from this commit. The Legacy shell for tests is `/Home/Error?code=404`.

## Delivery: 8a ticket 5 (#178)

Started on `c5e077e` (#176 accepted). `c5e077e` itself was written by a Gemini session, which AGENTS.md
does not allow; the whole-part reviews read it like any other change and found it clean.

- `13059f8` fixes S4: Login's single sign-on POST redirects to the identity provider, which
  `form-action 'self'` blocked (#177, PR #199). While SSO is enabled, Login's policy adds the configured
  authority's origin (`ISsoAvailability.IdentityProviderOrigin`: scheme, ASCII host and port, from
  configuration only). Every other document is unchanged; `ExternalLogin` keeps its antiforgery check.
  The audit-tampering vault now runs with SSO off (`VaultFixture.SsoDisabled`) and pins the unchanged
  policy. Red on `c5e077e`: 4 Login header cases (`logs/ticket-178/s4-red*.txt`).
- `4e2f0c3` merges PR #199's branch (`2cc4408`), Cursor's Keycloak spec for #177.
- `279f0ac` merges `main` `d576dd9`. Nine conflicts, each listed in the commit message: AccountController
  and AccountViewModels (both sides deleted; the model file goes), VaultFixture (7b's `LegacyShell` kept,
  the unused Legacy form-post helper dropped), SignInReturnUrlTests and SelfServiceScopingTests (8a's UI
  API sign-in kept), users.spec.ts, playwright.config.ts, users-page.ts and ADR-0017 (both sides). The
  merge also gave break-glass.spec.ts and users-page.spec.ts the confirm verb main's dialog needs, and
  dynamic-secrets.spec.ts the 8a sign-out call. API types regenerated with no diff.
- `9c338e7` reconciles ADR-0016, ADR-0017 and ADR-0032; `fa8691c` holds the review fixes, and
  `013792f` drives the Keycloak spec through `AccountSignIn` (Standards T6 and T7).
- `50ec565` fixes the first real run of `sso.spec.ts` (CI 44948440278 on `9c338e7`): after the IdP's
  MFA the person landed on Login, because the provider's form post makes the redirect chain cross-site
  and the browser withheld the Strict session cookie from `/Secrets`. A successful callback now ends on
  a page of the vault's own whose meta refresh goes on to the target. Refusals still redirect; the
  cookies stay Strict. ADR-0032 records it.

Public test helpers for 8b: only `VaultFixture.SsoDisabled` was added; no signature changed.

Checks on the final source: `scripts/check.sh integration` on PostgreSQL with the part's filter
(`logs/ticket-178/integration-filter.txt`, 35 classes) 489 passed; `scripts/check.sh` 1040 unit;
`OverviewApiTests` 16 passed after the review fixes; after `50ec565`, 1040 unit and EmailIdentitySignIn,
Mailbox, SelfServiceScoping, SignInPages and CsrfAndSignOut tests on PostgreSQL (71 passed), and after
the nit fixes 1041 unit and EmailIdentitySignInTests (9 passed); `dotnet format`; both pending-model checks (no
model change); hook tests; Angular lint, Prettier, 750 Vitest, build checks, `check:api-types`, build,
`npm audit --audit-level=high`. Browser specs and both full suites run in CI.

Reviews (`logs/ticket-178/review-ledger.md`): `security-reviewer` (Opus 5.5 high) approves with no
findings. GPT Sol 6.1 was limited until 22 Nov 17:33, so Opus 5.5 high stood in for Standards and Spec;
one re-check of `fa8691c` and `013792f` approves. A separate `security-reviewer` pass covers `50ec565`
alone (`logs/ticket-178/security-review-hop.md`): approve, with two test nits fixed in the next commit
(alias landing asserted; a return URL with quotes and an ampersand appears only encoded). Owner items are in `logs/ticket-178/pr-body.md`.

Restart: the PR body is `logs/ticket-178/pr-body.md`; the coordinator fills in the CI run and the final
head, then opens the PR. The PR still needs GPT Sol 6.1 and Thermos before the owner merges.

## Delivery: 8b ticket 1 (#179)

Branch `area-8b-invitations`, stacked on 8a at `c5e077e`. `0a9137b` adds the awaiting-activation state
and the `AccountInvitations` table, with schema and Q18 data migrations in both sets and both
`db/*/migrations.sql` regenerated. It adds `POST /api/ui/users/{id}/invitation` (resend) and
`POST /api/ui/users/{id}/invitation-address` (address replacement), both with fresh proof, and
`invitationsAvailable` on `GET /api/ui/users/new`. It refuses invited accounts on every sign-in route,
and adds the `InvitationSent`, `InvitationAddressReplaced` and `AccountActivated` audit actions.
`POST /api/ui/users` still takes the password body, so F3 and F7 stay visible until #180. F9 was shown
failing in `logs/ticket-179/f9-failing.txt`, then made impossible: links come only from
`Identity:PublicUrl`. A Gemini session wrote `0a9137b`; a Claude Opus 5.5 high session took over after
Gemini's quota ran out. The follow-up commit holds the review fixes and this section.

Checks on `0a9137b`: `scripts/check.sh`, then `check.sh integration` on PostgreSQL with the filter in
`logs/ticket-179/integration-filter.txt`, `dotnet format`, both pending-model checks, hook tests, and
every Angular gate including `check:api-types`. After the fixes: `scripts/check.sh` and
`dotnet format`. Output is in `logs/ticket-179/opus-checks/`. SQL Server (including
`SqlServerAccountInvitationUpgradeTests`), the full suites and e2e run in CI: run 44946422470 on
`0a9137b`.

Reviews: a fresh `security-reviewer` (Claude Opus 5.5 high), then one re-check, recorded in
`logs/ticket-179/review-ledger.md`. Nothing important. Fixed: S2, the `InvitationSent` row now says
"issued"; S3, step-up records "Account awaiting activation"; S4, the unused C# copy of the selection
rule is deleted. For #182: S1, race tests for resend and replacement, and the unmapped
invitation-row unique violation.

Restart: ticket 2 (#180) starts from this commit. It switches create to the email contract, retires
generated addresses, and adds the activation endpoints and the `404 invitation_invalid` proof.

## Delivery: 8b ticket 2 (#180)

#179 closed first: its CI failed on two fixtures written before the awaiting-activation column and
the invitation upgrade. `705dc98` fixes them and pins both sides of the upgrade's generation rule;
CI 45038755227 on `705dc98` is green on all ten jobs.

The #180 commit switches `POST /api/ui/users` to `{ firstName, lastName, username, email, clearance,
project }`: the account is created awaiting activation with its invitation, `UserCreated` names the
entered address, and the 201 reports `invitation` (`sent` / `not_delivered`) and `expiresAtUtc`.
Generated addresses, `Identity:EmailDomain` and the outbox provisioning email are gone; the README
says so. `POST /api/ui/auth/activation` shows the seed, generated once and stored only while the token
is current; `POST /api/ui/auth/activation/complete` sets the password under Profile's rules (now
shared as `PasswordRules.Refusal`), enrolls the authenticator, spends the token, advances the
generation and records `AccountActivated` in one commit under the account's stamp. Both are anonymous,
throttled and on the allowlist; unknown, expired, disabled, used, replaced and readdressed tokens get
one `404 invitation_invalid`. The New user form takes Email with the decision record's hint and shows
both outcomes in place of the form, so the create no longer sets a Users notice. F3 and F7 were shown
failing in `logs/ticket-180/f3-f7-failing.txt` and are now impossible.

Helpers: the integration `SignInNewAccountAsync` and the browser suite's `signInNewAccount` invite,
read the token from the SMTP capture, activate (spending the previous TOTP step) and sign in with
password and TOTP. Every `VaultFixture` now has a mailbox and `Identity:PublicUrl`; the unavailable
case moved to its own small fixture. Changed helper signatures are listed in
`logs/ticket-180/final-report.md`.

Checks: `scripts/check.sh`, `dotnet format`, both pending-model checks (no model change), hook tests,
`npm run lint`, `npm test -- --watch=false`, `npm run check:api-types` and `npm run test:build-checks`.
`check.sh integration` on PostgreSQL in slices (`logs/ticket-180/integration-filters.txt`), the last
of each green. SQL Server, the full suites and every browser spec run in CI.

Reviews: a fresh `security-reviewer` (Claude Opus 5.5 high), then one re-check
(`logs/ticket-180/review-ledger.md`). Nothing important. Fixed: N1, a disabled account's invitation is
unusable. For #182: N2, an unrelated administrator change during activation answers "link invalid";
N3, an inspect racing a disable can show the seed once; and a line in ADR-0033 that disabling pauses an
invitation while replacing the address revokes it.

Restart: ticket 3 (#181) starts from this commit.

## Delivery: 8b ticket 3 (#181)

Started from `b301b4e` (#180 accepted, CI 45055941173 green). Commits on `area-8b-invitations`:
- `406cc2e` merges `origin/main` (`6db89e5`, Area 8a PR #201, redesign batches 3b #192 and 4 #202, and #200) cleanly, keeping `#180`'s `AccountDeletionCollection` isolation and regenerating `schema.generated.ts`.
- `0ebfb83` exposes `awaitingActivation` and usable `invitationExpiresAtUtc` on the Users list and details responses, serves `/Account/Activate` anonymously through `ServeSignInStep` with the Angular CSP and anonymous antiforgery token, adds the Angular Activation page (fragment-only token scrubbed via `replaceState`, EnrollTotp QR/manual secret, Profile password rules and strength meter, redirect to `/Account/Login?activated=1` with Login's ready sentence), adds New user's `invitationsAvailable: false` explanation, adds Users and Edit user status pills plus ConfirmationDialog + StepUpDialog flows for Resend invitation and address replacement (sending no mail itself), updates Overview's "Provision a user" subtitle, and adds `e2e/invitations.spec.ts` (create → SMTP capture → activate → sign in, resend invalidates old link, address replacement invalidates old link without sending mail and resend delivers to the new address, expired link, dark/light themes, keyboard, 300 px layout). Red runs before the code change are in `logs/ticket-181/red-run.txt`.
- This final commit fixes security review Finding 1 (exposes `isDisabled` on `UserDetailsResponse`, regenerates `schema.generated.ts`, hides the Edit user Invitation panel on disabled accounts to match the Users list) and records this section.

Model handoff note: Claude Opus 5.5 high (`claudeAgent`) produced `406cc2e` and the backend/Angular/unit/integration implementation before hitting its API rate limit right before the browser spec; the owner switched the thread to Gemini 4 Neon (High) to finish `invitations.spec.ts`, integration shape-test updates, the Finding 1 fix, verification, and delivery. Both security review passes ran on Claude Opus 5.5 high (`claudeAgent`).

Public test helpers: no existing public signatures changed. Additive helpers only: `e2e/activation.ts` (new), `smtp-capture.ts` (`invitationCount`), `users-state.ts` (`expireInvitation`), `users.ts` (`resend`, `replaceInvitationAddress`, `resendButton`), `user-role.ts` (invitation panel locators), `account-sign-in.ts` (`activatedNotice`).

Checks (after the Finding 1 fix): `scripts/check.sh` (restore, Release build, 1087 unit tests), PostgreSQL integration filter (`logs/ticket-181/integration-filter.txt`, 6 classes, 111 passed), `dotnet format --verify-no-changes`, both pending-model checks (no model change), 6 guardrail hook tests, Angular `npm run lint` (ng lint, e2e eslint & tsc, lint-checks), `npm run format:check`, 797 Vitest tests (46 files), 15 build checks, `npm run check:api-types`, production `npm run build`, `npm audit --audit-level=high`. SQL Server, full integration suites and browser specs run in CI (pending coordinator push).

Reviews (`logs/ticket-181/review-ledger.md`): fresh `security-reviewer` (Claude Opus 5.5 high, `claudeAgent`) approved `0ebfb83` with 1 Nit and 2 Notes; fresh re-check (Claude Opus 5.5 high, `claudeAgent`) approved the Finding 1 fix with no new issues. Left for `#182`: optional server-side refusal of resend/address-replacement on a disabled invited account (ADR-0033 says disabling pauses an invitation).

Restart: ticket 4 (#182) verifies the whole Area 8b part after CI is green on this head and prepares the PR.
