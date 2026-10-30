# Credential generations invalidate old sessions and pending proofs atomically

A database-backed credential generation advances in the same transaction as a sensitive credential
change and its audit event. Sessions, pending sign-in proofs and credential-management ceremonies
carry the generation their proof established; validation rejects an older generation. Timestamp
arithmetic and cache-only revocation cannot make a password change atomic with its invalidation.

The original generation travels through verification, mutation and session issuance. A stale proof
must never be stamped with the latest generation merely because the account was read again. A
replacement session uses the committed mutation result and a new session id. Rollback also rolls
back the generation, and concurrent security changes must not overwrite each other.

Every completed password change, active email change, recovery-code regeneration, and passkey
addition or removal invalidates prior sessions and retains only a genuinely fresh current session.
Authenticator reset invalidates every session, including the current one. Display-name and avatar
edits, and merely starting a change, do not end sessions. Pending proofs obtained before a retired
credential must not remain usable afterward.

Discoverable passkey sign-in has an explicit boundary: its anonymous challenge is not yet proof of
an account. Capture the account generation when the credential identifies its owner, before
verifying the assertion, and fence completion against concurrent changes. An unchanged, registered
passkey may answer an unexpired anonymous challenge issued before another credential change. Named
account proofs and management ceremonies remain strictly generation-bound.

Device observations retain their original credential generation and session revocation version;
listings filter invalid observations so an already-running old request cannot resurrect a device
record. Metadata cleanup or recording failure cannot restore authentication validity.

A committed mutation remains committed if cookie renewal fails or its reply is lost. Old sessions
remain invalid; credentials and generation are not restored, and the mutation is not automatically
retried. A known commit with failed session renewal has a distinct changed-but-sign-in-required
outcome. An ambiguous reply is reported as unknown, followed by a read or sign-in check before a
deliberate retry. Lost recovery plaintext requires explicit regeneration. Device metadata is
best-effort and cannot undo the credential commit.

Generation-less sessions and pending proofs are refused after deployment, requiring one fresh
sign-in. Nodes running the older validation rules must be retired before the new invalidation
guarantee can be relied on. Explicit device sign-out remains external session-store revocation and
keeps ADR-0021's revoke-first behavior; it is not blocked by an audit failure.
