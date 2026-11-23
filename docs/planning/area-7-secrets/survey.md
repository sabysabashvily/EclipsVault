# Area 7 Overview, Secrets and Dynamic secrets: Legacy survey

Surveyed on `area-6a-encryption-keys` at `ab7997e` (`main` plus Area 6a) by reading the code. Nothing
was run, and no finding below has been reproduced; each is a code-read claim that a ticket must prove
with a failing test first (lesson 2).

## What serves the pages today

Every controller below carries `[Authorize(Policy = VaultPolicies.Session)]` (through `VaultController`
or directly). MVC's global `AutoValidateAntiforgeryToken` covers every POST. Area keys `overview`,
`secrets` and `dynamic-secrets` are `Session` in `UiAreas`.

| Page | Routes | Controller, view, model | Core and Infrastructure behind it |
| --- | --- | --- | --- |
| Overview | `GET /` (signed in) | `HomeController.Index` → `Views/Home/Dashboard`, `DashboardViewModel` | `ISecretService.ListAsync` + `VisibleToAsync`; `DashboardService` (`IUserRepository.ListAsync`, `IAuditLogReader.ListRecentAsync(10, username?)`, `CountCriticalSinceAsync(24 h)`); `AuditDisplay` |
| Landing, Error | `GET /` (signed out), `GET /Home/Error?code=` | `HomeController.Index`/`Error`, `Views/Home/Index`, `Shared/Error` | none. Stay until Area 9 (ADR-0018) |
| Secrets | `GET /Secrets` | `SecretsController.Index`, `Views/Secrets/Index`, `SecretListItemViewModel` | `ListAsync` (drops decoys, expired, shredded) + `VisibleToAsync` (Enumerate) |
| Palette search | `GET /Secrets/Search?q=` (JSON) | `SecretsController.Search` | same as the list; < 2 chars → `[]`; name or project contains `q`; prefix matches first, then name; 8 rows; `{ id, name, project, environment, sensitivity (int), sensitivityName, url }`. Called by both palettes: Angular `shell/command-palette.ts` and Legacy `site.js` |
| New secret | `GET/POST /Secrets/Create` | `SecretsController.Create`, `Views/Secrets/Create`, `CreateSecretViewModel` | `SecretCreationPolicy` (clearance ≥ sensitivity; own project unless Top Secret); `SecretService.CreateAsync` (insert + `SecretCreated` via interceptor) |
| Secret details | `GET /Secrets/Details/{id}`; `POST Reveal`, `StepUpReveal`, `RevealVersion`, `Rotate`, `RotateManaged`, `RestoreVersion`, `Grant`, `RevokeGrant`, `Delete` | `SecretsController`, `Views/Secrets/Details`, `SecretDetailsViewModel`, `RotateSecretViewModel`, `ShareSecretViewModel` | `SecretService`, `SecretGrantService`, `StepUpService`, `SecretAccessHandler` → `SecretAccessPolicy`, `ILicenseState`, `IEvaluationWindowStore` |
| Access denied | `GET /Account/Denied?secretId=` | `AccountController.Denied`, `Views/Account/Denied`, `AccessDeniedViewModel` | reasons from TempData; `IAccessRequestService.FilingRefusalAsync`. Also the cookie's `AccessDeniedPath` for every refused policy |
| File a request | `POST /AccessRequests/Create` | `AccessRequestsController` (Legacy, Area 4 left it here) | `SecretToRequestAsync`, `AuthorizeAsync` for the reasons, `CreateAsync`; redirects to `/AccessRequests?filed=1` or `?refused=<code>` |
| Dynamic secrets | `GET /DynamicSecrets`; `POST Issue`, `Revoke` | `DynamicSecretsController`, `Views/DynamicSecrets/Index`, `DynamicSecretsViewModel`, `IssueCredentialViewModel` | `DynamicSecretService` (`IDynamicSecretRepository`, `IDynamicSecretBackend` per backend, `IPremiumFeatureUsage`), `SecretAccessHandler` on the role |

## Authorization on every action

