## Parent

#SPEC

## What to build

Failures answer in the caller's format, and the vault's own pages become server pages (ADR-0034):

- `GlobalExceptionMiddleware`: the UI API keeps Problem Details; `/api/v1/` gets `{ "error": "<code>" }` with the real status for every mapped exception; every other request gets the error server page with the real status, written directly (no redirect). A honey-token trip outside the APIs still signs out and redirects to sign-in.
- `IpBlacklistMiddleware`: `/api/v1/` gets `403 { "error": "blocked" }`; the blocked page uses the server-page template with its text unchanged.
- One server-page template (blocked, error, not found) with the spec's copy; `wwwroot/css/server-page.css` with only the sign-in card rules, tokens and `@font-face` it needs.
- `no-store` on every `/api/` response.
- `HomeController.Error`, `Views/Shared/Error` and `ErrorViewModel` deleted.
- List anything an earlier Area left behind that the spec's scope names (views, actions, models, `site.js` code); ticket 2 deletes it.

This ticket also commits the grill's records: `docs/planning/area-9-cleanup/`, ADR-0034, the ADR-0014, ADR-0017 and ADR-0018 changes, and `CONTEXT.md`.

## Acceptance criteria

- [ ] F5, F6, F7 and F11 are each first shown failing, then fixed.
- [ ] `UiApiFailureContractTests`: every kind on a non-API path answers its real status with the page text and no `Location` header; every kind on a test-only `/api/v1/` endpoint answers `{ "error": "<code>" }`; no body contains exception text.
- [ ] A blocked address gets the blocked page (`403`, HTML) and its stylesheet (`200 text/css`); `/api/v1/` gets `403 { "error": "blocked" }`; `/Account/Recover` and the UI API recover call are still reachable.
- [ ] `/api/v1/` responses carry `Cache-Control: no-store`.
- [ ] Tests that expected `/Home/Error?code=…` now expect the page.
- [ ] `scripts/check.sh` and `scripts/check.sh integration "<filter>"` pass; affected browser specs pass with `scripts/check.sh e2e`.

## Blocked by

None.
