## Parent

#SPEC

## What to build

Verify becomes a pure Core function over the trail streamed in sequence order, with constant memory, and checks it against the stored checkpoints signed by the current signing key (ADR-0030): a valid signature, the checkpoint's head hash at its sequence, and no entries missing up to the latest checkpoint. Checkpoints signed by another key are counted as not checked. The outcome is anchored, unanchored or tampered, with the messages in #SPEC. Verify runs one at a time; another request is refused as busy. README line 287 is corrected, and the Legacy Verify notice shows the new outcomes until ticket 4 retires it.

## Acceptance criteria

- [ ] The finding (A1: a truncated tail, or rewritten and rehashed entries, shown as intact) is first shown failing, then fixed.
- [ ] Unit literal table: intact anchored and unanchored, tail truncated before and after a checkpoint, entries rewritten and rehashed, a bad signature, another key's checkpoint, an empty trail.
- [ ] Tampering host (own Production host, collection runs alone, SQL Server and PostgreSQL): deleting the newest entries past a checkpoint and rewriting entries are each reported as tampered with the right entry.
- [ ] Rows are streamed, never loaded whole; the reviewer confirms the query shape.
- [ ] A concurrent Verify is refused as busy, proven through a seam.
- [ ] The 1.0.0 verifier note in the README stays.
- [ ] Locked restore; Release build with 0 warnings; format; unit and integration suites pass.

## Blocked by

- #T1
