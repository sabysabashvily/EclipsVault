# Area 6 Audit log and Encryption keys: decision record

Status: all 23 recommendations accepted (rounds 1–3) and confirmed by the owner on 2026-11-09.
Issue drafts are in [issues/](issues/). Published 2026-11-09 as #135 (6a; #137–#139), #136 (6b; #140–#144) and the KMS follow-up #145.

Survey and findings: [survey.md](survey.md). Rounds: [checkpoint.md](checkpoint.md). Glossary:
`CONTEXT.md` (Encryption: Master key, Data key, Master key rotation; Audit: Audit trail, Checkpoint,
Audit bundle, Signing key). ADRs: 0017 and 0028 amended, 0029 and 0030 new.

## Decisions

| Ref | Decision | Agreed behaviour |
| --- | --- | --- |
| Q1 | Split | Two parts, each with its own spec, branch and PR: **6a Encryption keys** (K1–K7) first, then **6b Audit log** (A1–A5) from `main` after 6a merges. |
| Q2 | Scope | Both pages retire in their part: views, view model, controllers. `AuditDisplay` stays (Dashboard and Secret details, Area 7) and feeds the new endpoint's labels. Dashboard links stay with Area 7. `SelfServiceScopingTests` moves off the `/Audit` HTML. |
| Q3 | Rotation rules | Batches of 500 rows; a row re-wrapped only if its wrapped data key and master key id are unchanged since read; one pass at a time across replicas under a vault-wide database lock (`409 rotation_running`); rows under a master key that is not loaded skipped and reported; each batch clears its secrets from the cache; a departing caller stops the pass between batches. ADR-0029. |
| Q4 | Rotation audit rows | One critical `KekRotated` row per committed batch, in its transaction, naming old and new master key ids and counting secrets and versions. No per-secret `SecretUpdated` row for a re-wrap. A recording failure rolls back that batch: `503 audit_unavailable`, earlier batches stay. |
| Q5 | Honest Verify | Streams the chain in sequence order with constant memory. Checks every checkpoint signed by the current signing key (signature, head hash at its sequence, no entries missing up to the latest); others counted "not checked". Outcomes: anchored, unanchored, tampered. Page text and README line 287 stop over-claiming. ADR-0030. |
| Q6 | Checkpoint | Commits with its `AuditCheckpointCreated` row through an interceptor rule; on failure `503 audit_unavailable` and no checkpoint. |
| Q7 | Export | Signs the committed head, writes `AuditBundleExported`, then streams rows up to that head; a failed audit write sends no bytes. Verify and Export one at a time each (`409 audit_busy`). Schema v2, file name and indentation unchanged. |
| Q8 | Step-up | Only Export needs a sign-in or step-up within `StepUp:MaxAuthAgeMinutes` (`403 step_up_required`, StepUpDialog). Rotate, Checkpoint, Verify and viewing never ask. ADR-0028 amended: bulk disclosure counts as a grant. |
| Q9 | Recording access | Opening the audit log writes `AuditLogViewed`; Verify writes `AuditChainVerified` with its outcome, critical on tampering. Both fail closed. Two new `AuditAction` values with labels and activity descriptions. |
| Q10 | Who sees what | Area 5 Q11 extends here: names and IPs to every administrator, unfiltered, from any network; Export adds step-up. A test pins that a decoy appears only after it trips and that rotation never trips one. |
| Q11 | KMS engine (K7) | 6a copies the page; a follow-up issue is filed after confirmation. |
| Q12 | Sub-points | Every round-1 sub-point stands (recorded in the rows above). |
| Q13 | Encryption UI API | See below. |
| Q14 | Encryption presentation | Exact copy: three notices, Keys in use, Rotate panel and operator steps. ConfirmationDialog with the Legacy text; Legacy flash in the StatusLine. Deliberate: button disabled when `awaitingRotation` is 0; success adds " n item(s) remain on keys that are not loaded." when above 0; 409 "Another rotation is already running. Try again when it finishes."; 503 "Rotation stopped because the audit trail could not record it. n item(s) were re-wrapped before it stopped; run it again to continue."; status reloads after any outcome. |
| Q15 | Encryption glossary | Master key, Data key, Master key rotation. The UI keeps its "KEK" wording. |
| Q16 | 6a tests | See "Testing". |
| Q17 | 6a delivery | See "Delivery outline". |
| Q18 | Audit UI API | See below. Verify and Checkpoint update the page in place, so only opening the page writes a view row. Entries ordered by time, then sequence. |
| Q19 | Verify notices | Anchored (ok): "✓ Hash chain intact." "n entries verified. The signed checkpoint at sequence s matches, so entries up to #s are proven unchanged against the vault's signing key; later entries are covered by the hash chain until the next checkpoint. To prove the trail to an outside party, export a bundle and verify it offline against the vault's public key." Unanchored (warn): "✓ Hash chain intact, but no signed checkpoint covers it yet." "n entries verified. Without a checkpoint, someone with database access could delete the newest entries or rewrite the trail and recompute its hashes without detection here. Create a signed checkpoint." Tampered (critical): "✗ Tampering detected." reason "(First broken entry: #n.)", with the new reasons "The chain ends at #n, before the signed checkpoint at #s: entries were deleted.", "Entry #s does not match the signed checkpoint: the trail was rewritten.", "The checkpoint at sequence s has an invalid signature." Plus "c checkpoint(s) signed by another key were not checked." when c > 0. README line 287 corrected. |
| Q20 | Audit presentation | Exact copy: header, Verify, attestation panel and text, latest-checkpoint line, TableFilter, table, critical rows, relative time with UTC tooltip. Deliberate: Q19 notices; checkpoint result in the StatusLine with the Legacy text; Export through StepUpDialog, same file name; 409 "Another verification or export is running. Try again when it finishes."; standard 503 text. Labels "Audit log viewed" (muted), "Audit chain verified" (muted; critical row on tampering). |
| Q21 | Audit glossary | Audit trail, Checkpoint, Audit bundle, Signing key. |
| Q22 | 6b tests | See "Testing". |
| Q23 | 6b delivery | See "Delivery outline". ADR-0030. |

