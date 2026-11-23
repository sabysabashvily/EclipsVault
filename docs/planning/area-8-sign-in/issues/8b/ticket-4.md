## Parent

#SPEC

## What to build

Verify part 8b against #SPEC and prepare its single PR. No new behaviour.

## Acceptance criteria

- [ ] No path lets an administrator choose or see a new account's credential; #117's acceptance criteria are met and the PR closes it.
- [ ] If anything merged into `main` meanwhile: `main` merged, API types regenerated, then build and tests pass (lesson 16).
- [ ] The whole part's diff from `main` gets the `security-reviewer` agent and Standards and Spec reviews by GPT Sol 6.1 at xhigh, with a finding-by-finding ledger; fixes get their own re-check. The security review checks token entropy, storage and single use, the link's host, unrecorded delivery, refusal by every route, the last-administrator rule and the data migration.
- [ ] `CONTEXT.md`, README, ADR-0017 and ADR-0033 match the final implementation.
- [ ] After the last merge of `main`, `scripts/check.sh`, `scripts/check.sh integration "<the part's filter>"` and `scripts/check.sh e2e <the part's specs>` pass on the final source, and every required check passes in CI on the exact head, including SQL Server, the full browser suite and `e2e:verify-guards`.
- [ ] The single 8b PR body is written with the pr skill; the PR gets GPT Sol 6.1 and Thermos before the owner merges.

## Blocked by

- #T3
