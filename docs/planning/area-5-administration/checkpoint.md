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

## Delivery: ticket 5 (#110)

Completed on 2026-11-04 in the dedicated implementation thread, as the only implementation worker
on `area-5a-accounts` from pushed `272441ad2f10dcc2a8f61081a4d1740d20df7248`. The owner authorized
starting while #109's final browser CI was active. Dependency run `41699054031` was verified successful
on that exact SHA, including immediately before the first local commit. The coordinator owns #109 closure.

Implementation commit: `abb5e0af767a5b66f598f1860ff7eeb42593c4ec`,
"Serve New user and Edit user in Angular with fresh proof" (56 characters).
This final Delivery ticket 5 checkpoint update is committed separately and last.

New user and Edit user now use Angular at their existing AdminOnly URLs, with typed session-only
metadata/create/details/role endpoints, no-store and antiforgery. New grants require the shared fresh
strong proof, with one retry after proof. Core retains atomic auditing, validation, last-administrator
locking and generation changes. Saved notices preserve Legacy text. An unchanged own Unicode role
keeps its sessions and notice; a real self-demotion ends both browsers and returns to sign-in.
The corresponding MVC actions, views, models and old account-form helper are retired. Dashboard's
existing "Provision a user" action reaches Angular. Networks and service accounts remain outside scope.

Validation:

- locked restore, Release build with 0 warnings and 0 errors, and .NET format verification
- unit tests: 838 passed, no failures or skips
- full SQL Server integration: 948 passed, no failures or skips, 14m 1s
- full PostgreSQL integration: 948 passed, no failures or skips, 12m 51s
- both pending-model checks: no changes and no migrations
- agent hooks: 6 passed
- Angular install with `--ignore-scripts`, lint/browser types and 6 lint guards
- Vitest: 396 passed in 29 files
- Prettier, 15 build checks, generated API verification and production Angular build
- npm high/critical audit gate passed; 21 moderate findings remain visible
- complete production browser suite: 167 passed, no failures or skips, 52.4 minutes
- all 8 browser-health guard probes produced the expected real failures; verification exited 0

The final browser/guard runner was 22967 and exited 0. A preceding focused Production probe passed
8/8 (bootstrap plus the final seven scenarios) in 16.6 seconds. It supplements the completed full run.
Heavy checks ran serially on pinned Node 24.22.0, with four .NET processors and bounded image-build CPU.
Only owned containers were capped. Transient host CPU spikes exceeded 90%; SQL/vault caps were later
reduced to one CPU each, and later observed samples were below the ceiling. No unrelated process or
system setting changed. The report records the exact samples and caps.

The stale Legacy create and role flaws were demonstrated first on saved state. Security, Standards
and Spec reviews completed serially in fresh GPT Sol 6.1 xhigh sessions after Claude Opus 5.5 high
was unavailable at its weekly limit. These are approved same-model fallback reviews. Security found
no demonstrable defects. Standards and Spec findings were resolved with literal test cases, explicit
proof setup, named constants, cancellation focus and Unicode no-op navigation. Small fixes were made
directly afterward; no additional independent recheck is claimed. Original reports and the resolution
ledger remain in `logs/ticket-110/`.

Failures and interruptions are retained. They include the password DOM cleanup regression, browser
pattern syntax, saved-clearance display, and a Dashboard page-object locator that used "New user"
instead of the unchanged Legacy accessible link name. The final full run passed those flows. Docker
stopped after 129 and later 142 passes; the owner attributed the interruptions to usage limits and a
network issue. Those incomplete suites are not counted as passing. No retry, timeout or browser-health
rule was weakened.

Two scope mistakes are disclosed in the report. Earlier Docker contexts may have traversed or
transferred unrelated graph output to the local builder because the ignore rule was missing; the
final build excludes it without inspecting the tree. An interrupted-run cleanup initially captured
unfiltered container metadata in Python memory, which may have included a disposable test signing
key. No key value was printed or saved by that inspection, and no key file was opened. Later
inspection requests only the required fields. Shared build cache was not pruned.

