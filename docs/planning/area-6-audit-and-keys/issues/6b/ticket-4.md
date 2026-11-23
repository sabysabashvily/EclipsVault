## Parent

#SPEC

## What to build

The Audit log is served by the Angular UI at `/Audit` and `/Audit/Index` (route table entries under `AdminOnly`), with the UI API in #SPEC: `GET /api/ui/audit`, `POST /api/ui/audit/verify`, `POST /api/ui/audit/checkpoints` and the streamed `POST /api/ui/audit/export`.

## Acceptance criteria

- [ ] Endpoints are `AdminOnly`, session-only, `no-store`, anti-forgery-protected on POSTs, with explicit types; TypeScript regenerated. Problems: `403 step_up_required`; `409 audit_busy`, `nothing_to_checkpoint`; `503 audit_unavailable`. Entries are ordered by time, then sequence; labels come from `AuditDisplay`.
- [ ] The page matches Legacy word for word, with the deliberate changes in #SPEC: the three Verify notices, the checkpoint result in the StatusLine, Export through the StepUpDialog with the same file name, the 409 and 503 texts. Verify and Checkpoint update the page in place without writing another view row.
- [ ] `AuditController`, `Views/Audit/Index` and `AuditIndexViewModel` are removed; `AuditDisplay` stays. The authorization-surface and area tests are updated deliberately.
- [ ] `SelfServiceScopingTests` read `GET /api/ui/audit` instead of the `/Audit` HTML.
- [ ] HTTP integration tests: shapes, every code, the authorization matrix (anonymous 401, non-administrator 403 on the API and `/Account/Denied` on the page), anti-forgery and `no-store`; the decoy rule (a decoy's name appears only after it trips).
- [ ] AuditFailureFixture: all four endpoints answer 503 with nothing changed.
- [ ] Vitest: loading, error with retry, empty and list states, the filter, every Verify notice, the step-up dialog, every StatusLine outcome.
- [ ] Playwright under browser-health: view; verify anchored after creating a checkpoint; Export behind a real step-up code, checking the file name and schema; the Dashboard's links open the page; both shells, palettes, keyboard and both themes. Run locally only for this ticket's specs; the full suite runs in CI.
- [ ] Release build with 0 warnings; unit, integration, Vitest, lint, format check, build checks, `check:api-types`, build and audit pass.

## Blocked by

- #T3
