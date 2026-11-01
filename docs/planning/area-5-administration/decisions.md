# Area 5 Administration: decision record

Status: all 26 recommendations accepted (rounds 1–3) and confirmed by the owner on 2026-11-01.
Published as #104 (5a spec; tickets #106–#112) and #105 (5b spec; tickets #113–#116), with the
initial-password follow-up as #117. No code, branch or commit exists yet. #103 merged on 2026-11-01 (main `7953598`), so 5a can start.

Survey and findings: [survey.md](survey.md). Glossary: `CONTEXT.md` (Administrator, Trusted network,
Intrusion-defence block, Notification outbox; Credential generation, Step-up and Area widened). ADRs:
0017 and 0023 amended, 0028 new.

## Decisions

| Ref | Decision | Agreed behaviour |
| --- | --- | --- |
| Q1 | Split | Two parts, each with its own spec, branch and PR: **5a Accounts** (Users, Service accounts, #92; U1–U7, S1–S4, X1–X3), then **5b Vault settings** (Networks, Notifications, License; N1, N2, NT1, L1). |
| Q2 | Scope | All five pages retire in their part: views, view models and controllers. Dashboard and Denied links stay (Area 7), as do the Legacy licence banner (final cleanup) and `/Profile/Avatar`. Nothing moves to another Area. |
| Q3 | Atomic commits (#92) | Every database change on Users and Service accounts commits in one transaction with its one audit row: account create, role, enable/disable, unlock, delete; service-account create, enable/disable, delete; key issue and revoke. |
| Q4 | Durable session end | Role change and disabling advance the credential generation in the same transaction as the change and its audit row. Re-enabling never revives sessions, and these two actions no longer call the session store. Force logout keeps the revocation version; deletion is already durable. |
| Q5 | Last administrator | At least one enabled TopSecret account remains. Demoting (self included), disabling or deleting an administrator answers `409 last_administrator` when none would remain. Enabled accounts only, rechecked under a vault-wide lock inside the change's transaction. Self-demotion is allowed while another enabled administrator exists. |
| Q6 | Own row | Own row offers Edit and Force logout. The server refuses own Reset MFA, Disable and Delete with `409 own_account`; the reset message points to Profile. |
| Q7 | Step-up for grants | Strong sign-in within `StepUp:MaxAuthAgeMinutes` (default 10) for: create user, edit role, enable, unlock, reset another account's MFA, issue a key, enable a service account, add a network, trust the current address, lift a block. Never for removing access: disable, delete, force logout, revoke a key, disable or delete a service account, remove a network. |
| Q8 | Input rules | Core enforces every Legacy form rule, plus: defined clearance and ceiling values, a ceiling strictly below the account, "First Last" ≤ 64 characters, key expiry 0–3650 days (0 = none, negatives refused), project key 1–64 characters after trimming. `400 validation_failed` with errors per field. |
| Q9 | API key display | The raw key appears only in the `no-store` issue response, held in page memory in the "copy it now" box until the visitor leaves. It never goes into a cookie, storage, URL, log or audit row (prefix only). A reload shows no key. Revoke is addressed under its account; another account's key is `404`. |
| Q10 | Unblock | Lift, then audit. If the audit write fails, restore the exact block record and answer `503 audit_unavailable`. If restoring fails too, answer `503 unblock_unrecorded` and log critical. |
| Q11 | Names in admin views | Accepted: Notifications, block reasons and the audit log show secret and decoy names to every administrator, unfiltered (ADR-0028 consequence). |
| Q12 | Route table | Administration paths are served under `AdminOnly` (non-administrators get `/Account/Denied`), plus the id paths `/Admin/EditUser/{id:guid}` and `/ServiceAccounts/Details/{id:guid}`. Every endpoint is `AdminOnly` in its own right. |
| Q13 | License page | Exact copy. The installation text names `ECLIPSVAULT_LICENSE` or the file at `License:FilePath`. Tests prove no evaluation row is created and that a missing, invalid or expired licence is answered normally. |
| Q14 | Initial password | Out of scope; a follow-up issue is filed after confirmation (one-time enrollment link or forced change at first sign-in). |
| Q15 | Glossary | Administrator, Trusted network, Intrusion-defence block, Notification outbox added. |
| Q16 | Step-up term | Widened to administrator grants; "sudo mode" is listed under Avoid. |
| Q17 | Carried details | 5a starts after #103 merges; 5b branches from `main` after 5a merges. Audit failure `503 audit_unavailable` with nothing changed; Force logout revoke-then-audit, `503 revoked_unrecorded`. Step-up via `403 step_up_required` → code dialog → retry, or "sign in again". Unique-name races `409`, never `503`. The server supplies the New user email domain. |
| Q18 | Commercial sentences | Copied word for word: "never gated", "still receives security patches", "not covered by your current tier", and the README pointer. Any rewording is the owner's to supply. |
| Q19 | Users UI API | See below. An unchanged role, enabling an enabled account and unlocking an unlocked one answer 204 and record nothing. Acting on yourself (a demotion, Force logout) ends your session, and the next call goes to sign-in. |
| Q20 | Step-up endpoint | `POST /api/ui/step-up { code }` → 204 through `IStepUpService` (shared lockout, failure counting, replay protection), then re-issue the session with a fresh step-up time. A wrong code is `400 step_up_failed` "That authenticator code is not valid. Try again.". `403 step_up_required` carries `proof: "code" | "sign_in"`. One retry after success; cancel changes nothing. Checks run `AdminOnly` → freshness → lookup. Area 7 reuses it. |
| Q21 | Service accounts UI API | See below. `allowedRanges` stays free text, split and checked once in Core; `ceilings` lists only levels below the account; key status is computed on the server; revoking a revoked key is 204 with nothing recorded. |
| Q22 | 5b UI API | See below. A block's range goes in the body; `updatesLapsed` is computed on the server at request time; the License endpoint never fails on licence state. |
| Q23 | Presentation | Exact copy. Every Legacy `data-confirm` becomes ConfirmationDialog with its text; actions Legacy didn't confirm stay unconfirmed. Legacy flash text is word for word in the StatusLine (success or error only). New user returns to Users and a new service account opens its page, each with its flash. The issued key shows in the "copy it now" box with Copy. New keyboard-accessible TableFilter (Users, Notifications) and RowMenu (Users "More"). The Notifications body stays a `title` tooltip. |
| Q24 | Test plan | See "Testing" below. |
| Q25 | Delivery | See "Delivery outline" below. |
| Q26 | Step-up endpoint policy | `POST /api/ui/step-up` requires a session, not `AdminOnly`: it proves only the caller's own factor and grants nothing, since freshness is still checked at each `AdminOnly` endpoint. Area 7's reveals reuse it for every account. Lockout, failure counting, replay protection, anti-forgery and `no-store` apply. |

## UI API contract

All endpoints: `AdminOnly`, session-only, `no-store`, anti-forgery on writes, the caller only from
the session, explicit response types, regenerated TypeScript, Problem Details with a stable `code`.

### 5a Accounts

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

Problems: `400 validation_failed` (errors per field), `step_up_failed`; `403 step_up_required`
(`proof`); `404 user_not_found`, `service_account_not_found`, `key_not_found`; `409 username_taken`,
`name_taken`, `last_administrator`, `own_account`, `account_changed`; `503 audit_unavailable`,
`revoked_unrecorded` (sign-out only).

### 5b Vault settings

```text
GET    /api/ui/networks        { currentAddress, currentAddressTrusted, configuredRanges,
                                 trustedNetworks: [{ networkId, range, label, addedBy, addedAtUtc }],
                                 blocks: [{ range, reason, blockedAtUtc }] }
POST   /api/ui/networks/trust-current-address → 201 { range }
POST   /api/ui/networks        { range, label } → 201 { range }
DELETE /api/ui/networks/{networkId} → 204
POST   /api/ui/networks/blocks/lift { range } → 204
GET    /api/ui/notifications   { delivery: "off"|"smtp"|"log", smtpTarget,
                                 outbox: [{ sentAtUtc, to, subject, body, eventType, transport,
                                            status: "sent"|"failed"|"suppressed", error }] }
GET    /api/ui/license         { status, message, licence: { issuedTo, tier, contact, licenseId, issuedAtUtc,
                                 expiresAtUtc, updatesUntilUtc, maxNodes } | null, updatesLapsed, beyondTier }
```

Problems: `400 validation_failed`; `403 step_up_required` (trust, lift); `404 network_not_found`,
`block_not_found` ("That range is not currently blocked."); `409 network_exists`;
`503 audit_unavailable`, `unblock_unrecorded`.

## Carried over unchanged

The provisioning email text (best-effort, after commit); breach screening and the 12-character
minimum; the generated `first.last.N@domain` email; `/8` as the widest trusted range; key format
(`evk_`, 256 bits, SHA-256) and scope semantics; the 200-row outbox; the licence status snapshot at
startup; area keys and `AdminOnly` visibility; Legacy wording, including "Force logout" and "Reset MFA".

## Testing

- **Unit:** last-administrator and own-account cases, input rules and key options as literal tables;
  the step-up freshness rule and which actions need it; key status; unchanged changes record nothing.
- **SQL Server Production HTTP:** every endpoint's shape and code; an authorization matrix (anonymous
  401, non-administrator 403 on the API and `/Account/Denied` on the pages); session-only,
  anti-forgery, `no-store`; stale versus fresh step-up; a demoted administrator's old cookie refused
  with no session-store call; re-enabling doesn't revive sessions; a key revoked under the wrong
  account is 404; an issued key works on the Public API, then fails once revoked.
- **AuditFailureFixture:** every write leaves everything unchanged, including Unblock's restore.
- **Races on SQL Server and real PostgreSQL:** two administrators demoting each other; a demotion
  against a disable; two creates with the same username or service-account name.
- **Vitest:** loading, error with retry, empty, list, every dialog, the step-up dialog, every StatusLine state.
- **Playwright** under browser-health, locally and in CI: create a user → they sign in; an edited role
  ends their session; disable then enable; a locked account unlocked (lockout set in the run's own
  database, no throttle waited out); Reset MFA → re-enrollment; delete; step-up with a real code;
  service account → key → Public API call → revoke; the non-administrator Denied page; both shells,
  palette, keyboard and both themes.
- **Helpers:** `VaultFixture`, the integration helpers and `e2e/account-creation.ts` move off the Legacy
  forms in the ticket that retires each form.
- **Decoys:** 5a holds no decoy state (lesson 11); 5b's block reasons follow Q11.

## Delivery outline

Two specs, published together after confirmation; 5b's spec is blocked by 5a's. Tests land with their
behaviour; each page is served in the ticket that builds it. The security reviewer reviews every
ticket, and each part ends with a whole-part review and its single PR. Issue drafts: [issues/](issues/).
No schema change is expected: the credential generation and the account concurrency stamp exist.

**5a Accounts**, branch `area-5a-accounts` from `main` after #103 merges:

| Ticket | Behaviour | Decisions |
| --- | --- | --- |
| 1 | Commit account changes with their audit rows; role change and disable advance the generation. Commits the records. | Q3, Q4 (#92) |
| 2 | Keep an enabled administrator, refuse own-account changes, enforce input rules | Q5, Q6, Q8 |
| 3 | Commit service-account and key changes with their audit rows; check key options | Q3, Q8, Q9 |
| 4 | Users served by Angular, with row actions, TableFilter, RowMenu and step-up | Q7, Q12, Q19, Q20, Q23 |
| 5 | New user and Edit user served by Angular; helpers off the Legacy forms | Q12, Q19, Q23 |
| 6 | Service accounts list, create and details served by Angular | Q9, Q12, Q21, Q23 |
| 7 | Verify the whole part and prepare its PR | whole-part reviews and gates |

**5b Vault settings**, branch `area-5b-vault-settings` from `main` after 5a merges:

| Ticket | Behaviour | Decisions |
| --- | --- | --- |
| 1 | Lift a block only when it can be recorded | Q10 |
| 2 | Networks served by Angular, with step-up on its grants | Q7, Q12, Q22, Q23 |
| 3 | Notifications and License served by Angular | Q11, Q13, Q18, Q22, Q23 |
| 4 | Verify the whole part and prepare its PR | whole-part reviews and gates |
