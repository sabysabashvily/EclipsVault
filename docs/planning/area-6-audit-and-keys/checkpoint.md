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
