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
