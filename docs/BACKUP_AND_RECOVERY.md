# Backup & recovery

The "I lost everything" runbook. EclipsVault seals every secret with your master key (KEK), so a
recoverable deployment is not one thing to back up — it is **three**, kept in the right places. This
expands on the summary in [`INSTALL.md`](INSTALL.md) §7.

> **The one rule that matters:** the KEK is what makes a backup meaningful *and* what makes it
> dangerous. A database backup without the KEK is inert ciphertext (good — that is the point). A
> backup *beside* the KEK is plaintext waiting to be read. Keep them in **different systems with
> different access**.

## What to back up

| # | Item | Where it is | If you lose it |
|---|---|---|---|
| 1 | **Database** | Your SQL Server / PostgreSQL | All secrets, versions, users, audit trail — gone. |
| 2 | **Data Protection key ring** | `DataProtection__KeyRingPath` | Sessions/antiforgery break; sealed at rest with the KEK, so it is inert on its own. |
| 3 | **The KEK** (`ECLIPSVAULT_KEK`) and any **retired KEKs** (`ECLIPSVAULT_KEK_RETIRED`) | Your secret manager / KMS | **Every secret is permanently unrecoverable.** There is no vendor recovery. |
| 4 | **Audit signing key** (`ECLIPSVAULT_AUDIT_SIGNING_KEY`) | Your secret manager | You can still read the audit trail, but new checkpoints won't chain to the old ones under the same key. |

Items 1–2 go in your normal encrypted backup. Items 3–4 live in a secret manager (or KMS) and are
**never** written next to the backup, into an environment file that gets snapshotted, or into the
same cloud account with the same credentials that reach the backup.

If you use the KMS engine (`Crypto:Engine=VaultTransit`), the KEK lives in HashiCorp Vault instead of
`ECLIPSVAULT_KEK` — then item 3 is "back up / seal-and-recover your Vault Transit key" per Vault's own
runbook, and the app never holds the master key at all.

## Restore procedure

1. **Provision a clean database** and restore the backup (item 1).
2. **Restore the key ring** directory to `DataProtection__KeyRingPath` (item 2).
3. **Supply the same KEK.** Set `ECLIPSVAULT_KEK` to the exact key that sealed this data. If secrets
   were written under earlier keys and not yet rotated forward, also set `ECLIPSVAULT_KEK_RETIRED`
   (the `;`-separated list) so their DEKs still unwrap (item 3).
4. **Supply the audit signing key** (`ECLIPSVAULT_AUDIT_SIGNING_KEY`, item 4).
5. **Start the app** with `ASPNETCORE_ENVIRONMENT=Production` and the normal connection string.
6. **Verify** (this is the part people skip):
   - Startup logs the license line and does **not** warn about a missing/malformed KEK.
   - Sign in, complete MFA, and **reveal one secret** — proves the KEK unwraps a real DEK.
   - On the admin **Audit log** page, run **Verify integrity** — the hash chain reports intact.
   - Optionally, export an audit bundle and verify it offline with `EclipsVault.AuditVerifier`.

If step 6 reveals a secret, your restore is real. If it cannot, stop and fix the KEK before trusting
the environment — a half-restored vault that "starts fine" but cannot decrypt is the worst outcome,
because it looks healthy.

## Restoring Redis (multi-node deployments)

With `Redis:Enabled=true`, Redis holds which sessions have been signed out. Redis is not in the
backup list above because losing it costs only sessions: when its state is gone, every session is
refused and everyone signs in again. **Restoring it from a snapshot, backup or lagging replica is
different.** The copy can predate sign-outs, so a session that was signed out everywhere, or whose
account an administrator demoted, disabled or force-signed-out, becomes valid again.

So after any Redis restore or failover that may have lost writes, **do not let traffic back until
every node has moved to a new session-store incarnation:**

1. **Stop traffic** to every vault node (at the load balancer, or stop the nodes).
2. **Pick a new incarnation**, one never used before, for example the date and time of the
   recovery: `Redis__SessionIncarnation=2026-10-24T1530`.
3. **Set it on every node** and restart them all. Session keys live under the incarnation, so the
   nodes ignore everything in the restored copy and refuse every existing session and pending
   sign-in. The value lives in your deployment configuration, outside Redis, so the restore
   cannot bring the old one back.
4. **Retire any node you cannot change.** A node still on the old incarnation would accept the
   restored sessions; so would a node running a release older than the one that introduced
   `SessionIncarnation`.
5. **Resume traffic.** Everyone signs in again once.

The vault cannot tell a restored copy from the live one, so it cannot do this for you: the
guarantee holds only when the recovery follows these steps. Losing Redis entirely needs no action;
the missing state already refuses the sessions it covered. If a credential change cannot recheck
Redis before committing, it answers `503 session_store_unavailable` and that transaction saves
nothing. If the change commits but Redis then prevents issuing its replacement session, the change
stands and the response is `changed_sign_in_required`: sign in again rather than repeat the change.
These outcomes point at Redis, not the audit trail.

A mailbox request whose code was sent but whose delivery-completion recheck loses Redis answers
`503 email_request_incomplete`. The request remains recorded and the active email is unchanged;
verify again before deliberately resending. The unrecorded delivery cannot activate the address.

Redis must run with `maxmemory-policy noeviction`, which is Redis's own default but not every managed
service's: many default to `volatile-lru`. Evicting an account's revocation version is safe, but the
record that one device signed out is a key with an expiry, and evicting it lets a copy of that
device's cookie back in. Under `noeviction` a full Redis refuses the write instead, so the sign-out
fails where the user can see it rather than being undone later.

The vault enforces this at startup. It reads `maxmemory_policy` from `INFO memory` on every Redis
server known when it starts, and refuses to start when the policy is anything else or cannot be read,
with a message naming the setting and the fix. It does not check again while it runs: a policy
changed later with `CONFIG SET` or a parameter group, or a server added or promoted after startup,
goes unnoticed until the next restart. Pin `maxmemory-policy noeviction` in `redis.conf` or the
parameter group of every node, including replicas and any restored or replacement Redis.

## Test the restore (do this on a schedule, not after a disaster)

A backup you have never restored is a hypothesis. Once a quarter (and after any KEK rotation):

1. Restore items 1–2 into a **throwaway** environment.
2. Supply the KEK and audit key from your secret manager.
3. Run step 6 above.
4. Tear the throwaway environment down.

This exercise is also what proves your KEK custody actually works — if you cannot lay hands on the KEK
during a drill, you would not have had it during a real outage either.

## KEK rotation (keeps the recovery window bounded)

Rotating the KEK limits how long any single key must survive to keep old data readable. Generate a new
key, move the previous one into `ECLIPSVAULT_KEK_RETIRED`, restart, then run the rotation from the
admin **Encryption keys** page. Once it reports everything on the current KEK, the retired key can be
dropped from the list — and from that point a backup only needs the current KEK. Full steps are in the
[`README`](../README.md) under *Master key (KEK)*.

## What recovery cannot do

Stated plainly so it is never a surprise (see [`THREAT_MODEL.md`](THREAT_MODEL.md)): if the KEK and all
retired KEKs are lost, the data is gone. This is a cryptographic guarantee working as intended, not a
failure — it is also why nobody, including the vendor, can read your secrets. Your recovery plan **is**
your KEK-custody plan.
