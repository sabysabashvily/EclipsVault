# Area 4 Sharing: grill checkpoint

Branch for the grill: none. Checkout on `main` at `f31b0a8`. Records stay uncommitted; the first
ticket commits them. Survey: [survey.md](survey.md) (findings F1–F10).

## Round 1: asked 2026-10-30, all ten recommendations ACCEPTED by the owner

| Q | Topic | Recommendation |
| --- | --- | --- |
| Q1 | Security review | Area 4 joins ADR-0017's `security-reviewer` list (2–8); one sentence, like Area 2's. |
| Q2 | Scope split | Shared with me moves (route table + delete `SecretsController.SharedWithMe` and its view). Sharing panel, Details and Denied stay with Area 7. Access requests moves whole to `/api/ui/access-requests`, except `POST /AccessRequests/Create`, which stays a Legacy form endpoint for the Denied page, rewired to fixed Core, redirecting to `/AccessRequests?filed=1` or `?refused=<code>`; Angular shows it in the StatusLine and strips it from the URL. |
| Q3 | Who decides (F1, F2) | The sharing-panel rule: administrator or project member and the secret's ABAC read passes at decision time. Queue lists only decidable requests (same filter as the Secrets list), never your own. Anything else is `404 request_not_found`. |
| Q4 | Who files (F7, F8) | Server computes eligibility and denial reasons; posted `deniedReasons` ignored then removed. File only when clearance passes, project fails and no active share; network/time ignored. Refusals: `share_cannot_help`, already able to open, already pending. Rechecked at approval: `409 request_ineligible`. Honey-token trap kept; one `AccessRequested` row, no metadata-viewed row. |
| Q5 | Atomic transitions (F4, F5) | One transaction per transition via a `DescribeAccessRequestChange` interceptor rule; approve = share + status + `SecretShared` + `AccessRequestApproved`. Pending-only with a concurrency check: one winner, others `409 request_not_pending`. Audit failure `503 audit_unavailable`, nothing changes. Email after commit, best-effort. New ADR with Q3, Q4, Q8. |
| Q6 | Expired shares (F6) | In Core: a new share replaces an expired one in the same transaction; one `SecretShared` row noting the replacement, no revoked row. Active duplicates still refused. |
| Q7 | Shared with me filter (F3) | Same ABAC enumeration filter as the Secrets list, grant included; hidden rows omitted, empty state unchanged, no hidden count. |
| Q8 | Under-cleared shares (F3) | `GrantAsync` refuses a recipient whose clearance is below the sensitivity, for both callers; existing ones stay, hidden by Q7. |
| Q9 | Share by account (F9) | Approval shares to `RequesterUserId`, issuer from the session; usernames are display snapshots only. |
| Q10 | Approval inputs (F10) | `expiresInDays` 0–3650 (0 = none), server-validated `400 validation_failed`; no decision note on either endpoint; a stored note still displays. |

Recorded: ADR-0017 lists Area 4 for the security reviewer; ADR-0027 records Q3–Q8; CONTEXT.md
gains Expired share, Access request, Requester and Reviewer. All uncommitted.

## Round 2: asked 2026-10-30, all eight recommendations ACCEPTED by the owner

Q11 UI API contract, Q12 existing requester-written denial text, Q13 existing ineligible requests,
Q14 lost share-index race, Q15 Denied page form, Q16 presentation, Q17 concurrency mechanism,
Q18 test plan.

ADR-0027 gained Q12, Q13 and Q14. No questions remain; see [decisions.md](decisions.md).

## Delivery: ticket 1 (#97) done locally, 2026-10-30

Branch `area-4-sharing` from `main` at `f31b0a8`, not pushed, no PR; #97 stays open for the owner.

- `eaef66e` commits these records, `CONTEXT.md` and ADR-0017's amendment and ADR-0027.
- `a36d542` applies Q6, Q8 and Q14 in `SecretGrantService` and `SecretGrantRepository`; the audit
  interceptor records a replaced expired share as one `SecretShared` row.
- `63f4a3e` fixes the review findings: a failed save is a conflict only when the share index refused it
  or a replacement lost its expired share, and another share is visible; a failed lookup keeps the audit
  failure.

Race and fault tests share a two-database fixture (`ShareDatabases`, SQL Server and PostgreSQL) that later
tickets can reuse for access-request races. The audit-failure test calls the share service directly,
because the sharing panel audits a metadata view before it shares. On the Legacy panel the conflict is
the flash "They already have a share on this secret."; the UI API's `409 share_exists` belongs to the
ticket that adds approval to the UI API.

