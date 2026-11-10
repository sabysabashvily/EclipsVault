# Area 6 grill checkpoint

Started 2026-11-09 on `main` at `4feec8c`. Model: Claude Opus 5.5 (high). Survey: [survey.md](survey.md).

## Lessons Area 5 adds (16–19)

16. **A text-clean merge of `main` can fail to build.** Build and test after every merge, before pushing.
17. **Per-ticket browser suites run in CI**, not in full on the owner's Mac; a ticket runs only its affected
    specs locally.
18. **A fix made after a review needs its own re-check** by the reviewer that raised it.
19. **Tests must not assert on shared state** another test can change (counts, "latest" rows, the newest
    audit entry); scope each assertion to rows the test created.

## Round 1 (asked 2026-11-09)

Q1 Split · Q2 Scope · Q3 Rotation correctness · Q4 Rotation audit rows · Q5 Honest Verify ·
Q6 Checkpoint atomicity · Q7 Export · Q8 Step-up · Q9 Recording access to the trail ·
Q10 Who sees what · Q11 KMS engine.

### Answers (2026-11-09)

The owner restated each recommendation's headline. Accepted as stated:

| Ref | Accepted |
| --- | --- |
| Q1 | Two parts: 6a Encryption keys (K1–K7) first, 6b Audit log (A1–A5) from `main` after 6a merges. ADR-0017 amended. |
| Q2 | Both pages retire in their part: views, view model, controllers. |
| Q3 | Batches of 500; never overwrite a changed row; one pass at a time (`409 rotation_running`); skip and report rows under missing keys; clear cache per batch; stop between batches. ADR-0029. |
| Q4 | One critical `KekRotated` row per committed batch, naming old and new key ids, counting secrets and versions. ADR-0029. |
| Q5 | Verify streams the chain in sequence order, constant memory. |
| Q6 | Checkpoint and its audit row in one transaction via an interceptor rule; `503 audit_unavailable`, no checkpoint. |
| Q7 | Export signs the committed head, writes its audit row, then streams rows up to that head. |
| Q8 | Only Export needs fresh proof. |
| Q9 | Viewing and verifying are recorded, fail-closed. |
| Q10 | Area 5 Q11 extends to this Area; Export adds step-up. |
| Q11 | 6a copies the page; KMS follow-up issue after confirmation. |

Sub-points the reply did not restate are confirmed in round 2 (Q12).

## Round 2 (asked and answered 2026-11-09)

Convention, set by Q12: a restated headline accepts the whole recommendation, sub-points included.

| Ref | Accepted |
| --- | --- |
| Q12 | Every round-1 sub-point stands: `AuditDisplay` stays and is reused; no per-secret `SecretUpdated` on a re-wrap; Verify checks checkpoints signed by the current key (signature, head hash at its sequence, no rows missing past the latest), counts others "not checked", has three outcomes, and the page and README line 287 stop over-claiming; Verify and Export one at a time (`409 audit_busy`), 503 sends no bytes, schema v2 and file name unchanged; ADR-0028 bulk-disclosure line (added); `AuditLogViewed` per page load, `AuditChainVerified` with outcome, critical on tampering; decoy test. |
| Q13 | Encryption UI API as proposed (GET status, POST rotate → 200 counts; `409 rotation_running`; `503 audit_unavailable` with committed counts). |
| Q14 | Exact copy; ConfirmationDialog and StatusLine; button disabled when `awaitingRotation` is 0; missing-keys sentence; 409 and 503 texts; status reloads after any outcome. |
| Q15 | Master key, Data key, Master key rotation added to `CONTEXT.md` under Encryption. |
| Q16 | Six test layers: deterministic K1 interleaving on both databases; `KekRotationFixture` host; AuditFailureFixture; unit cache test; Vitest; Playwright for the page with nothing pending. |
| Q17 | Branch `area-6a-encryption-keys`; tickets: 1 rotation rules, 2 Angular page, 3 whole-part verification and PR. |

## Round 3 (asked and answered 2026-11-09)

