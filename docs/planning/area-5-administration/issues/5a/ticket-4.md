## Parent

#SPEC

## What to build

Users is served by the Angular UI at `/Admin/Users` (route table entry under `AdminOnly`), with its row actions, and the step-up that granting needs:

```text
GET    /api/ui/users              { users: [{ userId, displayName, username, email, clearance, clearanceLabel,
                                     project, mfaEnrolled, status: "active"|"locked"|"disabled", avatarUrl, isYou }] }
POST   /api/ui/users/{userId}/enable | /disable | /unlock | /reset-mfa | /sign-out → 204
DELETE /api/ui/users/{userId}     → 204
POST   /api/ui/step-up            { code } → 204
```

- Enable, unlock and resetting another account's MFA need a strong sign-in within `StepUp:MaxAuthAgeMinutes`; disable, delete and sign-out never do. Checks run `AdminOnly` → freshness → lookup. A stale session gets `403 step_up_required` with `proof: "code" | "sign_in"`.
- `POST /api/ui/step-up` verifies through `IStepUpService` and re-issues the session with a fresh step-up time, as the Legacy reveal does; a wrong code is `400 step_up_failed` "That authenticator code is not valid. Try again.". A shared step-up dialog asks for the code (or offers "Sign in again" with a return URL), then retries the action once; cancelling changes nothing.
- New shared, keyboard-accessible components: TableFilter (the Legacy filter box) and RowMenu (the "More" menu).

## Acceptance criteria

- [ ] Endpoints are `AdminOnly`, session-only, `no-store`, anti-forgery-protected on writes, take the caller only from the session, and return explicit types; the TypeScript is regenerated and committed.
- [ ] Problems: `400 step_up_failed`; `403 step_up_required`; `404 user_not_found`; `409 last_administrator`, `own_account`, `account_changed`; `503 audit_unavailable`, `revoked_unrecorded` (sign-out only).
- [ ] The page matches Legacy: subtitle, New user button, filter, columns, avatar, "you" badge, clearance badge, MFA and status badges (Disabled, then Locked, then Active), Edit, Unlock only when locked, Enable or Disable, More with Force logout, Reset MFA and Delete user. Your own row offers only Edit and Force logout. Every Legacy confirm text is a ConfirmationDialog; Enable and Unlock are unconfirmed. Outcomes use the Legacy flash text in the StatusLine; the list reloads after every write. Acting on yourself (Force logout) ends your session and sends you to sign-in.
- [ ] The route table, Angular routes (title "Users — EclipsVault"), both sidebars and the palette open the page in Angular; a non-administrator gets `/Account/Denied`. The Legacy `Users` view and the retired row actions are removed; `VaultFixture` and the integration helpers that used them move to the UI API; the authorization-surface and area tests are updated deliberately.
- [ ] HTTP integration tests: pinned shapes; every problem code; the authorization matrix (anonymous 401, non-administrator 403); stale versus fresh step-up, a wrong code counting towards lockout, and a replayed code refused; anti-forgery and `no-store`.
- [ ] AuditFailureFixture through the new endpoints: each write answers `503 audit_unavailable` with nothing changed; sign-out answers `503 revoked_unrecorded` and the account is signed out.
- [ ] Vitest: loading, error with retry, list, filter, RowMenu, every dialog including step-up (code and sign-in variants), every StatusLine outcome.
- [ ] Playwright under browser-health, locally and in CI: disable then enable (step-up with a real authenticator code); unlock an account locked in the run's own database; Reset MFA → that account re-enrolls at sign-in; Force logout ends the other account's session; delete; the last-administrator refusal; a non-administrator gets Denied; both shells, palette, keyboard and both themes. Specs never wait out real throttles.
- [ ] Release build with 0 warnings; unit, integration, Vitest, lint, format check, build checks, `check:api-types`, build, audit, `e2e` and `e2e:verify-guards` pass.

## Blocked by

- #T3
