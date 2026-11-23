## Parent

#SPEC

## What to build

Verify part 6b against #SPEC and prepare its single PR. No new behaviour.

## Acceptance criteria

- [ ] No Legacy Audit view, view model or controller action remains. A retired-routes test pins that `/Audit/Verify`, `/Audit/CreateCheckpoint` and `/Audit/Export`, in any letter case and with other HTTP methods, no longer reach a handler.
- [ ] Every link into the page works from both shells, both palettes and the Dashboard.
- [ ] The whole part's diff from `main` gets the `security-reviewer` agent and Standards and Spec reviews by GPT Sol 6.1 at xhigh, with a finding-by-finding ledger. Fixes made after a review get their own re-check. The security review checks fail-closed recording of all four actions, that Verify cannot report a truncated or rewritten trail as intact up to the latest checkpoint, that no signing key is shown or logged, bundle compatibility (v2 unchanged, the 1.0.0 note kept), and decoy state.
- [ ] `CONTEXT.md`, the README, ADR-0017, ADR-0028 and ADR-0030 match the final implementation.
- [ ] After the last merge of `main`, the branch builds and every required check passes on the final source, including the full browser suite and `e2e:verify-guards` in CI.
- [ ] The single 6b PR body is written with the pr skill.

## Blocked by

- #T4
