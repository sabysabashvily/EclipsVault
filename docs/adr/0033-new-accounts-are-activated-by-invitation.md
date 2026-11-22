# New accounts are activated by invitation, never by a password an administrator chose

An administrator creating an account used to choose its first password and pass it on. Whoever signed
in first with it enrolled the authenticator, so the administrator could take over a new account before
its owner, and everything it did was attributed to that person (#117). The Area 8 grill also found
that the pending authenticator seed stays the same until enrollment completes and its display is not
recorded: anyone who knew the first password could copy the seed without enrolling and keep both
credentials after the owner enrolled the same seed.

## Decisions

- **The administrator never chooses a credential.** The New user form takes the person's real email
  address instead of a password; it becomes the account's email (unique under ADR-0025). Generated
  `first.last.N@domain` addresses and `Identity:EmailDomain` are retired.
- **An invitation activates the account.** Creating an account sends a single-use link valid for 72
  hours to that address. The token is 256 random bits, stored only as a SHA-256 hash. Opening the link
  spends nothing; the page shows the username and an authenticator seed, made on the first opening and
  kept until the token is spent or replaced, and one POST sets the
  password (Profile's rules), enrolls the authenticator, spends the token, advances the credential
  generation and writes `AccountActivated`, in one transaction. The person then signs in normally.
- **Until activation, the account cannot sign in by any route.** The password stage fails as for a
  wrong password; SSO refuses it (`NotActivated`); it has no passkey and cannot use break-glass. It does
  not count as an enabled administrator, and its authenticator cannot be reset. Disabling it pauses
  its invitation: while it is disabled the link shows and activates nothing (`404 invitation_invalid`),
  and re-enabling makes a link that has not expired usable again. Replacing the address or deleting
  the account revokes the link.
- **The link is built only from `Identity:PublicUrl`**, never from the request, and carries the token in
  its fragment, so it stays out of request logs, `Referer` and link scanners. It travels only through
  the unrecorded mail path (ADR-0024), never the notification outbox, logs or audit details.
- **Resending replaces the token.** Creating an account, resending an invitation and replacing its
  address need fresh proof (ADR-0028); resend and replacement refuse an activated account. `UserCreated` names the invitation address;
  `InvitationSent` records each resend.
- **No mail, no invitations.** Without real mail delivery or `Identity:PublicUrl`, New user and Resend
  are unavailable with an explanation. There is no fallback that shows the link to an administrator.
- **Existing accounts that never signed in await activation.** A data migration in both database sets
  moves to awaiting activation every account with no enrolled authenticator, no passkey and no
  `LoginSucceeded` or `PasskeyLogin` row in the audit trail; an administrator resends their invitation.
  The migration also retires proofs that account already handed out: it advances the credential
  generation and clears any pending authenticator secret, and it writes no audit row. Pending-sign-in
  validation, final session issuance and SSO refuse an account that is awaiting activation even when a
  stamp still matches. An old node that can still issue a pending sign-in is retired before migrated
  traffic. The operator enrolls the bootstrap administrator before the upgrade. The migration never
  invites the last enabled administrator: if every enabled administrator would otherwise be awaiting
  activation, the earliest one stays on the password it already has and enrolls at first sign-in
  (`vault-admin` first, when that account is in the set). Other classified accounts are invited.
  An administrator replaces a stored invitation address with fresh proof; the row
  `InvitationAddressReplaced` names the new address. Replacing spends the current token in the same
  transaction, because the link already sent may have reached someone else; it sends no mail, and the
  administrator resends to the new address. Resend sends only to the stored address and takes no new one. Both databases prove the exclusion and the replacement.

## Consequences

- An administrator can still type an address they control. The trail records every address an
  invitation went to, and the real person notices they never received one.
- A vault without SMTP cannot add local accounts until mail is configured. Development and the browser
  suite capture mail (Mailpit, the e2e SMTP capture).
- A lost or expired link needs an administrator to resend; the person cannot request one. Resend
  cannot correct the address. Replacing it is a separate fresh-proof action that kills the link already
  sent; the person waits for the resend to the corrected address.
- The last enabled administrator left on the old password can still be enrolled by whoever knows
  that password. That is the one account the migration refuses to lock out.
- SSO's email matching now meets real addresses instead of generated ones.

## Considered Options

- **Administrator-set password replaced at first sign-in.** Rejected: the administrator who knows the
  first password can make the forced change and enroll themselves, so it cannot meet #117's criterion.
- **Show the invitation link to the administrator to hand over.** Rejected: the administrator could
  open it first, which is the gap being closed.
- **Token in the query string.** Rejected: request logging and scanners record or fetch it.
- **Leave never-used accounts as they are.** Rejected: their administrator-chosen passwords are the
  same gap.
