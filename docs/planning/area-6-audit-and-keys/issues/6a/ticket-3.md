## Parent

#SPEC

## What to build

Verify part 6a against #SPEC and prepare its single PR. No new behaviour.

## Acceptance criteria

- [ ] No Legacy Encryption view or controller action remains. A retired-routes test pins that `/Encryption/Rotate`, in any letter case and with other HTTP methods, no longer reaches a handler.
- [ ] Every link into the page works from both shells and both palettes.
- [ ] The whole part's diff from `main` gets the `security-reviewer` agent and Standards and Spec reviews by GPT Sol 6.1 at xhigh, with a finding-by-finding ledger. Fixes made after a review get their own re-check. The security review checks that no master key, data key or signing key is shown or logged, and decoy state (a decoy re-wrapped without tripping).
- [ ] `CONTEXT.md`, ADR-0017, ADR-0028 and ADR-0029 match the final implementation.
- [ ] After the last merge of `main`, the branch builds and every required check passes on the final source, including the full browser suite and `e2e:verify-guards` in CI.
- [ ] The single 6a PR body is written with the pr skill.

## Blocked by

- #T2