Cleanup verified all eight owned Compose projects' containers, networks and labelled volumes absent,
all owned image tags removed, and two owned runtime key-ring directories deleted without reading
contents. The two older unrelated exited SQL containers were left untouched. Evidence is preserved.
No push, PR, issue action, branch/worktree change, history rewrite or later-ticket implementation was
performed.

Exact commands, counts, commit SHAs/subject lengths, review resolutions, readability excerpts and
cleanup evidence are in `logs/ticket-110/final-report.md`. The implementation and this checkpoint
remain local. #111 and #112 have not started in this thread.

## Restart: ticket 6 (#111)

Started on 2026-11-04 in its dedicated implementation thread, as the sole implementation worker,
from pushed `9aa2425cae74ced339724f74cb142390bf3eb969` on `area-5a-accounts`. The owner authorized
implementation while #110 CI run `41879345754` runs on that exact SHA. Its required success will be
verified before any local commit. The coordinator owns #110 closure. Scope is #104/#111 only,
with local commits and final checkpoint last, no push or PR. Evidence is under `logs/ticket-111/`.

## Delivery: ticket 6 (#111)

Completed locally on 2026-11-05 in its dedicated thread, as the sole implementation worker, on
`area-5a-accounts`. Implementation commit is `bda1af226e428671d74131782cb2d2a1116857c3`. This checkpoint is committed last,
with the prior #110 prefix preserved byte for byte. Before the first local commit, immutable
coordinator evidence verified #110 CI run `41879345754` completed successfully on exact dependency
SHA `9aa2425cae74ced339724f74cb142390bf3eb969`; all required jobs succeeded and only workflow_dispatch
commit-subjects skipped. The coordinator confirmed #110 closure. Its checks were not rerun.

Service accounts list, Create and Details now use Angular at their existing mixed-case bookmarks,
with explicit AdminOnly route authorization. The typed cookie-session UI API keeps Core validation,
normalization, scoping, unique-name handling and atomic fail-closed auditing. Issuing a key and enabling
an account require shared fresh proof before target lookup; removals do not. The raw key appears only
in the issuance response and current page memory, clears on departure/session loss, and never passes
through TempData or saved notices. Both shells and palette reach Angular. The Legacy service-account
controller, views, models and dead form helper are retired. Networks remains Legacy; #112 is untouched.

Final validation passed:

- locked restore, .NET format/verification and Release build with zero warnings or errors
- unit suite: 844 passed; exact malformed-input preflight: 10 passed
- full SQL Server integration: 995 passed, zero failures/skips, 14m 32s
- full PostgreSQL integration: 995 passed, zero failures/skips, 13m 42s
- both pending-model checks: no changes; six agent hook tests passed
- pinned Node 24.22.0 install with --ignore-scripts, lint/browser types and six lint guards
- Vitest: 429 passed in 30 files; Prettier and 15 build checks passed
- API types regenerated/verified and production Angular build/index gates passed
- refreshed npm high/critical audit gate passed with 15 moderate findings, earlier 21 retained
- complete production browser suite: 175 passed, zero failures/skips, 54.3 minutes
- all eight deliberate browser-health failures verified, guard verification exit 0

The final browser/guard runner PID was 44995 and exited 0. All eight new service-account scenarios
passed, including real stale-proof cancellation/focus and retry, scoped Public API use/revocation,
one-time key lifetime/copy, enable/disable/delete, both shells/palette, keyboard/themes and denied
non-administrator bookmarks. All six real audit-failure writes preserve state and return 503.

Three untouched-Legacy regressions were demonstrated first: stale issuance persisted a key, stale
enabling persisted the grant, and TempData recovered the raw-key handoff from the cookie. No raw key
or cookie values were printed. Security, Standards and Spec reviews covered all permitted changes
and new/deleted files in separate fresh GPT Sol 6.1 xhigh sessions after Claude Opus 5.5 high was rate
limited. This is the authorized same-model fallback. Security found no demonstrable defects. Three
Standards findings and one Spec finding were resolved with literal test cases, split endpoint tests,
exact field problems and explicit no-op 204 assertions. Small fixes were made directly before final
validation; no additional independent recheck is claimed. Original reports and resolutions remain.

