# Area 7 grill checkpoint

Started 2026-11-11 in worktree branch `area-7-grill` at `ab7997e` (`main` plus Area 6a, PR #147).
Model: Claude Opus 5.5 (high). Survey: [survey.md](survey.md).

## Lessons Area 6 adds (20–23)

20. **A restated headline accepts the whole recommendation.** The owner often replies by restating the
    first line of a recommendation; every sub-point then stands. Say so once, so the owner can correct it.
21. **Every PR gets GPT Sol 6.1 (xhigh) and the owner's Thermos (Cursor Cloud) before merge**, and a fix
    after either gets a re-check by the reviewer that raised it. Plan around GPT's usage windows: a GPT
    outage blocks merges, and GPT is never replaced by another model unless the owner says so.
22. **Accepted risks go into the ADR's Consequences**, not only the ledger.
23. **Cheap, non-protected work goes to Cursor** (docs, inventories, browser-suite runs) in a fresh agent
    per review. Protected code never does.

## Round 1 (asked 2026-11-11)

Q1 Split and order · Q2 Scope and disposition · Q3 Recording a denied read · Q4 One event per action,
atomic · Q5 Shares are read-only · Q6 Concurrent changes to one secret · Q7 Step-up · Q8 A taken name ·
Q9 Licence lookups on page views · Q10 Denied page reasons · Q11 Overview figures.

### Answers (2026-11-11)

"I accept everything you recommend for each Q": every round-1 recommendation stands with all its
sub-points (lesson 20).

| Ref | Accepted |
| --- | --- |
| Q1 | Three parts, each with its own spec, branch and PR: 7a Overview and Secrets list (with Create, palette search, F6, F10); 7b Secret details (every action, sharing panel, Delete, Denied, filing; F1–F5, F9); 7c Dynamic secrets (F5 issue half, F7, F8). 7a first; 7b and 7c from `main` after 7a merges, 7c may run beside 7b. ADR-0017 amended. |
| Q2 | 7a: signed-in `/` served by Angular from `HomeController.Index` (landing stays for signed-out); `/Secrets`, `/Secrets/Index`, `/Secrets/Create`; `GET /api/ui/secrets/search` with the exact contract, both palettes switched, `/Secrets/Search` deleted; Dashboard, Index, Create views and models deleted. 7b: `/Secrets/Details/{id}`, `/Account/Denied`; filing `POST /api/ui/access-requests`, outcome to `/AccessRequests?filed=1|refused=<code>`; `SecretsController`, Legacy `AccessRequestsController`, Details and Denied views deleted. 7c: `/DynamicSecrets`, controller and view deleted. Landing, Error (Area 9) and `AuditDisplay` stay. |
| Q3 | Option B: lookup writes no row (decoys still trip first); served details write `SecretMetadataViewed`; a refused by-id action (UI API and Public API) writes `SecretAccessDenied` with reasons, not critical; both fail-closed; self-service views show it without the name. ADR-0031. |
| Q4 | One event per action in the change's transaction; restore → `SecretVersionRestored`, managed rotation → `SecretUpstreamRotated`; no Viewed row on actions; audit failure `503 audit_unavailable`, nothing changed, upstream put back. |
| Q5 | Rotate, managed rotation, restore, delete need the sharing right; recipients view and reveal only; `403 not_permitted`; controls hidden from server flags. ADR-0031, `CONTEXT.md` Share. |
| Q6 | Conditional write on the wrapped data key; version number assigned inside it; `409 secret_changed`; never an audit failure; deterministic race tests on both databases. |
| Q7 | Reveal and version reveal unchanged (`403 step_up_required`, StepUpDialog, `/api/ui/step-up`); sharing (panel and approval) and dynamic issue at or above the step-up sensitivity need fresh proof; rotate, restore, delete, revoking never ask. ADR-0031, ADR-0028 amended. |
| Q8 | Names stay vault-unique; `409 name_taken` "A secret with that name already exists."; form keeps values; oracle accepted in ADR-0031. |
| Q9 | Evaluation-days reads on page views fail open (note omitted, warning logged); reveal never depends on them; gates unchanged; viewing never starts a window. |
| Q10 | Denied reasons kept word for word; disclosure of project and sensitivity accepted in ADR-0031. |
| Q11 | Critical count stays for everyone; staff count and admin links only to administrators, accounts loaded only for them; notice uses the true expiring count. |

Records written: ADR-0031 (new), ADR-0017 and ADR-0028 amended, `CONTEXT.md` Share.

## Round 2 (asked 2026-11-11)

Q12 Secrets glossary · Q13 7a UI API · Q14 7b UI API · Q15 Managed-rotation outcomes · Q16 7c UI API and
outcomes · Q17 Presentation · Q18 Test plan.

### Answers (2026-11-11)

"I accept everything you recommend for each Q": every round-2 recommendation stands with all its
sub-points (lesson 20). Full contracts are in the decision record.

| Ref | Accepted |
| --- | --- |
| Q12 | Secrets section added to `CONTEXT.md`: Secret, Reveal, Secret version, Secret rotation, Managed secret, Decoy, Access denial, Dynamic role, Dynamic credential, Lease. |
| Q13 | 7a UI API (overview, list, search with unchanged shape and `url`, create); codes `validation_failed`, `creation_denied` with reasons, `name_taken`, `audit_unavailable`; reads unaudited; administrator cards from `/api/ui/me` areas; Create sets the Legacy success flash for the Legacy Details page until 7b. |
| Q14 | 7b UI API (details, reveal, version reveal, rotate, rotate-managed, restore, shares, revoke share, delete, eligibility, filing) with the listed codes; Denied receives reasons in navigation memory, reload shows the generic text; filing keeps Area 4's redirects; revealed values only in page memory. |
| Q15 | Managed rotation: `409 not_managed`, `409 rotation_unavailable`, `402 premium_not_licensed`, `502 upstream_refused` ("…The credential is unchanged.", no audit-trail claim), put back on audit failure or lost race (`503 audit_unavailable` / `409 secret_changed`, "Nothing changed."), `503 upstream_drifted` with critical row attempted and critical log fallback. |
| Q16 | 7c UI API; issue codes incl. `access_denied` (denial row on the role), `step_up_required`, `mint_refused`, `audit_unavailable` with undo and critical log on undo failure (never the password); revoke `404 lease_not_found`, `200 { status }` with the F11 sentence on backend refusal, `503 revoked_unrecorded`; fixed `revocationNote` sentences, raw errors only in logs; password only in the issue response and page memory. |
| Q17 | Exact copies; Legacy flash and confirm texts in StatusLine and ConfirmationDialog; StepUpDialog for reveal, share, issue and Area 4 approval; change controls hidden unless `canChange`, panel unless `canShare`; true expiring count; "That share no longer exists."; new texts for `secret_changed`, `name_taken`, `not_permitted`, Q15, Q16; lists reload after writes. |
| Q18 | Unit, Production HTTP on both databases with the full authorization matrix and exact audit rows, AuditFailureFixture for every write, deterministic races on both databases, real SQL Server backend in Testcontainers, Vitest, Playwright under browser health incl. dynamic issue/revoke against the run's own SQL Server (7c's first ticket proves the e2e target and seeded role). |

