# Area 5 Administration: Legacy survey

Surveyed on `area-4-sharing` at `a621855` (PR #103 still open) by reading the code. Nothing was run,
and no finding below has been reproduced; each is a code-read claim that a ticket must prove with a
failing test first. The Administration code is the same on `main`.

## What serves the five pages today

All five controllers carry `[Authorize(Policy = VaultPolicies.AdminOnly)]`: an authenticated session
whose clearance claim is TopSecret (4). There is no separate administrator role. A non-administrator
is sent to `/Account/Denied` by the cookie handler. No page runs an ABAC (secret) check.

| Page | Routes | Controller, view, model | Core behind it |
| --- | --- | --- | --- |
| Users | `GET /Admin/Users` | `AdminController.Users`, `Views/Admin/Users.cshtml`, `UsersViewModel` | `IUserAdminService.ListAsync` |
| New user | `GET/POST /Admin/CreateUser` | `CreateUserViewModel` | `CreateAsync` |
| Edit user | `GET/POST /Admin/EditUser/{id}` | `EditUserViewModel` | `GetAsync`, `SetRoleAsync` |
| (row actions) | `POST /Admin/ResetTotp/{id}`, `DeleteUser/{id}`, `SetEnabled/{id}` (`enabled`), `ForceLogout/{id}`, `Unlock/{id}` | redirect to Users with a flash | `ResetTotpAsync`, `DeleteAsync`, `SetEnabledAsync`, `ForceLogoutAsync`, `UnlockAsync` |
| Networks | `GET /Admin/Networks`; `POST TrustCurrentIp`, `AddNetwork` (`Cidr`, `Label`), `RemoveNetwork/{id}`, `Unblock` (`network`) | `Views/Admin/Networks.cshtml`, `NetworksViewModel` | `ITrustedNetworkService`, `IIpBlacklist`, `Abac:TrustedIpCidrs` |
| Service accounts | `GET /ServiceAccounts`, `GET/POST /ServiceAccounts/Create` | `ServiceAccountsController`, `Views/ServiceAccounts/Index`, `Create` | `IServiceAccountService.ListAsync`, `CreateAsync` |
| Service account | `GET /ServiceAccounts/Details/{id}`; `POST IssueKey/{id}`, `RevokeKey/{id}?keyId=`, `SetEnabled/{id}`, `Delete/{id}` | `Details.cshtml`, `ServiceAccountDetailsViewModel` | `GetAsync`, `IssueKeyAsync`, `RevokeKeyAsync`, `SetEnabledAsync`, `DeleteAsync` |
| Notifications | `GET /Notifications` | `NotificationsController`, `Views/Notifications/Index`, `NotificationsViewModel` | `INotificationService.ListRecentAsync(200)`, `IEmailTransportStatus` |
| License | `GET /License` | `LicenseController`, `Views/License/Index`, `LicenseViewModel` | `ILicenseState` (startup singleton), `LicenseNudgeState` |

## Behaviour

**Users** lists every account (avatar from `/Profile/Avatar`, display name with a "you" badge,
username · email, clearance badge, project, MFA Enrolled/Pending, status Disabled/Locked/Active) with a
client-side filter. Row actions: Edit; Unlock (only when locked); Enable (when disabled) or Disable
(confirm, hidden on your own row); a More menu with Force logout (confirm), Reset MFA (confirm) and
Delete user (confirm, hidden on your own row).

**New user** takes first and last name (64 each), username (`UsernameRules`), initial password (≥12,
breach-screened live through `/api/ui/passwords/check-breached`), clearance and project. The email is
generated as `first.last.N@<Users:EmailDomain>`; the hint text hard-codes `@eclipsvault.local`. Success
flashes the username and email and sends a "Your EclipsVault account is ready" email.

**Edit user** changes clearance and project only, and says saving revokes the user's sessions. Your
own row warns that the last administrator's clearance cannot be removed.

**Networks** shows your address as the vault sees it with Trusted/Not trusted and, when untrusted, a
"Trust this address" button; runtime trusted networks (add by IP/CIDR, at most /8 wide, with a label;
remove with a confirm); the static `Abac:TrustedIpCidrs`; and intrusion-defence blocks (range, reason,
time) with Unblock (confirm). Trusted networks gate only Confidential+ secrets in the ABAC network
rule. They do not gate sessions or administration pages, and no page can block a range: blocks come
only from honey-token trips (reason `Honey-token '<name>' tripped`).

**Service accounts** lists name, clearance, project, active key count, status and created date. A
service account's page issues keys (expiry days, clearance ceiling below the account, project scope,
metadata-only, allowed source ranges), lists keys with scope badges, last use and status, revokes an
active key (confirm), enables or disables the account (no confirm) and deletes it (confirm). Keys are
`evk_` + 256 random bits, stored as SHA-256; the raw key is shown once.

**Notifications** shows the delivery configuration (off / SMTP target / Log transport) and the newest
200 outbox rows: time, recipient, subject (body in its `title` tooltip), event, transport, status and
a failure marker. Read-only.

**License** shows status, message and, for a trusted licence, licensee, tier, contact, licence id,
issued, expiry ("never — perpetual"), update window and node allowance; an update-window-ended
notice; premium features in use beyond the tier; and installation instructions. Read-only; it reads
the startup snapshot and never touches capability evaluations.

## Audit writes and email

