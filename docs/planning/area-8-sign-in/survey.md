# Area 8 Sign-in: Legacy survey

Surveyed on `area-6a-encryption-keys` at `ab7997e` (`main` plus Area 6a) by reading the code, with
Area 7's uncommitted records beside it. Nothing was run, and no finding below has been reproduced;
each is a code-read claim that a ticket must prove with a failing test first (lesson 2).

## What serves the pages today

`AccountController` (530 lines, 16 actions) is `[Authorize]` at the class and opens actions one by one
with `[AllowAnonymous]`; `AuthThrottleFilter` covers the whole controller. MVC's global
`AutoValidateAntiforgeryToken` covers every POST, anonymous ones included. All views use `_AuthLayout`.

| Page or call | Routes | Who may call | Services behind it |
| --- | --- | --- | --- |
| Sign in | `GET/POST /Account/Login` | anonymous; a full session is redirected to its target | `VaultAuthenticationService.ValidateCredentialsAsync` → MFA-pending cookie |
| Authenticator code | `GET/POST /Account/Totp` | MFA-pending cookie only | `VerifyTotpAsync`, `SessionIssuer.SignInAsync` |
| Recovery code | `GET/POST /Account/RecoveryCode` | MFA-pending | `VerifyRecoveryCodeAsync`, `SessionIssuer` |
| First enrollment | `GET/POST /Account/EnrollTotp` | MFA-pending | `BeginTotpEnrollmentAsync` (GET writes the seed), `CompleteTotpEnrollmentAsync`, `TotpQrCode` (PNG data URI) |
| Passkey sign-in | `POST /Account/PasskeyLoginBegin`, `POST /Account/PasskeyLoginComplete` (JSON, from `site.js`) | anonymous | `PasskeyService.BeginAssertionAsync`/`CompleteAssertionAsync`, `IPasskeyCeremonyStore` keyed by the ASP.NET session cookie `EclipsVault.Ceremony` |
| Break-glass | `GET/POST /Account/Recover` | anonymous; the only path `IpBlacklistMiddleware` exempts | `ValidateCredentialsAsync` + `VerifyBreakGlassAsync` (Top Secret only), `BreakGlassSignIn` |
| Sign-out | `POST /Account/Logout` | session; the one unsafe action the throttle skips (by `MethodInfo`) | `ISessionRegistry.RevokeAsync`, then cookie cleared. Called by the Legacy layout's form and Angular's `account-menu.ts` |
| SSO | `POST /Account/ExternalLogin` (form, antiforgery), `GET /Account/ExternalCallback` | anonymous | OIDC code + PKCE into the `OidcCorrelation` cookie (Lax, 10 min); `SsoSignInService` decides; trusted IdP MFA issues the session, otherwise joins the MFA-pending flow |
| Denied | `GET /Account/Denied` | session | moves in Area 7b |
| Access blocked | any path from a blocked range | — | `IpBlacklistMiddleware`'s inline HTML (not a view), or `403 blocked` Problem Details on `/api/ui` |

Cookies: `EclipsVault.Session` (Strict, sliding, revocable per request), `EclipsVault.MfaPending`
(Strict, 6 minutes absolute, validated on every request against the credential generation and the
revocation version), `EclipsVault.Ceremony` (passkey ceremony session, 6-minute idle), `EclipsVault.Csrf`.
`UseStaticFiles` runs before the IP block, so stylesheets and the Angular bundles load for a blocked
caller; documents and APIs do not.

## Audit, throttle and lockout per credential post

Since #127, every factor commits its bookkeeping with an audit row in one `IAuthenticationAttempt`,
and the sign-in itself is recorded by `SessionIssuer` under the account lock, only when a session is
issued (`SignInRefused` otherwise). Every row below is fail-closed.

| Post | Throttle (per IP) | Lockout (5 / 15 min, per account) | Rows |
| --- | --- | --- | --- |
| Password | yes | counted; checked first | unknown name: `LoginFailed` via `IAuditSink`; known: `LoginFailed` (locked, wrong, disabled) in the attempt |
| Authenticator code | yes | counted | `TotpFailed` or `SignInFactorVerified`; then `LoginSucceeded` "Password + TOTP" or `SignInRefused` |
| Recovery code | yes | counted | `TotpFailed` or `RecoveryCodeUsed`; then `LoginSucceeded` "Password + recovery code" or `SignInRefused` |
| Enrollment confirm | yes | counted | `TotpFailed` or `TotpEnrolled`; then `LoginSucceeded` "Password + TOTP enrollment" or `SignInRefused`. The seed itself (GET) writes no row |
| Passkey begin | yes | no | none |
| Passkey complete | yes | counted on a bad signature | `LoginFailed` or `SignInFactorVerified`; then `PasskeyLogin` or `SignInRefused` |
| Break-glass | yes | counted | as password + code; then `LoginSucceeded`, `BreakGlassRecovery`, and `IpRangeUnblocked` if a block was lifted, in that order, uncancellable |
| SSO callback | no (GET) | no | `SsoIdentityLinked` or `SsoSignInRefused`; then `LoginSucceeded` "Single sign-on" or `SignInRefused` |
| Sign-out | no | no | none |

## What each page reveals before a session

