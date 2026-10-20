# Removing access is atomic with its audit row where it can be, and never blocked where it cannot

Fail-closed auditing says an action whose audit row cannot be written does not happen. Read literally for an action that *removes* access, that would leave an intruder's session or a colleague's share open because the audit store is down. We split the rule by where the change lives:

- **A change stored in the database is written in one transaction with its audit row.** Creating and revoking a share both go through `AuditSaveChangesInterceptor`, so both happen with their row or neither does. An audit failure answers `503 audit_unavailable`, and the share is unchanged.
- **A session lives in the session store (Redis or memory), outside that transaction.** Signing out a device, or every other device, revokes first and audits second. If the audit write fails, the revocation stands and the UI API answers `503 revoked_unrecorded`, saying the device was signed out but not recorded. "Sign out all other sessions" revokes every other session before auditing any of them.

Either way, the audit trail never claims a revocation that did not happen: revoking a session that is not the caller's, or no longer exists, answers `404 session_not_found` and writes no row.

## Considered Options

- **Audit first, then revoke, for sessions.** Rejected: it keeps access open for as long as auditing is down, which is exactly when an intruder benefits.
- **Report success and only log the audit failure.** Rejected: it hides an audit failure from the account that needs to tell an administrator.