To restart: check out `area-4-sharing`, read `logs/ticket-97/final-report.md`, then start ticket 2 (#98).

## Delivery: ticket 2 (#98) done locally, 2026-10-30

Same branch, on top of `6244a50`; not pushed, no PR, #98 stays open for the owner.

- `3896660` commits filing, approval, rejection and withdrawal with their audit rows through
  `DescribeAccessRequestChange`. Approval goes through `IAccessRequestCommitter`, which commits the
  share (or the replacement of an expired one), the decision, `SecretShared` and
  `AccessRequestApproved` together. `AccessRequest.Status` is a concurrency token, so one decision
  wins; losers return false ("That request is no longer pending."). Expiry is 0–3650 days in Core,
  and decision notes are no longer accepted.
- `3ed7fec` and the commits after it answer the reviews. Panel shares and approval shares are built
  by `ShareRecipient.NewShare`; the committer is split into steps and shares
  `OneSharePerRecipient` with the share repository; the tests state literal expectations.

The concurrency token needed no migration: both pending-model checks pass. Access-request races reuse
`ShareDatabases` and hold the audit chain until both contenders have queued their writes.

Left for later tickets: who may file and who may decide is #99, and so is removing Legacy's metadata
read before filing. The UI API codes and the Angular pages are #100 and #101. The Legacy flash
"a grant was created" is also shown when an active share is reused; #101 copies Legacy text, so the
owner may want new wording.

To restart: check out `area-4-sharing`, read `logs/ticket-98/final-report.md`, then start ticket 3 (#99).

## Delivery: ticket 3 (#99) done locally, 2026-10-31

Same branch, on top of `6041150`; not pushed, no PR, #99 stays open for the owner.

- `e01cf1c` applies Q3 and Q4. `AccessRequestReviewers` (Web) is the one reviewer rule: someone other
  than the requester, administrator or member of the secret's project, who passes `SecretAccessHandler`
  now. Legacy's queue, Approve and Reject call it, and #100's UI API is to call it too. An undecidable
  and an unknown id get the same "That request was not found.", with no audit row.
  `AccessRequestRules.ShareRefusal` (Core) is the filing rule, used when filing, on the Denied page and
  again at approval. The server writes the denial reasons; Legacy's hidden `deniedReasons` field is
  gone. Migration `ClearPendingDenialReasons`, in both provider sets, clears that text on pending
  requests only.
- `8cde1d2` and `3b0d53b` answer the reviews:
  - any lookup a requester makes trips a decoy, the Denied page included;
  - approval refuses an expired or shredded secret;
  - names and comments are clearer.

Behaviour later tickets inherit:
- Approving a requester who already holds an active share is now refused with the reviewer's
  sentence. #98 used to reuse the share.
- Requests for dead secrets stay in the queue so they can be rejected.
- Legacy still labels an administrator's queue "All projects".
- Both model snapshots gained `Status`'s concurrency token, which #98 left out; the schema is unchanged.

Checks on `3b0d53b`:
- locked restore;
- Release build with 0 warnings;
- format verify;
- unit tests 729/729;
- integration tests 758/758;
- both pending-model checks;
- guardrail tests 6/6.

To restart: check out `area-4-sharing`, read `logs/ticket-99/final-report.md`, then start ticket 4 (#100).

## Delivery: ticket 4 (#100) done locally, 2026-10-31

Same branch, on top of `94c9f77`; not pushed, no PR, #100 stays open for the owner.

- `976ba5e` serves Shared with me in Angular.
  - `GET /api/ui/shared-with-me` is session-only and no-store.
  - Each row goes through `VisibleToAsync`, the Secrets list's own handler, with the share counted, because `SharedSecretDto` is an `IGrantableResource`.
  - The Legacy action and view are gone. The route table, both sidebars and the palette open the Angular page.
  - `ClosedProductionWindowFixture` is a third vault, whose production window never opens.
- `8570bd6` leaves shares on decoys out, as the Secrets list does. This came from the security review.
- `7c824e2` answers the Standards and Spec reviews.
- `478c8c3` and `29fd32c` fix two differences from Legacy that the browser suite found:
  - spaces around the Access until date;
  - Angular had no environment-badge colours.

Owner items, both resolved by the coordinator before the push, with the owner's approval:
- `976ba5e`'s 74-character subject was reworded to "Serve Shared with me in Angular, listing only
  shares the user can use" (69). The six commits were replayed on `94c9f77` with the same tree, so the
  SHAs below changed: `976ba5e`→`a2ce589`, `8570bd6`→`69e23df`, `7c824e2`→`2476c8a`,
  `478c8c3`→`2880e3b`, `29fd32c`→`da428f9`, `729fd22`→`2d05ce1`.
- Shared by me's query (`ListIssuedByAsync`) now leaves out decoys too, with
  `SharedByMeApiTests.A_share_on_a_decoy_secret_is_left_out` shown failing first (2 items listed).

Behaviour later tickets inherit:
- `.badge.env-*` now exists in Angular's `styles.css`.
- `e2e/secrets-list.ts` is a page object for the Legacy Secrets list.
- `e2e/look.ts` compares computed styles by name.
- The Playwright chain is now `shared-by-me` → `shared-with-me` → `mailbox`.

Checks:
- on `7c824e2` (later commits change only Angular files):
  - locked restore;
  - Release build with 0 warnings;
  - format verify;
  - unit tests 729/729;
  - integration tests 771/771;
  - both pending-model checks;
  - guardrail tests 6/6.
- on `29fd32c`:
  - lint, format check, build, build checks;
  - Vitest 292/292;
  - e2e 122/122;
  - guards 8/8.
- `check:api-types` was last run on `478c8c3`, and `npm audit` (exit 0, 21 moderate) before `976ba5e`.
  The package files and response types have not changed since.

To restart: check out `area-4-sharing`, read `logs/ticket-100/final-report.md`, then start ticket 5 (#101).