- **Password:** one message, "Invalid username or password.", for unknown, wrong, locked and
  disabled. Unknown names verify a dummy hash; disabled accounts fail after the hash. A locked account
  answers before any hash (F5).
- **Passkey:** the begin call takes the username typed in the password form and returns that
  account's credential ids; an unknown name or an account without passkeys returns an empty list (F4).
- **SSO:** one message for every refusal; the trail records which.
- **Break-glass:** one message for every failure, including "not an administrator".
- **Enrollment:** the authenticator seed, to anyone holding the MFA-pending cookie of an account
  without one (F3).

## Return URLs and links

`SignInTarget` accepts only a local, printable-ASCII path that does not lead into `/Account` (except
`/Account/Denied`), with no `%` and no dot segments, and `LocalRedirect` checks again. The passkey
call returns `/Account/Login?ReturnUrl=` so the final redirect is a top-level navigation. Defaults:
`Url.Action("Index", "Secrets")` for password, code, enrollment and passkey; `Url.Action("Index",
"Dashboard")` for SSO; `/` for break-glass; `/` after sign-out (F1, F2).

Into sign-in: the session cookie's `LoginPath`, Angular's 401 handler (`signInUrl`), the landing page,
`?signOut=sign_out_incomplete` from Profile and local sign-out, the blocked page → Recover, the
provisioning email. Out: Totp ↔ RecoveryCode, Login → Totp or EnrollTotp, every success → the target.

## Leftover Area 3 methods

`ProfileService.ChangePasswordAsync` and `ResetOwnMfaAsync`, `PasskeyService.CompleteRegistrationAsync`
and `RemoveAsync`, `MfaRecoveryService.GenerateAsync`: no Web caller. Tests call them, mostly as setup
(`SignInRefusalAuditTests`, `AccountChangeRaceTests`, `PendingProofTests`, `AuthenticationAuditFailureTests`,
`CredentialGenerationPersistenceTests`, `AccountTransactionFailureTests`, `ProfileServiceTests`) and as
fakes in two unit test files.

## Provisioning today (#117)

`UserAdminService.CreateAsync` takes the administrator's password, screens it for breaches, and
**generates** the email as `first.last.N@<Identity:EmailDomain>` (default `eclipsvault.local`), then
sends "the password an administrator gave you" through the notification outbox, which stores message
bodies and shows them to every administrator. The account's first sign-in enrolls its authenticator.

## Findings

| Ref | Severity | Finding |
| --- | --- | --- |
| F1 | High (lands with 7a) | **Sign-in will break when Area 7a deletes the Secrets list action.** The default target is `Url.Action("Index", "Secrets")`. Once 7a removes `SecretsController.Index`, that returns null, and `LocalRedirect(null!)` throws after `SessionIssuer` has recorded `LoginSucceeded` and appended the cookie: every sign-in without a return URL ends on the error page. |
| F2 | Medium | **SSO with trusted IdP MFA and no return URL ends on the error page.** Its default is `Url.Action("Index", "Dashboard")`; there is no `DashboardController`. `SsoSignInRedirectTests` fakes the URL helper, so it never sees this. |
| F3 | Medium (#117) | **Whoever passes the password stage first can copy the authenticator seed.** The pending seed stays stable until enrollment completes and its creation is not audited. Someone who knows a new account's initial password can view the seed without enrolling; when the owner later enrolls that same seed, the copier holds the password and the authenticator indefinitely. A forced password change at first sign-in does not help: the administrator who knows the initial password can make that change. |
| F4 | Low | **Named passkey sign-in enumerates accounts.** The begin call returns credential ids for a typed username that has passkeys, and nothing for one that does not or does not exist. |
| F5 | Low | **A locked account answers faster.** The password stage checks lockout before hashing, so after five failures the quick reply confirms the username exists. Break-glass shares the path. |
| F6 | Low | **Sign-out that cannot revoke says nothing.** If the session store refuses the tombstone, the cookie is cleared and the person is told nothing, while a copied cookie stays valid until it expires (ADR-0021's `sign_out_incomplete` covers only signing out everywhere). |
| F7 | Info (#117) | **The account's email is not a mailbox.** It is generated from the name and a configured domain with a sequence suffix, so an emailed invitation needs a real address the administrator enters. The outbox stores bodies, so an invitation link must never pass through it (ADR-0024's unrecorded delivery exists). |
| F8 | Info | **No UI API action is anonymous today**, and `AuthorizationSurfaceTests` pins that; the sign-in endpoints need a deliberate exception, as ADR-0016 foresees. |

## Verified clean

Recovery codes are spent by a conditional update (single use under a race); authenticator steps are
spent atomically; the MFA-pending cookie is 6 minutes absolute, bound to the credential generation and
revocation version, and accepted only by the three second-factor actions; passkey challenges are
single-use, 120 seconds, bound to the ceremony session and purpose, and a named ceremony accepts only
that account's key; discoverable sign-in captures the generation before verifying (ADR-0023); #93's
ordering holds for every path, break-glass included; the return-URL guard; anonymous antiforgery on
every POST and `SameSite=Strict`; the throttle meters every unsafe action except sign-out; SSO gives
one message for every refusal.

## Evidence gaps

No browser spec for recovery-code sign-in, break-glass or an IdP round trip (sizing report). No
Production HTTP test drives passkey sign-in end to end except `LegacyPasskeyCeremonyTests`.