Failures and interruptions remain in logs/ticket-111/. These include expected baseline failures,
initial HTTP/frontend assertion mistakes, corrected formatting gates, an interrupted format runner
with no exit record, and a preflight that could not initialize while Docker was stopped. The rerun
passed. A precommit path check initially overlooked the deletion half of a detected rename; it was
corrected with --no-renames before committing. No test timeout, retry or health rule was weakened.

Heavy checks and reviews ran serially, with no integration/browser overlap, task-local scratch and
normal priority. .NET used four processors and serial MSBuild/tests; the resumed formatter used two.
The production image build used a 2.5 CPU quota, and only owned browser services were capped, with
SQL/vault at one CPU each. The 90% host ceiling was exceeded in earlier quick/format attempts and
in two browser samples, at 12:24:55 and 13:04:18 local time, reaching 95.81% and 94.09%. All samples
are preserved. Later database samples peaked at 78.16%. Strict ceiling compliance is not claimed.
No unrelated process or global setting changed. Docker context exclusions were verified before build
without accessing the protected trees.

Cleanup verified both owned Compose projects' containers, networks and labelled volumes absent,
owned image removed, captured Testcontainers sessions and named containers absent, no owned runtime
key-ring directories remaining, and owned monitors stopped. No key contents were read and shared
cache was not pruned. All 49 implementation paths, including seven deletion markers, match the
actual tested sources and committed contents. Tracked state is clean after this checkpoint.

Exact commands, counts, commit subjects/lengths, review resolutions, three readability excerpts,
resource limitations and cleanup/source manifests are in logs/ticket-111/final-report.md. The remaining
15 moderate npm findings are visible and unsuppressed. Completion is reported to the coordinator;
reporting adds no delivery authorization. No push, PR, issue action, branch/worktree/history change
or later-ticket work was performed. The coordinator owns subsequent delivery decisions.

## Restart: ticket 7 (#112)

Started on 2026-11-05 in the dedicated ticket thread as the sole implementation worker, from pushed
`cdb62b17677c20fc42e0f55f095be0ae7eb0d7eb` on `area-5a-accounts`, with clean tracked state. The owner
authorized whole-part verification while #111 CI run `42003069030` runs on that immutable SHA.
Every required dependency job must succeed before the first local commit. The coordinator owns
dependency closure and subsequent delivery, already authorized by the owner to verify, push and open
the whole-part PR after final gates and reviews. This worker prepares artifacts and commits locally.

The whole-part review base is `dc7ae769dde424ffeee0a4013b23c42951181fbc`. Main's separate PostgreSQL
dev/docs changes at `139aaf9fa4d8919d7c0830fd31797a179fe7aba1` are inspected for compatibility without
merging. Evidence and scratch stay in `logs/ticket-112/`. Protected trees, private keys and full
container environments remain excluded. No branch/worktree/history changes, delivery actions,
duplicate implementation worker, #113 or 5b work are authorized in this worker.

## Delivery: ticket 7 (#112)

Completed the whole-part verification locally on 2026-11-05 in the dedicated ticket thread, retaining
exclusive checkout ownership on `area-5a-accounts`. Implementation commit: `961b27cb0c3908c4d2d69d7308c3d8f9d25e0cd7`.
The separate checkpoint commit is last; its exact SHA is recorded in logs/ticket-112/final-report.md.
The starting 32,984-byte checkpoint prefix is preserved byte for byte.

Before the first local commit, independently verified #111 workflow run `42003069030` completed
successfully on exact starting SHA `cdb62b17677c20fc42e0f55f095be0ae7eb0d7eb`: all ten required jobs
passed, with only workflow_dispatch commit-subjects skipped as expected. The coordinator confirmed
#111's closure. This dependency evidence does not claim CI for the later local #112 commits.

