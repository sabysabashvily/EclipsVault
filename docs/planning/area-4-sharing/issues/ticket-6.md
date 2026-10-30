## Parent

#SPEC

## What to build

Verify the whole Area against #SPEC and prepare its single PR. No new behaviour.

## Acceptance criteria

- [ ] No Legacy Area 4 view, view model or controller action remains except `POST /AccessRequests/Create`. A retired-routes test pins that every removed route, in any letter case and with other HTTP methods, no longer reaches a handler.
- [ ] Every link into and out of both pages works from both shells and both palettes: Shared by me ↔ Shared with me, Denied → Access requests, Open → Legacy Details, empty states → Legacy Secrets, My activity's sharing entries.
- [ ] The whole Area diff from `main` gets the `security-reviewer` agent and independent Standards and Spec reviews (code-review skill), by a different model from the author where possible, with a finding-by-finding resolution ledger.
- [ ] `CONTEXT.md`, ADR-0017 and ADR-0027 match the final implementation.
- [ ] Every required check passes on the final source, run separately where local Docker memory needs it: locked restore, Release build with 0 warnings, format, unit, integration, both pending-model checks, agent guardrails, and in the Angular workspace lint, format check, Vitest, build checks, `check:api-types`, build, audit, `e2e` and `e2e:verify-guards`.
- [ ] The single Area 4 PR body is written with the pr skill.

## Blocked by

- #T5
