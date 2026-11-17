# Sign-in pages keep the anonymous surface narrow and say nothing about who has an account

ADR-0016 moved the sign-in ceremonies to the UI API. The Area 8 grill read the Legacy sign-in and found
that it told an anonymous caller more than it should: a named passkey sign-in returned the account's
credential ids, and a locked account answered before its password was hashed. It also found that the
default page after sign-in was computed from a controller action, which fails once the action is
retired, and that a sign-out that could not revoke its session said nothing. This ADR settles how the
Angular sign-in pages are reached and what they may reveal.

## Decisions

- **Only sign-in is anonymous.** `/Account/Login` and `/Account/Recover` are served to anyone; a
  signed-in visitor to Login is still redirected by the server to their target. `/Account/Totp`,
  `/Account/RecoveryCode` and `/Account/EnrollTotp` are served only with the pending sign-in cookie.
  The calls under `/api/ui/auth/` are the UI API's only anonymous or pending-sign-in actions; the
  allowlist tests name each one. Sign-in pages never call `/api/ui/me`.
- **Antiforgery before a session.** Serving an anonymous sign-in page sets an anonymous request token;
  every call that changes who the browser is (password, second factor, sign-out) issues a fresh one
  for the new identity (ADR-0004).
- **A blocked network reaches exactly two auth surfaces:** the `/Account/Recover` page and
  `POST /api/ui/auth/recover`. The page carries its own token, so no third exemption exists. Two
  branches never reach the blacklist and grant no account authority: static files, which already load
  before the block, and `/local-sign-out` GET and POST (ADR-0026), which terminate before it, read no
  account and revoke nothing. The access-blocked page stays server-generated HTML.
- **The throttle meters every unsafe auth call except sign-out**; GET, HEAD and OPTIONS stay unmetered.
- **Nothing tells an anonymous caller that an account exists.** The password stage hashes before it
  checks lockout. Passkey sign-in takes no username: it uses discoverable passkeys only, and Profile
  registers passkeys as discoverable (`residentKey: "required"`).
- **The server alone chooses where sign-in lands**, through the existing return-URL guard, and the
  defaults are literal paths: `/Secrets` after every sign-in method, `/` after break-glass. The Angular
  UI navigates there with a full page load.
- **Sign-out says when it could not revoke.** It revokes, then clears the cookie. If the revocation
  fails, the cookie is still cleared and the answer is `503 sign_out_incomplete`.
- **Audit rows and order stay as #127, with fail-closed block restoration.** An already-recorded
  session may remain when a later recovery step fails. If `IpRangeUnblocked` is rejected, restore the
  exact removed ranges and metadata without request cancellation, preserving concurrent changes.
  Protect the source before removal can grant access. Where the blacklist is shared, that guard is
  shared too; the address-wide removal cannot delete it. Unconfirmed restoration keeps the source
  blocked across replicas and answers `503 recovery_incomplete`; confirmed restoration after audit
  rejection answers `503 audit_unavailable`. A timeout must not release the guard into an unrecorded
  grant. The owner accepted this limited replacement of no compensation on 2026-11-13. Ticket 1
  implements the shared helper correction before the new recover call; ticket 3 verifies its UI.

## Consequences

- Anyone who knows a username can lock that account for the lockout period. This is the accepted cost
  of account lockout; the per-network throttle bounds it.
- A passkey stored as non-discoverable can no longer sign in, though it still proves the account in
  Profile. Registrations already asked for discoverable keys with user verification.
- Messages that the Legacy UI carried across a redirect are shown where they arise: the recovery-code
  notice and the break-glass outcome on their own pages, a restarted sign-in on Login, SSO failures as
  `?sso=failed|refused`.
- An unresolved block-lift operation can retain a source guard until an operator reconciles the
  required audit and range state. This favours refused access over an unrecorded grant. Tests must
  prove a later protected request is still refused when restoration fails; returning a 503 alone
  does not establish that invariant. Both in-memory and shared Redis implementations need proof.

## Considered Options

- **Keep named passkey sign-in and return fake credential ids for unknown names.** Rejected: it hides
  existence only if the fakes are indistinguishable in count and timing, which is more code than
  dropping the name.
- **Exempt a token endpoint for the recovery page as well.** Rejected: ADR-0016 allows one endpoint,
  and the page can carry its own token.
- **Let the Angular UI pick the landing page.** Rejected: the return-URL guard is security logic and
  stays on the server (ADR-0012).