Added 47 real retired-handler HTTP cases and two actor-scoped administration activity tests, plus
production browser Dashboard/avatar/activity checks and captures of all six Accounts pages in both
themes. Standards fixes split independent scenarios, validate saved cookie JSON from unknown and
remove redundant comments/indirection. Two actual Spec defects were reproduced first: four real-
bundle layout probes failed for missing Service account Details CSS, and the component filter test
failed because the displayed you badge was absent from search. Restoring Legacy CSS and the badge
search text made the exact regressions pass. Two narrow ADR statements now match the implementation.

Whole-part Security, Standards and Spec reviews covered the fixed-base permitted diff, original
planning records, all new files and eight deletions. Claude Opus 5.5 high was rate limited; fresh
GPT Sol 6.1 xhigh sessions completed the authorized same-model fallback serially. Security found no
demonstrable defect. All three Standards violations and two judgment-call smells are resolved or
answered in the ledger; both Spec defects are fixed. Small later fixes were made directly before
final validation; no independent recheck is claimed. Original reports, manifests and exact later
path deltas remain under logs/ticket-112/.

Final validation passed on the final source snapshot:

- locked restore, .NET format verification and Release build with zero warnings/errors
- unit suite: 844 passed, no failures/skips
- full SQL Server integration: 1,047 passed, no failures/skips, 15m 4s
- full PostgreSQL integration: 1,047 passed, no failures/skips, 12m 33s
- both pending-model checks: no changes; six agent hook tests passed
- pinned Node 24.22.0 install --ignore-scripts, lint/browser types and six lint guards
- Prettier, 431 Vitest cases in 30 files, 15 build checks and full limited-worker Vitest repeat
- API types regenerated/checked, production Angular build/index gates, high/critical audit passed
- complete production browser suite: 184 passed, 55.6 minutes, no failures/skips
- all eight deliberate browser-health failures verified; guard command exit 0

Manually inspected all twelve real production captures: Users/New/Edit and Service accounts/Create/Details
in dark and light. No persistent layout defect was observed. This is manual inspection plus Legacy
source comparison, not an automated pixel-diff baseline; the light Service accounts list capture
includes an entrance-animation frame. No passwords or raw keys are present in these selected captures.

All expected red probes, prior attempts, exact commands, durations, exits and resource samples remain.
The focused runner briefly waited on its own caffeinate child after checks exited 0; only that child
was terminated and the runner was corrected. One quick Vitest host sample reached 99.3%; the entire
431-case suite was repeated with two workers and file parallelism disabled, with a 55.3% sampled
peak. Other final resource evidence and lifecycle limitations are recorded in the report. No retry,
timeout, guard rule, assertion or advisory threshold was weakened. The 15 moderate npm findings
remain visible. Strict continuous CPU-ceiling compliance is not claimed.

Heavy suites/reviews stayed serial, with no integration/browser overlap and normal priority. .NET
used two processors, MSBuild one worker and Angular two; Docker exposed about 4.1 GB, the production
image build used a two-CPU quota and only owned services were capped. All 166 final database samples
peaked at 88.84%; all 338 browser samples peaked at 87.41%, with no observed over-90 sample in either
phase. Three integration cap updates raced containers already removed by their lifecycle. Caps were
applied after containers were observed; continuous startup bounds and between-sample load are not
proved. All evidence is retained, without unrelated process or global setting changes.

Main's six separate PostgreSQL dev/docs commits at `139aaf9fa4d8919d7c0830fd31797a179fe7aba1` touch
seven non-overlapping paths. No concrete compatibility problem was found; both provider suites passed
on this part. Main was not merged and no runtime test of a merged tree is claimed. No schema change,
runtime dependency, commercial/legal edit, #117 redesign, #113 or 5b work was introduced.

Cleanup verified both exact owned Compose projects, captured Testcontainers sessions, task-prefixed
containers, networks and labelled volumes absent. The sole owned image was removed; no owned key-ring
directories, monitor processes or process-bound caffeinate children remain. Every captured task runner
exited 0. No private keys or full container environments were read, protected trees were not accessed
or enumerated, and no shared Docker cache/image/volume or package cache was pruned.

