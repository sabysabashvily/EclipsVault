Follows #SPEC6A (Area 6 part 6a, Encryption keys). This spec is **Area 6 part 6b, Audit log** (ADR-0017 amendment). The owner accepted all 23 grill decisions and both delivery outlines, and confirmed them on 2026-11-09.

## Problem Statement

Opening the Audit log from the Angular UI still leaves the app for a Legacy page (ADR-0001).

The grill's survey found these flaws, deductions from reading the code at `4feec8c`; each ticket proves its finding with a failing test first.

- **Verify can show "intact" for a tampered trail (A1).** It re-walks only the rows present, so deleting the newest entries, or rewriting entries and recomputing every hash, still shows "✓ Hash chain intact". It never checks the vault's own signed checkpoints, yet the page and README line 287 say any edit or deletion "would be caught here".
- **Verify and Export load the whole trail into memory (A2)**, and can be repeated freely.
- **A checkpoint is saved before it is audited (A3)**, so one can exist with no audit row.
- **Viewing and verifying the audit log are not recorded (A4).** PCI DSS v4.0 requirement 10.2.1.3 asks that all access to audit logs be captured.
- **Export needs no fresh proof (A5).** A stolen administrator session can take the whole trail in one request.

## Solution

The Angular UI serves the Audit log at `/Audit` and `/Audit/Index` under `AdminOnly`, copying the Legacy page exactly except for the named fixes. Verify anchors the chain to signed checkpoints and says where proof ends; Export streams a bundle after recording it, behind step-up; checkpoints, viewing and verifying are recorded fail-closed (ADR-0030).

## User Stories

1. As an administrator, the Audit log entry in both sidebars and both palettes, and the Dashboard's critical-events card and "Full audit log" button, open the page in the Angular UI.
2. As an administrator, I see the newest 200 entries, critical ones highlighted, and filter them in the page.
3. As an administrator, Verify tells me whether the trail is intact and anchored by a signed checkpoint, intact with no checkpoint, or tampered and where, and never calls a truncated or rewritten trail intact up to the latest checkpoint.
4. As an administrator, I create a signed checkpoint and see its sequence and key.
5. As an administrator whose last strong sign-in is too old, Export asks for an authenticator code once, or to sign in again, then downloads the bundle.
6. As an outside auditor, an exported bundle verifies offline against the vault's pinned public key, with every verifier that reads format v2.
7. As an auditor of the vault, every view, verification, checkpoint and export of the trail is itself in the trail.
8. As anyone, if any of these cannot be recorded, it does not happen and the page says the audit trail is unavailable.
9. As a non-administrator, `/Audit` sends me to the Denied page and the UI API refuses me.

## Implementation Decisions

### Scope (Q2)

`AuditController`, `Views/Audit/Index` and `AuditIndexViewModel` go. `AuditDisplay` stays (the Dashboard and Secret details use it) and feeds the endpoint's labels. The Dashboard's links stay with Area 7.

### Verify (Q5, Q19, ADR-0030)

Verify is a pure Core function over the trail streamed in sequence order, with constant memory. It checks every stored checkpoint signed by the current signing key: a valid signature, the checkpoint's head hash at its sequence, and no entries missing up to the latest checkpoint. Checkpoints signed by another key are counted as not checked. Outcomes and notices:

- **Anchored (ok):** "✓ Hash chain intact." "n entries verified. The signed checkpoint at sequence s matches, so entries up to #s are proven unchanged against the vault's signing key; later entries are covered by the hash chain until the next checkpoint. To prove the trail to an outside party, export a bundle and verify it offline against the vault's public key."
- **Unanchored (warn):** "✓ Hash chain intact, but no signed checkpoint covers it yet." "n entries verified. Without a checkpoint, someone with database access could delete the newest entries or rewrite the trail and recompute its hashes without detection here. Create a signed checkpoint."
- **Tampered (critical):** "✗ Tampering detected." with its reason and "(First broken entry: #n.)". New reasons: "The chain ends at #n, before the signed checkpoint at #s: entries were deleted.", "Entry #s does not match the signed checkpoint: the trail was rewritten.", "The checkpoint at sequence s has an invalid signature."
- When any checkpoints were skipped: "c checkpoint(s) signed by another key were not checked."

