# Access requests are decided under the sharing rule, and a share discloses nothing it cannot grant

Approving an access request issues a share. The Legacy UI let a different, weaker rule decide it:
any administrator or member of the secret's project could approve, whether or not they could read
the secret themselves, and the review queue showed them secret names the Secrets list hides. The
Area 4 grill found the same gap on the recipient's side: a share's recipient saw the secret's name,
project and sensitivity even when the share could never let them open it.

## Decisions

- **One rule issues a share.** A reviewer is anyone who could issue the share from the secret itself
  at that moment: an administrator or a member of its project who also passes the secret's access
  check. The queue lists only requests the caller could decide now, never the caller's own. Any
  other request answers the same not-found response, so a request id reveals nothing.
- **A share must be able to help.** A share lifts only the project rule. So sharing refuses a
  recipient whose clearance is below the secret's sensitivity, and a request may be filed only by an
  account whose clearance suffices, that is outside the secret's project, and that holds no active
  share. Network and time denials do not block filing because they change by the hour. Approval
  re-checks the same rule.
- **The server writes what the reviewer reads.** The denial reasons shown to a reviewer are computed
  by the server when the request is filed, never taken from the request.
- **Shared with me lists only what its recipient could list.** Each row passes the same access filter
  as the Secrets list, share included, so a share hidden there is hidden here, with no count of
  hidden rows.
- **Each request transition is atomic and has one outcome.** Filing, approving, rejecting and
  withdrawing each commit in one transaction with their audit rows (ADR-0021's database rule).
  Approval commits the share, the decision and both audit events together. A decision applies only
  to a pending request, so exactly one of approve, reject or withdraw wins a race. Email follows the
  commit and stays best-effort.
- **An expired share is replaced, not a blocker.** A new share to a recipient whose share expired
  replaces it in the same transaction, recorded as one share event.
- **A lost race is not an audit failure.** When a share cannot be saved because one for that
  recipient now exists, the answer is a conflict saying so. Every other failed save still fails
  closed as the audit trail being unavailable.

These rules live in Core, so the Legacy sharing panel obeys them as well as the Angular UI.

## Consequences

- An issuer learns that a recipient's clearance is below one secret's sensitivity when a share is
  refused. The issuer can already read that secret, so this was accepted.
- Pending requests filed before this change carry denial text their requester wrote. A migration
  clears it, because a forged row cannot be told from a genuine one; reviewers see none for them.
- Pending requests that the rule now refuses stay pending. Approving one is refused with the reason,
  and a reviewer rejects it, so no request is closed without a person deciding.
- Under-cleared shares issued before this change stay in the database but disappear from
  Shared with me.

## Considered Options

- **Keep "administrator or project member" for reviewers.** Rejected: it lets someone who cannot read
  a secret, or cannot share it from the secret's page, put it in another account's hands.
- **Allow shares that cannot help, and only hide them.** Rejected: the share would still be a record
  in the issuer's Shared by me and the audit trail claiming access was given when it was not.
- **Recompute the denial reasons when the reviewer looks.** Rejected: the requester's network and
  time at filing are gone by then, and the reviewer should see what was actually refused.