All 177 final source paths, including eight deletion markers, are verified against the actual tested
snapshot and committed blobs. The final checkpoint append is the sole expected post-test metadata
change; its hash and unchanged starting prefix are recorded separately. Tracked state is clean.
See final-source-manifest.json and committed-source-verification.json, acceptance-matrix.md,
follow-ups.md, review-resolutions.md, the original reviews and final-report.md in logs/ticket-112/.
Prepared PR title/body are pr-title.txt and pr-body.md there, using the required skills.

The local handoff returns exclusive checkout ownership to the coordinator through the supported
thread tool. The coordinator already has owner authorization to verify, push and open the real
whole-part PR after these gates/reviews. No new permission is requested or implied. This worker
performed no push, PR/issue action, merge, branch/worktree change or history rewrite or later-ticket work.
No merge is authorized. Any later source repair returns to this same ticket thread with exclusive
ownership, rather than a duplicate implementation worker.


## Restart: Part 5b, delivery ticket 1 (#113)

Started on 2026-11-06 in the dedicated ticket thread, as the sole implementation worker, on
`area-5b-vault-settings` from clean tracked HEAD `94a73e1060bf620cc1907f7da76310e286c98f1b`.
The coordinator merged #122 at that commit, verified main contains it and #104/#112 are closed,
then created this branch under the owner's authorization. Scope is #113 only, with local commits,
required serial reviews and final local gates. Networks and recovery remain Legacy. No push, CI
dispatch, issue/PR action, branch/history change or #114 work is authorized in this worker.
Evidence and task-local scratch stay in `logs/ticket-113/`; protected trees and earlier evidence
remain untouched. Exclusive checkout ownership returns to the coordinator after the local handoff.


## Delivery: Part 5b, delivery ticket 1 (#113)

Completed locally on 2026-11-06 in the dedicated ticket thread as the sole implementation worker on
`area-5b-vault-settings`, from `94a73e1060bf620cc1907f7da76310e286c98f1b`. Implementation commit is
`c042dda25b5062cf925ed97e95cd63ab4f09830b`, "Restore lifted blocks when their audit write fails" (50 characters).
This checkpoint is committed separately and last. Its exact SHA is in `logs/ticket-113/final-report.md`.
The original 40,838-byte prefix is preserved byte for byte. No push, CI dispatch, PR/issue action,
branch/worktree/history change or #114 work was performed. Coordinator owns later delivery.

Administration lifting now retains the removed store record, lifts first and writes exactly one
`IpRangeUnblocked` event. A rejected audit restores the original range, reason and time. Redis keeps
its exact original key content. Missing blocks change nothing and record nothing. Failed exact
restoration is distinct `503 unblock_unrecorded`, with a safe critical alert and a Legacy 503 page
that does not claim restoration succeeded. Normal Legacy form behavior stays intact. Networks stays
Legacy, and break-glass recovery is unchanged. Auditing and compensation finish after a browser
disconnect so a cancelled audit wait cannot later commit after mistaken restoration.

The real finding was reproduced first through the Production AuditFailureFixture and Legacy Unblock
form: the audit insert failed, but the block collection was empty. The original failing command,
exit 1 and 22.715-second duration remain in the evidence. The repaired test proves original state,
effective HTTP 403 blocking and no audit row, with setup/sign-in before rejection and restoration
of auditing in finally. Both final database suites passed it. Both-store unit coverage proves the
sequence, exact event, compensation, no-op, conflict and cancellation outcomes. Real Redis also proves
byte-identical JSON restoration and an actual closed-connection restoration failure.

Final local gates passed on the final source snapshot:

