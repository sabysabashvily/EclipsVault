# Administrator changes keep an enabled administrator and grant access only with fresh proof

The Area 5 grill read the Legacy administration pages and found that they saved account and
service-account changes before auditing them (#92). It also found that the vault could be left with
no usable administrator, and that a stolen administrator session could install lasting access without
any fresh proof. An administrator is any vault account with Top Secret clearance; there is no
separate role.

## Decisions

- **Each database change commits with its one audit event.** Creating, changing the role of,
  enabling, disabling, unlocking and deleting an account, and every service-account and API-key write,
  commit in one transaction with their audit row (ADR-0021's database rule). An audit failure answers
  `503 audit_unavailable` with nothing changed. Force logout changes only the session store, so it
  keeps ADR-0021's revoke-then-audit order. Losing a race on a unique name is a conflict, not an
  audit failure.
- **The vault always keeps an enabled administrator.** Demoting, disabling or deleting an
  administrator is refused when no other enabled administrator would remain. Disabled accounts do not
  count. The check runs under a vault-wide lock inside the change's own transaction, so two
  concurrent changes cannot both pass it. Demoting yourself is allowed while another enabled
  administrator exists.
- **Administration never acts on your own account in ways Profile governs.** The server refuses
  resetting, disabling or deleting your own account; your own authenticator is reset from Profile,
  under its account verification (ADR-0022). Signing yourself out everywhere stays available.
- **Granting lasting access needs a recent strong authentication; removing access never does.**
  Creating an account, changing a role, enabling or unlocking an account, resetting another account's
  authenticator, issuing an API key, enabling a service account, trusting a network and lifting an
  intrusion-defence block each need a sign-in or step-up within `StepUp:MaxAuthAgeMinutes`. A stale
  session gets `403 step_up_required` and proves itself with the existing step-up check. Disabling,
  deleting, signing out, revoking a key, disabling or deleting a service account and removing a
  network never ask, because an incident response must never wait on a second factor.
- **Lifting a block is undone if it cannot be recorded.** The block lives outside the database, so it
  cannot share a transaction with its audit row. The vault lifts it, records the lift, and restores
  the exact block record if the record cannot be written. The trail never claims a lift that did not
  happen. Successful restoration answers `503 audit_unavailable`; if restoration also fails, the
  vault answers `503 unblock_unrecorded` and logs critical without claiming the block was restored.
  Compensation preserves the original store record and does not overwrite a different block raised
  in the meantime. Such a conflict is a restoration failure requiring inspection, not a successful
  restoration. Once a block is lifted, auditing and compensation finish even if the browser leaves.

- **Bulk disclosure needs the same fresh proof as a grant.** Exporting the whole audit trail asks for
  a sign-in or step-up within `StepUp:MaxAuthAgeMinutes` (Area 6); its step-up asks "Confirm it's
  you before exporting the audit trail." Viewing, verifying and
  checkpointing the trail, and master key rotation, never ask: none discloses in bulk or grants access.

## Consequences

- The Notification outbox, block reasons and the audit log are monitoring views. They show secret
  names, and a tripped decoy's name, to every administrator wherever they are, without the Secrets
  list's access filter. An outbox row has no link to its secret, so it cannot be filtered that way.
- An administrator without an authenticator (passkey or SSO only) proves freshness by signing in
  again.

## Considered Options

- **Keep "count every Top Secret account" for the last-administrator check.** Rejected: it counts
  disabled administrators, so the only enabled one could demote themselves.
- **Ask for fresh proof on every administrator action.** Rejected: an administrator cutting off a
  compromised account must not be slowed by a second factor.
- **Record the lift before lifting.** Rejected: if the lift then failed, the trail would claim a lift
  that never happened.
