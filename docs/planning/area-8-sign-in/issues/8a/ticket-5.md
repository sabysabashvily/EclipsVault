## Parent

#SPEC

## What to build

Verify part 8a against #SPEC and prepare its single PR. No new behaviour.

## Acceptance criteria

- [ ] No Legacy sign-in view or action remains except `ExternalLogin`, `ExternalCallback` and `Logout`.
- [ ] Every link into sign-in works: the cookie's login path, Angular's 401 handler including `StaleSessionSignOut`, the step-up dialog's "Sign in again", the account menu, the landing page, the access-blocked page, Profile's three sign-out-everywhere codes, the authenticator-reset sentence, and local sign-out's `sign_out_incomplete`. No Angular caller still posts `/Account/Logout`.
- [ ] If 7b or 7c merged first: `main` merged, API types regenerated, then build and tests pass (lesson 16).
- [ ] If #T4 could not be done by Cursor, it is done here.
- [ ] The whole part's diff from `main` gets the `security-reviewer` agent and Standards and Spec reviews by GPT Sol 6.1 at xhigh, with a finding-by-finding ledger; fixes get their own re-check. The security review checks the anonymous and pending surface, the IP-block exemption, enumeration parity, return URLs, CSRF before a session, #127's ordering and the pending sign-in's binding.
- [ ] `CONTEXT.md`, ADR-0016, ADR-0017 and ADR-0032 match the final implementation.
- [ ] After the last merge of `main`, `scripts/check.sh`, `scripts/check.sh integration "<the part's filter>"` and `scripts/check.sh e2e <the part's specs>` pass on the final source, and every required check passes in CI on the exact head, including SQL Server, the full browser suite and `e2e:verify-guards`.
- [ ] The single 8a PR body is written with the pr skill; the PR gets GPT Sol 6.1 and Thermos before the owner merges.

## Blocked by

- #T3
- #T4
