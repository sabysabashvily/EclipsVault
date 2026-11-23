# Area 6 Audit log and Encryption keys: Legacy survey

Surveyed on `main` at `4feec8c` (Area 5 merged) by reading the code. Nothing was run, and no finding
below has been reproduced; each is a code-read claim that a ticket must prove with a failing test first.

## What serves the two pages today

Both controllers carry `[Authorize(Policy = VaultPolicies.AdminOnly)]` (TopSecret clearance). A
non-administrator is sent to `/Account/Denied`. Area keys `audit` and `encryption` are `AdminOnly` in
`UiAreas`. MVC's global `AutoValidateAntiforgeryToken` covers every POST. No action takes input.

| Page | Routes | Controller, view, model | Core and Infrastructure behind it |
| --- | --- | --- | --- |
| Audit log | `GET /Audit`; `POST /Audit/Verify`, `/Audit/CreateCheckpoint`, `/Audit/Export` | `AuditController`, `Views/Audit/Index`, `AuditIndexViewModel` (+ `AuditDisplay` labels, shared with Dashboard and Secret details) | `IAuditLogReader` → `AuditLogReader` (`ListRecentAsync(200)`, `VerifyIntegrityAsync`); `IAuditCheckpointService` → `AuditCheckpointService` (`CreateCheckpointAsync`, `GetLatestAsync`, `ExportAsync`, `SigningKeyId`); `IAuditCheckpointSigner` → `EcdsaAuditCheckpointSigner` |
| Encryption keys | `GET /Encryption`; `POST /Encryption/Rotate` | `EncryptionController`, `Views/Encryption/Index`, model `KekStatus` | `IKekRotationService` → `KekRotationService` (`GetStatusAsync`, `RotateAsync`); `IKekProvider` → `EnvironmentKekProvider`; `ICryptoEngine.RewrapAsync` (AES-GCM or Vault Transit) |

Offline side: `AuditBundle`, `AuditBundleSchema` (v1, v2), `AuditBundleVerifier` (Core, BCL only) and
the `EclipsVault.AuditVerifier` tool (`--expected-key` pins the public key).

## Behaviour

