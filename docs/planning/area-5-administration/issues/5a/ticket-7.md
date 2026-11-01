## Parent

#SPEC

## What to build

Verify part 5a against #SPEC and prepare its single PR. No new behaviour.

## Acceptance criteria

- [ ] No Legacy Users or Service accounts view, view model or controller action remains; Networks stays for 5b. A retired-routes test pins that every removed route, in any letter case and with other HTTP methods, no longer reaches a handler.
- [ ] Every link into and out of the pages works from both shells and both palettes: sidebars, the Dashboard's Users card and New user action, Users ↔ New user ↔ Edit user, Service accounts ↔ Create ↔ Details, avatars, My activity's administration entries.
- [ ] The whole part's diff from `main` gets the `security-reviewer` agent and independent Standards and Spec reviews (code-review skill), by a different model from the author where possible (GPT Sol 6.1 at xhigh when the author is Claude), with a finding-by-finding resolution ledger. The review includes an authorization matrix over every endpoint: anonymous, non-administrator, administrator with a stale and a fresh sign-in, and own account.
- [ ] `CONTEXT.md`, ADR-0017, ADR-0023 and ADR-0028 match the final implementation; #92's acceptance criteria are checked off with evidence.
- [ ] Every required check passes on the final source, run separately where local Docker memory needs it: locked restore, Release build with 0 warnings, format, unit, integration, both pending-model checks, agent guardrails, and in the Angular workspace lint, format check, Vitest, build checks, `check:api-types`, build, audit, `e2e` and `e2e:verify-guards`.
- [ ] The single 5a PR body is written with the pr skill.

## Blocked by

- #T6