Every by-id Details action calls `SecretService.GetDetailsAsync(id)` first, which reads the envelope
(cache-aside), **trips a decoy, then writes `SecretMetadataViewed`**, and only then runs the ABAC check
(`SecretAccess`: clearance ≥ sensitivity; own project, Top Secret, or an active share; Production only
inside the window; Confidential+ only from a trusted network). A denial redirects to `/Account/Denied`
with the policy's reasons in TempData.

| Action | Gate after ABAC | Step-up | Audit rows written (in order) | Atomic with the change? |
| --- | --- | --- | --- | --- |
| Details (GET) | none | no | `SecretMetadataViewed` (before ABAC) | n/a |
| Reveal | none | Secret+ and last strong auth > 10 min | Viewed; `SecretRevealed` before decrypt | yes, fail-closed |
| StepUpReveal | none | verifies TOTP, re-issues the cookie | Viewed; `StepUpVerified`/`StepUpFailed`; Revealed or VersionRevealed | yes |
| RevealVersion | none | as Reveal | Viewed; `SecretVersionRevealed` before decrypt | yes |
| Rotate | none | no | Viewed; `SecretUpdated` (interceptor, with the archived version) | yes |
| RotateManaged | none; licence gate `ManagedRotation` | no | Viewed; `SecretUpdated` (interceptor); then standalone `SecretUpstreamRotated` | **no**: second row after commit |
| RestoreVersion | none | no | Viewed; `SecretUpdated` (interceptor); then standalone `SecretVersionRestored` | **no** |
| Grant | `CanShare` (admin or project member) | no | Viewed; `SecretShared` (interceptor) | yes (ADR-0021/0027) |
| RevokeGrant | `CanShare` | no | Viewed; `SecretShareRevoked` (interceptor) | yes |
| Delete | none | no | Viewed; `SecretDeleted` (interceptor; versions cascade) | yes |
| Create | `SecretCreationPolicy` | no | `SecretCreated` (interceptor) | yes |
| Issue (dynamic) | ABAC on the role, re-checked; role enabled; licence gate `DynamicSecrets` | no | lease insert + lease row (interceptor) after the upstream mint; mint undone if the insert fails | yes, mint-then-record |
| Revoke (dynamic) | lease owner or admin; one "no" for unknown/closed/other's | no | backend revoke, then lease close + row (interceptor) | revoke-then-record |

## What each page reveals, and to whom

- **List, search, Overview counts and Expiring soon** show only rows the caller could open now
  (`AccessKind.Enumerate`, same rules), so Confidential+ names vanish off the trusted network and
  Production names outside the window. Decoys are dropped in `ListAsync` for every caller.
- **Overview, non-administrator:** own last 10 audit rows (with resource names), vault-wide critical
  count for 24 h. **Administrator:** last 10 rows vault-wide (decoy names included, ADR-0028), critical
  count, staff account count, links to Audit log, Users, New user, Networks.
- **Details:** name, environment, sensitivity, project, algorithm, created/updated/expires, archived
  version list (number, time, archived-by username, change note), managed principal name, licence
  nudge. The sharing panel (recipient, issuer, since, expiry, expired ones included) only to `CanShare`.
- **Denied:** the policy's reasons, which name the secret's project and sensitivity, to anyone holding
  its id; the filing form when Area 4's eligibility rule allows it.
- **Dynamic secrets:** roles the caller passes ABAC for; own leases (administrators: everyone's 50
  newest) with the stored `RevocationError` text; the minted password once, in the Issue response.

## Decoy state (lesson 11)

List, search and Overview: excluded by `ListAsync`. Details and every by-id action: trap fires before
ABAC, for any caller. Version list, sharing panel: only reachable through Details. Access requests:
Area 4 filtered decoys and traps filing. Dynamic secrets: roles are not decoys. **Create:** a decoy's
name is taken (unique index), see F6. Only `DbSeeder` sets `IsHoneyToken`; a cached envelope keeps its
decoy flag for the cache TTL (5 min), which matters only if a row is flagged by hand.

## Links

