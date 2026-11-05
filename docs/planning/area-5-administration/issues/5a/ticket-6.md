## Parent

#SPEC

## What to build

Service accounts is served by the Angular UI at `/ServiceAccounts`, `/ServiceAccounts/Index`, `/ServiceAccounts/Create` and `/ServiceAccounts/Details/{id:guid}` (route table entries under `AdminOnly`):

```text
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

Issuing a key and enabling need a fresh strong sign-in; creating, disabling, revoking and deleting do not. The raw key is shown once, in the Legacy "copy it now" box with Copy, held only in page memory until the visitor leaves; a reload shows no key.

## Acceptance criteria

- [ ] Endpoints follow the Area's rules (as #T4); TypeScript regenerated. Problems: `400 validation_failed`; `403 step_up_required`; `404 service_account_not_found`, `key_not_found`; `409 name_taken`; `503 audit_unavailable`.
- [ ] The three pages match Legacy: list columns, empty state, create form and hint, the key form (expiry, ceiling options from the server, project scope, metadata-only, allowed ranges, both hints), the key table with scope badges and statuses, the Account panel and the danger zone. Revoke and Delete use ConfirmationDialog with the Legacy text; Enable/Disable stay unconfirmed. Legacy flash text in the StatusLine; a new account opens its page with its flash.
- [ ] The raw key never reaches a cookie, browser storage, the URL, a log or an audit row: a Vitest test and an HTTP test prove it.
- [ ] `ServiceAccountsController`, its views and view models are removed; `VaultFixture`'s service-account helper moves to the UI API; the authorization-surface and area tests are updated deliberately.
- [ ] HTTP integration tests: shapes, every problem code, the authorization matrix, step-up, anti-forgery and `no-store`; a key revoked under another account is 404; an issued key works on the Public API and is refused once revoked.
- [ ] AuditFailureFixture through the new endpoints: every write answers `503 audit_unavailable` with nothing changed.
- [ ] Vitest: loading, error, empty, list, form errors, issued-key box, dialogs, step-up and StatusLine states.
- [ ] Playwright under browser-health, locally and in CI: create a service account → issue a scoped key (step-up) → call the Public API with it → revoke → the call is refused; disable and delete; keyboard and both themes.
- [ ] Release build with 0 warnings; every check listed in #T4 passes.

## Blocked by

- #T5
