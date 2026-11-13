This spec is **Area 6 part 6a, Encryption keys** (ADR-0017 amendment). Part 6b, Audit log (#SPEC6B), follows after this part's PR merges. The owner accepted all 23 grill decisions and both delivery outlines, and confirmed them on 2026-11-09.

## Problem Statement

Opening Encryption keys from the Angular UI still leaves the app for a Legacy page (ADR-0001).

The grill's survey found these flaws, deductions from reading the code at `4feec8c`; each ticket proves its finding with a failing test first.

- **Master key rotation can destroy a secret's current value (K1).** The pass loads every pending row, re-wraps each data key and saves once at the end, and `Secret` has no concurrency token. A value replaced in between (by an operator, or by the managed-rotation job re-passwording a real principal) is sealed under a fresh data key; the pass then writes the old data key over it, and the value no longer decrypts.
- **One pass holds the audit chain for the whole vault (K2).** All rows save in one transaction, so every other audited action waits 15 seconds on the chain lock and fails closed. Every pending row is loaded into memory at once.
- **Two passes can run at once (K3).**
- **One row under a master key that is not loaded fails the whole pass (K4)** with a server error, though the button stays enabled. If that key is lost, no other secret can ever leave the retired key.
- **Rotation's audit rows say the wrong thing (K5).** Each re-wrapped secret is recorded as `SecretUpdated`; archived versions get no row; the summary row is written after the commit, so it can be missing.
- **Rotation leaves stale envelopes in the secret cache (K6).**

## Solution

The Angular UI serves Encryption keys at `/Encryption` and `/Encryption/Index` under `AdminOnly`, copying the Legacy page exactly except for the named fixes. Master key rotation runs in resumable batches, each committed with its audit row, never overwrites a changed secret, runs one pass at a time, and moves every row whose master key is loaded (ADR-0029).

## User Stories

1. As an administrator, the Encryption keys entry in both sidebars and both palettes opens the page in the Angular UI.
2. As an administrator, I see whether every secret is under the current master key, which keys are in use with their secret and version counts, the current and loaded keys, and how many items await rotation.
3. As an administrator, I re-wrap every pending item after confirming, and see how many secrets and versions moved and how many remain on keys that are not loaded.
4. As an administrator, if another rotation is running, I am told so and nothing changes.
5. As an operator, a secret whose value changes while a rotation runs keeps its new value.
6. As anyone using the vault during a long rotation, my audited actions are not refused because the rotation holds the audit trail.
7. As anyone, if a batch cannot be recorded, that batch is undone and the page says how much moved before it stopped.
8. As a non-administrator, `/Encryption` sends me to the Denied page and the UI API refuses me.

## Implementation Decisions

### Scope (Q2)

`EncryptionController` and `Views/Encryption/Index` go. The KMS engine's misreport (K7) is out of scope: the page is copied as is, and a follow-up issue covers it.

### Master key rotation (Q3, Q4, ADR-0029)

- Batches of 500 rows, secrets and archived versions. Each batch commits with one critical `KekRotated` row in its transaction, naming the old and new master key ids and counting secrets and versions. A change that only re-wraps a data key writes no per-secret `SecretUpdated` row.
- A row is re-wrapped only if its wrapped data key and master key id are still what the pass read; a row changed, deleted or shredded meanwhile is skipped.
- One pass at a time across replicas under a vault-wide database lock; another request answers `409 rotation_running`.
- Rows under a master key that is not loaded are skipped and counted; every other row moves.
- Each committed batch clears its secrets from the secret cache. A departing caller stops the pass between batches, never inside one.
- A recording failure rolls back that batch and stops the pass with `503 audit_unavailable`; earlier batches stay committed.

### UI API contract (Q13)

```text
GET  /api/ui/encryption        { currentKekId, loadedKekIds,
                                 keys: [{ kekId, status: "current"|"retired"|"not_loaded", secrets, versions }],
                                 awaitingRotation, onMissingKeys }
POST /api/ui/encryption/rotate → 200 { currentKekId, secretsRewrapped, versionsRewrapped, onMissingKeys }
```

`AdminOnly`, session-only, `no-store`, anti-forgery on the POST, explicit types, regenerated TypeScript. `awaitingRotation` counts rows a pass can move; `onMissingKeys` counts rows under master keys that are not loaded; labels, order and counts are computed on the server. Problems: `409 rotation_running`; `503 audit_unavailable` carrying `secretsRewrapped` and `versionsRewrapped` for the committed batches. Rotation needs no step-up (ADR-0028).

### Presentation (Q14)

Exact copy: the three notices, the Keys in use table, the Rotate panel and the operator steps, word for word. The confirm becomes ConfirmationDialog with the Legacy text; the Legacy flash text goes in the StatusLine. Deliberate changes:

- The button is disabled when `awaitingRotation` is 0.
- Success adds " n item(s) remain on keys that are not loaded." when that count is above 0.
- `409`: "Another rotation is already running. Try again when it finishes."
- `503`: "Rotation stopped because the audit trail could not record it. n item(s) were re-wrapped before it stopped; run it again to continue."
- The page reloads its status after any outcome.

### Decoys (Q10)

Rotation re-wraps a decoy's data key without unsealing it, so it never trips.

## Testing Decisions

- **K1 on SQL Server and real PostgreSQL**, by a deterministic interleaving through a seam, never timing: the pass reads a row, its value then changes, the batch commits; the new value still decrypts and the row is skipped.
- **`KekRotationFixture`**, a Production host of its own in a collection that runs alone, with data sealed under a retired master key and one row under a key that is not loaded: response shapes, the authorization matrix, anti-forgery and `no-store`, one `KekRotated` row per batch and no `SecretUpdated`, `409` for a concurrent pass, missing keys skipped, secrets readable afterwards, a decoy re-wrapped without tripping.
- **AuditFailureFixture:** the failing batch rolls back; earlier batches stay.
- **Unit:** each committed batch clears its secrets from the cache (fake cache).
- **Vitest:** every notice, the disabled button, the dialog, every StatusLine outcome.
- **Playwright** under browser-health: the page with nothing pending (ok notice, disabled button), the dialog, both shells, both palettes, keyboard and both themes. The rotate path is proven over HTTP, because the e2e stack holds one master key. Browser suites run in CI; a ticket runs only its affected specs locally.

## Delivery

Branch `area-6a-encryption-keys` from `main`, three sequential tickets, one PR. The `security-reviewer` agent reviews every ticket and the whole part; Standards and Spec reviews go to GPT Sol 6.1 at xhigh.

## Out of Scope

- The KMS engine's status and rotation (follow-up issue).
- Re-encrypting payloads under new data keys.
- Running rotation as a background job.
- Any redesign.

## Records

`docs/planning/area-6-audit-and-keys/`; `CONTEXT.md` (Master key, Data key, Master key rotation); ADRs 0017, 0021, 0028, 0029.