Q18–Q23 accepted in full: Audit UI API, Verify notices, Audit presentation, audit glossary (added to
`CONTEXT.md`), 6b test plan, 6b delivery. ADR-0030 written. Full record: [decisions.md](decisions.md).
Frontier empty. Confirmed by the owner on 2026-11-09. Issue drafts written to `issues/`. Published 2026-11-09 as #135 (6a; #137–#139), #136 (6b; #140–#144) and the KMS follow-up #145. The owner's "go" also started branch `area-6a-encryption-keys` from `main` with these records, and the launch of #137 in its own thread.

## Delivery: ticket 1 (#137)

Implemented 2026-11-09 on `area-6a-encryption-keys` by Claude Opus 5.5 (high), in the root checkout.
The grill's records were already committed by the coordinator in `4fe7c93`. Not pushed; no PR.

Commits after `4fe7c93`:

- `33f2879` Rotate master keys in batches that never overwrite a changed secret
- `5ed7b29` Keep re-wrapping KMS rows and report a pass's own failure, not unlocking
- this checkpoint section, committed last

What the pass does now (ADR-0029): secrets, then archived versions, 500 rows a batch. Each batch takes
the audit chain, re-wraps a row with a conditional `ExecuteUpdate` that matches the wrapped data key
and master key id it read (a changed, deleted or shredded row is skipped and counted nowhere), and
commits with one critical `KekRotated` row through `AuditSaveChangesInterceptor.SaveOperationAsync`.
No `SecretUpdated` row. One pass at a time across replicas under a session-owned database lock
(`ISessionLocker`: `sp_getapplock @LockOwner='Session'` / `pg_try_advisory_lock`); a second pass gets
`KekRotationRunningException`. Rows under keys that are not loaded are skipped and counted
(`KekRotationResult.OnMissingKeys`). Each batch evicts its secrets from the cache; cancellation is
checked only between batches. Under the KMS engine every row not under the current key still goes to
the engine's re-wrap (#145 owns that page). No schema change.

Checks at `5ed7b29`: `scripts/check.sh` (restore, Release build 0 warnings, 895 unit tests);
`dotnet format --verify-no-changes`; both pending-model checks; hook tests; integration
`FullyQualifiedName~KekRotation` 10/10 on SQL Server and PostgreSQL; full integration 1160/1160 on
SQL Server and on PostgreSQL. Failing-first runs against the old pass are in `logs/ticket-137/red-*.log`.
Reviews: security reviewer and GPT Sol 6.1 (Standards, Spec), each re-checked; ledger in
`logs/ticket-137/review-ledger.md`.

For #138: map `KekRotationRunningException` to `409 rotation_running`; the 503 needs the committed
counts carried out of `KekRotationService.RotateRowsAsync`; the Legacy flash still omits rows on
missing keys. For the owner: one batch holds the audit chain for 500 sequential updates, so at about
30 ms of database latency other audited actions can exceed the 15-second chain wait during a pass.