README line 287 is corrected to match.

### Recording (Q6, Q9)

- A checkpoint commits with its `AuditCheckpointCreated` row through an interceptor rule.
- Opening the audit log writes `AuditLogViewed`; Verify writes `AuditChainVerified` with its outcome, critical when tampering is found. Both new actions get labels ("Audit log viewed", "Audit chain verified", muted) and activity descriptions.
- Each fails closed with `503 audit_unavailable`.

### Export (Q7, Q8)

Export signs the committed head, writes `AuditBundleExported`, then streams the entries up to that head. A failed audit write sends no bytes. Schema v2, the file name and indentation are unchanged. Export needs a sign-in or step-up within `StepUp:MaxAuthAgeMinutes` (ADR-0028's bulk-disclosure rule). Verify and Export each run one at a time; another request answers `409 audit_busy`.

### UI API contract (Q18)

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

`AdminOnly`, session-only, `no-store`, anti-forgery on POSTs, explicit types, regenerated TypeScript. Entries are ordered by time, then sequence. Verify and Checkpoint update the page in place, so only opening the page writes a view row. Problems: `403 step_up_required` (export); `409 audit_busy` (verify, export), `nothing_to_checkpoint` (the Legacy text); `503 audit_unavailable` (all four).

### Who sees what (Q10)

Names and IPs show to every administrator, unfiltered, from any network (Area 5 Q11); Export adds step-up. A decoy is recognisable only after it trips.

### Presentation (Q20)

Exact copy: header, Verify, the attestation panel and its text, the latest-checkpoint line, the shared TableFilter, the table, critical rows, relative time with a UTC tooltip. Deliberate changes: the Verify notices above; the checkpoint result in the StatusLine with the Legacy text; Export through the StepUpDialog; `409`: "Another verification or export is running. Try again when it finishes."; the standard 503 text.

## Testing Decisions

- **Unit:** Verify as a literal table: intact anchored and unanchored, tail truncated before and after a checkpoint, entries rewritten and rehashed, a bad signature, another key's checkpoint, an empty trail.
- **Production HTTP on SQL Server and PostgreSQL:** shapes, every code, the authorization matrix, anti-forgery and `no-store`; one view row per page load; the verify row with its outcome; export refused when stale and allowed when fresh; the streamed bundle passing `AuditBundleVerifier` pinned to the host's public key; `409 audit_busy` through a seam; the decoy rule.
- **Tampering on its own host**, in a collection that runs alone, so no other test sees a tampered trail.
- **AuditFailureFixture:** view, verify, checkpoint and export answer 503 with nothing changed, no checkpoint row and no export bytes.
- **`SelfServiceScopingTests`** read `GET /api/ui/audit` instead of the `/Audit` HTML.
- **Vitest:** every state, dialog and notice.
- **Playwright** under browser-health: view; verify anchored after creating a checkpoint; Export behind a real step-up code (file name and schema); links from both shells, palettes and the Dashboard; keyboard and both themes. Browser suites run in CI; a ticket runs only its affected specs locally.

## Delivery

Branch `area-6b-audit-log` from `main` after #SPEC6A's PR merges, five sequential tickets, one PR. The `security-reviewer` agent reviews every ticket and the whole part; Standards and Spec reviews go to GPT Sol 6.1 at xhigh.

## Out of Scope

- Storing each checkpoint's public key, or checking checkpoints signed by other keys.
- Searching the trail beyond the newest 200 entries.
- Scheduled exports.
- Any redesign.

## Records

`docs/planning/area-6-audit-and-keys/`; `CONTEXT.md` (Audit trail, Checkpoint, Audit bundle, Signing key); ADRs 0017, 0021, 0028, 0030.