- locked restore, .NET format verification, Release build with zero warnings/errors
- unit tests: 858 passed, no failures or skips
- full SQL Server integration: 1,054 passed, no failures or skips, 21m 40s reported
- full PostgreSQL integration: 1,054 passed, no failures or skips, 13m 32s reported
- both pending-model checks: no changes; six hook tests passed
- pinned Node 24.22.0 install --ignore-scripts, lint/browser types and six lint guards
- Prettier, 431 Vitest tests in 30 files, 15 build checks, generated API consistency and production build
- npm high/critical gate passed; 15 moderate findings remain visible
- all 184 production browser tests passed in 54.4m, no failures/skips and zero retries
- all eight intentional browser-health failures verified; guard command exit 0

Security, Standards and Spec ran serially in fresh GPT Sol 6.1 xhigh sessions after Claude Opus 5.5
high was rate limited. These are the authorized same-model fallback, not cross-model reviews.
Security found no demonstrable defect. Three Standards violations and two judgment-call smells were
fixed directly. Spec found no implementation gap or scope expansion on the updated snapshot.
Original reports, reviewed manifests and the finding ledger remain unchanged. Five small fixes after
Security/Standards were not independently rechecked by those axes; Spec reviewed the updated snapshot.
No later independent recheck is claimed. The final checkpoint append is subsequent metadata.

Heavy suites/reviews stayed serial with normal priority, two .NET processors, explicit root MSBuild
one worker, two Angular workers, bounded Vitest without file parallelism, about 4.1 GB Docker, a
two-CPU image build and one Playwright worker. Database samples reached 91.05% and 92.2% busy; browser
sampling reached 94.28%. Owned database/vault caps were lowered to 0.75 CPU. The exact owned browser
monitor was replaced to keep the lower caps for guard startup; tests were uninterrupted. Every sample
and cap update is retained. Startup and between-sample bounds are not proved; strict continuous
CPU-ceiling compliance is not claimed. No unrelated process or global setting changed.

The inherited Profile suite still waits for real production throttle windows under its unchanged
per-case limits. This timing limitation is disclosed. No wait, retry, timeout, health exemption,
advisory suppression or weaker assertion was introduced or changed for #113.

Cleanup verified both owned browser projects, ten browser containers, both captured anonymous volumes,
project networks/labelled volumes, all five captured Testcontainers sessions and the sole owned image
absent. Owned host key-ring directories and monitors are gone, including the replacement monitor;
recorded runner and browser-child PIDs were checked directly. No private key contents or full container
environments were read. No shared cache, unrelated resources, protected trees or earlier evidence
were pruned or removed. Context exclusions were verified before the production build.

All 19 implementation files match the actual tested snapshot, staged blobs and implementation commit.
The full final source verification also binds final HEAD and working bytes, with zero deletion markers.
The checkpoint append is the sole post-test source change. Exact commits/subjects, counts, commands,
exits/durations, original reviews and resolutions, readability excerpts, resource/cleanup limits,
source manifests and restart instructions are in `logs/ticket-113/final-report.md` and its evidence.

Compensation does not overwrite a different newer block; that is a failed exact restoration requiring
inspection. Lost Redis replies and cross-store concurrency remain limitations. No schema, dependency,
commercial/legal or licensing-policy change was made. All writer nodes need the new code before
relying on compensation. The handoff returns exclusive checkout ownership to the coordinator through
the supported thread tool. It adds no delivery authorization and starts no later ticket.


## Delivery: Part 5b ticket 2 (#114)

Networks is served by the Angular UI at `/Admin/Networks` under `AdminOnly`, through
`/api/ui/networks` (list, trust the current address, add, remove, lift a block). The Legacy page,
its `AdminController` actions, view and view models are gone, and so is the Legacy-only error page
for an unrecorded unblock, which nothing could reach after the form was retired. The Dashboard
action, the Denied page link and the Legacy sidebar now link to `/Admin/Networks` directly.

