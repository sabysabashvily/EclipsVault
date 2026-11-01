## Parent

#SPEC

## What to build

Networks is served by the Angular UI at `/Admin/Networks` (route table entry under `AdminOnly`):

```text
GET    /api/ui/networks        { currentAddress, currentAddressTrusted, configuredRanges,
                                 trustedNetworks: [{ networkId, range, label, addedBy, addedAtUtc }],
                                 blocks: [{ range, reason, blockedAtUtc }] }
POST   /api/ui/networks/trust-current-address → 201 { range }
POST   /api/ui/networks        { range, label } → 201 { range }
DELETE /api/ui/networks/{networkId} → 204
POST   /api/ui/networks/blocks/lift { range } → 204
```

Trusting the current address, adding a range and lifting a block need a strong sign-in within `StepUp:MaxAuthAgeMinutes`, through 5a's step-up dialog; removing a range never does. A lost race on the unique range answers `409 network_exists`, not an audit failure.

## Acceptance criteria

- [ ] Endpoints are `AdminOnly`, session-only, `no-store`, anti-forgery-protected on writes, with explicit types; TypeScript regenerated. Problems: `400 validation_failed`; `403 step_up_required`; `404 network_not_found`, `block_not_found`; `409 network_exists`; `503 audit_unavailable`, `unblock_unrecorded`.
- [ ] The page matches Legacy: current address panel with Trusted/Not trusted and "Trust this address" only when untrusted, the add form, runtime networks table, configured ranges, blocks table with its `MM-dd HH:mm` time. Remove and Unblock use ConfirmationDialog with the Legacy text; Legacy flash text in the StatusLine; the page reloads after every write.
- [ ] Networks and its actions are removed from `AdminController` (and the controller with them); the authorization-surface and area tests are updated deliberately.
- [ ] HTTP integration tests: shapes, every code, the authorization matrix, step-up, anti-forgery and `no-store`; a /7 range refused; the current address as forwarded headers resolve it.
- [ ] AuditFailureFixture: add, trust-current, remove and lift change nothing and answer the audit failure.
- [ ] Race tests on SQL Server and real PostgreSQL: two concurrent adds of one range give one row and one `409`.
- [ ] Vitest: loading, error, empty and list states, dialogs, step-up and StatusLine.
- [ ] Playwright under browser-health, locally and in CI: trust and remove a range with step-up; lift a block written into the run's own Redis for a documentation range; the Dashboard's Networks action and the Denied page's "Manage trusted networks" open the page; keyboard and both themes.
- [ ] Release build with 0 warnings; unit, integration, Vitest, lint, format check, build checks, `check:api-types`, build, audit, `e2e` and `e2e:verify-guards` pass.

## Blocked by

- #T1