## UI API contract

All endpoints: `AdminOnly`, session-only, `no-store`, anti-forgery on writes, the caller only from the
session, explicit response types, regenerated TypeScript, Problem Details with a stable `code`.

### 6a Encryption keys

```text
GET  /api/ui/encryption        { currentKekId, loadedKekIds,
                                 keys: [{ kekId, status: "current"|"retired"|"not_loaded", secrets, versions }],
                                 awaitingRotation, onMissingKeys }
POST /api/ui/encryption/rotate → 200 { currentKekId, secretsRewrapped, versionsRewrapped, onMissingKeys }
```

`awaitingRotation` counts rows a pass can move; `onMissingKeys` counts rows under master keys that are
not loaded. Problems: `409 rotation_running`; `503 audit_unavailable` carrying `secretsRewrapped` and
`versionsRewrapped` for the batches committed before it stopped.

### 6b Audit log

```text
GET  /api/ui/audit             { entries: [{ entryId, timestampUtc, action, actionLabel, tone, resource,
                                 actor, sourceIp, details, isCritical }],
                                 signingKeyId, latestCheckpoint: { sequence, createdAtUtc, signingKeyId } | null }
POST /api/ui/audit/verify      → 200 { outcome: "anchored"|"unanchored"|"tampered", entriesVerified,
                                 anchoredAtSequence, firstBrokenSequence, checkpointsChecked,
                                 checkpointsNotChecked, message }
POST /api/ui/audit/checkpoints → 201 { sequence, createdAtUtc, signingKeyId }
POST /api/ui/audit/export      → 200 application/json, streamed, Content-Disposition attachment
```

Problems: `403 step_up_required` (export); `409 audit_busy` (verify, export), `nothing_to_checkpoint`
(Legacy text, unreachable from the UI); `503 audit_unavailable` (all four).

## Carried over unchanged

Legacy wording, including "KEK", "Re-wrap under current KEK" and the attestation explanation; the
200-row audit view and its client-side filter; bundle schema v2, the offline verifier and its 1.0.0
note; the signing key and master keys from environment variables, shown only as fingerprints; soft
premium usage on checkpoint and export, never on viewing; area keys and `AdminOnly` visibility.

