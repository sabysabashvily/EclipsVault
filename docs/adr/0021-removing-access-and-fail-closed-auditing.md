# Removing access is atomic with its audit row where it can be, and never blocked where it cannot

Fail-closed auditing says an action whose audit row cannot be written does not happen. Read literally for an action that *removes* access, that would leave an intruder's session or a colleague's share open because the audit store is down. We split the rule by where the change lives:

- **A change stored in the database is written in one transaction with its audit row.** Creating and revoking a share both go through `AuditSaveChangesInterceptor`, so both happen with their row or neither does. An audit failure answers `503 audit_unavailable`, and the share is unchanged.
- **A session lives in the session store (Redis or memory), outside that transaction.** Signing out a device, or every other device, revokes first and audits second. If the audit write fails, the revocation stands and the UI API answers `503 revoked_unrecorded`, saying the device was signed out but not recorded. "Sign out all other sessions" revokes every other session before auditing any of them.

If a session-store call fails during "sign out all other sessions", the vault still attempts every
other device and audits every confirmed removal. It answers `503 sign_out_incomplete` with `signedOut`
counting only confirmed removals, and the page refreshes its list. A lost store reply may conceal a
completed removal, so the vault cannot claim an outcome or an audit row for that device. If recording
confirmed removals also fails, the same response warns about both failures. `revoked_unrecorded`
continues to mean every store operation completed and only recording failed.

Either way, the audit trail never claims a revocation that did not happen: revoking a session that is not the caller's, or no longer exists, answers `404 session_not_found` and writes no row.

## Account-wide sign-out during the Profile migration

Explicit account-wide sign-out changes a session-store revocation version atomically instead of
comparing authentication times with a wall-clock cutoff. New sessions carry the current version;
ordinary cookie renewal retains its original version. This lets a genuinely new session survive an
earlier sign-out in the same second without giving it a future authentication time. Missing store
state invalidates old versions. The memory and Redis implementations have the same behavior.

Capture the revocation version before loading the account snapshot used for authentication, carry
it through verification and issuance, and refuse issuance if it changed. Reading a newer version
after authentication must not give stale role claims a valid new session. Sliding expiration,
display-name/avatar renewal and secret step-up retain the original version. This applies to every
account-wide revocation caller, including role changes, disabling, forced sign-out, deletion and
intrusion response. New store state is initialized atomically with a non-repeating version; validation
never recreates missing state from a cookie.

Restoring populated Redis state can restore a version that was already revoked. Before accepting
traffic after recovery that may have lost session-store writes, coordinate a new session-store
incarnation held outside the restored Redis keyspace. All nodes must use it for session issuance,
validation and pending session-bound proofs, invalidating sessions from the earlier incarnation.
The recovery procedure gates traffic until rotation is complete across all nodes; old nodes that
cannot enforce it must be retired. Document and test that recovery gate. This guarantee requires
coordinated recovery and does not claim automatic detection of an undisclosed store rollback.

The database credential generation in ADR-0023 remains separate: it makes credential replacement
atomic with invalidation and auditing. Per-device revocation also remains in force. Explicit
sign-out still revokes before auditing, and an audit failure never restores access.

Sign out everywhere clears the current browser's cookie even when revocation or auditing fails.
Confirmed revocation with failed auditing answers `503 revoked_unrecorded`; uncertain or incomplete
revocation answers `503 sign_out_incomplete`. The UI explains the actual outcome, including when
other devices may remain signed in, and preserves its warning through the trip to Legacy sign-in.

ADR-0026 supplies the narrowly scoped local-cookie-clearing path when store-backed authentication
cannot reach the normal sign-out handler. That path cannot revoke sessions on other devices.

## Considered Options

- **Audit first, then revoke, for sessions.** Rejected: it keeps access open for as long as auditing is down, which is exactly when an intruder benefits.
- **Report success and only log the audit failure.** Rejected: it hides an audit failure from the account that needs to tell an administrator.
