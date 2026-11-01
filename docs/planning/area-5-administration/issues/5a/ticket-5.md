## Parent

#SPEC

## What to build

New user and Edit user are served by the Angular UI at `/Admin/CreateUser` and `/Admin/EditUser/{id:guid}` (route table entries under `AdminOnly`; the route table learns id paths):

```text
GET    /api/ui/users/new          { emailDomain, clearances: [{ value, label }] }
POST   /api/ui/users              { firstName, lastName, username, password, clearance, project } → 201 { userId, username, email }
GET    /api/ui/users/{userId}     { userId, username, displayName, clearance, project, isYou, clearances }
PATCH  /api/ui/users/{userId}/role { clearance, project } → 204
```

Both need a fresh strong sign-in (step-up dialog from #T4). The New user hint shows the configured email domain. The password field keeps its live breach check. Success returns to Users with the Legacy flash.

## Acceptance criteria

- [ ] Endpoints follow the Area's rules (as #T4); TypeScript regenerated. Problems: `400 validation_failed` with errors per field; `403 step_up_required`; `404 user_not_found`; `409 username_taken`, `last_administrator`, `account_changed`; `503 audit_unavailable`.
- [ ] Both pages match Legacy: titles, subtitles, field labels and order, placeholders, hints (domain from the server), the own-account note on Edit user, Back to users. Field errors show beside their fields.
- [ ] An unchanged role saves with the Legacy flash, records nothing and signs nobody out. Demoting yourself ends your session and sends you to sign-in.
- [ ] The Legacy `CreateUser` and `EditUser` actions, views and view models are removed; `VaultFixture`, the integration helpers and `e2e/account-creation.ts` (and the five specs using it) move to the new UI API and page; the authorization-surface and area tests are updated deliberately.
- [ ] HTTP integration tests: shapes, every problem code, the authorization matrix, step-up, anti-forgery and `no-store`; the id path in any letter case and an unknown id.
- [ ] AuditFailureFixture: create and role change answer `503 audit_unavailable` with nothing changed.
- [ ] Vitest: both forms' loading, error, field errors, step-up and success states.
- [ ] Playwright under browser-health, locally and in CI: create a user → they sign in and enroll; edit their role → their open session ends; the Dashboard's New user link and Users' Edit open the Angular pages; keyboard and both themes.
- [ ] Release build with 0 warnings; every check listed in #T4 passes.

## Blocked by

- #T4
