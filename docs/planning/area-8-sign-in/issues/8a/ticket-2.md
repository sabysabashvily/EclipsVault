## Parent

#SPEC

## What to build

Login, Totp, RecoveryCode and EnrollTotp served by the Angular UI at their URLs (spec Q2, Q4, Q10, Q12).

- Route table: Login anonymous (a signed-in visit redirected by the server to its guarded target); Totp, RecoveryCode and EnrollTotp only with the pending sign-in cookie, otherwise redirected to Login; each anonymous document sets the anonymous request token and carries the Angular CSP.
- Pages are exact copies; the SSO button is a real form POST to `/Account/ExternalLogin` with the token in a hidden field; the passkey button runs discoverable sign-in; the recovery-code notice with codes left and Continue; Login shows the restarted sign-in, `?sso=failed|refused`, `?signOut=sign_out_incomplete` and the three sign-out-everywhere codes in StatusLine; the SSO callback lands on these pages.
- Move every remaining caller of `POST /Account/Logout` onto `POST /api/ui/auth/sign-out`: the account menu (`account-menu.ts`), `StaleSessionSignOut` (`ui-api-errors.ts`, reached by a Profile `401 stale_session`) and the step-up dialog's "Sign in again" (`step-up-dialog.ts`). One shared in-flight sign-out, so a disposed Profile write does not cancel it. Keep the antiforgery token. A completed clear that could not revoke still navigates to Login with the local-clear warning. A transport failure stays `sign_out_failed` ("Could not sign out. Reload the page to try again.") and does not navigate. `/local-sign-out` stays the outage path. Area 9 deletes `Logout` only after these three callers are gone.
- Profile's sign-out-everywhere outcomes survive the move (Area 9 findings F2 and A9-3, handoff of 2026-11-12). `ProfileDetailsController.SignOutEverywhere` writes TempData for all three, and `profile-writes.ts` swallows `revoked_unrecorded` and `sign_out_incomplete` before navigating to a bare Login. Send each outcome to its own query code before deleting that TempData write: `?signOut=everywhere` ("You have been signed out of all sessions. Sign in again to continue."), `?signOut=revoked_unrecorded` ("You have been signed out of all sessions, but the vault couldn't record it in the audit trail. Tell your administrator."), `?signOut=everywhere_incomplete` ("This browser was signed out, but the vault couldn't confirm that your other devices were. Sign in and try again."). Success uses its own code. Do not put the third sentence on `?signOut=sign_out_incomplete`; that code remains "This browser's cookie was cleared. Other devices may still be signed in."
- Authenticator reset keeps its own sentence, "Your authenticator was reset. Sign in to set up a new one.", and its own handoff. `PreparedProfileTests` shows a typed `?signOut=authenticator_reset` must not claim it, so the handoff is not a query code: the reset response sets a short-lived HttpOnly cookie scoped to `/Account/Login`, the Login document reads it once and clears it, and the Angular page shows the sentence from that document. Delete the reset TempData write in the same ticket.
- Delete the four views, their view models, the Legacy actions and `site.js`'s passkey sign-in. `AccountController` keeps `ExternalLogin`, `ExternalCallback`, `Logout` and Denied until 7b removes it.
- Move the integration (`VaultFixture`, `SignInProof` and their callers) and e2e (`account-sign-in.ts` and callers) sign-in helpers to the new flow.

## Acceptance criteria

- [ ] Production HTTP: route-table rules for anonymous, pending and signed-in visitors; retired Legacy actions answer as other retired routes do.
- [ ] Vitest: every page and error state. Login shows all three sign-out-everywhere sentences and the local-clear sentence from their own codes, shows the authenticator-reset sentence from its handoff, and shows nothing for `?signOut=authenticator_reset`. The account menu, `StaleSessionSignOut` and the step-up dialog post `/api/ui/auth/sign-out`. The stale sign-out still coalesces and still survives disposal. A `sign_out_incomplete` result reaches the local-clear warning rather than `sign_out_failed`.
- [ ] The Profile browser spec proves the three sign-out-everywhere outcomes and the authenticator-reset sentence reach Login. The reset sentence does not survive a second load. Both TempData writes are gone. No application caller in this part still posts `/Account/Logout`.
- [ ] `scripts/check.sh e2e <affected specs>`: password + TOTP, recovery code and its notice, first enrollment, passkey with the virtual authenticator, sign-out and `sign_out_incomplete`, a return URL honoured and an outside one refused, both themes, keyboard.
- [ ] `scripts/check.sh` and `scripts/check.sh integration "<this ticket's filter>"` pass; `npm run lint`, `npm test -- --watch=false` and `npm run check:api-types` pass. CI runs SQL Server and the full browser suite.

## Blocked by

- #T1
