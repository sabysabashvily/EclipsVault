This spec is **Area 8 part 8a, Sign-in** (ADR-0017 amendment). It starts from `main` after part 7a (#SPEC7A) merges and may run beside parts 7b (#SPEC7B) and 7c (#SPEC7C) in its own worktree. Part 8b (#SPEC8B) follows it. The owner accepted all 27 grill decisions and the two delivery outlines, and confirmed them on 2026-11-11.

## Problem Statement

Signing in, the second factor, first authenticator enrollment, passkey sign-in and break-glass recovery are the last Razor pages a person meets (ADR-0001), and ADR-0016 moves their ceremonies to the UI API.

The grill's survey found these flaws, deductions from reading the code at `ab7997e`; each ticket proves its finding with a failing test first.

- **SSO lands on an error page (F2).** With trusted IdP MFA and no return URL, the default target is `Url.Action("Index", "Dashboard")`; there is no such controller, so the redirect throws after the session is issued.
- **Named passkey sign-in enumerates accounts (F4).** The begin call returns credential ids for a typed username with passkeys, and nothing otherwise.
- **A locked account answers faster (F5).** The password stage checks lockout before hashing; break-glass shares the path.
- **Sign-out that cannot revoke says nothing (F6).** A copied cookie stays valid until it expires.

F1 (the Secrets default breaking when 7a retires its action) is fixed in 7a ticket 2.

## Solution

The Angular UI serves `/Account/Login`, `/Account/Totp`, `/Account/RecoveryCode`, `/Account/EnrollTotp` and `/Account/Recover` at their URLs, copying the Legacy pages exactly except for the named fixes. Each step is a UI API call with the same server-side state, rows and order as today (ADR-0016, #127). The anonymous surface stays narrow and reveals nothing about who has an account (ADR-0032). SSO stays server redirects. The five credential methods Area 3 left behind are removed.

## User Stories

1. As anyone, I sign in with my password and an authenticator code, or a recovery code, or enroll my authenticator on first sign-in, and land where I was going or on Secrets.
2. As anyone, I sign in with a passkey without typing my username.
3. As anyone, I sign in through the identity provider as before.
4. As an attacker, I cannot tell from any answer, its timing or the passkey call whether a username exists.
5. As an administrator on a blocked network, I recover with all my factors from the Recover page and see whether a block was lifted.
6. As anyone, signing out tells me if this browser was signed out but the session could not be revoked.

## Implementation Decisions

### Scope (Q2)

`AccountController` keeps only `ExternalLogin`, `ExternalCallback` (same URLs) and `Logout` (the Error page's layout, until Area 9); Angular's sign-out uses the UI API with the same implementation. The callers that post `/Account/Logout` today are the account menu (`account-menu.ts`), `StaleSessionSignOut` (`ui-api-errors.ts`, entered by a Profile `401 stale_session`) and the step-up dialog's "Sign in again" (`step-up-dialog.ts`). Login, Totp, RecoveryCode, EnrollTotp and Recover views, their view models, `PasskeySignInState`'s client JSON and `site.js`'s passkey sign-in go. The access-blocked page stays server HTML. Landing and Error wait for Area 9; Denied moves in 7b. Area 9 deletes `Logout` only after no caller remains. `/local-sign-out` stays the outage path.

### Anonymous surface (Q4, ADR-0032)

Login and Recover are anonymous; a signed-in visit to Login is redirected by the server to its target. Totp, RecoveryCode and EnrollTotp are served only with the pending sign-in cookie, otherwise the UI API answers `401 sign_in_expired` as Problem Details. Sign-in pages never call `/api/ui/me`. Serving an anonymous sign-in page sets an anonymous request token; the password, second-factor and sign-out calls reissue it for the new principal, and the next POST validates. A blocked network's auth surfaces are the `/Account/Recover` page and `POST /api/ui/auth/recover`. Static files and `/local-sign-out` GET/POST terminate before the blacklist and grant no account authority (ADR-0026). `AuthorizationSurfaceTests` names every anonymous and pending action deliberately. The throttle meters every unsafe auth call except sign-out; GET, HEAD and OPTIONS stay unmetered.

### Enumeration, landing and sign-out (Q5–Q7)

The password stage hashes before it checks lockout. Passkey sign-in takes no username (discoverable only); Profile registers `residentKey: "required"`. Defaults are literal: `/Secrets` after every sign-in method, `/` after break-glass; the existing guard decides and Angular navigates with a full page load. Sign-out revokes, then clears the cookie; a failed revoke answers `503 sign_out_incomplete`, cookie cleared. Lockout DoS is accepted (ADR-0032).

### Audit (Q9)

Rows and order stay as #127. `SessionIssuer` retains its recorded-session behavior. Ticket 1 hardens the shared `BreakGlassSignIn` path before exposing the new recover API; ticket 3 later removes the Legacy page. The owner allowed exact block restoration on 2026-11-13, superseding no compensation for block lifting only. Failure is per row:

- The session row is refused: no cookie, no `BreakGlassRecovery`, the block stays.
- `BreakGlassRecovery` fails after `SignInAsync` has returned: the cookie stays, the block stays, the answer is `503 audit_unavailable`. The middleware does not sign the cookie out.
- The address was not blocked: the session and the recovery row stay, and there is no lift row.
- `IpRangeUnblocked` fails: the recorded session stays, exact removed ranges and metadata are restored without request cancellation, and concurrent changes are preserved. Confirmed restoration answers `503 audit_unavailable`. An unconfirmed restoration answers `503 recovery_incomplete`, while a guard keeps subsequent protected requests from the source refused. No success answer or unrecorded access grant is allowed.

The guard protects the source before removal can grant access. It is shared across replicas when the blacklist is shared, cannot be removed by the address-wide removal itself, and does not expire into access while the result is unrecorded. Release requires a committed result or confirmed safe restoration. Preserve #127's rows and order; the guard is not a substitute for the required result row. The failure response describes completed steps truthfully. Both in-memory and Redis behavior need execution evidence.

### Leftover methods (Q8)

`ProfileService.ChangePasswordAsync` and `ResetOwnMfaAsync`, `PasskeyService.CompleteRegistrationAsync` and `RemoveAsync`, `MfaRecoveryService.GenerateAsync` are removed with their interface members; their tests move to the verified services or direct seeding.

### UI API contract (Q11)

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

`no-store`, anti-forgery, explicit types, regenerated TypeScript. Problems: `400 sign_in_failed`, `400 code_invalid`, `400 passkey_failed`, `400 recovery_failed`, `400 validation_failed`, `401 sign_in_expired`, `409 sign_in_restarted`, `429 throttled`, `503 audit_unavailable`, `503 recovery_incomplete`, `503 sign_out_incomplete`, with the sentences listed in the decision record. A wrong credential is never a 401. `redirect` is always the guarded target; the return URL is judged only at the final step. Login starts SSO with a real form POST to `/Account/ExternalLogin` carrying the token in a hidden field.

Pending proof and the request token (the session scheme already maps a UI API challenge to Problem Details; the pending scheme does not):

| Proof on the request | Anonymous calls | Pending calls | Sign-out |
| --- | --- | --- | --- |
| Missing | proceed | `401 sign_in_expired` | `401` |
| Expired cookie | treated as missing; the handler drops it before the action | `401 sign_in_expired` | `401` |
| Stale generation or revocation | the pending cookie is rejected and signed out before the action | `401 sign_in_expired`; enrollment does not run | `401` |
| Session cookie, no pending cookie | proceed; a signed-in Login document still redirects | `401 sign_in_expired` | proceeds |
| Pending cookie, no session | password starts over | proceeds | `401` |

An HTML redirect is not a pending-call answer. `UiAntiforgery.GetAndStoreTokens` binds to `HttpContext.User`. `SessionIssuer.SignInAsync` signs the cookie in and does not replace that principal; `ReplaceAsync` does. Each issuance that reissues a token replaces the principal first, so the token names the identity the next request will have. The acceptance test sends the next POST. A `Set-Cookie` assertion alone does not pass.

### Presentation (Q10, Q12)

Exact copies, word for word, with the step track, theme toggle and brand link. Deliberate: the passkey button ignores the username; the recovery-code notice with codes left and Continue; the break-glass outcome with "Continue to the vault"; Login shows a restarted sign-in, `?sso=failed|refused`, `?signOut=sign_out_incomplete` and the three sign-out-everywhere codes in StatusLine; the authenticator-reset sentence is its own one-time handoff and `?signOut=authenticator_reset` shows nothing; the QR stays a server PNG data URI.

## Testing Decisions

- **Unit:** guard and literal defaults (F2 failing first); hash-before-lockout.
- **Production HTTP**, locally on PostgreSQL with `scripts/check.sh integration "<filter>"`, both databases in CI: every call's shape and code; anonymous and pending allowlists; a blocked network reaching the two auth surfaces, with `/local-sign-out` still answering and granting nothing; throttle per POST, never on sign-out or GETs; token reissue proved by the next POST; the pending-proof table; enumeration parity (unknown, locked, disabled); `SignInRefusalAuditTests` and the break-glass tests ported; SSO through the post-IdP fixture.
- **AuditFailureFixture:** every auth call, plus selective rejection of each break-glass row. A refused session row leaves no cookie or lift; later failures may retain the recorded session but cannot leave an unrecorded access grant. Assert exact range restoration and a later real protected request, including restore failure, overlapping ranges, cancellation and concurrent changes. The Redis guard is checked across replicas. Both database providers run the required HTTP coverage.
- **Vitest:** every page.
- **Playwright** under browser health, affected specs locally with `scripts/check.sh e2e <spec>...`: password + TOTP, recovery code and its notice, first enrollment, passkey with the virtual authenticator, break-glass from a blocked network, sign-out and `sign_out_incomplete`, SSO through Keycloak.

## Delivery

Branch `area-8a-sign-in` from `main` after 7a merges, five tickets, one PR. If 7b or 7c merges first, 8a merges `main`, regenerates the API types, then builds and tests (lesson 16). Claude Opus 5.5 (high) implements tickets 1–3 and 5; ticket 4 is test infrastructure for a Cursor agent from 8a's pushed HEAD. The `security-reviewer` agent reviews every ticket and the whole part; Standards and Spec reviews go to GPT Sol 6.1 at xhigh; the PR gets GPT Sol and Thermos before the owner merges.

## Out of Scope

- Provisioning and account activation (8b, #SPEC8B).
- The landing page, Error page, MVC removal and the Legacy `/Account/Logout` (Area 9).
- Self-service password reset or account recovery without existing proof.
- Any redesign.

## Records

`docs/planning/area-8-sign-in/`; `CONTEXT.md` (Pending sign-in); ADRs 0004, 0016, 0017, 0021, 0022, 0023, 0026, 0032. Closes #93's remaining obligation to keep `SignInRefusalAuditTests`.
