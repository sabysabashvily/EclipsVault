## Parent

#SPEC

## What to build

Verify part 5b against #SPEC and prepare its single PR. No new behaviour.

## Acceptance criteria

- [ ] No Legacy Networks, Notifications or License view, view model or controller action remains, and `AdminController` is gone. A retired-routes test pins that every removed route, in any letter case and with other HTTP methods, no longer reaches a handler.
- [ ] Every link into and out of the three pages works from both shells and both palettes, the Dashboard, the Denied page and both licence notices.
- [ ] The whole part's diff from `main` gets the `security-reviewer` agent and independent Standards and Spec reviews, by a different model from the author where possible, with a finding-by-finding ledger. The security review checks the four licensing invariants and decoy state (a block from a decoy trip; an outbox row naming a decoy).
- [ ] `CONTEXT.md`, ADR-0017 and ADR-0028 match the final implementation.
- [ ] Every required check passes on the final source (as #T2), run separately where local Docker memory needs it.
- [ ] The single 5b PR body is written with the pr skill.

## Blocked by

- #T3