## Testing

**6a Encryption keys**

- **Deterministic K1 interleaving on SQL Server and PostgreSQL:** the pass reads a row, its value then
  changes, the batch commits; the new value still decrypts and the row is skipped. A seam, never timing.
- **`KekRotationFixture`** (own Production host, collection runs alone): data sealed under a retired
  master key plus one row under a key that is not loaded. Shapes, authorization matrix, one
  `KekRotated` per batch and no `SecretUpdated`, `409` for a concurrent pass, missing keys skipped,
  secrets readable afterwards, a decoy re-wrapped without tripping.
- **AuditFailureFixture:** the failing batch rolls back; earlier batches stay.
- **Unit:** the cache is cleared per batch (fake cache).
- **Vitest:** every notice, the disabled button, the dialog, every StatusLine outcome.
- **Playwright** under browser-health: the page with nothing pending, the dialog, both shells, keyboard,
  both themes. The rotate path is proven over HTTP.

**6b Audit log**

- **Unit:** Verify as a pure Core function over streamed rows and checkpoints, as a literal table:
  intact anchored and unanchored, tail truncated before and after a checkpoint, rows rewritten and
  rehashed, a bad signature, another key's checkpoint, an empty trail.
- **Production HTTP on SQL Server and PostgreSQL:** shapes, authorization matrix, one view row per page
  load, the verify row with its outcome, export refused when stale and allowed when fresh, the streamed
  bundle passing `AuditBundleVerifier` pinned to the host's public key, `409 audit_busy` through a seam,
  the decoy rule.
- **Tampering on its own host** in a collection that runs alone (lesson 19).
- **AuditFailureFixture:** view, verify, checkpoint and export answer 503 with nothing changed, no
  checkpoint row and no export bytes.
- **`SelfServiceScopingTests`** read `GET /api/ui/audit` instead of the `/Audit` HTML.
- **Vitest:** every state. **Playwright** under browser-health: view, verify anchored after a
  checkpoint, Export behind a real step-up code (file name and schema), both shells, keyboard, both themes.

Both parts: browser suites run in CI; a ticket runs only its affected specs locally (lesson 17).

## Delivery outline

Two specs, published together after confirmation; 6b's spec is blocked by 6a's. Tests land with their
behaviour; each page is served in the ticket that builds it. The security reviewer reviews every
ticket; Standards and Spec reviews go to GPT Sol 6.1 at xhigh. Each part ends with a whole-part review
and its single PR. No schema change is expected in either part.

**6a Encryption keys**, branch `area-6a-encryption-keys` from `main`:

| Ticket | Behaviour | Decisions |
| --- | --- | --- |
| 1 | Rotate in batches that commit with their audit row, never overwrite a changed secret, run one pass at a time, skip missing keys, clear the cache. Commits every grill record: this folder, ADR-0029, ADR-0030, the ADR-0017 and ADR-0028 amendments and the glossary. | Q3, Q4, Q15, Q16 |
| 2 | Encryption keys served by Angular: UI API, page, route table; Legacy view and controller retired. | Q2, Q13, Q14, Q16 |
| 3 | Verify the whole part and prepare its PR. | whole-part reviews and gates |

**6b Audit log**, branch `area-6b-audit-log` from `main` after 6a merges:

| Ticket | Behaviour | Decisions |
| --- | --- | --- |
| 1 | Record each checkpoint with its audit row, and record viewing and verifying. | Q6, Q9, Q21 |
| 2 | Verify streams the chain and checks it against signed checkpoints, one run at a time; README corrected. | Q5, Q19, Q22 |
| 3 | Export streams a bundle after recording it, one at a time, behind step-up. | Q7, Q8, Q22 |
| 4 | Audit log served by Angular; tests off the `/Audit` HTML; Legacy view and controller retired. | Q2, Q18, Q20 |
| 5 | Verify the whole part and prepare its PR. | whole-part reviews and gates |

After confirmation: issue drafts under `issues/` (two specs, eight tickets, the K7 follow-up), published
on the owner's word.
