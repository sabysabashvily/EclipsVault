This spec is **Area 8 part 8b, Provisioning** (ADR-0017 amendment). It closes #117. It starts from `main` after part 8a (#SPEC8A) merges; Area 9 does not wait for it. The owner accepted all 27 grill decisions and the two delivery outlines, and confirmed them on 2026-11-11.

## Problem Statement

An administrator creating an account chooses its first password and passes it on. Whoever signs in first with it enrolls the authenticator, so the administrator can take over a new account before its owner, and everything it does is attributed to that person (#117).

The grill's survey found these flaws, deductions from reading the code at `ab7997e`; each ticket proves its finding with a failing test first.

- **The pending seed can be copied (F3).** It stays the same until enrollment completes, and its display is not recorded: anyone who knows the first password can copy it without enrolling and keep both credentials after the owner enrolls. A forced password change cannot fix this, because the administrator can make that change.
- **The account's email is not a mailbox (F7).** It is generated as `first.last.N@<Identity:EmailDomain>`, and the provisioning email goes through the outbox, which stores bodies and shows them to every administrator.
- **A link built from the request can be poisoned (F9).** Any link the vault mails must not take its host from the request.

## Solution

New accounts are activated by invitation (ADR-0033). The administrator enters the person's real email; the vault mails a single-use, 72-hour link built from `Identity:PublicUrl`; the person chooses a password and enrolls an authenticator in one step, then signs in. Until then the account cannot sign in by any route.

## User Stories

1. As an administrator, I create an account with the person's email and never choose or see a credential.
2. As an administrator, I see which accounts await activation and until when, and resend an invitation, which kills the old link.
3. As an invited person, I open the link, choose my password, set up my authenticator and then sign in.
4. As an invited person with a used or expired link, I am told to ask an administrator for a new one.
5. As an administrator of a vault without mail, I am told that invitations need email delivery and the vault's public address.
6. As anyone, an account created before this change and never used cannot be taken over with its old password.

## Implementation Decisions

### Model (Q13, Q14, Q16, Q18)

An account is awaiting activation until its activation commits. An invitation holds the SHA-256 of a 256-bit token, its expiry (72 hours) and, once inspected, the pending authenticator seed; resending replaces it. The New user form takes `email` instead of `password`; it becomes the account's email (ADR-0025, generic `409 email_taken`). Generated addresses and `Identity:EmailDomain` are retired; the README changes. Migrations in both database sets; a data migration moves to awaiting activation every account with no enrolled authenticator, no passkey and no `LoginSucceeded` or `PasskeyLogin` row (the trail is only read), except the last enabled administrator. The operator enrolls the bootstrap administrator before the upgrade. If every enabled administrator would otherwise be awaiting activation, the earliest one is left on the password it already has (`vault-admin` first, when that account is in the set) and enrolls at first sign-in. The migration advances the credential generation and clears any pending authenticator secret of the accounts it invites, so a pending sign-in cookie or a copied seed from before the upgrade cannot enroll. Pending validation, final issuance and SSO refuse the awaiting-activation state even if a stamp still matches. Old nodes are retired before that traffic. An administrator replaces a stored invitation address with fresh proof; `InvitationAddressReplaced` names the new address and commits with the spending of the current token, so the link already sent dies; the administrator then resends. Resend stays address-less. Both databases prove the exclusion and the replacement.

The create-contract change, the activation calls, the account-creating helper switch and the New user form land in ticket 2 together. Ticket 1 leaves `POST /api/ui/users` on today's password body, so the current form and the full suites stay green.

### Delivery and the link (Q13, Q14, Q17)

The link is `<Identity:PublicUrl>/Account/Activate#<token>`, never built from the request. It goes only through the unrecorded mail path (ADR-0024), never the outbox, logs or audit details. The account and invitation commit first; then delivery, whose outcome the response reports. Without real mail delivery or `Identity:PublicUrl`, New user and Resend are unavailable with an explanation.

### Invited accounts (Q16)

Refused by every sign-in route, including pending-sign-in validation and final session issuance: the password stage fails as for a wrong password (`LoginFailed` "Account awaiting activation", same timing), SSO refuses with `NotActivated`, no passkey, no break-glass. A stamp that still matches is not enough. Not counted as an enabled administrator. Authenticator reset answers `409 not_activated`. Role changes, disabling, deleting and force sign-out work.

### Audit (Q14, Q15)

`UserCreated` names the invitation address; `InvitationSent` on resend; `InvitationAddressReplaced` names the new address; `AccountActivated` on completion. Each commits with its change and is fail-closed. Create, resend and address replacement need fresh proof (ADR-0028). Replacement spends the current token in the same transaction as `InvitationAddressReplaced`, so the link already sent answers `404 invitation_invalid`; it sends no mail, and the administrator resends to the new address.

### UI API contract (Q13–Q17)

```text
GET  /api/ui/users/new  → { clearances, invitationsAvailable }
POST /api/ui/users      { firstName, lastName, username, email, clearance, project }
                        → 201 { userId, username, email, invitation: "sent" | "not_delivered", expiresAtUtc }
POST /api/ui/users/{id}/invitation → 200 { invitation: "sent" | "not_delivered", expiresAtUtc }
POST /api/ui/users/{id}/invitation-address { email } → 200 { email }
POST /api/ui/auth/activation          { token }                  anonymous → { username, displayName, secretBase32, qrCodeDataUri, expiresAtUtc }
POST /api/ui/auth/activation/complete { token, password, code }  anonymous → 204
```

Problems: `409 username_taken`, `409 email_taken`, `403 step_up_required`, `409 invitations_unavailable`, `409 already_active`, `409 not_activated`, `404 invitation_invalid` (unknown, used, expired or replaced: one answer), `400 validation_failed`, the password rules' codes, `400 code_invalid` (the invitation stays usable), `429 throttled`, `503 audit_unavailable`. Inspecting generates the seed once and spends nothing; completing sets the password (Profile's rules), enrolls the authenticator, spends the token, advances the credential generation and writes `AccountActivated` in one transaction. Both activation calls are throttled; no lockout. User details and the Users list gain the activation state and invitation expiry.