- Into: sidebar (`/`, `/Secrets`, `/Secrets/Create`, `/DynamicSecrets`), both palettes (search →
  `/Secrets/Details/{id}`), Shared with me "Open" → Details, cookie `AccessDeniedPath` → Denied.
- Out of Overview: `/Secrets` (two stat cards, quick action, "All secrets"), `/Secrets/Create` (header,
  quick action), `/Secrets/Details/{id}` ("Rotate"), admin only `/Audit` (card, "Full audit log"),
  `/Admin/Users`, `/Admin/CreateUser`, `/Admin/Networks`.
- Out of Secrets: Create, Details (row and "Open"). Create → Details on success.
- Out of Details: "Back to list"; Denied on refusal; Index after Delete.
- Out of Denied: "Back to secrets", admin "Manage trusted networks", filing → `/AccessRequests?…`.

## Findings

| Ref | Severity | Finding |
| --- | --- | --- |
| F1 | Medium | **A denied read is recorded as `Viewed`.** `GetDetailsAsync` writes `SecretMetadataViewed` before the ABAC check, for every by-id action (Public API `GET /api/v1/secrets/{id}` too). The trail says someone viewed metadata they were refused, has no denial event, and every action adds a Viewed row beside its own. The denied caller's My activity and Overview "Your recent activity" then show the secret's **name**, which the Denied page never shows. |
| F2 | High | **A share lets its recipient change and delete the secret.** Rotate, managed rotate, restore and delete check only the read policy, which a share satisfies; only Grant/Revoke check `CanShare`. A recipient outside the project can overwrite, roll back or delete the issuer's secret and its history. |
| F3 | Medium | **Save-then-audit on restore and managed rotation.** Both commit with a generic `SecretUpdated`, then write their real event standalone. If that write fails, the change stands and the caller is told it failed; managed rotation's catch-all even says "The credential is unchanged". |
| F4 | Medium | **Concurrent changes to one secret.** The version number is a count read before the save, and `DbSet.Update` writes every column. Two rotations either collide on the unique `(SecretId, VersionNumber)` index, reported as "audit trail unavailable", or, when the second counts after the first commits, the second archives the stale value and the first rotation's value disappears from history. |
| F5 | Medium | **Step-up has two bypasses.** Issuing a dynamic credential for a Secret or Top Secret role needs no fresh proof, though revealing a stored secret of the same sensitivity does. Sharing a high-sensitivity secret (panel or access-request approval) hands another account that reveal with no fresh proof. |
| F6 | Low | **Create with a taken name** (any project's, or a decoy's: names are unique vault-wide) fails the save and is reported as "Vault unavailable (fail-closed)". It is also an existence oracle for names the caller cannot list. |
| F7 | Low | **Dynamic secrets failure paths.** If the lease cannot be recorded and the undo also fails, a live credential exists with no record and no log line. A revoke whose close cannot be recorded leaves the lease `Active` though the credential is gone, behind a generic 503. `RevocationError` stores the backend's raw exception text and shows it to the lease owner and every administrator. |
| F8 | Low | **Licence lookups do not fail open on page views.** The evaluation-days read on Details (managed secret, unlicensed) and Dynamic secrets is not caught; a failure breaks the page, and Reveal builds the page after decrypting, so a recorded reveal can show nothing. |
| F9 | Low | **Denied discloses attributes.** Its reasons name the secret's project and sensitivity to anyone holding the id. |
| F10 | Low | **Overview details.** A non-administrator sees the vault-wide critical count; the service loads every account for every caller; the "n secrets will expire" notice counts the 8-row table, not all expiring secrets. |
| F11 | Low | **A failed dynamic revoke says it worked.** `RevokeAsync` returns true after recording `RevocationFailed`, so the page says "Credential handed back — it no longer works." while the credential may still work. Found while preparing round 2. |

## Evidence gaps (from the sizing report)

No Production HTTP suite drives reveal, step-up, rotate, grant or dynamic issue/revoke; no Vitest
pages; no browser spec for dynamic mint/revoke.