| Action | Audit event | How it is written today | Email |
| --- | --- | --- | --- |
| Create user | `UserCreated` | save, then `IAuditSink` | provisioning notice to the generated address |
| Edit role | `UserRoleChanged` | save, store revoke, then sink | none |
| Enable / disable | `UserEnabled` / `UserDisabled` | save, store revoke on disable, then sink | none |
| Force logout | `UserForceLoggedOut` | store revoke, then sink (ADR-0021 order) | none |
| Reset MFA | `UserTotpReset` | one transaction via `IAccountChangeCommitter`, generation advanced (#85) | none |
| Delete user | `UserDeleted` | store revoke, delete, then sink | none |
| Unlock | `AccountUnlocked` (target's sign-in history) | save, then sink | none |
| Add / remove network | `TrustedNetworkAdded` / `Removed` | one transaction, interceptor rule | none |
| Unblock | `IpRangeUnblocked` | Redis/memory delete, then sink | none |
| Service account create, enable/disable, delete | `ServiceAccount*` | save, then sink | none |
| Issue / revoke key | `ApiKeyIssued` / `ApiKeyRevoked` | save, then sink | none |

## Links

**Into the pages**: both sidebars and both palettes (`users`, `networks`, `service-accounts`,
`notifications`, `license` area keys, all `AdminOnly`); the Overview dashboard's Users card and its New
user and Networks quick actions (Area 7); the Denied page's "Manage trusted networks" (Area 7); the
Legacy licence banner and the Angular shell's licence notice ("View license"). **Out of the pages**:
Users ↔ New user ↔ Edit user; Service accounts ↔ Create ↔ Details; avatars from `/Profile/Avatar`.

**Tests that drive these forms**: `VaultFixture` creates accounts and service accounts and forces
logout or deletion through the Legacy forms, as do about ten integration test files and the
`e2e/account-creation.ts` page object used by five browser specs. `AreaVisibilityTests` pins the
admin paths. There are no unit tests for `UserAdminService` or `ServiceAccountService`.

## Findings

U1. **Account changes save first and audit second (#92).** Create, role change, enable/disable,
unlock and delete leave an unaudited change when the audit write fails.

U2. **The last administrator can be removed.** `IsLastAdminAsync` counts every TopSecret account,
disabled ones included, and nothing stops an administrator demoting themselves. With one enabled and
one disabled administrator, the enabled one can demote themselves and leave no usable administrator.
The check is also unserialized: two administrators demoting or disabling each other at once both see
two administrators.

U3. **Demotion and disabling end sessions only in the session store.** Each request re-reads the
account and checks that it exists, is enabled, and has the session's credential generation, but not
the clearance or project in the cookie. If the store revocation fails after the change commits, a
demoted administrator keeps `AdminOnly`. A disabled account's sessions are refused while it is
disabled, but if that revocation failed they come back when it is re-enabled.

U4. **Posted clearance values are not checked.** Model binding accepts any integer for
`ClearanceLevel` on New user, Edit user and New service account. A clearance of 7 passes every
sensitivity check, yet it is not an administrator and the last-administrator check ignores it.

U5. **A long name is a server error.** First and last name are 64 characters each, but the display
name column is 64, so "first last" can overflow it. Once commits are atomic, that failure would be
reported as the audit trail being unavailable.

U6. **Your own row resets your own MFA without account verification.** Profile requires account
verification for the same reset (ADR-0022).

U7. **The administrator chooses, and knows, the new account's initial password.** Whoever signs in
first enrolls the authenticator, so an administrator can take over a new account before its owner.

N1. **Unblocking lifts the block before auditing it.** The block lives in Redis or memory, outside the
database transaction. If the audit write fails, the range stays unblocked with no record. Lifting a
block grants access, so fail-closed auditing applies.

N2. **An off-network administrator session can trust its own address in one click**, and then passes
the network rule for Confidential+ secrets.

S1. **Every service-account write saves first and audits second.** An issued key can exist unaudited.

S2. **Key options are trusted as posted.** An unknown clearance ceiling silently becomes "no limit". A
huge expiry overflows into a server error, and a negative one means "never expires".

S3. **The raw API key travels in a cookie.** It is handed to the Details page through TempData, which
uses the cookie provider here: the key is written to the browser, encrypted, and sent back.

S4. **Revoking a key ignores the account in its URL**, so any account's page can revoke any key.

X1. **A stolen administrator session can install lasting access with no fresh proof.** It can create a
TopSecret account with a password it chose, issue a full-access API key, raise a clearance, reset
another account's MFA, re-enable or unlock an account, trust a network, or lift a block. ADR-0022
already refuses a stolen session a lasting sign-in method on its own account.

X2. **Unique-index losses would be reported as audit failures** once these writes commit with their
audit rows (username, service-account name, CIDR), the same trap Area 4 fixed for shares.

X3. **The Angular route table authorizes only a session.** The Legacy pages send a non-administrator to
`/Account/Denied`. Edit user and a service account's page also need a path with an id, which the
route table cannot hold today.

NT1. **The outbox names secrets to every administrator, wherever they are.** Access-decision and
expiry emails carry secret names in their subject or body, shown even off the trusted network, where
the Secrets list hides Confidential+ names. Networks shows a tripped decoy's name in a block reason.
The audit log (Area 6) shows the same names to the same people.

L1. **The License page's installation text is wrong.** It says to put the token in `license.key` in the
content root. The code reads only `ECLIPSVAULT_LICENSE` or the file named by `License:FilePath`, which
is what the README says.

## Facts that are not findings

- Force logout already revokes before auditing (ADR-0021).
- Reset MFA already commits atomically and advances the credential generation (#85).
- Trusted networks commit atomically through the interceptor, and refuse ranges wider than /8.
- Deleting an account is durable: the per-request account read refuses the session.
- Removing a trusted network cannot end an administrator's session or lock them out of
  administration. It only affects Confidential+ secrets, and the address can be trusted again.
- Step-up verification (`IStepUpService`) shares sign-in lockout and failure counting.
- Viewing License reads a startup snapshot and creates no `FeatureEvaluation` row.
