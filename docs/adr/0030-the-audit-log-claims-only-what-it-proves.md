# The audit log claims only what it proves, and access to it is recorded

The Area 6 grill found that Verify re-walked only the rows present. Deleting the newest entries, or
rewriting entries and recomputing every hash, still showed "Hash chain intact", while the page said
any edit or deletion "would be caught here". It never compared the trail with the vault's own signed
checkpoints. Verify and Export also loaded the whole trail into memory, Export needed no fresh proof,
and viewing or verifying the trail left no record.

## Decisions

- **Verify anchors the chain to signed checkpoints.** It streams the trail in sequence order and checks
  every stored checkpoint signed by the current signing key: a valid signature, the checkpoint's head
  hash at its sequence, and no entries missing up to the latest checkpoint. Checkpoints signed by
  another key are counted as not checked. The result is one of: intact and anchored at a sequence,
  intact with no checkpoint, or tampered with its first broken entry.
- **The page says where proof ends.** Entries up to the latest checkpoint are proven against the
  signing key; later ones only by the hash chain. Proof to an outside party is an audit bundle checked
  offline against a pinned public key.
- **Export records itself before the first byte.** It signs the committed head, writes its audit row,
  then streams the entries up to that head. If the row cannot be written, nothing is sent. The bundle
  format stays version 2.
- **Verify and Export run one at a time**; another request answers `409 audit_busy`.
- **Export needs fresh proof** (ADR-0028's bulk-disclosure rule).
- **Access is recorded, fail-closed.** Opening the audit log writes `AuditLogViewed`; Verify writes
  `AuditChainVerified` with its outcome, critical when tampering is found; a checkpoint commits with
  its `AuditCheckpointCreated` row. If the row cannot be written, the action does not happen.

## Consequences

- Someone with database access can still delete checkpoints, or change entries written after the
  latest one, without Verify noticing. Only bundles an auditor already holds expose that, so the
  advice to export on a schedule stands.
- Entry and checkpoint times are stamped to whole microseconds, the finest PostgreSQL stores, so the
  time read back is the one that was hashed or signed. SQL Server keeps the finer time, so its existing
  entries are unchanged. Entries and checkpoints an earlier version wrote on Linux with PostgreSQL were
  stamped to 100 ns and stored rounded: Verify, and the offline verifier on an exported bundle, report
  them as tampered. They are not repaired, and Verify is not made to tolerate them.
- The trail grows by one entry per page view and per verification. This is the record PCI DSS v4.0
  requirement 10.2.1.3 asks for.

## Considered Options

- **Keep Verify as a chain walk and only reword the page.** Rejected: an honest message about a check
  that misses truncation is still a check that misses truncation, and the checkpoints already exist.
- **Store each checkpoint's public key so every checkpoint can be checked.** Rejected for now: it needs
  a schema change, and a database writer could replace the stored key too.
- **Leave viewing unrecorded.** Rejected: access to audit logs is itself an auditable event for the
  compliance-driven buyers this vault serves.
