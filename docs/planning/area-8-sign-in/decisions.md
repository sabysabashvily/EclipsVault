# Area 8 Sign-in and Provisioning: decision record

Status: all 27 recommendations accepted (rounds 1–3) and confirmed by the owner on 2026-11-11.
The Q27 challenge (2026-11-12, against `ab7997e`) is folded. A8-1 was accepted on 2026-11-12.
A8-5 is recorded in Area 9's records; this worktree does not edit ADR-0017. Not published.

Survey and findings F1–F9: [survey.md](survey.md) (F9, link poisoning, was found in round 2). Rounds:
[checkpoint.md](checkpoint.md). Glossary: `CONTEXT.md` (Pending sign-in, Invitation, Account activation).
ADRs: 0017 amended, 0032 and 0033 new.

## Decisions

| Ref | Decision | Agreed behaviour |
| --- | --- | --- |
| Q1 | Split | Two parts, each with its own spec, branch and PR: **8a Sign-in** (ceremonies on the UI API; F2, F4–F6; the five leftover methods) from `main` after 7a merges, may run beside 7b and 7c; **8b Provisioning** (#117; F3, F7, F9) from `main` after 8a merges. Area 9 needs only 8a. ADR-0017 amended. |
| Q2 | Scope | Angular at the same URLs: `/Account/Login`, `/Account/Totp`, `/Account/RecoveryCode`, `/Account/EnrollTotp`, `/Account/Recover` (8a) and `/Account/Activate` (8b). SSO `ExternalLogin` and `ExternalCallback` stay server actions at their URLs. Legacy `/Account/Logout` stays for the Error page's layout until Area 9; Angular uses the UI API sign-out; one implementation. The callers are the account menu, `StaleSessionSignOut` and the step-up dialog's "Sign in again". The access-blocked page stays server HTML. Landing and Error wait for Area 9; Denied moves in 7b. The six Account views, their models and `site.js`'s passkey sign-in go. |
| Q3 | Provisioning (#117) | Invitations (ADR-0033). Option B rejected: it cannot meet the acceptance criterion. |
| Q4 | Anonymous surface | Login and Recover anonymous (a signed-in Login visit redirects server-side); Totp, RecoveryCode, EnrollTotp only with the pending sign-in cookie; sign-in pages never call `/api/ui/me`; anonymous documents set the request token, every identity change reissues it for the new principal and the next POST validates; a blocked network's auth surfaces are only the Recover page and `POST /api/ui/auth/recover`. Static files and `/local-sign-out` GET/POST never reach the blacklist and grant no account authority (ADR-0026). Allowlist tests name every auth call; throttle on every unsafe auth call except sign-out. ADR-0032. |
| Q5 | Enumeration (F4, F5) | Hash before the lockout check; discoverable-only passkey sign-in, no username; Profile registers `residentKey: "required"`. Lockout DoS accepted (ADR-0032). |
| Q6 | Landing (F1, F2) | Literal defaults: `/Secrets` after every sign-in method, `/` after break-glass; the server chooses through the unchanged guard; Angular does a full page load. F1 goes to 7a ticket 2 (`logs/area-8/coordinator-handoff.md`). |
| Q7 | Sign-out (F6) | Revoke, then clear; a failed revoke still clears the cookie and answers `503 sign_out_incomplete`; no audit row (unchanged). |
| Q8 | Leftover methods | Remove all five; tests move to the verified services or direct seeding; `SignInRefusalAuditTests` keeps every assertion. |
| Q9 | Audit rows (#93) | Preserve #127's rows and order. Keep the already-audited session on later failure. Harden block lifting in 8a ticket 1: retain exact removed blocks, restore them on rejected `IpRangeUnblocked`, and keep the source blocked if restoration cannot be confirmed. See [Break-glass failure outcomes](#break-glass-failure-outcomes). This replaces the no-compensation choice for block lifting only (owner, 2026-11-13). |
| Q10 | Messages across a navigation | Recovery-code notice and break-glass outcome on their own pages with Continue; restarted sign-in is `409` back to a fresh Login document with `?signIn=restarted`, retaining the return URL; this exception to in-app step navigation loads Login's identity-provider form policy without widening second-factor documents; SSO failures `?sso=failed|refused`. Sign-out everywhere uses three Login query codes, one per outcome. Authenticator reset has its own one-time handoff and its own sentence; a typed query cannot claim it. |
| Q11 | 8a UI API | See below. |
| Q12 | 8a presentation | Exact copies except the Q10 messages and a passkey button that ignores the username; QR stays a server PNG data URI. |
| Q13 | Invitation address (F7, F9) | New user takes the person's email; generated addresses and `Identity:EmailDomain` retired; link base only from required `Identity:PublicUrl`; token in the fragment of `/Account/Activate`. |
| Q14 | Invitation lifecycle | 256-bit token, SHA-256 at rest, single use, 72 hours; Resend replaces the token and sends only to the stored address; create, resend and invitation-address replacement need fresh proof; commit, then deliver through the unrecorded mail path; `UserCreated` (with address), `InvitationSent`, `InvitationAddressReplaced` (with the new address), `AccountActivated`. Replacing the address spends the current token in the same transaction as `InvitationAddressReplaced` and sends no mail; the administrator resends to the new address (owner, 2026-11-12). |
| Q15 | Activation | Inspect POST (seed generated once, nothing spent), complete POST (one transaction), then `/Account/Login?activated=1`; one `404 invitation_invalid`. |
| Q16 | Invited accounts | Refused by every sign-in route, including pending-sign-in validation and final session issuance (`LoginFailed` "Account awaiting activation", SSO `NotActivated`); not an enabled administrator; reset `409 not_activated`; other administration works; Users shows Invited / Invitation expired. |
| Q17 | No mail | Without SMTP or `Identity:PublicUrl`, New user and Resend unavailable with an explanation; no fallback. |
| Q18 | Never-used accounts | Migration in both sets: no authenticator, no passkey, no `LoginSucceeded`/`PasskeyLogin` row → awaiting activation, except the last enabled administrator, who is never invited. It advances the credential generation and clears any pending authenticator secret of the accounts it does invite, and it writes no audit row. The operator enrolls the bootstrap administrator before the upgrade. A generated address is corrected by fresh-proof replacement, not by resend. Both databases. |
| Q19 | 8b texts | See "Texts". |
| Q20 | Glossary | Invitation, Account activation (and Pending sign-in from round 1). |
| Q21 | Test plan | See "Testing". |
| Q22 | 8a delivery | See "Delivery outline". |
| Q23 | 8b delivery | See "Delivery outline". |
| Q24 | Remaining texts | See "Texts". |
| Q25 | IdP browser test | Playwright through a digest-pinned Keycloak in the e2e stack; Cursor ticket inside 8a. |
| Q26 | Reviews | Opus 5.5 (high) implements every protected ticket; per ticket `security-reviewer` plus GPT Sol 6.1 (xhigh) Standards and Spec, re-checks by the raising reviewer; per PR GPT Sol, Thermos, CI on the exact head, owner merge; Cursor only for Q25 and browser reruns. |
| Q27 | Cross-model challenge | One GPT Sol 6.1 (xhigh) challenge of this table and the drafts by the coordinator, before publication. |

## UI API contracts

Every endpoint: `no-store`, anti-forgery on writes, explicit response types, regenerated TypeScript,
Problem Details with a stable `code`. Only the calls below are anonymous or accept the pending sign-in.

### 8a Sign-in (Q11)

```text
GET  /api/ui/auth/options                                   anonymous → { ssoEnabled, ssoDisplayName }
POST /api/ui/auth/password   { username, password, returnUrl? }    anonymous → { next: "totp" | "enroll" }
POST /api/ui/auth/totp       { code, returnUrl? }                  pending   → { redirect }
POST /api/ui/auth/recovery-code { code, returnUrl? }               pending   → { redirect, codesRemaining }
POST /api/ui/auth/enrollment                                       pending   → { secretBase32, qrCodeDataUri }
POST /api/ui/auth/enrollment/confirm { code, returnUrl? }          pending   → { redirect }
POST /api/ui/auth/passkey/begin { returnUrl? }                     anonymous → WebAuthn options
POST /api/ui/auth/passkey/complete { credential }                  anonymous → { redirect }
POST /api/ui/auth/recover    { username, password, code }          anonymous, IP-block exempt → { blockLifted }
POST /api/ui/auth/sign-out                                         session   → 204
```

Problems: `400 sign_in_failed` ("Invalid username or password."); `400 code_invalid` (each page's Legacy
sentence); `400 passkey_failed` (today's fixed passkey messages); `400 recovery_failed` ("Recovery could
not be completed with these credentials."); `400 validation_failed`; `401 sign_in_expired` (no pending
sign-in; back to Login); `409 sign_in_restarted` ("Your account changed while you were signing in. Sign in
again."; passkey: "Sign-in timed out. Try again."); `429 throttled`; `503 audit_unavailable`;
`503 recovery_incomplete` ("Your sign-in was recorded, but the vault could not finish restoring network access. This source remains blocked. Contact an administrator.");
`503 sign_out_incomplete`. A wrong credential is never a 401. The password, second-factor and sign-out
calls reissue the request token for the principal the next request will have, and the next POST validates
it. `redirect` is always the guarded target; the return URL rides as a page query parameter and is judged
only at the final step. Login starts SSO with a real form POST to `/Account/ExternalLogin` carrying the
token in a hidden field. The pending-cookie transitions are in the 8a spec.

### 8b Provisioning (Q13–Q17)

```text
GET  /api/ui/users/new  → { clearances, invitationsAvailable }               (EmailDomain removed)
POST /api/ui/users      { firstName, lastName, username, email, clearance, project }
                        → 201 { userId, username, email, invitation: "sent" | "not_delivered", expiresAtUtc }
POST /api/ui/users/{id}/invitation → 200 { invitation: "sent" | "not_delivered", expiresAtUtc }
POST /api/ui/users/{id}/invitation-address { email } → 200 { email }
POST /api/ui/auth/activation          { token }                  anonymous → { username, displayName, secretBase32, qrCodeDataUri, expiresAtUtc }
POST /api/ui/auth/activation/complete { token, password, code }  anonymous → 204
```

Problems: `409 username_taken`; `409 email_taken` (generic); `403 step_up_required` (create, resend and address replacement);
`409 invitations_unavailable` (no SMTP or `Identity:PublicUrl`); `409 already_active` (resend and address replacement);
`409 not_activated` (authenticator reset of an invited account); `404 invitation_invalid` (unknown, used,
expired or replaced token); `400 validation_failed`, the password rules' codes, `400 code_invalid` (the invitation
stays usable); `429 throttled`; `503 audit_unavailable`. User details and the Users list gain the
activation state and invitation expiry.

## Texts (Q19, Q24)

- Recovery-code notice: "You signed in with a recovery code — that code is now used up. *N* remaining.
  Generate a new set from your profile if you're running low." with Continue.
- Break-glass: the two Legacy outcome sentences with "Continue to the vault".
- Login query `?signOut=sign_out_incomplete`: "This browser's cookie was cleared. Other devices may still be signed in." This code stays the local-clear text only.
- Login query `?signOut=everywhere`: "You have been signed out of all sessions. Sign in again to continue."
- Login query `?signOut=revoked_unrecorded`: "You have been signed out of all sessions, but the vault couldn't record it in the audit trail. Tell your administrator."
- Login query `?signOut=everywhere_incomplete`: "This browser was signed out, but the vault couldn't confirm that your other devices were. Sign in and try again."
- Authenticator reset, its own one-time handoff, not a query code: "Your authenticator was reset. Sign in to set up a new one." `?signOut=authenticator_reset` shows nothing. The Login document reads the handoff once and clears it.
- Invitation address, on the user page: "Use *address* for *username*? The link already sent stops working. Resend to send a new one there."
- Login: "Your account is ready. Sign in with your new password." (`?activated=1`).
- New user field "Email", hint "The invitation goes here. They choose their own password and set up their
  authenticator." Outcomes: "Invitation sent to *address*. It expires in 72 hours." / "The account was
  created, but the invitation could not be delivered. Resend it from the user's page."
- Email subject "You're invited to EclipsVault"; body "Hello *name*, an administrator created an
  EclipsVault account for you with the username '*username*'. Open this link within 72 hours to choose
  your password and set up your authenticator: *link*. If you weren't expecting this, ignore this email."
- Overview "Provision a user" subtitle: "They set their own password and authenticator".
- Activation page: title "Activate your account"; lead "Choose your password and set up your
  authenticator. Multi-factor sign-in is mandatory."; EnrollTotp's block; Profile's password fields and
  meter; button "Activate account"; invalid link "This invitation link is not valid. It may have expired
  or been used. Ask an administrator to send a new one."
- Resend action "Resend invitation"; confirmation "Send a new invitation to *address*? The previous link
  stops working."
- Users status "Invited · expires *date UTC*" / "Invitation expired".
- Unavailable: "Invitations need email delivery and the vault's public address (Identity:PublicUrl).
  Configure both to add accounts."

## Carried over unchanged

Every Legacy sentence not named above; the return-URL guard; the pending sign-in's 6-minute life and
generation binding; the lockout policy and the per-IP throttle; #127's rows and order; SSO's single
refusal message and its server-side redirects; the access-blocked page's HTML; Profile's account
verification (ADR-0022).

## Testing (Q21)

- **Unit:** the guard and literal defaults (F1 and F2 failing first), hash-before-lockout, the invitation
  token (hash, expiry, single use, replacement), activation as one transaction, the Q18 selection rule.
- **Production HTTP**, locally on PostgreSQL with `scripts/check.sh integration "<filter>"`, both databases
  in CI: every auth call's shape and code; the anonymous and pending allowlists; a blocked network reaching
  the two auth surfaces and, separately, `/local-sign-out` with no account authority; the throttle on each
  POST but not sign-out or GETs; token reissue proved by the next POST; the pending-cookie matrix;
  enumeration parity (unknown, locked, disabled, invited); ported `SignInRefusalAuditTests` and break-glass
  tests, one failure per break-glass row; invited accounts refused by every route including a still-matching
  stamp, SSO through the post-IdP fixture; races between two activations and between activation and resend;
  the Q18 migration on both databases, including a pre-migration pending cookie and pending secret.
- **AuditFailureFixture:** every auth call, activation, create and resend.
- **Vitest:** every page.
- **Playwright** under browser health, affected specs locally with `scripts/check.sh e2e <spec>...`: password
  + TOTP, recovery code and its notice, first enrollment, passkey with the virtual authenticator,
  break-glass from a blocked network, sign-out and `sign_out_incomplete`, SSO through Keycloak (Q25), and
  the invitation path (create → SMTP capture → activate → sign in, resend, expired link).
- Account-creating helpers (about 12 integration and 9 e2e files) switch to invitation plus activation in 8b.
- `scripts/check.sh` on every ticket; CI runs SQL Server, the whole browser suite and `e2e:verify-guards`.

## Delivery outline

Two specs, published together after Area 7's specs and the Q27 challenge. 8a is blocked by 7a's spec; 8b
by 8a's. Tests land with their behaviour; each page is served in the ticket that builds it. 8b changes the
schema: migrations in both sets.

**8a Sign-in**, branch `area-8a-sign-in` from `main` after 7a merges, may run beside 7b and 7c:

| Ticket | Behaviour | Decisions |
| --- | --- | --- |
| 1 | Sign-in rules on the server: `/api/ui/auth/*`, F2, F4, F5, F6, discoverable registration, five methods removed, refusal and break-glass tests ported, allowlists and throttle, pending-cookie matrix. The `/Account/Recover` path exemption stays (GET and POST) and the new recover call is added. Commits every grill record. | Q4–Q9, Q11 |
| 2 | Login, Totp, RecoveryCode and EnrollTotp served by Angular; route-table rules; account menu, `StaleSessionSignOut` and the step-up dialog on the UI API; the three sign-out-everywhere sentences and the authenticator-reset sentence; views, models and `site.js` passkey code deleted; sign-in helpers moved. | Q2, Q4, Q10, Q12 |
| 3 | Break-glass served by Angular. The same change deletes the Legacy Recover POST and narrows the exemption to the Recover document and `POST /api/ui/auth/recover`. | Q2, Q4, Q10, Q12 |
| 4 | Browser test through a real IdP (Keycloak in the e2e stack). Cursor, test infrastructure only. | Q25 |
| 5 | Verify the whole part, including enumeration parity, and prepare its PR. | whole-part reviews and gates |

**8b Provisioning**, branch `area-8b-provisioning` from `main` after 8a merges:

| Ticket | Behaviour | Decisions |
| --- | --- | --- |
| 1 | Invited accounts and invitations: model and migrations, resend, invitation-address replacement, unrecorded delivery, `Identity:PublicUrl`, refusal by every route, administrator rules, Q18 migration (never the last enabled administrator), audit actions. Create still takes a password, so the current form and helpers pass. | Q13, Q14, Q16–Q18 |
| 2 | Create switches to email; activation on the server; account-creating helpers and the New user form switch together. | Q13, Q15, Q19, Q21 |
| 3 | Activation page, Users status, Resend, invitation-address replacement and the Overview subtitle served by Angular. | Q15, Q19, Q24 |
| 4 | Verify the whole part and prepare its PR. | whole-part reviews and gates |

Schedule risk: nine tickets after Areas 6b and 7, with Area 9 to follow; the delivery plan's single
18 November slot does not hold them. 8a overlaps 7b and 7c; 8b never blocks Area 9.

## Settled after the challenge

- **A8-1, accepted 2026-11-12.** Enroll the bootstrap administrator before the upgrade. The migration never invites the last enabled administrator. Invitation-address replacement is fresh proof and audited. Resend stays address-less. Both databases. Changed by the owner the same day: replacement spends the current token in the same transaction, because the link already sent may have reached someone else and could activate the account, possibly an administrator, for up to 72 hours. It still sends no mail.
- **A8-3, owner revision 2026-11-13.** "Allow exact block restoration (Recommended)". Retain the recorded session; restore exact removed blocks if the required lift row fails, with a fail-closed guard if restoration cannot be confirmed. See the failure outcomes below. This supersedes the earlier no-re-block choice.
- **A8-5.** Decided by the owner on 2026-11-12 and recorded in Area 9's records. ADR-0017's Area 9 paragraph is that worktree's.

## Break-glass failure outcomes

The owner accepted exact block restoration on 2026-11-13 after the fresh GPT re-check narrowed A8-3.
Ticket 1 implements this with the new recover API and the shared helper, so both the Legacy form and
UI API obey it before ticket 3 removes the Legacy page. Preserve the existing three rows and order.

- Rejected `LoginSucceeded`: no session and no block lift.
- Rejected `BreakGlassRecovery`: the already-recorded session may remain, but the source stays blocked.
- No matching block: keep the recorded session and recovery row, with no `IpRangeUnblocked` row.
- Rejected `IpRangeUnblocked`: keep the recorded session, restore the exact removed ranges and their
  metadata without the request's cancellation, and preserve concurrent changes. Never report access
  restored without the required audit row.

A guard must protect the affected source before removal can create an unrecorded access grant. In
Redis deployments it is shared across replicas. The address-wide removal must not delete its own
guard. Do not release it merely because a timeout or lease expires; release requires a committed
lift result or confirmed safe restoration. If restoration cannot be confirmed, subsequent protected
requests from that source remain refused, and `503 recovery_incomplete` says the recorded sign-in
could not finish restoring network access. Return `503 audit_unavailable` for audit rejection with
confirmed restoration. A 503 response alone is not proof that the source is blocked.

Selective tests reject each row separately and inspect cookie, exact ranges and a later protected
request, including overlapping ranges, cancellation, concurrent changes, restoration failure and
cross-replica Redis checks. Keep existing audit/refusal assertions. The final design is implemented
and security-reviewed by local Claude Opus 5.5 high; this plan is not execution evidence.
