Follows #96 (Area 4, PR #103). ADR-0017 puts **Area 5: Administration** next. Its grill split it in two (ADR-0017 amendment): this spec is **part 5a, Accounts**, covering Users and Service accounts. Part 5b, Vault settings (Networks, Notifications, License), follows in #SPEC5B. The owner accepted all 26 grill decisions and both delivery outlines, and confirmed them on 2026-11-01.

## Problem Statement

Opening Users or Service accounts from the Angular UI still leaves the app for a Legacy page. The Angular UI has to replace every Razor view (ADR-0001), and these pages are where administrators create, change and remove the accounts and API keys that reach secrets.

The grill's survey found these flaws. They are deductions from reading the code at `a621855`, not reproduced exploits; each ticket proves its finding with a failing test first.

- **Account and service-account changes save first and audit second** (#92). Create, role change, enable/disable, unlock and delete, and every service-account and key write, leave an unaudited change when the audit write fails.
- **The vault can be left with no usable administrator.** The last-administrator check counts disabled administrators, nothing stops self-demotion, and the check is not serialized.
- **Demotion and disabling end sessions only in the session store.** If that call fails after the change commits, a demoted administrator keeps administrator sessions, and re-enabling an account revives sessions that disabling failed to revoke.
- **Posted values are trusted.** Any integer is accepted as a clearance. A long first and last name overflows the display name and becomes a server error. An unknown key ceiling silently means "no limit", and a key's expiry is unbounded.
- **Your own row resets your own MFA without the account verification Profile requires.**
- **The raw API key travels in a cookie** (TempData), and revoking a key ignores the account in its URL.
- **A stolen administrator session can install lasting access with no fresh proof:** create a Top Secret account, issue a full-access key, raise a clearance, reset another account's MFA, re-enable or unlock an account.
- **Unique-name races would surface as audit failures** once commits are atomic.
- **There are no unit tests** for `UserAdminService` or `ServiceAccountService`.

## Solution

The Angular UI serves Users at `/Admin/Users`, New user at `/Admin/CreateUser`, Edit user at `/Admin/EditUser/{id}`, and Service accounts at `/ServiceAccounts`, `/ServiceAccounts/Index`, `/ServiceAccounts/Create` and `/ServiceAccounts/Details/{id}`, copying the Legacy pages exactly except for the named fixes. Every change commits with its audit row (ADR-0028), role changes and disabling end sessions durably through the credential generation (ADR-0023), the vault always keeps an enabled administrator, and granting lasting access needs a recent strong sign-in (step-up). The rules live in Core, so they hold for every caller.

## User Stories

### Reaching the pages

1. As an administrator, the Administration group's Users and Service accounts entries, the ⌘K palette and the Overview dashboard's links open these pages inside the Angular UI, from either shell.
2. As an administrator, my bookmarks to every page above keep working, in any letter case, with the sign-in return URL.
3. As a non-administrator, those addresses still send me to the Denied page, and the UI API refuses me.

### Users

4. As an administrator, I see every account with its avatar, name, username, email, clearance, project, MFA state and status, and can filter the list.
5. As an administrator, I create an account with an initial password that passes the breach screen; the generated email uses the configured domain, and the new person is emailed.
6. As an administrator, I change an account's clearance and project; that account's sessions end, durably.
7. As an administrator, I disable (after confirming) and re-enable an account; its old sessions never return.
8. As an administrator, I unlock a locked account, reset another account's MFA (after confirming), sign an account out everywhere (after confirming) and delete an account (after confirming).
9. As an administrator, my own row offers only Edit and Force logout; resetting my own MFA happens in Profile.
10. As an administrator, I cannot demote, disable or delete the last enabled administrator, even racing another administrator.
11. As an administrator whose last strong sign-in is too old, granting access asks for an authenticator code once, or to sign in again if I have no authenticator, and then goes ahead.

### Service accounts

12. As an administrator, I list, create, enable, disable and delete service accounts.
13. As an administrator, I issue a key with an expiry of 0–3650 days, a ceiling below the account, a project scope, metadata-only and allowed ranges, and see the raw key once, on that page only.
14. As an administrator, I revoke an active key after confirming, and it stops working on the Public API at once.

### Failure and integrity

15. As anyone, if the audit trail cannot be written, a change does nothing and says the audit trail is unavailable; Force logout still signs the account out and says it was not recorded.
16. As an administrator, an invalid value is refused with the field's message, and a name taken meanwhile is a conflict, not an audit failure.

## Implementation Decisions

### Scope (Q2, Q12)

- `AdminController`'s Users, CreateUser, EditUser, ResetTotp, DeleteUser, SetEnabled, ForceLogout and Unlock actions, their views and view models go. Networks and its actions stay until 5b.
- `ServiceAccountsController`, its views and view models go.
- The route table owns the six paths above under `AdminOnly`, including the id paths `/Admin/EditUser/{id:guid}` and `/ServiceAccounts/Details/{id:guid}`. Dashboard and Denied links (Area 7) and `/Profile/Avatar` stay.

### UI API contract (Q19, Q20, Q21)

```text
GET    /api/ui/users              { users: [{ userId, displayName, username, email, clearance, clearanceLabel,
                                     project, mfaEnrolled, status: "active"|"locked"|"disabled", avatarUrl, isYou }] }
GET    /api/ui/users/new          { emailDomain, clearances: [{ value, label }] }
POST   /api/ui/users              { firstName, lastName, username, password, clearance, project } → 201 { userId, username, email }
GET    /api/ui/users/{userId}     { userId, username, displayName, clearance, project, isYou, clearances }
PATCH  /api/ui/users/{userId}/role { clearance, project } → 204
POST   /api/ui/users/{userId}/enable | /disable | /unlock | /reset-mfa | /sign-out → 204
DELETE /api/ui/users/{userId}     → 204
POST   /api/ui/step-up            { code } → 204
GET    /api/ui/service-accounts            { serviceAccounts: [{ serviceAccountId, name, clearance, clearanceLabel,
                                              project, activeKeys, enabled, createdAtUtc }] }
GET    /api/ui/service-accounts/new        { clearances }
POST   /api/ui/service-accounts            { name, clearance, project } → 201 { serviceAccountId }
GET    /api/ui/service-accounts/{id}       { serviceAccountId, name, clearance, clearanceLabel, project, enabled,
                                              createdAtUtc, ceilings: [{ value, label }],
                                              keys: [{ keyId, prefix, createdAtUtc, expiresAtUtc, lastUsedAtUtc,
                                                       status: "active"|"expired"|"revoked", fullAccess, metadataOnly,
                                                       clearanceCeilingLabel, projectScope, allowedRanges }] }
POST   /api/ui/service-accounts/{id}/keys  { expiresInDays, clearanceCeiling, projectScope, metadataOnly, allowedRanges } → 201 { keyId, prefix, apiKey }
DELETE /api/ui/service-accounts/{id}/keys/{keyId} → 204
POST   /api/ui/service-accounts/{id}/enable | /disable → 204
DELETE /api/ui/service-accounts/{id}       → 204
```

`AdminOnly` (step-up: any session), session-only, `no-store`, anti-forgery on writes, the caller only from the session, explicit response types, regenerated TypeScript. Problems: `400 validation_failed` (errors per field), `step_up_failed`; `403 step_up_required` with `proof: "code" | "sign_in"`; `404 user_not_found`, `service_account_not_found`, `key_not_found` (including another account's key); `409 username_taken`, `name_taken`, `last_administrator`, `own_account`, `account_changed`; `503 audit_unavailable`, `revoked_unrecorded` (sign-out only).

### Commits and sessions (Q3, Q4, ADR-0021, ADR-0023, ADR-0028)

- Every database change commits in one transaction with exactly one audit row; an audit failure answers `503 audit_unavailable` with nothing changed. The provisioning email follows the commit, best-effort.
- Role change and disabling advance the credential generation in that transaction and no longer call the session store. Force logout keeps ADR-0021's revoke-then-audit order and answers `503 revoked_unrecorded` when only the audit fails.
- An unchanged role, enabling an enabled account, unlocking an unlocked account and revoking a revoked key answer 204 and record nothing.
- A lost unique-name race answers `409` with the pre-check's message, never `503`.

### Administrators and own account (Q5, Q6)

- At least one enabled Top Secret account remains: demote, disable and delete answer `409 last_administrator` otherwise, counted on enabled accounts under a vault-wide lock inside the change's transaction. Self-demotion is allowed while another enabled administrator exists.
- Reset MFA, Disable and Delete on your own account answer `409 own_account`; the reset message points to Profile.

### Step-up for grants (Q7, Q20)

- Create user, edit role, enable, unlock, reset another account's MFA, issue a key and enable a service account need a strong sign-in within `StepUp:MaxAuthAgeMinutes`. Removing access never does.
- Checks run `AdminOnly` → freshness → lookup. `POST /api/ui/step-up` verifies through `IStepUpService` (shared lockout, failure counting, replay protection) and re-issues the session with a fresh step-up time, as the Legacy reveal does. A wrong code answers `400 step_up_failed` "That authenticator code is not valid. Try again.". The dialog retries the action once; with no authenticator it offers "Sign in again" with a return URL.

### Input rules and keys (Q8, Q9)

- Core enforces every Legacy form rule plus: defined clearance and ceiling values; a ceiling strictly below the account; "First Last" ≤ 64 characters; key expiry 0–3650 days (0 = none); project key 1–64 characters after trimming. The server supplies the New user email domain.
- The raw key appears only in the issue response, held in page memory until the visitor leaves; never in a cookie, storage, URL, log or audit row.

### Presentation (Q23)

Exact copy: titles, subtitles, columns, badges, empty states, UTC formats, button sizes. Every Legacy `data-confirm` becomes ConfirmationDialog with its text; Edit, Save role, Enable, Unlock, Issue key and service-account Enable/Disable stay unconfirmed. Legacy flash text word for word in the StatusLine. New user returns to Users and a new service account opens its page, each with its flash. The key shows in the "copy it now" box with Copy. New keyboard-accessible shared TableFilter and RowMenu.

## Testing Decisions

- **Unit:** last-administrator and own-account cases, input rules and key options as literal tables; the freshness rule and which actions need step-up; key status; unchanged changes record nothing.
- **SQL Server Production HTTP:** every shape and code; an authorization matrix (anonymous 401, non-administrator 403 on the API and `/Account/Denied` on the pages); session-only, anti-forgery and `no-store`; stale versus fresh step-up; a demoted administrator's old cookie refused with no session-store call; re-enabling revives nothing; a key revoked under the wrong account is 404; an issued key works on the Public API and fails once revoked.
- **AuditFailureFixture:** every write leaves everything unchanged and answers the audit failure; Force logout signs out and answers `revoked_unrecorded`.
- **Races on SQL Server and real PostgreSQL:** two administrators demoting each other; demotion against disable; two creates with the same username or service-account name.
- **Vitest:** loading, error with retry, empty, list, every dialog including step-up, every StatusLine state.
- **Playwright** under browser-health, locally and in CI: create a user → they sign in; an edited role ends their session; disable then enable; unlock a locked account (lockout set in the run's own database); Reset MFA → re-enrollment; delete; step-up with a real code; service account → key → Public API → revoke; a non-administrator gets Denied; both shells, palette, keyboard and both themes. Specs never wait out real throttles.
- `VaultFixture`, the integration helpers and `e2e/account-creation.ts` move off the Legacy forms in the ticket that retires each form.

## Delivery

Branch `area-5a-accounts` from `main` once #103 has merged, seven sequential tickets, one PR. Each page is served by the Angular UI in the ticket that builds it. The first ticket commits `CONTEXT.md`, the ADR-0017 and ADR-0023 amendments, ADR-0028 and `docs/planning/area-5-administration/`. The `security-reviewer` agent reviews every ticket and the whole part before the PR. Closes #92.

## Out of Scope

- Networks, Notifications and License (#SPEC5B).
- Provisioning without an administrator-known password (follow-up issue).
- The Dashboard and Denied pages (Area 7); step-up for reveals (Area 7 reuses this endpoint).
- Any redesign of these pages.

## Records

Survey, decisions and contract: `docs/planning/area-5-administration/`. Glossary: `CONTEXT.md` (Administrator; Credential generation and Step-up widened). ADRs: 0017 and 0023 (amended), 0021, 0022, 0028.
