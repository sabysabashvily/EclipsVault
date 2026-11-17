## Parent

#SPEC

## What to build

Verify part 7b against #SPEC and prepare its single PR. No new behaviour.

## Acceptance criteria

- [ ] No Legacy Secrets, Details, Denied or filing handler remains; `/Secrets/*` and `/Account/Denied` are served by Angular.
- [ ] Every link into and out of Details and Denied works from both shells and both palettes, Shared with me's "Open" and the Overview's "Rotate" included.
- [ ] The decoy matrix is checked across the whole part (lesson 11): a secret made a decoy after it has versions, shares and a pending access request trips on every by-id endpoint, eligibility and filing, and appears in no list.
- [ ] The whole part's diff from `main` gets the `security-reviewer` agent and Standards and Spec reviews by GPT Sol 6.1 at xhigh, with a finding-by-finding ledger. Fixes made after a review get their own re-check. The security review checks fail-closed recording, the change right, step-up, the conditional write and that no revealed value is cached or logged.
- [ ] `CONTEXT.md`, ADR-0017, ADR-0028 and ADR-0031 match the final implementation.
- [ ] After the last merge of `main`, `scripts/check.sh`, `scripts/check.sh integration "<the part's filter>"` and `scripts/check.sh e2e <the part's specs>` pass on the final source, and every required check passes in CI on the exact head, including SQL Server, the full browser suite and `e2e:verify-guards`.
- [ ] The single 7b PR body is written with the pr skill; the PR gets GPT Sol 6.1 and Thermos before the owner merges.

## Blocked by

- #T4