**Audit log** shows the newest 200 rows by timestamp (time ago with a UTC tooltip, action badge and
tone from `AuditDisplay`, resource name or type, actor username, source IP, details; critical rows
highlighted), a client-side filter, and an Attestation panel: the signing key id, the latest checkpoint
(sequence, time, key id) or "none yet", and buttons Create signed checkpoint and Export signed bundle.
**Verify integrity** re-renders the page with "✓ Hash chain intact. … Any edit, deletion, or reordering
of a row would break the chain and be caught here." or "✗ Tampering detected. … (First broken entry: #n.)".
Create checkpoint flashes "Signed checkpoint created at sequence n with key k." or, with nothing
chained, "There are no audit entries to checkpoint yet." (info). Export downloads
`eclipsvault-audit-bundle-yyyyMMdd-HHmmss.json`, indented.

**Encryption keys** shows one notice (critical "Some secrets reference a KEK that is not loaded", warn
"n item(s) are still on a retired KEK", or ok "All secrets are wrapped under the current KEK"), a Keys in
use table (KEK id, current/retired/not loaded, secret and version counts), and a Rotate panel (current
KEK, keys loaded, awaiting rotation, the operator steps) with "Re-wrap under current KEK" behind a
confirm, disabled when nothing awaits. Flash: "Rotated n secret(s) and m version(s) to KEK k." or
"Everything is already wrapped under the current KEK (k)."

## What each page reveals, and to whom

Every administrator, from any network, sees:

- **Audit log:** the newest 200 rows of every account: usernames, source IPs, secret names (Confidential+
  included, off the trusted network too), a tripped decoy's name, share and request details; the
  signing key id (a fingerprint of the public key) and the latest checkpoint.
- **Export:** the whole chained trail, adding user ids, resource ids, hashes and the public key.
- **Encryption keys:** KEK ids (`kek-` + 4 bytes of SHA-256 of the key) and per-key counts.

No private key, KEK or DEK is shown or logged. A decoy is recognisable in the trail only after it trips:
its creation is an ordinary `SecretCreated` row. Rotation re-wraps a decoy's DEK without unsealing the
payload, so it never trips.

## Audit writes

| Action | Audit event | How it is written today |
| --- | --- | --- |
| View Audit log | none | — |
| Verify | none | — |
| Create checkpoint | `AuditCheckpointCreated` | save the checkpoint, then `IAuditSink` (not atomic) |
| Export | `AuditBundleExported` | `IAuditSink` after reading every row, before the file is returned (fail-closed) |
| View Encryption keys | none | — |
| Rotate | one `SecretUpdated` per re-wrapped secret (interceptor), none per version; then a critical `KekRotated` summary | one `SaveChanges` for every row, then the summary through `IAuditSink` (not atomic) |

Checkpoint and export also record soft premium usage (`audit-attestation`); viewing never does.

## Links

**Into the pages:** both sidebars and both ⌘K palettes (`audit`, `encryption`); the Overview dashboard's
critical-events card and "Full audit log" button (Area 7). **Out of the pages:** none; Export is a
download. **Tests that drive them:** `SelfServiceScopingTests` reads `/Audit` HTML four times to find
audit rows; `AreaVisibilityTests`, `AuthorizationSurfaceTests`, `UiApiFailureContractTests` (domain
exception mapping), the sidebar and palette specs and `e2e/sidebar.ts`. No test covers
`KekRotationService` or `AuditLogReader.VerifyIntegrityAsync`; `AuditCheckpointServiceTests` covers
only premium usage.

## Findings

K1. **Rotation can destroy a secret's current value.** Rotation loads every non-current secret, re-wraps
each DEK, and saves once at the end, with no concurrency token on `Secret`. If the value is replaced in
the meantime (an operator's rotate, or the managed-rotation job re-passwording a real principal),
the new ciphertext is sealed under a fresh DEK, and rotation's save then writes the old DEK over it.
The current value no longer decrypts. Under managed rotation the real principal already has the new
password, so the system it guards is locked out.

K2. **One rotation holds the audit chain for the whole vault.** All rows save in one transaction whose
interceptor holds the audit-chain lock until commit. Every other audited action, secret reads included,
waits on that lock (15 seconds) and then fails closed while a large rotation runs. Every pending row is
also loaded into memory at once.

K3. **Two rotations at once both run.** Nothing serialises them; each writes every row and every audit
row twice.

K4. **A key that is not loaded blocks every rotation.** Re-wrapping a row whose KEK is missing throws, so
the whole pass fails with a server error and nothing moves, even though the page keeps the button
enabled. If that key is truly lost, no other secret can ever leave the retired key.

K5. **Rotation's audit rows say the wrong thing.** Each re-wrapped secret is recorded as `SecretUpdated`,
telling an auditor an administrator edited every secret. Archived versions get no row. The summary row
is written after the commit, so it can be missing.

K6. **Rotation leaves stale envelopes in the secret cache.** Cached envelopes keep the old wrapped DEK
until their TTL; with Redis they survive a restart that removes the retired key.

K7. **The page is wrong under the KMS engine.** With `Crypto:Engine=VaultTransit`, status and rotation
compare Transit key ids (`vault:<key>:v<n>`) against the environment KEK's id. Every secret shows as
"not loaded" and every pass re-wraps, and reports, every secret.

A1. **Verify can show "intact" for a tampered trail.** It re-walks only the rows present, so deleting the
newest rows, or rewriting rows and recomputing every hash, still shows "✓ Hash chain intact". It never
checks the vault's own signed checkpoints. The page and README line 287 say any edit or deletion "would
be caught here"; README line 288 already says a database writer can recompute every hash.

A2. **Verify and Export load the whole trail into memory**, and Export serialises it again. Any
administrator, or a stolen administrator session, can repeat them freely.

A3. **Creating a checkpoint saves first and audits second.** A checkpoint can exist with no audit row.

A4. **Viewing and verifying the audit log are not recorded.** PCI DSS v4.0 requirement 10.2.1.3 asks that
all access to audit logs be captured. Export is recorded.

A5. **Export needs no fresh proof.** A stolen administrator session can take the whole trail (every
account's usernames, IPs and secret names) in one request.

## Facts that are not findings

- Export writes its audit row before returning the file, and fails closed.
- A checkpoint reads only the committed head, so it is consistent during concurrent writes.
- The signing key and KEKs come from environment variables and are never shown or logged; the page shows
  fingerprints only.
- The offline verifier reads v1 and v2 bundles and refuses newer formats without alleging tampering.
  The 1.0.0 verifier note (README line 288) stays.
- Rotation re-wraps DEKs only; it never re-encrypts payloads or decrypts a secret. Re-sealing under a new
  DEK happens only when a secret's value changes (Area 7).
- Open issues: none touches this Area. #131 is a licence test; the invariant it guards (viewing never
  starts an evaluation window) applies here only in that GET endpoints must not record premium usage.