To restart: `git switch area-6a-encryption-keys`, `scripts/check.sh`, then
`ECLIPSVAULT_TEST_DATABASE_PROVIDER=Postgres dotnet test EclipsVault.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~KekRotation"`
(and without the variable for SQL Server). Ticket 2 (#138) starts from the HEAD after this commit.

## Delivery: ticket 2 (#138)

Implemented 2026-11-10 on `area-6a-encryption-keys` in the root checkout, from `dc07934`. Not pushed;
no PR. Claude Fable 5.2 (high) wrote the implementation and three existing commits, whose trailers
name it. GPT Sol 6.1 (xhigh) finished the delivery records after the Claude usage limit stopped the
session. The brief had named Claude Opus 5.5; the model difference is recorded in the final report.

Commits after `dc07934`:

- `bcc19ff` Serve Encryption keys in Angular and say what a stopped rotation moved
- `d8a566e` Prove a stopped rotation reports every committed batch, versions too
- `8041aaa` Declare the rotation service among the ports Infrastructure implements
- this checkpoint section, committed last

What changed:

- **UI API.** `GET /api/ui/encryption` and `POST /api/ui/encryption/rotate`, `AdminOnly`, session-only,
  `no-store`, anti-forgery on the POST, explicit types, regenerated TypeScript. No step-up.
  `awaitingRotation` counts the rows a pass can move and `onMissingKeys` the rows under master keys
  that are not loaded. Under the KMS engine every row not under the current key still counts as
  awaiting, as Legacy counted it, so #145's misreport is neither fixed nor made worse.
- **Refusals.** A running pass answers `409 rotation_running` (it was a generic 400). A pass stopped
  by the audit trail throws `KekRotationStoppedException`, which carries what the committed batches
  moved, and answers `503 audit_unavailable` with `secretsRewrapped` and `versionsRewrapped` (the 503
  had no counts). The exception derives from `AuditWriteFailedException`, unsealed for it, so a caller
  that does not report the counts still gets the global fail-closed 503. The Q14 texts are the
  problems' titles, which the page shows as they are. ADR-0029's batch rules are unchanged.
- **Page.** Angular serves `/Encryption` and `/Encryption/Index` under `AdminOnly` through the route
  table, word for word from Legacy, with the Q14 changes: the button is disabled when nothing can
  move, success adds the items left on keys that are not loaded, and the status reloads after any
  outcome. `EncryptionController` and `Views/Encryption/Index` are gone; the Legacy sidebar link
  keeps its destination and the Angular sidebar marks the entry as Angular.
- **Placement.** `IKekRotationService` moved to `Core/Application/Abstractions` on the Standards
  reviewer's finding; its records stay in `KeyManagement`. No schema change.

Shown failing first (`logs/ticket-138/red-postgres.log`): the running pass answered 400 instead of
409, and the 503 carried only `code`, `status` and `title`.

Checks at `8041aaa`: locked restore; `dotnet format --verify-no-changes`; Release build with 0
warnings; 897 unit tests; integration 1180/1180 on SQL Server and 1180/1180 on PostgreSQL; both
pending-model checks; 6 hook tests; Angular lint, format check, 505 Vitest tests, 15 build checks,
`check:api-types`, production build and `npm audit --audit-level=high` (10 moderate advisories,
no high or critical findings). `scripts/check.sh e2e encryption.spec.ts` passed 39/39, including
the sign-in project's sidebar and palette coverage. Final attempt 4 took 306 seconds. Attempts 1 and
3 were interrupted; their output remains in the ticket logs. The full browser suite and
`e2e:verify-guards` run in CI. No CI run was dispatched for this ticket.

Reviews: security reviewer (Claude Opus 5.5, high), no defects; GPT Sol 6.1 (xhigh) Standards (S1–S4)
and Spec (G1), each fixed and closed by its reviewer's re-check. Ledger:
`logs/ticket-138/review-ledger.md`.

For the owner:

- The e2e stack loads one master key, so the Rotate button is always disabled there. The browser tests
  that open the confirmation answer the status with one pending row through request interception;
  rotation itself is proven over HTTP on `KekRotationFixture`.
- The 503's counts can under-report (a commit whose acknowledgement was lost) but never over-report.
- The security reviewer noted that a timeout in the audit-chain hand-over helper can leave its next
  holder waiting until fixture teardown. No such timeout occurred in the final suites.
- Feature-folder interfaces implemented in Infrastructure remain elsewhere (`ITrustedNetworkRepository`,
  `ISecretGrantRepository`, `IAccessRequestRepository`), against the AGENTS.md placement rule.

Cleanup: the interrupted browser stack's five containers, one volume and one network were removed
by their project labels and captured mounts. The final runner removed its own stack and volumes;
its reused image was removed afterwards. No ticket runner, container, network or image remains.
Evidence and the full delivery report are in `logs/ticket-138/`.

To restart, first verify the branch is `area-6a-encryption-keys` and the tracked checkout is clean.
Run `scripts/check.sh`, then
`ECLIPSVAULT_TEST_DATABASE_PROVIDER=Postgres dotnet test EclipsVault.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~EncryptionApiTests|FullyQualifiedName~KekRotation"`,
and `scripts/check.sh e2e encryption.spec.ts` for the page. Ticket 3 (#139) starts from the HEAD after
this commit.