Trusting the current address, adding a range and lifting a block need a strong sign-in within
`StepUp:MaxAuthAgeMinutes` (Q7); removing a range does not. Checks run `AdminOnly`, then freshness,
then validation or lookup. A range lost to a concurrent add answers `409 network_exists`; any other
failed save answers `503 audit_unavailable` with nothing trusted or removed. Before the fix both were
`500 internal_error`; the red runs are kept in `logs/ticket-114/red-*.log`. A lift that can be
neither recorded nor restored answers `503 unblock_unrecorded` over HTTP, proved by holding the
audit chain while the range is blocked again. Block reasons show decoy names unfiltered (Q11).

Author Claude Opus 5.5 (high). Security, Standards and Spec reviews ran serially in fresh GPT Sol
6.1 xhigh sessions, cross-model to the author; the brief's Claude reviewers assumed a GPT author.
Security found nothing (a 2 m 34 s pass). Standards found seven issues, all fixed. Spec found one
(no HTTP test for `unblock_unrecorded`), fixed with that test. Two later fixes to the browser spec
(project order, an address assertion) were found by the browser suite and seen by no reviewer.

Local gates on the final sources: locked restore, format, Release build with 0 warnings, 864 unit,
1092 integration on SQL Server and 1092 on PostgreSQL (the first PostgreSQL run failed once on a
`ProofDisclosure` false positive, a code matching a logged duration), both model checks, six hook
tests, 450 Vitest in 31 files, 15 build checks, `check:api-types`, the production build and the
high/critical audit. The production browser suite passed 202 of 202 in 58.0 m (one worker, zero retries, image `eclipsvault-ticket114-final:test`). `e2e:verify-guards` verified all eight intentional failures (exit 0). Earlier browser runs failed on timing in Profile
and passkey specs while other apps loaded the host, and twice on the new Networks spec; every
attempt is kept with its command, exit and duration.

Owner items: the browser chain makes each new spec cost a full suite per attempt;
`ProofDisclosure.ContainsSecret` matches codes inside logged durations; the Profile spec fails under
host load. Full detail in `logs/ticket-114/final-report.md`. The handoff returns exclusive checkout
ownership to the coordinator; it adds no delivery authorization and starts no later ticket.

## Delivery: Part 5b ticket 3 (#115)

Notifications and License are served by the Angular UI at `/Notifications` and `/License` (and their
`/Index` bookmarks) under `AdminOnly`, through `GET /api/ui/notifications` and `GET /api/ui/license`.
Both endpoints are read-only, accept only the cookie session and answer `no-store`. The Legacy
controllers, views and view models are gone. The Legacy sidebar and licence banner still use
`asp-controller`, and the conventional route keeps generating `/Notifications` and `/License` for
them, so those views are unchanged; the brief's premise that the links would break was wrong
(`logs/ticket-115/red-links.log` passed before any change).

License reads the startup snapshot and never starts an evaluation window. It computes the update
window at request time and answers normally for a missing or malformed licence; a signed perpetual
licence stays Valid. The installation text now names the inputs the vault reads,
`ECLIPSVAULT_LICENSE` or the file at `License:FilePath`, in the owner-approved sentence; the
commercial copy is unchanged. The Notifications filter matches the whole visible row, as the Legacy
filter did (Spec finding P1, red-first in `logs/ticket-115/red-filter.log`).

Authorship is mixed: GPT Sol 6.1 (xhigh) wrote the work up to 18:24Z on 2026-11-08; Claude Opus 5.5
(high) continued from that uncommitted state. Security, Standards and Spec reviews ran serially in
fresh read-only GPT Sol 6.1 xhigh sessions on the Codex provider: cross-model to the Claude part,
same-model to the GPT part. Security found nothing. Standards found six issues: five fixed, one
(a narrower outbox port) left out of scope. Spec found one (P1), fixed after its snapshot and not
re-reviewed.