## Round 3 (asked 2026-11-11)

Q19 7a delivery · Q20 7b delivery · Q21 7c delivery · Q22 Reviews per ticket · Q23 GPT Sol challenge.

### Answers (2026-11-11)

The owner replied "confirmed" to round 3. Taken as accepting Q19–Q23 in full (lesson 20) and confirming
the whole decision table and delivery outline.

| Ref | Accepted |
| --- | --- |
| Q19 | 7a on `area-7a-overview-and-secrets` from `main` after 6b merges: 1 Overview (commits the records), 2 Secrets list, search and New secret, 3 verify and PR. |
| Q20 | 7b on `area-7b-secret-details` from `main` after 7a merges: 1 recording, 2 change right, conditional write, sharing step-up, 3 Details page, 4 Denied and filing, 5 verify (decoy matrix) and PR. |
| Q21 | 7c on `area-7c-dynamic-secrets` from `main` after 7a merges, may run beside 7b: 1 issue and revoke rules (proves the e2e target), 2 page, 3 verify and PR. |
| Q22 | Opus 5.5 (high) implements all eleven; per ticket security review plus GPT Sol Standards and Spec in GPT windows; re-checks by the raising reviewer; per PR GPT Sol, Thermos, CI on the exact head, owner merge; Cursor only for browser reruns and docs. |
| Q23 | GPT Sol 6.1 (xhigh) challenge of the decision table and drafts after 20:25, run by the coordinator as a fresh delegate, before publication. |

Frontier empty. Confirmed by the owner on 2026-11-11. Decision record: [decisions.md](decisions.md).
Issue drafts and publication manifest written to `issues/` (three specs, eleven tickets), unpublished.
The coordinator publishes them after Area 6b merges and the Q23 challenge is folded in.

## After confirmation (2026-11-11)

- The owner pointed out that verification now runs through `scripts/check.sh` (AGENTS.md). Every ticket
  and spec now names `scripts/check.sh`, `integration "<filter>"` (PostgreSQL locally) and
  `e2e <specs>`, with SQL Server and the full browser suite in CI.
- The Cursor e2e dynamic-secrets probe (`logs/area-7/cursor-e2e-dynamic-probe-result.md`, coordinator's
  checkout) found the browser setup feasible with an e2e-only change; folded into 7c ticket 1, which
  still proves `CREATE LOGIN`/`DROP LOGIN` on the pinned image first.

## Coordinator note, 11 Nov 2026

Area 8 finding F1 (sign-in default landing after `SecretsController.Index` is removed) was folded into 7a ticket 2 by the coordinator, as the Area 8 grill and the owner accepted.

## GPT Sol 6.1 challenge (2026-11-12): HOLD

