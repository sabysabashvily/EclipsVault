## Parent

#SPEC

## What to build

Verify part 7c against #SPEC and prepare its single PR. No new behaviour.

## Acceptance criteria

- [ ] No Legacy Dynamic secrets handler remains.
- [ ] Every link into the page works from both shells and both palettes.
- [ ] If 7b merged first: `main` merged, API types regenerated, then build and tests pass (lesson 16).
- [ ] The whole part's diff from `main` gets the `security-reviewer` agent and Standards and Spec reviews by GPT Sol 6.1 at xhigh, with a finding-by-finding ledger. Fixes made after a review get their own re-check. The security review checks that the password is never stored, logged or cached, step-up on issue, the licence invariants, and the failure paths.
- [ ] `CONTEXT.md`, ADR-0017, ADR-0028 and ADR-0031 match the final implementation.
- [ ] After the last merge of `main`, `scripts/check.sh`, `scripts/check.sh integration "<the part's filter>"` and `scripts/check.sh e2e <the part's specs>` pass on the final source, and every required check passes in CI on the exact head, including SQL Server, the full browser suite and `e2e:verify-guards`.
- [ ] The single 7c PR body is written with the pr skill; the PR gets GPT Sol 6.1 and Thermos before the owner merges.

## Blocked by

- #T2