### Presentation (Q19, Q24)

The texts in the decision record: the New user Email field and hint, both creation outcomes, the email subject and body, Overview's "Provision a user" subtitle, the Activation page, Resend and its confirmation, the Users status, the unavailable explanation, and Login's `?activated=1` sentence. The Activation page reuses EnrollTotp's block and Profile's password fields.

## Testing Decisions

- **Unit:** token hash, expiry, single use and replacement; activation in one transaction; the migration's selection rule; the link never built from the request.
- **Production HTTP**, locally on PostgreSQL with `scripts/check.sh integration "<filter>"`, both databases in CI: create and resend shapes, codes and fresh proof; invited accounts refused by every route with parity, including a pending cookie whose stamp still matches, SSO through the post-IdP fixture; the last-administrator rule ignoring invited accounts; races between two activations and between activation and resend; the link and token absent from the outbox, logs and audit rows; the data migration on both databases, including a pre-migration pending cookie, a stored pending secret and an in-flight SSO stamp, the last enabled administrator left uninvited and still able to sign in, and an address replacement that resend then delivers to.
- **AuditFailureFixture:** create, resend and activation.
- **Vitest:** Activation, New user, Users status and Resend.
- **Playwright** under browser health: create → read the email from the SMTP capture → activate → sign in; resend kills the old link; an expired link.
- Account-creating helpers (about 12 integration and 9 e2e files) switch to invitation plus activation.

## Delivery

Branch `area-8b-provisioning` from `main` after 8a merges, four tickets, one PR. Claude Opus 5.5 (high) implements; the `security-reviewer` agent reviews every ticket and the whole part; Standards and Spec reviews go to GPT Sol 6.1 at xhigh; the PR gets GPT Sol and Thermos before the owner merges.

## Out of Scope

- Self-service invitation requests or password reset.
- Invitations to SSO-only accounts without a local password.
- Any redesign of the Users pages.

## Records

`docs/planning/area-8-sign-in/`; `CONTEXT.md` (Invitation, Account activation); ADRs 0017, 0022, 0023, 0024, 0025, 0028, 0033. Closes #117.