Report: `logs/area-6/gpt-2026-11-12/reports/area7-challenge.md` (coordinator's checkout). Each finding
was checked against the code at `ab7997e`.

| Ref | Verdict | Disposition |
| --- | --- | --- |
| A7-1 | Holds: two managed rotations both read P0, the loser's put-back resets upstream under the winner; a manual rotate during a managed one is overwritten upstream the same way. | Folded: a cluster-wide lock per backend and principal (existing `ISessionLocker`, Core port) on every write to a managed secret's value; `409 change_in_progress`; races added (Q6, ADR-0031, 7b ticket 2). |
| A7-2 | Holds structurally: the inner `catch` covers `StoreRotatedValueAsync`, which evicts after commit. Eviction is an in-memory `Remove` today, so the trigger is near-theoretical; the put-back also uses the request's token. | Folded: put back only before commit, ignoring cancellation; a unit test for commit-then-failed-eviction (Q4, 7b ticket 1). |
| A7-3 | Holds: `ShredAsync` writes the worker's stale entity with `Update`. | Folded: conditional shredding, skip when locked, races in both orders (Q6, 7b ticket 2). |
| A7-4 | Holds: the recipe is three DDL statements without a transaction; `MintAsync` throws before the undo block. | Folded: run the role's (idempotent) revocation statements for the known login on mint failure; critical log if that fails (Q16, 7c ticket 1). |
| A7-5 | Holds, and worse than stated: `AuditAction` is stored as an int, so 7b and 7c would each add a number. | Folded: the event, label and self-service redaction move to 7a ticket 1; shared-file reconciliation rule for 7b, 7c and 8a (delivery outline, specs). |
| A7-6 | Holds: 9a deletes every view and the conventional route but waits only for 8a. | Owner, 2026-11-12: already decided in Area 9's records (9a ticket 1 after 8a; ticket 2 and the merge also wait for 7b and 7c); cited in the delivery outline. |
| A7-7 | Holds: the problem list attached the row only to `access_denied`. | Folded as Q3's reading, confirmed by the owner on 2026-11-12: row for `access_denied` and `not_permitted`; none for `step_up_required` (Legacy records only `StepUpVerified`/`StepUpFailed`), `not_found`, lists or eligibility. |
| A7-8 | Holds: one Internal role cannot exercise step-up. | Folded: Internal and Secret roles, the stale saved administrator session, evaluation-window assertions; access and step-up checks before the licence gate (7c ticket 1). |
| A7-9 | Holds: `ListDueLeasesAsync` selects only expired leases. | Owner accepted the delay (Q25): sentence corrected, ADR-0031 Consequences, 7c ticket 1 test. |
| A7-10 | Holds: restore re-seals the stored value only; the backend keeps the newer password. | Owner chose refusal (Q24): `409 restore_managed`, Restore hidden, a named 7b fix (ADR-0031, 7b spec, tickets 2 and 3). |

Owner's answers, 2026-11-12: A7-6 already decided in Area 9's records; A7-7 reading confirmed; A7-9 and
A7-10 accepted as recommended. All ten findings are resolved; the manifest now waits only for Area 6b's merge.

## GPT Sol 6.1 re-check r2 (2026-11-13): HOLD

Report: `logs/area-6/gpt-2026-11-12/reports/area7-recheck-r2.md` (coordinator's checkout). Closed: A7-1,
A7-2, A7-3, A7-5, A7-6, A7-7; Q24 and Q25 stay owner-accepted. The earlier "all ten resolved" line
above was premature. Folded:

| Ref | Fix |
| --- | --- |
| A7-4 | Any failed or uncertain mint (error, lost acknowledgement, cancellation, in any statement) runs the cleanup without the request's token; failed or timed-out cleanup answers `502 mint_incomplete` without claiming removal, logs role and login at critical level, and points the operator to `docs/INCIDENT_RESPONSE.md`; the leftover-login risk is in ADR-0031's Consequences. 7c ticket 1 tests each case. |
| A7-8 | 7c ticket 1 proves the e2e target through the still-Legacy page with the Internal role, plus the server step-up and evaluation assertions; the Internal+Secret browser, StepUpDialog, evaluation and one-time-display flow moved to ticket 2 with every assertion. |
| R2-1 | A successful retry by the sweep closes the lease as Expired (`ReapDueLeasesAsync` passes `LeaseStatus.Expired`); a failed retry closes it as RevocationFailed, unchanged. |
| R2-2 | Q26: effective TTL clamped to the role's maximum and to 1440, omitted or explicit; leases issued before 7c keep their expiry. Folded rather than asked: it matches the Legacy form's 1–1440 limit, tightens only, and keeps longer roles issuing. |

## GPT Sol 6.1 re-check r3 (2026-11-13): HOLD

Report: `logs/area-6/gpt-2026-11-12/reports/area7-recheck-r3.md` (coordinator's checkout). Closed: A7-4,
A7-8. The r2 folds of R2-1 and R2-2 fixed Q25, Q26 and 7c ticket 1 but left other records contradicting
them. Folded:

| Ref | Fix |
| --- | --- |
| R2-1 | Q16, Q25, 7c's spec contract, ADR-0031 and 7c ticket 1 now say the lease closes as Expired only if the sweep's retry succeeds and its close is recorded; a failed retry (a non-idempotent recipe can fail on repeat) closes it as RevocationFailed; while auditing stays unavailable neither close is recorded and each later sweep retries (`DynamicLeaseWorker` logs and retries next interval). Q25's display delay is unchanged. AuditFailureFixture cases cover a sweep under rejected writes and after restore. |
| R2-2 | The 1440-minute bound in Q16, Q25, the spec, ADR-0031 and 7c ticket 1 is limited to leases issued after 7c; older leases keep their expiry. Q26 corrected: an omitted TTL issues the role's default capped at 1440 (default 30, maximum 2880 issues 30). Ticket 1 acceptance adds that case and a stored 2880-minute lease staying Active past 1440 until it expires. Q26 is the owner's decision (2026-11-13): keep the cap; a lease longer than 1440 minutes is issued at 1440, and leases issued before 7c keep their own expiry. |

Owner's answer, 2026-11-13: Q26 keeps the cap (recorded in decisions.md). Waiting for GPT Sol 6.1's
re-check of R2-1 and R2-2; nothing else is pending in Area 7's records.

## Opus re-check r4 (2026-11-13): READY

Report: `logs/area-6/gpt-2026-11-12/reports/area7-recheck-r4-opus.md` (coordinator's checkout). Claude Opus
5.5 substituted for GPT Sol 6.1 on the owner's word. R2-1 and R2-2 CLOSED; design READY. The manifest's
GPT re-check gate is met by this Opus substitute on the owner's word; publication still waits for
Area 6b's merge. Its P3 nits folded: ADR-0031 "after this decision" → "after 7c"; 7c spec user story 2
adds "and at most 1440 minutes"; the Testing summary's sweep close points to Q25. 7c ticket 1's notes
record that `RevokeAtBackendAsync`'s catch re-closes an audit failure on the success close as
RevocationFailed; the causes stay apart and the security reviewer checks it.

## Published (2026-11-13)

On the owner's word, Area 7 was published on 13 November 2026, before Area 6b merged: specs 7a #158,
7b #159, 7c #160; tickets 7a #163–#165, 7b #166–#170, 7c #171–#173 (#161 and #162 are Area 8's specs).
`issues/manifest.json` records each number. 7a started the same day on `area-7a-overview-and-secrets`,
stacked on the Area 6b branch at `3f13097`; the coordinator merges 6b's later changes into it.

## Delivery: 7a ticket 1 (#163)

Branch `area-7a-overview-and-secrets`, from `3f13097` (the Area 6b branch, owner override). Claude Opus 5.5
high implemented the ticket. GPT Sol 6.1 xhigh completed the delivery on 15 November after the usage reset.

Commits:

- `06c86a0` Record Area 7's grill decisions, ADR-0031 and its published issues. The ADR-0017, ADR-0028
  and `CONTEXT.md` edits applied as a three-way patch with no conflicts.
- `bfb5ca8` Serve the Overview from Angular with administrator-only figures.
- `0b819c3` Prove the Overview through observed results and real routing.
- `0527bfe` Check the Overview's highlights by their rendered colours.

What changed: `GET /api/ui/overview`; signed-in `/`, `/Home` and `/Home/Index` serve the Angular
document (`HomeController.Index`, through `AngularUiRoutes.PrepareIndex`), signed out the landing page;
the Legacy Dashboard view and model are deleted. Accounts are loaded and the vault's activity is read
only for administrators; anyone else's feed is read by account id (security review S1: a reused
username no longer inherits a deleted account's rows). The notice counts every expiring secret (F10).
`AuditAction.SecretAccessDenied = 18`, label "Access denied" (warn), activity title "Was refused access
to a secret"; `ActivityDescriber.ResourceNameShownToActor` withholds its secret's name in My activity,
the Overview's own feed and the Your data export.

For #164: this ticket moved #143's `AuditApiTests.The_Legacy_sidebar_link_keeps_its_destination` from
the Legacy Dashboard at `/` to the Legacy sidebar on `/Secrets`. #164 retires the Legacy Secrets list,
so that link check must move again, to an Angular route or the UI API.

Verification: `scripts/check.sh` passed, with 938 unit tests after the first review fixes. Full
integration suites at `bfb5ca8` passed 1241 tests on SQL Server and 1241 on PostgreSQL, one suite at a
time. The affected browser specs passed 267 tests. After the review fixes, the Overview integration
filter passed 14 tests on PostgreSQL and its browser spec passed 40 tests. Angular lint, formatting,
tests, API type check, build checks, production build and high-severity audit passed. Both pending-model
checks found no changes. The full integration suites were not repeated after the test and comment fixes.

Security review by Claude Opus 5.5 high closed S1. Spec review and its re-check by GPT Sol 6.1 xhigh
returned READY. Standards review closed STD-1, STD-2, STD-3, STD-6 and STD-7 on its second pass;
the fresh re-check pinned to `0527bfe` closed STD-4 and STD-5 with no new findings and returned READY.

For the final Standards re-check and detailed commands, counts and limitations, see
`logs/ticket-163/final-report.md` and `review-ledger.md` (ignored). To restart, read this section on
`area-7a-overview-and-secrets`. The coordinator merges Area 6b's later changes before #164 starts.

## Delivery: 7a ticket 2 (#164)

Implemented from `94b9eb6` on `area-7a-overview-and-secrets`, verified 15–16 November 2026.
Claude Opus 5.5 high implemented the feature and several browser corrections. GPT Sol 6.1 xhigh
continued verification and made the validation-boundary and final browser-test corrections.
Status: implementation verified, delivery blocked on the broader Users browser dependency gate
and the unexplained earlier Audit checkpoint failure.
Nothing pushed, no PR or issue comment, and no CI result claimed.

Code commits:

- `dc18823`: Users waits for Overview before ending the saved administrator session.
- `269f323`: Angular Secrets and New secret, UI API list/search/create, both palettes, taken-name
  conflict, retired Legacy handlers/views/models, literal sign-in target and HTTP/browser coverage.
- `b0cc7f1`: deterministic sign-in budgets and debounce, literal test expectations, stored-time TTL
  check and the SecretDetails page object.
- `4659a71`: Legacy field wording, empty-TTL refusal, row clicks, deletion outcome and textarea style.
- `1ac8122`: visible palette badge text and rendered colour checks.
- `03ef186`: native fractional and out-of-range TTL refusal.
- `8366abd`: request-type validation opt-out, so explicit field messages reach the caller; malformed
  bodies still use the non-quoting factory.
- `6bf4929`, `bf85c57`, `a49e265`: cached chunks and real response capture in browser helpers.
- `d0b0fde`: exact Secrets subtitle text without template whitespace.
- `2ab0e95`: wait for the filtered row before checking Tab focus.
- `c524be3`: distinguish the sensitivity badge from the palette's identically named result kind.
- This checkpoint, committed last.

The list and search retain the existing access filter, drop decoys and write no per-row audit.
Search retains the Legacy bare-array contract and ordering. Create enforces the existing policy,
answers `409 name_taken`, and commits its one creation event with the secret. Audit failure stores
nothing. Successful creation opens Legacy Details with its existing success message. There is no
EF model change or migration.

Verification:

- Locked restore, Release build with zero warnings and 949/949 .NET unit tests through
  `scripts/check.sh`; focused PostgreSQL HTTP filter 276/276.
- Full integration with collection parallelism disabled: SQL Server 1294/1294 in 16m13s and
  PostgreSQL 1294/1294 in 13m30s. Later changes are Angular presentation and browser tests/helpers.
- Angular lint, formatting, 578/578 unit tests, 15/15 build checks, API type check, production build
  and high-severity audit pass. Ten moderate audit findings remain visible. Both pending-model
  checks, .NET formatting and six hook tests pass.
- Final Secrets browser run r7: 36/36, including its ten cases and 26 Chromium dependencies.
  Both themes, both palettes, keyboard, native TTL refusal, conflict and success paths pass.
  The ticket-owned production image was reused for this test-only correction and then removed.
- Run r6 separately passes all 14 Overview and 15 Audit cases. Its Secrets selector failure was
  corrected and passed in r7. All failed and partial runs remain under `logs/ticket-164/`.
- Disposable UI proof passes real sign-in, list, opening a non-decoy without revealing its value,
  and returning to the list. Screenshot and accessibility snapshot were inspected. An isolated
  copy of the verification harness avoids an existing owner's PostgreSQL container and uses a
  relative IPC socket because this worktree's absolute socket path exceeds the platform limit.
  Original skill files are unchanged. The verification vault, browser and containers are stopped.

The baseline taken-name proof reached the unique index and redirected to error 500, rather than
the spec's predicted 503. The valid baseline proof was obtained after implementation, following
earlier host-start failures; no chronological failing-first claim is made. F1 did not reproduce
with either Index alone or the whole Legacy Secrets controller removed. The owner explicitly
accepted documenting that non-reproduction and retaining the literal `/Secrets` target.

Security r1–r3 by Opus approved all protected changes. GPT Sol 6.1 xhigh Standards r5/r6 and Spec r4
are READY, with no required findings open. Fresh same-model re-checks of the small GPT corrections
are disclosed in the reports. Details, exact commands, subject lengths and three readability
excerpts are in `logs/ticket-164/final-report.md` and `review-ledger.md`.

Remaining blocker: browser run r5 passed 193 cases, failed two and left 84 unrun. Its Secrets
keyboard failure is fixed. The other failure is the existing Profile scenario at
`e2e/profile.spec.ts:1355`: a committed password with failed session renewal answered 503, but the
expected sign-in-required alert did not appear and the form stayed disabled. This blocked the
Users dependency chain, so the owner's complete Overview/Users ordering gate is not green.
No protected Profile implementation was changed, and no retry or skip was added to hide it.

The coordinator also keeps r3's Audit checkpoint failure open until it has a diagnosis. In that
run, Create signed checkpoint stayed disabled and no request was sent within 15 seconds. Later
passes in r5 and r6 establish those runs' results but do not explain r3. Its retained trace and
screenshot remain evidence for the responsible Area 6b investigation. Audit production code was
not changed here.

To restart, confirm this branch and checkpoint with a clean tracked tree. Read the final report
and ledger. Obtain a root-cause correction for the Profile scenario in its responsible scope,
diagnose the earlier Audit failure, then finish the owner-required Users dependency run.
Keep the shared Docker lock, .NET 10.0.404
and Node 24.22.0. The coordinator owns the next action; do not start #165 here.

## Delivery: 7b ticket 1 (#166)

Branch `area-7b-secret-details`, from `94b9eb6` (7a ticket 1 merged with Area 6b; owner override: 7b stacked
on 7a before 7a merges). Claude Opus 5.5 high implemented the ticket.

Commit: `d142bdd` Record secret refusals as denials and each action as one event.

What changed: lookup by id records nothing (decoys still trip first); a served Legacy details page records
`SecretMetadataViewed`; a refused by-id action on the Legacy pages and `GET /api/v1/secrets/{id}` records
`SecretAccessDenied` with the reasons, as do the Legacy share and revoke refusals for a non-sharer; both rows
fail closed. Restore and managed rotation commit `SecretVersionRestored` and `SecretUpstreamRotated` in the
change's transaction: `ISecretRepository.RotateAsync` takes the action and saves through the interceptor's
operation path, which replaces the generic secret row for the secret it names. Managed rotation reports
"unchanged" only for a certain SQL Server refusal (statement never sent, or errors 15151, 15247, 15114–15118);
any other failure puts the old password back ignoring cancellation; a store failure is put back only when a
fresh read shows the archived version did not commit; drift is recorded critical, or logged critical when
that row fails. `AuditChain.CommitAsync` now ends its transaction when the commit fails. The audit-failure
vault gets a real SQL Server login target (`VaultFixture.HasManagedTarget`; a PostgreSQL run starts one
SQL Server for it). No model change, so no migrations or database scripts.

For the coordinator's merges: `SecretsController.cs` is also changed by #164 (here only the by-id actions
and `CheckAccessAsync`); `ISecretRepository.RotateAsync` gained a parameter, so any new fake repository
from 7a or 7c needs it; `SecretRepository` now takes `AuditSaveChangesInterceptor`.

Verification: F1 and F3 failed first at `94b9eb6` (5 unit tests; 8 of 13 integration tests on PostgreSQL,
run from a copy of the start code with the new tests). On the final tree `scripts/check.sh` passed (0
warnings, 948 unit tests), `dotnet format` clean, `scripts/check.sh integration` with the ticket filter
passed 17 on PostgreSQL. Full integration suites, one at a time: SQL Server 1265 passed before the
Standards edits; PostgreSQL 1268 passed before the last two comment-only edits. Both pending-model checks,
the hook tests and every Angular check (555 Vitest) passed. No browser spec changed.

Reviews: security (Opus 5.5 high) found S1, S2 and R2-1..R2-4, all fixed and closed on its third pass.
Spec (GPT Sol 6.1 xhigh) READY after SPEC-1 (evidence) closed. Standards (GPT Sol 6.1 xhigh) READY after
STD-1..3 closed across two re-checks. Details: `logs/ticket-166/final-report.md` and `review-ledger.md`
(ignored). To restart, read this section on `area-7b-secret-details`; ticket 2 (#167) starts after the
coordinator accepts this one.

## Delivery: 7a ticket 3 (#165)

#164's blockers closed outside this ticket. The coordinator's distinct ordering gate passed all 279
browser cases on `3876205`, merged `main` (`6c6194a`) in `f1aa53f`, and corrected one merged test in
`01baac2`. The r3 Audit delay stays a local observation: most likely an I/O stall in the emulated
SQL Server under host load, not proven, and not seen again.

#165 started from `01baac2`. It fixes one finding of the part's security review: a member's
Overview feed named the administrator who unlocked their account (SEC-165-1). The own feed now names
the member; its test failed first with "vault-admin". The rest is tests and docs. The Overview shape
test no longer depends on other tests' secrets. Visual checks in the sign-in spec moved into
`visual parity:` tests. New tests cover `Home/Error` and the links into Overview, Secrets and New
secret from both shells' sidebars and palettes, plus Details' Back to list. `CONTEXT.md` and
ADR-0017 match the code.

Security-reviewer (Opus 5.5 high) reviewed the whole part, then re-checked SEC-165-1 and `01baac2`:
both closed. GPT Sol 6.1 xhigh Standards and Spec reviewed the whole part, re-checked each fix, and
approved the part. P-1, the Your data export carrying the unlocking administrator's IP, predates 7a
and is an owner follow-up after 7a merges. P-2 is Area 8 F2 in #175.

Local checks on the source of the final head, all passing:
- the part's PostgreSQL integration filter, 279 tests;
- 949 unit tests;
- the Secrets and Overview browser specs with their Chromium dependencies, 65 cases;
- format, both pending-model checks, the hook tests, and the Angular lint, test, build and audit.

The PR body is `logs/ticket-165/pr-body.md`. Details are in `logs/ticket-165/final-report.md` and
`review-ledger.md`. To restart, confirm the final head and a clean tracked tree. Next: push, CI on
the final head and the PR. Those are the coordinator's. 7b is stacked on this branch and has to take
its final head.

## Delivery: 7b ticket 2 (#167)

Branch `area-7b-secret-details`, from `5ff08c7` (final 7a head `325bab8` merged into 7b by the
coordinator). Claude Opus 5.5 high implemented the ticket; one Opus usage-limit pause on 17 November
preserved the work unchanged (`logs/ticket-167/rate-limit-checkpoint.md`).

Commit: `af432b8` Refuse recipients' changes and stale writes; step up sensitive shares.

What changed: rotate, managed rotation, restore and delete need the change right (`SecretChangeRight`:
administrator, or a member of the secret's project), as sharing already did; a share recipient is refused
with one fail-closed `SecretAccessDenied` row ("A share grants read access only."). `SecretRepository`
rotates and shreds only while the stored wrapped data key (and, for a shred, the past expiry) is still what
was read, checked under the audit chain in the write's own transaction, and numbers the archived version
inside that write; the loser gets `SecretChangedException`, and managed rotation puts the password back.
Every write to a managed secret's value takes `IManagedPrincipalLock` (the session locker, keyed by backend
and principal) without waiting, re-reads the secret under it and holds it through commit or put-back;
another writer gets `ChangeInProgressException` before the backend is touched. Restoring a managed secret's
version is refused with `RestoreManagedException` and changes nothing. The lifecycle worker shreds through
`ISecretService.ShredIfExpiredAsync` and leaves a locked or changed secret for the next sweep. Legacy Grant
and UI API approve need fresh proof at or above `StepUp:MinimumSensitivity` (`403 step_up_required` on the
UI API); Access requests opens StepUpDialog and sends the approval once more. No model change.

The Legacy actions answer these refusals as Details flashes with the spec's sentences; the `409` and
`403 not_permitted` codes arrive with #168's `/api/ui/secrets/{id}` endpoints.

For the coordinator's merges: `ISecretRepository.RotateAsync` takes the read wrapped key, `ShredAsync`
returns whether it shredded, `ReloadAsync` is new and `CountVersionsAsync` is gone; `SecretService` takes
`IManagedPrincipalLock`; `SecretRepository` takes `AuditChain`.

Verification: the red proof stays tied to `32740c7` (25 PostgreSQL tests, 12 failing for F2, F4, F5's
sharing half, the second managed rotation and the managed restore; `logs/ticket-167/`). On the final
source `scripts/check.sh` passed (978 unit tests), `dotnet format` clean, both pending-model checks and the
hook tests passed, every Angular check passed (581 Vitest) except `npm audit`, which fails on every branch
for picomatch GHSA-fc5q-39xc-vjpf (no patched version). The ticket's PostgreSQL filter passed 115 before
three wording-only edits. Full suites, SQL Server and the browser suite run in CI on `af432b8`.

Reviews: security (Opus 5.5 high) found no critical, high or medium issue and four lows: R1-3 and R1-4
fixed, R1-1 (a rotation can clear the expiry-notice marker, predates this ticket) left for the owner, R1-2
(Legacy shows change buttons to a recipient) left to #168; a fresh re-check accepted every resolution. GPT
reviews the whole part in #170. Details: `logs/ticket-167/final-report.md` and `review-ledger.md`
(ignored). To restart, read this section; ticket 3 (#168) starts after the coordinator accepts this one.

## Delivery: 7b ticket 3 (#168)

Branch `area-7b-secret-details`, from `274a064` (main `fb39fc2` merged into 7b by the coordinator).
Claude Opus 5.5 high implemented the ticket, with one Opus 5.5 high delegate for the Angular page.

Commit: `8f11597` Serve a secret's details from the Angular UI and retire the Legacy page.

What changed: `SecretDetailsController` answers `/api/ui/secrets/{id}` (page, reveal, version reveal,
rotate, managed rotation, restore, share, revoke, delete), no-store, under global antiforgery. Every action
runs the access check, then the change right for a change; each refusal writes one fail-closed
`SecretAccessDenied` row; reveal, version reveal and a sensitive share answer `403 step_up_required`; a decoy
answers exactly as an unknown id and trips the trap. Another secret's version or share answers as missing.
The managed licence note (`ManagedRotationNote`) only peeks at the evaluation window and leaves the note out
when the read fails. An existing grant raises `ShareExistsException` (`409 share_exists`).
`SecretRepository.DeleteAsync` now raises `AuditWriteFailedException` on a failed save, so a delete whose
audit row cannot be stored answers `503 audit_unavailable` (it answered 500). The Angular page
(`/Secrets/Details/{id}`) copies the Legacy words, shows change controls only from `canChange` and
sharing only from `canShare` (#167's R1-2), hides Restore for a managed secret, keeps a revealed value only
in page memory and opens StepUpDialog when asked. New secret announces the stored secret through router
state; the 7a flash bridge is gone. The Legacy `SecretsController`, Details view and view models are
deleted. No model change.

Legacy shell touched: `_Layout.cshtml`'s Sharing links now point at the Angular paths.

Deviations: access_denied opens `/Account/Denied?secretId=` without the reasons in navigation memory,
because the Denied page stays Legacy until #169; the page's response carries `evaluationKnown` so a failed
evaluation read leaves the note out; the theme test checks the theme attribute and a screenshot, with no
`visual parity:` colour checks. Not covered over HTTP: `402 premium_not_licensed` and
`409 rotation_unavailable`, which the fixtures cannot produce without side effects (Vitest covers the page's
handling).

Verification on the final source: `scripts/check.sh` passed (981 unit tests), `dotnet format` clean, both
pending-model checks and the hook tests passed, the Angular checks passed (623 Vitest, lint, build checks,
`check:api-types`; `npm audit --audit-level=high` passed with 10 moderate findings). The ticket's PostgreSQL filter passed 352 tests (30 classes); after the last test-only
fix its three cases passed again. Browser specs were written but not run locally (`secret-details.spec.ts`,
its own Playwright project); they, the full suites and SQL Server run in CI.

Reviews: security (Opus 5.5 high) found no defects; one low (`.First` in the version label) and two
coverage gaps were fixed. A fresh re-check of the fixes found no defects, one low test gap (fixed and
re-checked OK) and one non-security label: two simultaneous deletes give the loser 503 rather than 500,
left as is. GPT reviews the whole part in #170. Details: `logs/ticket-168/final-report.md` and
`review-ledger.md` (ignored). To restart, read this section; ticket 4 (#169) starts after the coordinator
accepts this one.

## Delivery: 7b ticket 4 (#169)

Branch `area-7b-secret-details`, from `372aea9`. Claude Opus 5.5 high implemented the ticket. Two #168 CI
fixes came first, each as its own commit: `7dedfcd` and `66cd930` declare the browser's intended 403 when a
refused recipient opens a secret.

What changed: the UI API answers `GET /api/ui/access-requests/eligibility?secretId=` (records nothing) and
`POST /api/ui/access-requests` (204). Filing takes the denial reasons from the vault's own access check and
refuses an unknown field (400), so a caller cannot supply them. It writes only Area 4's `AccessRequested`
row; when that cannot be stored it answers 503 `audit_unavailable` and stores nothing. Refusals answer 409
with `share_cannot_help`, `already_able_to_open`, `already_pending`, `reason_too_short` or
`secret_not_found`. A decoy trips and answers 404 on both endpoints. The Angular page at `/Account/Denied`
copies the Legacy words. It lists the policy's reasons only when a refused open hands them over in
navigation memory (Secret details now does), so a reload shows the generic text. It checks `secretId` as a
GUID, offers the form or the refusal sentence, shows "Manage trusted networks" from the server's areas, and
opens Access requests with `filed=1` or `refused=<code>`. No model change.

Deleted: `AccountController.Denied`, `RequestRefusalMessageAsync` and `DenialReasonsTempDataKey`;
`Views/Account/Denied.cshtml`; `AccessDeniedViewModel`; and the Legacy `AccessRequestsController`
(`POST /AccessRequests/Create`). `/Account/Denied` stays the cookie's `AccessDeniedPath`, now served by
Angular, and `MayLeadIntoAccount` still allows it. No Legacy-shell check was touched on this branch. 7c's
checks that were re-homed to `/Account/Denied` now land on the Angular page, so the merge must re-check them.

Deviations: an unknown secret answers eligibility with 200 `secret_not_found` while a decoy answers 404, as
Legacy behaved. The owner question is in the ledger for #170. The theme tests check the theme attribute
and a screenshot, with no `visual parity:` colour checks (override 4).

Verification on the final source: `scripts/check.sh` passed (981 unit tests), `dotnet
format` clean, both pending-model checks and the hook tests passed. The Angular checks passed: 633 Vitest
tests, lint, prettier, build checks, `check:api-types` and build; `npm audit --audit-level=high` also passed with 10 moderate findings.
The ticket's PostgreSQL filter passed 291 tests (14 classes). Browser specs were written but not run locally
(`access-requests.spec.ts`: Denied with reasons → filing → Access requests, the under-cleared refusal
sentence, keyboard, both themes, the titled document). They, the full suites and SQL Server run in CI.

Reviews: security (Opus 5.5 high) found no defects. One low (the eligibility comment claimed a decoy and an
unknown id answer alike; reworded) and one coverage gap (stale cookies on the two new endpoints) were fixed.
A fresh re-check of the fixes found no defects. GPT reviews the whole part in #170. Details:
`logs/ticket-169/final-report.md` and `review-ledger.md` (ignored). To restart, read this section; ticket 5
(#170) starts after the coordinator accepts this one.

## Delivery: 7b ticket 5 (#170)

Branch `area-7b-secret-details`, from `04afbe3`. Claude Opus 5.5 high verified the part and prepared its PR.

Commits:
- `f2cc2a4` merges `main` `de3a968` (part 7c). `SqlServerBackend`'s nothing-sent callback now receives the
  failure, so managed rotation reports a certain refusal with its cause. Test vaults have one SQL Server target
  (7c's `DynamicSecretsTarget`). Each resolution is in `logs/ticket-170/review-ledger.md`.
- `043733f` and part of `12a2bfb`: `/Account/Denied` is now Angular, so the checks that need a Legacy page
  open the error page (`/Home/Error?code=404`), the last signed-in Legacy page until Area 9. Browser specs
  name it once in `e2e/legacy-shell.ts`, which also declares the 404 the browser logs; .NET tests in
  `VaultFixture.LegacyShell`. Area 9 changes those two lines.
- `1f06e27`: an unknown secret answers eligibility and filing with the decoy's 404 `not_found`, byte for
  byte (owner, 20 Nov); `secret_not_found` is gone. A decoy still revokes the session and blocks the range.
- `12a2bfb` and `fa4278b`: review fixes, and `DecoyMatrixTests`: a secret made a decoy after it has a
  version, a share and a pending request trips on all nine by-id endpoints, eligibility and filing, each
  answer equal to an unknown id's, and no secret list shows it.
- CONTEXT.md, ADR-0017, ADR-0031 and decisions.md match the final code; ADR-0028 already did.

Reviews of `de3a968...HEAD`: GPT Sol 6.1 was out until 22 Nov 17:33, so security, Standards and Spec ran
on fresh Claude Opus 5.5 high sessions standing in for it. Security approved (two lows fixed); Standards
1 must-fix and 4 nits (fixed, Legacy-parity comment wording kept); Spec 0 not met, 4 partly met (a browser
test wrongly moved to the error page, the decoy matrix and doc details fixed; the rest listed for the owner).
One re-check confirmed every fix; its two new items were fixed in `fa4278b`.

Local checks on the final source: `scripts/check.sh` (unit) and the part's PostgreSQL filter, 632 of 632
at `12a2bfb`, then `DecoyMatrixTests` with `OverviewApiTests` 28 of 28 at `fa4278b`; `dotnet format`;
both pending-model checks; the hook tests; Angular lint (with the e2e type check), prettier, 680 Vitest,
build checks, `check:api-types`, build and `npm audit --audit-level=high`. Browser specs run only in CI;
early CI 44920519912 on `043733f` found the .NET and browser Legacy-shell failures fixed since.

To restart: read this section and `logs/ticket-170/final-report.md`. The PR body is
`logs/ticket-170/pr-body.md`; the coordinator fills its final head and CI run, pushes, runs CI with both
databases, the full browser suite and `e2e:verify-guards`, and opens the PR.

## Delivery: 7c ticket 1 (#171)

#171 started from `fb39fc2` (7a merged, #186). Issuing and revoking dynamic credentials now follow
ADR-0031 through the still-Legacy Dynamic secrets page. F5 (issue half), F7, F8 and F11 were first shown
failing against `fb39fc2` (8 red tests, `logs/ticket-171/red-findings-fb39fc2.txt`).

- `ba65fbb` Require fresh proof for credentials and report mint failures truthfully.
- `054eefe` Issue and revoke a real login on the e2e run's own SQL Server.

What changed:

- A Secret or Top Secret role issues only within the step-up window. A refused role writes
  `SecretAccessDenied` on it; a step-up refusal writes nothing. Both come before the licence gate.
- A failed or uncertain mint runs the role's revocation for the known login without the request's
  cancellation: "Nothing was issued" when that works, the `mint_incomplete` text and a critical log line
  naming role and login when it does not. A lease that cannot be recorded is undone the same way and
  still fails closed. A target never reached refuses without cleanup.
- Leases last at most 1440 minutes; revoke is truthful; `RevocationError` holds fixed notes; the
  evaluation read fails open. `docs/INCIDENT_RESPONSE.md` has the operator check.
- Test seams: `SerilogSetup` reads log sinks from services (production registers none), and
  `VaultFixture.DynamicSecretsTarget` gives a vault a SQL Server target database. `DynamicSecretsFixture`
  is a new collection that runs alone; `AuditFailureFixture` now has a target too.

Security-reviewer (Opus 5.5 high) found no Critical, High or Medium. Two Low findings were fixed and
closed on re-check; the third (a non-idempotent revocation relabelled on retry) is spec-accepted. It noted
one unproven residual risk for the owner: a connection dropped while the server is still running the
mint batch could let the cleanup run before `CREATE LOGIN`, leaving an unrecorded login with no critical log.

Checks on `054eefe`: locked restore, Release build with 0 warnings, 981 unit tests; both full integration
suites, 1314 each (SQL Server, then PostgreSQL), plus the 13 dynamic-secret tests on SQL Server after the
review fixes; the dynamic secrets browser spec with its dependencies, 29 cases; format, both
pending-model checks, hook tests, and the Angular lint, format, tests, build checks, API types, build
and audit.

To restart: confirm HEAD and a clean tracked tree; evidence and the final report are in
`logs/ticket-171/`. Next is ticket 2 (#172), which serves the page from Angular and adds the Secret role's
step-up browser flow; the e2e target and both roles are ready for it.

## Delivery: 7c ticket 2 (#172)

#172 started from `3121901` (#171's head). The Angular UI now serves `/DynamicSecrets`; the Legacy
controller, view and view models are gone. This ticket names no new finding, so there was no red-first run.

- `eee3ac7` Serve Dynamic secrets from Angular, asking for proof before issuing.
- `607fa6c` Compare dynamic lease times at the precision PostgreSQL stores (CI fix, tests only).

What changed:

- `GET /api/ui/dynamic-secrets`, `POST …/issue` and `POST …/leases/{leaseId}/revoke` with the spec's codes,
  reusing `AdministratorProblemResponse` (code, title, proof, errors). Two deliberate shapes beyond the
  written contract: `licence` is null when the evaluation read fails, and the revoke answer carries
  `revocationNote` beside `status`, so a refused revoke states its fixed cause. Stored notes that predate
  the fixed sentences are answered as `BackendRefusedNote`.
- The page issues through StepUpDialog, keeps the credential only in page memory and forgets it on
  departure, pagehide or a lost session; Revoke asks with the Legacy confirmation.
- Every sidebar page is now the Angular UI's. Browser specs and HTTP tests that used Dynamic secrets as
  their Legacy page use `/Account/Denied`; 7b moves Denied too, so whichever of 7b and 7c merges second
  must drop or re-home those Legacy-shell checks. `sign-in.spec.ts` and `e2e/sidebar.ts` are among them.
- `LegacyDynamicSecrets` became `DynamicSecretsApi` (UI API calls; the target helpers are unchanged).

Checks (local, 17 November rule): locked restore, Release build with 0 warnings, 983 unit tests; PostgreSQL
integration for every touched class, 328/329 then 31/31 after an ordinal-sort test fix, and 32/32 after the
review fix; format, both pending-model checks, hook tests; Angular lint, format, 619 Vitest, build checks,
API types, build. `npm audit --audit-level=high` fails on a new upstream advisory (picomatch,
GHSA-fc5q-39xc-vjpf, every version, fix only with `--force`) with no package change here; left for the owner.
CI run 44190362488 on `7e2da26`: integration (SqlServer) green; integration (Postgres) failed three
`DynamicSecretIssueTests` by under a microsecond (the Linux runner's clock reads 100 ns, PostgreSQL keeps whole
microseconds). `607fa6c` compares at the stored precision and leaves the lease logic unchanged: the issue
response's expiry can differ from the stored one by up to 999 ns, which no screen shows (both show whole seconds;
accepted by the coordinator). CI run 44195066273 on `607fa6c` passed every required check: build-test, integration
on SQL Server and PostgreSQL, e2e (full browser suite and verify-guards), image, sbom, migrations and
agent-guardrails. frontend and security-scan are red only on picomatch GHSA-fc5q-39xc-vjpf, under the owner's
merge exception (#188). The coordinator accepted #172 on 18 November.

Security-reviewer (Opus 5.5 high): no Critical, High or Medium. L1 (raw backend text in notes stored before
#171) fixed and re-checked; I1 (backend resolved after the licence gate) is pre-existing service order; I2
accepted under Q10; I3 is the Denied merge point above.

To restart: confirm HEAD and a clean tracked tree; evidence and the final report are in `logs/ticket-172/`.
Next is ticket 3 (#173), verify the whole part and prepare its PR.

## Delivery: 7c ticket 3 (#173)

#173 started from `ab90e85`, the checkpoint recording #172's CI result. 7b had not merged. Main had moved to
`1c55064` (redesign batch 1, #187), and the coordinator merges it in before CI.

- No Legacy Dynamic secrets handler remains. `RetiredDynamicSecretsRoutesTests` covers the routes, and
  `RetiredSelfServiceRoutesTests` pins the Legacy sidebar's `href="/DynamicSecrets"`.
- `461bb1a` adds browser tests that open Dynamic secrets from both shells' sidebars and palettes. The theme
  test already covers both palettes.
- ADR-0017 and ADR-0031 record the Legacy-shell move to Access denied, fixed lease notes, a role id revealing
  that the role exists, and three review outcomes. Two closes of one lease at once can raise a false
  RevocationFailed. A lost acknowledgement on the lease insert has two cleanup outcomes. A failed mint's
  surviving login is named only in a critical log line. CONTEXT.md and ADR-0028 already matched.
- Reviews of `git diff origin/main...HEAD`, finding by finding, in `logs/ticket-173/review-ledger.md`:
  - Security-reviewer (Opus 5.5 high): no Critical, High or Medium; all five checks hold.
  - GPT Sol 6.1 xhigh Standards: 7 Low and 1 Nit. Spec: 2 Medium and 2 Low.
  - One GPT re-check of the fixes, then a second round fixed exactly as it prescribed (budget rules, 19 Nov).
- Fix commits:
  - `5dda697`: Web holds a `LeaseDto`, not the entity.
  - `98f561a`: page-object locators, and straight Vitest cases.
  - `9c68925` and the last commit: a production HTTP test of the lapsed-evaluation 402, a literal
    50-newest lease list, and every failed-cleanup combination.
  - `dc1f42e`: the lease port moves to Abstractions, history comments go, and the palette loop is split.
- Left for the owner, also in the PR body:
  - S1: a critical audit row for a login that may remain, beyond the log line.
  - S2: the lease concurrency guard.
  - G-S5 to G-S7: older standards debt in `e2e/sign-in.spec.ts` and `e2e/sidebar.ts`. 7b's #169 rewrites
    the same lines, and browser specs run only in CI.

Local checks on the final source (17 November rule):
- `scripts/check.sh` with the PostgreSQL filter: 991 unit and 46 integration tests;
- `dotnet format`;
- Angular lint, the e2e type check and the Dynamic secrets Vitest spec (36 tests);
- `npm audit --audit-level=high`, which passes because the picomatch advisory was withdrawn on 18 November.

The PR body is `logs/ticket-173/pr-body.md`; its final-run line waits for CI. Next: the coordinator merges
main, pushes, runs CI on the exact head and opens the PR.