Local gates on the final application sources (the later browser-helper fixes were re-linted): locked restore, format, Release build with 0 warnings, 882 unit,
1114 integration on SQL Server and 1114 on PostgreSQL, both model checks, six hook tests, 478 Vitest
in 33 files, 15 build checks, `check:api-types`, the production build and the high/critical audit.
The production browser suite has not passed. Six attempts ran (one worker, zero retries, image
`eclipsvault-e2e-t115-*`): three failed in the inherited Profile spec under host load
(`profile.spec.ts:1357` once, `:1128` twice), one was cut off by a T3 restart, and two failed on the
new Notifications spec. So the License spec and the projects after it have never run, and
`e2e:verify-guards` was not reached. The fourth browser attempt found that the Profile outage test restarts the
SMTP capture on a new host port, leaving the runner's capture URL stale for later specs;
`e2e/smtp-capture.ts` now looks the port up on every read. The fifth found that the new page
objects also matched the shell's licence notice; they now read inside their own page component.
No review has seen either fix.

Owner items: the `asp-controller` premise in the brief and plan was wrong; the Profile spec fails
under host load (two different tests across three runs); the browser suite has not passed, so the
License spec is unproven in a browser; the outbox controller keeps the broad `INotificationService`
port. Full detail in `logs/ticket-115/final-report.md`. The handoff returns
exclusive checkout ownership to the coordinator; it adds no delivery authorization and starts no
later ticket.

## Delivery: Part 5b ticket 4 (#116)

Verified the whole part locally on 2026-11-09 in the dedicated ticket thread, as the sole worker on
`area-5b-vault-settings`, from pushed `59d7951`. No push, CI dispatch, PR or issue action, merge or
history change. Exact commits, subjects and lengths are in `logs/ticket-116/final-report.md`; this
section is the last commit.

Legacy is fully retired. No Networks, Notifications or License view, view model or controller action
remains, and `AdminController` is gone. The existing retired-route tests now try every removed action
in a second letter case and with other HTTP methods (`NetworksApiTests`, `MonitoringPagesTests`):
the old write actions answer 404 and change nothing; the Angular bookmarks answer other methods with 405.
Every link into the three pages has a browser test: both sidebars and both palettes, the Dashboard
and Denied "Manage trusted networks", and both "View license" notices. From each page, the sidebar
now leads back to Legacy Secrets and on to Your data (Spec finding). The Legacy banner's beyond-tier
link needs a signed valid licence, so it has no test of its own; it is the same tag helper as the
tested one.

Whole-part reviews ran on the diff from the merge base `3c3e7df`, one after another, each in a fresh
read-only GPT Sol 6.1 xhigh session: cross-model to the Opus parts (#114, the rest of #115, #116),
same-model to the GPT parts (#113, part of #115). Security found nothing. It covered the four
licensing invariants, decoy state (a decoy-trip block and an outbox row naming a decoy, both
administrator-only), step-up, the audit-failure and race answers, and break-glass. Standards found
five issues, all fixed. Spec found two. One was the missing outgoing-link tests, now fixed and not
re-reviewed. The other was a Production HTTP test for an expired licence. That needs a token signed
by the pinned vendor key, which agents never touch, so it is left to the owner.
`CONTEXT.md`, ADR-0017 and ADR-0028 match the code; no doc or decision change was needed. The #115
re-check nit is fixed: the SMTP capture reader names the endpoint and status when the capture fails.

Local gates on the final sources (`942eb1b`): locked restore, format, Release build with 0 warnings,
892 unit, 1,150 integration on SQL Server and 1,150 on PostgreSQL, both model checks, six hook tests;
Node 24.22.0 install, lint, Prettier, 486 Vitest in 33 files, 15 build checks, `check:api-types`,
the production build and the high/critical audit (15 moderate findings visible). The browser run
covered Networks, Notifications, License and every project they depend on. It passed 200 of 200 in
29.5 m on one worker with zero retries. The full suite (Users onwards) and `e2e:verify-guards` were
not run locally; CI runs them after the push. CPU went above 90% in 4 of 63 browser-run samples,
all during the image build and stack startup.

Owner items: the expired-licence HTTP test (Spec SPEC-2); #115's S6, a narrower outbox port, stays
deferred. The PR body is `logs/ticket-116/pr-body.md`. The handoff returns exclusive checkout
ownership to the coordinator; it adds no delivery authorization.
