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

## Considered Options

- **Audit first, then revoke, for sessions.** Rejected: it keeps access open for as long as auditing is down, which is exactly when an intruder benefits.
- **Report success and only log the audit failure.** Rejected: it hides an audit failure from the account that needs to tell an administrator.
