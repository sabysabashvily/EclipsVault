# Area 5 Administration: grill checkpoint

Started 2026-11-01 on `area-4-sharing` at `a621855`; PR #103 open, so nothing is committed. Survey:
[survey.md](survey.md). Open issues touching Administration: #92 (folded in). #66 (integration tests on
PostgreSQL) bears on any race test added here. #93 is sign-in (Area 8), and #65, #67–#69 and #95 do not
touch it.

## Round 1 (asked and answered 2026-11-01)

| Q | Topic | Recommendation | Answer |
| --- | --- | --- | --- |
| Q1 | Split | Two parts, two specs, two PRs. 5a Accounts (Users, Service accounts, #92), then 5b Vault settings (Networks, Notifications, License). Start after #103 merges; 5b branches from `main` after 5a merges. Amend ADR-0017 and CONTEXT's Area entry. | accepted |
| Q2 | Scope | All five pages and their controllers retire in their part. Dashboard and Denied links (Area 7), the Legacy licence banner (final cleanup) and `/Profile/Avatar` stay. Nothing moves to another Area. | accepted |
| Q3 | Atomic commits (#92, U1, S1) | Every database change on Users and Service accounts commits with its one audit row; `503 audit_unavailable` with nothing changed. Force logout stays revoke-then-audit (`503 revoked_unrecorded`). Email after commit, best-effort. | accepted |
| Q4 | Durable session end (U3) | Role change and disabling advance the credential generation in the same transaction; the store revocation for them goes; re-enabling never revives sessions. Amend ADR-0023 and CONTEXT. | accepted |
| Q5 | Last administrator (U2) | At least one enabled TopSecret account remains; demote (self included), disable and delete refused with `409 last_administrator`, counted on enabled accounts under a vault-wide lock in the change's transaction. | accepted |
| Q6 | Own row (U6) | Own row offers Edit and Force logout. Server refuses own Reset MFA, Disable and Delete with `409 own_account`; the reset points to Profile. | accepted |
| Q7 | Administrator step-up (X1, N2) | Granting lasting access needs a strong authentication within `StepUp:MaxAuthAgeMinutes`; removing access never does. `403 step_up_required`, a code dialog through `IStepUpService`, sign in again as the fallback. New ADR. | accepted |
| Q8 | Input rules (U4, U5, S2, X2) | Core enforces every form rule plus defined enum values, a display name that fits, key expiry 0–3650; `400 validation_failed`. Unique-index losses `409`, not `503`. Server supplies the email domain hint. | accepted |
| Q9 | API key display (S3, S4) | Raw key only in the issue response, held in page memory, never in cookie, storage, URL, log or audit. Revoke addressed under its account; another account's key is `404`. | accepted |
| Q10 | Unblock (N1) | Lift, audit, and on audit failure restore the exact block record and answer `503 audit_unavailable`; if restoring fails too, `503 unblock_unrecorded` and a critical log. | accepted |
| Q11 | Names in admin views (NT1) | Accept: Notifications, block reasons and the audit log are administrator monitoring views, unfiltered by the Secrets list's rule. Recorded as an ADR consequence. | accepted |
| Q12 | Route table (X3) | Administration paths served under `AdminOnly` (non-administrators get `/Account/Denied` as today); id paths `/Admin/EditUser/{id:guid}` and `/ServiceAccounts/Details/{id:guid}`. | accepted |
| Q13 | License page (L1) | Exact copy, installation text corrected to `ECLIPSVAULT_LICENSE` or `License:FilePath`. Commercial sentences verbatim unless the owner says otherwise. The read never starts an evaluation and never fails on licence state. | accepted |
| Q14 | Initial password (U7) | Out of scope: a follow-up issue after confirmation. Area 5 copies the form. | accepted |
| Q15 | Glossary | Add Administrator, Trusted network, Intrusion-defence block, Notification outbox; widen Step-up to cover administrator grants. | accepted, except the Step-up widening, which the answer left out (Q16) |

Round 1 records: CONTEXT.md (Administrator, Trusted network, Intrusion-defence block, Notification
outbox; Credential generation widened; Area entry), ADR-0017 and ADR-0023 amended, ADR-0028 new.
The owner's answers restated the core of each recommendation; Q16–Q18 confirm the parts they left out.

## Round 2 (asked and answered 2026-11-01)

| Q | Topic | Recommendation | Answer |
| --- | --- | --- | --- |
| Q16 | Step-up term | Widen CONTEXT's Step-up to cover an administrator granting lasting access. | accepted |
| Q17 | Unrestated parts of round 1 | Sequencing (after #103; 5b after 5a merges), the 503 codes, the step-up dialog mechanics, 409 for unique races, the server-supplied email domain. | accepted |
| Q18 | License commercial sentences | Word for word. | accepted |
| Q19 | Users UI API | Contract in decisions.md. | accepted |
| Q20 | Step-up endpoint and dialog | `POST /api/ui/step-up`; `403 step_up_required` says which proof; one retry. | accepted |
| Q21 | Service accounts UI API | As proposed. | accepted |
| Q22 | 5b UI API | As proposed. | accepted |
| Q23 | Presentation | Exact copy; Legacy confirms and flashes word for word; new TableFilter and RowMenu. | accepted |
| Q24 | Test plan | As proposed. | accepted |
| Q25 | Delivery | 5a: seven tickets; 5b: four; two specs published together, 5b blocked by 5a. | accepted |

Round 2 records: CONTEXT.md Step-up widened; [decisions.md](decisions.md) with the full table and
contracts; issue drafts in [issues/](issues/) (two specs, eleven tickets, the U7 follow-up, manifest).

## Round 3 (asked and answered 2026-11-01)

| Q | Topic | Recommendation | Answer |
| --- | --- | --- | --- |
| Q26 | Step-up endpoint policy | `POST /api/ui/step-up` requires a session, not `AdminOnly`: it proves the caller's own factor and grants nothing, and Area 7's reveals need it for every account. | accepted |

## Confirmed (2026-11-01)

The owner confirmed the decision table and both delivery outlines. Published: 5a spec #104 with
tickets #106–#112, 5b spec #105 with tickets #113–#116, follow-up #117 (needs-triage), and a
pointer on #92 to #104. Native sub-issue and blocked-by links return 404 on this repo, so each
ticket's Parent and Blocked by sections carry them, as in Area 4.

#103 merged on 2026-11-01 (main `7953598`) while the issues were being published, so #106 can start.

## Delivery: ticket 1 (#106)

Done locally on `area-5a-accounts` in the root checkout, on 2026-11-02, and not pushed. The branch and
the grill's records were already committed in `ced4414`.

- `82d27df` Save each account change with its audit row and end sessions durably.
  - Create, role change, enable, disable, unlock and delete commit through `IAccountChangeCommitter`,
    each with its one audit event.
  - Role change, disable and enable advance the credential generation instead of calling the session
    store. Enable advances it too, because ADR-0023 covers any change to the enabled state.
  - Delete no longer calls the store: the account read finds nothing.
  - Force logout stays revoke-then-audit and flashes when the audit fails.
  - A lost username race answers the form's conflict.
- This section is the ticket's last commit.

Each behaviour was shown failing against `ced4414` before the fix. Checks passed on the final tree:

- locked restore
- Release build with 0 warnings
- `dotnet format --verify-no-changes`
- unit tests: 748
- integration tests: 836, run serially with a local runsettings file after a parallel run lost one
  SQL Server fixture to memory pressure
- both pending-model checks (no migration)
- the hook tests: 6

Reviews ran on Claude Opus 5.5 at effort high: the security reviewer on the whole diff, and the Standards
and Spec reviews. The ticket had no cross-model review, because GPT Sol 6.1 is out until 2026-11-04.
The evidence, the reports and the ledger are in the ignored `logs/ticket-106/`.

Left for the owner:

- Saving an unchanged role still flashes "Their sessions were revoked…".
- A display name over 64 characters fails as the audit trail being unavailable.
- Two creates of different usernames for one person's name get the generic error page.
- `IUserRepository.AddAsync` is now used only by test fixtures.

To restart: `git switch area-5a-accounts` (head is this commit). Build the Angular UI on its `.nvmrc`
Node, then run the AGENTS.md checks. #107 (last administrator and own account) is next, on this branch.

## Delivery: ticket 2 (#107)

Done locally on `area-5a-accounts` in the root checkout, on 2026-11-02, and not pushed.

- `5bb792a` Merge main so part 5a's tests also run on PostgreSQL.
  - Brings in #66: the integration suite runs on PostgreSQL when
    `ECLIPSVAULT_TEST_DATABASE_PROVIDER=Postgres`.
- `2a65a15` Run the concurrent account-creation test on PostgreSQL as well.
  - Its wait read `sys.dm_exec_requests`, which only SQL Server has; on PostgreSQL it reads `pg_locks`.
  - With that, the suite passed 836 of 836 on each engine. No other PostgreSQL-only failure.
- `1c14c04` Keep an enabled administrator and refuse own-account admin changes.
  - Demote, disable and delete of an enabled Top Secret account recheck, under a vault-wide
    `EclipsVault:Administrators` lock, that another enabled administrator remains. The lock is taken
    through `IAuditChainLocker` after the account's lock and before the audit chain's, inside the change's
    own transaction.
  - Own Reset MFA, Disable and Delete are refused; the reset points to Profile.
  - Core enforces the New user and Edit user form rules, plus defined clearances, "First Last" ≤ 64 and
    a project key of 1–64 after trimming. Each refusal names its field.
  - A create that loses its generated address takes the next free one once, then asks to try again.
- This section is the ticket's last commit.

Each finding was shown failing against the code before the fix: 36 unit cases and 12 integration tests,
with the races on both databases. The undefined clearance never reached Core over the Legacy forms,
because MVC's enum binding refuses it first. Core's own rule is proven by the unit tests.

Checks passed on the final tree:

- locked restore
- Release build with 0 warnings
- `dotnet format --verify-no-changes`
- unit tests: 788
- integration tests on SQL Server: 851
- integration tests on PostgreSQL: 851
- both pending-model checks (no migration)
- the hook tests: 6

The integration suites ran serially with a local runsettings file. A new `SingleAdministratorFixture`
host proves the last-administrator refusal over HTTP.

Reviews: the security reviewer, then the Standards and Spec reviews.

- All three ran on Claude Opus 5.5 at effort high.
- GPT Sol 6.1 was tried for Standards and Spec and refused for its usage limit (until 2026-11-04), so
  the ticket had no cross-model review.
- The security reviewer found no defects, only one info item.
- The reports and the ledger are in the ignored `logs/ticket-107/`.

Left for the owner:

- A timed-out wait on the administrators lock or an account lock is reported as the audit trail being
  unavailable. This is the shared locker's message.
- The Legacy forms show no message when a posted clearance is undefined; they re-render and change
  nothing.

To restart: `git switch area-5a-accounts` (head is this commit). Build the Angular UI on its `.nvmrc`
Node, then run the AGENTS.md checks, the integration suite once per engine. #108 (service accounts) is
next, on this branch.

## Delivery: ticket 3 (#108)

Done locally on `area-5a-accounts` in the root checkout, on 2026-11-02, and not pushed.

- `ea292b1` Save each service-account and key change with its audit row.
  - Create, enable, disable and delete of a service account, and issue and revoke of an API key, each
    run through `IServiceAccountChanges`: one transaction holding an `EclipsVault:ServiceAccount:{id}`
    lock, taken before the audit chain's, with the change and its one event saved together through
    the audit interceptor. A change that is not committed rolls back and leaves nothing staged.
  - A create that loses the race for its name answers the form's conflict, not an audit failure.
  - Core checks key options: expiry 0–3650 days, a ceiling that is a defined level below the account
    or none, a project scope of at most 64 characters, and allowed ranges split and parsed once through
    `NetworkRules`, at most 512 characters stored. Service-account names, clearance and project key
    follow the Legacy form's rules. Each refusal names its field.
  - The Legacy key form refuses a value that fails to bind instead of treating it as no limit.
  - A key is revoked only under its own service account; revoking a revoked key records nothing.
  - The Public API's last-used stamp writes only that column. Writing the whole key row undid a revoke
    committed between the key's read and its stamp.
- `c3a6569` Check account changes through saved state and audit events.
  - Replaced unit-test staging traces with saved state and exact audit events, split the four
    missing-account outcomes, used literal timestamps, and clarified service and template names.
- This section's final update is the ticket's last commit.

Each finding was shown failing against `e310c86` before the fix: 14 integration tests on SQL Server and
6 on PostgreSQL, plus the two unbindable-option rows added after the security review. A suspected
variant, the last-used stamp re-enabling a disabled account, passed on `e310c86` and was dropped.

Validation:

- locked restore
- Release build with 0 warnings
- `dotnet format --verify-no-changes`
- unit tests: 829 after the review fixes (826 before them)
- integration tests on SQL Server: 870
- integration tests on PostgreSQL: a fresh full run passed 870 of 870 in 10m 48s.
  The earlier full run passed 811 of 870 under heavy load. Its 59 failed tests were 57 SQL Server
  execution timeouts across 11 `SqlServer*` classes and `AuthenticationSharingTests`, plus 2 Redis
  expiry checks. Those 14 classes then passed 89 of 89 on their own. All three logs are retained.
- The full integration runs preceded the final test-only and local-name changes. After those changes,
  the Release build, format, 829 unit tests and 19 affected integration tests per engine passed.
- both pending-model checks (no migration)
- the hook tests: 6

The integration suites ran serially with a local runsettings file.

Reviews: the security reviewer, then the Standards and Spec reviews.

- The initial three ran on Claude Opus 5.5 at effort high after GPT Sol 6.1 hit its usage limit.
- After the handoff, fresh GPT Sol 6.1 Standards and Spec sessions ran with `reasoningEffort: xhigh`.
  The ticket now has cross-model review. Standards found four items: three were fixed in `c3a6569`;
  the audit-mechanism convention item is answered by the owner's explicit permission to use a committer.
  Spec found no implementation gap; the pending full PostgreSQL check is now complete.
- The security reviewer found one low and one info item, both fixed.
- The original reports and finding-by-finding ledger are in the ignored `logs/ticket-108/`.

Left for the owner:

- A timed-out wait on a service account's lock is reported as the audit trail being unavailable, as
  for accounts and administrators in #107.
- The UI API for service accounts must bind its key options strictly and map each refusal's field.
- A post that omits `ttlDays` still issues a key that never expires. The Legacy form always sends it.
- The 512-character limit on stored allowed ranges is a new rule with its own message.
- The raw key is still handed to the Details page through TempData; #111 owns that.

To restart: `git switch area-5a-accounts` (head is this commit). Build the Angular UI on its `.nvmrc`
Node, then run the AGENTS.md checks, the integration suite once per engine. #109 is next, on this branch.

## Delivery: ticket 4 (#109)

Done locally on `area-5a-accounts` in the root checkout, on 2026-11-03, and not pushed.
Work continued in the separate implementation thread from the authorized uncommitted changes at
`94b5eee`. The coordinator stopped implementation; this ticket had one implementation worker.
#108 had already been pushed at that starting commit, with verified green CI run `41459789943`;
#108 and #92 were already closed.

- `418cd525` serves Users in Angular with fresh proof for row grants.
  - `/Admin/Users` is AdminOnly, with both sidebars and palettes pointing to Angular. The MVC Users
    view, model and row handlers are retired. Create/Edit stay Legacy for #110.
  - Cookie-session-only typed endpoints list users and apply the six row actions, with antiforgery,
    no-store responses and Problem Details. Enable, unlock and another account's MFA reset demand
    freshness before target lookup, independently of secret sensitivity. Removing access does not.
  - Real authenticator proof uses shared lockout, replay protection and source throttling. Renewal
    changes freshness while retaining the original session stamp, revocation version and lifetime.
  - Shared filtering, keyboard menus, confirmation and proof dialogs retain Legacy wording. Cancel
    writes nothing; successful proof retries once. Sign-in proof ends the stale session before Login.
    Own Force logout clears its cookie even when recording fails and retains the sign-in warning.
  - A separate typed notice endpoint preserves Legacy Create/Edit outcomes without changing the
    agreed Users list schema.
- This section's final update is the ticket's last commit.

The three stale-grant regressions were first shown failing against the Legacy handlers. The red
evidence, handoff files and all interrupted/failed checks remain in `logs/ticket-109/`.

Validation:

- locked restore and Release build with 0 warnings and 0 errors
- .NET format verification
- unit tests: 836
- full integration tests on SQL Server: 912, no failures or skips
- full integration tests on PostgreSQL: 912, no failures or skips
- both pending-model checks: no changes, no migrations
- hook tests: 6
- Angular lint and browser type checks, including 6 lint guard tests
- Vitest: 361 tests in 28 files
- Prettier, 15 build checks, generated API-type verification and production Angular build
- npm high/critical audit gate: passed, with 21 moderate findings still visible
- production browser tests: 159 passed, no failures or skips, in 50.0 minutes
- browser-health guard verification: all 8 intentional failures detected; verification exited 0

The database suites ran serially with four available .NET processors. Browser tests used one worker
and the unmodified production Dockerfile, built with a bounded CPU quota. The owner allowed 80–90%
total CPU and asked to avoid 90–100%; the earlier excessive throttling was removed.

An early slow PostgreSQL run exposed the shared administrator's aged proof. The fixture now refreshes
through the real proof endpoint using its real enrollment, without changing clocks or explicitly
stale cookies. A subsequent SQL Server run passed 911/912 because a history test assumed the shared
administrator had only one event. That test now enrolls its own account and preserves exact isolation
assertions. Both complete suites then passed 912/912. The first browser run was interrupted after
128 passes when its process and Docker stopped; its evidence was preserved and the full suite rerun.
A subsequent browser run passed 145 tests and timed out locating the open verification dialog in
Profile's last test, so all 13 Users tests were unrun. Its trace and log are retained. The captured
DOM had correct unique labels, and 100 replacements with production CSS passed in a diagnostic
probe. No speculative source change, timeout increase or retry was added; a fresh full unchanged
run passed all 146 inherited tests, including that case, and 12 Users tests. Its final Users test
failed because a bare status locator also matched an empty passkey region. Narrowing the page object
by the visible warning text fixed the reproduced ambiguity; a real production own-logout probe
passed, including the cookie and warning assertions. Post-fix lint/type/format gates passed. The
post-fix full rerun was interrupted after 145 passes when its runner and Docker stopped. The final
runner used an independent process session and sleep prevention; it passed all 159 tests, verified
all 8 guard probes and removed its own image. All interrupted evidence remains preserved.

Reviews covered the complete diff available from `94b5eee` at review time, including inherited work
and new source files. Review fixes and validation-only repairs were made directly afterward.

- Security, Standards and Spec each completed in a fresh GPT Sol 6.1 xhigh session. Claude Opus 5.5
  high attempts were rate-limited; the approved fallback gives same-model review, not cross-model.
- Security found no demonstrable defects.
- Standards findings were resolved with named test controls, separate filter/theme keyboard tests,
  accurate comments and a separate database-state helper.
- Spec findings were resolved by ending the stale session before sign-in and asserting the unlocked
  account's reloaded state. Small fixes were made directly, without more review delegates.
- Reports, exact commands/counts, the resolution ledger and readability excerpts are in the ignored
  `logs/ticket-109/final-report.md` and its linked evidence.

Left for the owner:

- #110 owns Create/Edit migration and their grant freshness; #111 owns service-account UI; #112 owns
  the whole-part PR. No later ticket was started.
- The npm audit's moderate findings remain visible. No gate or advisory was suppressed.
- #109 is committed locally only. No push, PR, issue comment/closure or #109 CI claim was made.
