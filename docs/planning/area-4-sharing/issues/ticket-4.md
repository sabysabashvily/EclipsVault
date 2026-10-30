## Parent

#SPEC

## What to build

Shared with me is served by the Angular UI at `/Secrets/SharedWithMe`. The rest of `/Secrets` stays in the Legacy UI. A recipient sees only the shared secrets they could list right now (ADR-0027).

```text
GET /api/ui/shared-with-me
    { shares: [{ secretId, name, project, environment, sensitivity, sensitivityLabel, sharedBy, accessUntilUtc }] }
```

## Acceptance criteria

- [ ] The endpoint is session-only, `no-store`, takes the account only from the session, returns an explicit response type, and the TypeScript is regenerated and committed.
- [ ] Each row passes the Secrets list's access filter with the share counted. Shares the recipient cannot list (clearance too low, off-network, outside the production window), expired shares and shares on shredded or expired secrets are left out, with no count.
- [ ] The route table, Angular routes (title "Shared with me — EclipsVault"), both sidebars' Sharing entries and the palette open the page inside the Angular UI; `/Secrets` and Details stay Legacy. `SecretsController.SharedWithMe` and its view are removed, and the authorization-surface and area tests are updated deliberately.
- [ ] The page matches Legacy: subtitle, columns, environment and sensitivity badges, "Shared by", "no expiry", UTC format, Open linking to Legacy Details, and the empty state with "Go to your secrets".
- [ ] HTTP integration tests: two recipients see only their own shares; parameters a caller adds have no effect; the under-cleared, off-network and production-window cases; `401`, `no-store` and the pinned JSON shape.
- [ ] Vitest: loading, error with retry, empty and list states.
- [ ] Playwright under the browser-health fixture, locally and in CI: the page matches Legacy in both themes and by keyboard; after an issuer shares, the recipient's context lists it and Open reaches Legacy Details; an under-cleared share is not listed; Area 2's "Shared with me opens Legacy" test is updated to the served page.
- [ ] Release build with 0 warnings; unit, integration, Vitest, lint, format check, build checks, `check:api-types`, build, audit, `e2e` and `e2e:verify-guards` pass.

## Blocked by

- #T1
