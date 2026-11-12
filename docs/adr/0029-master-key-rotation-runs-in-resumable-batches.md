# Master key rotation runs in resumable batches and never overwrites a changed secret

Rotation re-wraps every secret's and archived version's data key under the current master key.
It does not re-wrap the Data Protection key ring, so ring-dependent retired master keys must stay
loaded. The Area 6 grill found that the Legacy pass loaded every pending
row, re-wrapped them in memory and saved them in one transaction. A secret whose value changed in the
meantime got its new ciphertext paired with its old data key and could no longer be decrypted; the
single transaction held the audit chain, and so every other audited action, for the whole pass; two
passes could run at once; one row under a master key that was not loaded failed the whole pass; and
each re-wrapped secret was recorded as an edit.

## Decisions

- **Batches of 500 rows, each committed with its audit row.** One critical `KekRotated` row per batch,
  in the batch's transaction, names the old and new master key ids and counts secrets and versions.
  A change that only re-wraps a data key writes no per-secret `SecretUpdated` row. If the row cannot
  be written, that batch rolls back and the pass stops with `503 audit_unavailable`; earlier batches
  stay committed.
- **A changed row is never overwritten.** A row is re-wrapped only if its wrapped data key and master
  key id are still what the pass read. A row changed, deleted or shredded in the meantime is skipped;
  a changed value is already sealed under the current master key.
- **One pass at a time across replicas**, under a vault-wide database lock. Another request answers
  `409 rotation_running`.
- **A missing master key does not block the rest.** Rows under a master key that is not loaded are
  skipped and reported as remaining; every other row moves.
- **Each batch clears its secrets from the secret cache** after it commits. If the caller leaves, the
  pass stops between batches, never inside one.

## Consequences

- During and after an interrupted pass, rows sit under both the current and retired master keys. Keep
  retired keys loaded while any secret row or Data Protection ring entry still needs them; running
  the pass again continues where it stopped. A zero pending count describes secrets and versions
  only. Keep the keys with recovery material for backups that need them. Data Protection ring
  migration remains separate protected work.
- Other audited actions wait for the audit chain for at most one batch, not for the whole pass. An
  action that waits longer than the chain's 15-second limit still fails closed.
- One batch holds the audit chain for one round trip per row; on a high-latency database, run
  rotation in a quiet window.
- A secret read while its batch commits can be cached with its old envelope, and a cache that cannot
  be cleared after a batch commits stops the pass with an error. Either way such an envelope still
  names the retired master key until it expires (5 minutes by default). Recovery from such a stale
  entry must read the current database envelope independently of cache success. Neither waiting a
  cache lifetime nor reaching zero pending rows proves that a retired key can be removed: rotation
  does not re-wrap the Data Protection key ring, which is sealed with the master key too.
- If a batch's commit acknowledgement is lost, the counts a stopped pass reports can be lower than
  what moved. The status the page reloads afterwards is authoritative.
- A row whose data key no longer opens under its own master key is skipped and stays pending, so one
  damaged row cannot stop every pass. Under the KMS engine, a row already on the latest key version
  is neither written nor recorded.

## Considered Options

- **Keep one transaction for the whole pass.** Rejected: it locks every audited action in the vault
  for as long as the pass runs, and fails closed on all of them.
- **Refuse the whole pass while any master key is missing.** Rejected: if that key is truly lost, no
  other secret could ever leave the retired key.
- **One audit row per re-wrapped secret.** Rejected: a re-wrap changes no secret, and thousands of
  rows would bury the one event an auditor needs.
