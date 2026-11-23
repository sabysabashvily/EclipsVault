## Parent

#SPEC

## What to build

Master key rotation follows ADR-0029, through the existing Legacy Rotate action:

- Batches of 500 rows (secrets and archived versions), each committed with one critical `KekRotated` row in its transaction, naming the old and new master key ids and counting secrets and versions. A change that only re-wraps a data key writes no `SecretUpdated` row.
- A row is re-wrapped only if its wrapped data key and master key id are unchanged since the pass read it; a row changed, deleted or shredded meanwhile is skipped.
- One pass at a time across replicas under a vault-wide database lock; another pass is refused as already running.
- Rows under a master key that is not loaded are skipped and counted; the rest move.
- Each committed batch clears its secrets from the secret cache. Cancellation stops the pass between batches, never inside one.
- A recording failure rolls back that batch and stops the pass; earlier batches stay.

This ticket also commits the grill's records: `docs/planning/area-6-audit-and-keys/`, ADR-0029, the ADR-0017, ADR-0028 and ADR-0030 changes, and `CONTEXT.md`.

## Acceptance criteria

- [ ] Each finding (K1 lost value, K2 whole-vault lock, K3 concurrent passes, K4 missing key fails everything, K5 wrong audit rows, K6 stale cache) is first shown failing, then fixed.
- [ ] K1 is proven on SQL Server and real PostgreSQL by a deterministic interleaving through a seam, never by timing.
- [ ] `KekRotationFixture` (own Production host, collection runs alone): a pass over more than one batch writes one `KekRotated` row per batch and no `SecretUpdated` row; secrets and versions decrypt afterwards; a row under a key that is not loaded is skipped and counted; two concurrent passes give one run and one refusal; a decoy is re-wrapped without tripping.
- [ ] AuditFailureFixture: the failing batch rolls back and earlier batches stay committed.
- [ ] Unit: each committed batch clears its secrets from the cache.
- [ ] No schema change, or migrations in both sets if one proves necessary.
- [ ] Locked restore; Release build with 0 warnings; format; unit and integration suites pass.

## Blocked by

None.
