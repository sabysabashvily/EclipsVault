# Secret operations record denials as denials, shares only read, and nothing is cheaper than a reveal

The Area 7 grill read the Legacy Secret details, Secrets and Dynamic secrets pages. Every by-id action
looked the secret up through a method that wrote `SecretMetadataViewed` before the access check ran,
so a refused caller was recorded as having viewed the secret, and the secret's name then appeared in
that caller's own activity feed. A share's recipient could rotate, restore and delete the secret,
because those actions checked only read access. Restore and managed rotation committed a generic
`SecretUpdated` row and wrote their real event afterwards. Two rotations of one secret could lose a
value from its history or be reported as an audit failure. Minting a dynamic credential, or sharing a
high-sensitivity secret, needed no fresh proof, though revealing a secret of the same sensitivity did.

## Decisions

- **A denial is recorded as a denial.** Looking a secret up by id writes no audit row; a decoy still
  trips first, for any caller. A served details page writes `SecretMetadataViewed`. A refused by-id
  action, in the UI API and the Public API, writes `SecretAccessDenied` with the policy's reasons, not
  critical. Both rows are fail-closed: if the row cannot be written the answer is
  `503 audit_unavailable` and nothing is disclosed. Self-service views (My activity, the Overview's
  own feed, the Your data export) show a denial row without the secret's name; administrators see it.
  The row is written for `403 access_denied` and `403 not_permitted`; never for `403 step_up_required`,
  which is a challenge with its own step-up events, nor for `404 not_found` or any list.
- **One event per action, in the change's transaction.** Each action writes only its own event, never a
  Viewed row first. Restore commits with `SecretVersionRestored` and managed rotation with
  `SecretUpstreamRotated`, instead of `SecretUpdated`. An audit failure changes nothing (for managed
  rotation, the upstream password is put back, but only when the write did not commit).
- **A share only reads.** Rotate, managed rotation, restore and delete need the sharing right: an
  administrator, or a member of the secret's project who passes the access check. A recipient may view
  and reveal; anything else is `403 not_permitted`.
- **A changed secret is never overwritten.** Rotate, managed rotation and restore write only if the
  secret's wrapped data key is still the one they read (ADR-0029's conditional write); the version
  number is assigned inside that write. The loser gets `409 secret_changed`, never an audit failure.
  Every write to a managed secret's value first takes a cluster-wide lock on its backend and principal,
  held until it commits or puts the upstream password back, so one writer's put-back can never undo
  another's change; a second writer gets `409 change_in_progress` before the backend is touched.
  Shredding an expired secret is conditional the same way.
- **A managed secret is not restored.** Its value is the live password of a backend account; restoring a
  version would change only the vault's copy, and the vault would then serve a password the backend
  refuses. Restoring a version of a managed secret answers `409 restore_managed` and changes nothing;
  earlier versions stay readable, and a new password comes from managed rotation.
- **Nothing is cheaper than the reveal it gives.** Revealing a value or an archived version at or above
  `StepUp:MinimumSensitivity`, sharing such a secret (from its page or by approving an access request),
  and issuing a dynamic credential for such a role each need a sign-in or step-up within
  `StepUp:MaxAuthAgeMinutes` (`403 step_up_required`). Rotate, restore, delete and revoking a share or
  a lease never ask (ADR-0028).
- **A taken name is a conflict.** Secret names stay unique across the vault; creating one with a taken
  name answers `409 name_taken` and changes nothing.
- **A lease's note is a fixed sentence per cause.** When a revoke or the sweep cannot destroy a dynamic
  credential, the lease is closed as RevocationFailed with a fixed sentence; the backend's own error,
  which can quote a statement or name the target, goes only to the log. A note stored before this rule
  is answered as the fixed sentence.
- **Licence lookups on page views fail open.** A failed evaluation-window read leaves the page's licence
  note out and logs a warning; it never stops a page or a reveal, and viewing never starts a window.

## Consequences

- Someone who may create secrets learns that a name exists, though not its project, sensitivity, value
  or whether it is a decoy.
- The Denied page names the secret's project and sensitivity to anyone holding its id, never its name,
  so the person knows what to fix and whom to ask.
- Someone holding a dynamic role's id learns whether it exists, and its name when refused
  (`404 role_not_found` against `403 access_denied`), as the Legacy page told them.
- The trail grows by one `SecretAccessDenied` row per refused attempt. This is the record PCI DSS v4.0
  requirement 10.2.1.4 asks for.
- Share recipients who relied on rotating a shared secret must ask a project member or administrator.
- Two people changing one managed secret at once: the second is refused and must retry, rather than
  queued.
- A managed secret cannot be rolled back to an earlier password; its operator rotates it instead.
- When a revoke destroys a dynamic credential but its close cannot be recorded (`503 revoked_unrecorded`),
  the lease shows Active until its original expiry, when the sweep runs the revocation again. It
  closes as Expired only if that retry succeeds and its close is recorded. A failed retry closes it as
  RevocationFailed (a non-idempotent revocation recipe can fail on repeat), and while auditing stays
  unavailable neither close is recorded, so it stays Active and each later sweep tries again. The
  credential no longer works meanwhile; only the display lags. A lease issued after 7c
  lasts at most 1440 minutes, whatever the role's default or maximum; leases issued before it keep
  their own expiry, which can be longer.
- If minting fails part-way and the cleanup that follows also fails, a login the vault never recorded
  may remain on the target. The person is told the cleanup is unconfirmed, a critical log line names
  the role and login, and an operator checks the target (`docs/INCIDENT_RESPONSE.md`). Its password was
  never shown or kept.

## Considered Options

- **Keep recording a denied read as Viewed.** Rejected: the trail claims a view that was refused.
- **Record nothing for a denial, only log it.** Rejected: invalid access attempts are an auditable event
  for the compliance-driven buyers this vault serves.
- **Make names unique per project.** Rejected: a migration in both database sets, and it still discloses
  names within the project.
- **Let a share confer change and delete.** Rejected: sharing means view unless stated, and a recipient
  outside the project could destroy the issuer's secret and its history.
- **Restore a managed secret upstream too.** Rejected: it puts a retired password back into service, and
  needs its own licence, lock and put-back rules.
- **Restore only the stored copy, as Legacy did.** Rejected: the vault then serves a password the backend
  refuses.
