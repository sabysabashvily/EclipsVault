## Parent

#SPEC

## What to build

Verify part 9a against #SPEC and prepare its single PR. No new behaviour.

## Acceptance criteria

- [ ] No Razor view, Legacy controller action, `site.js` or `site.css` remains. A retired-routes test pins that `/Home/Error`, `/Home/Index` and `POST /Account/Logout`, in any letter case, no longer reach a handler.
- [ ] Every sidebar and palette link, sign-in, sign-out, SSO, the blocked page and break-glass recovery work end to end (`verify-eclipsvault`).
- [ ] The whole part's diff from `main` gets the `security-reviewer` agent and Standards and Spec reviews by GPT Sol 6.1 at xhigh, with a finding-by-finding ledger; fixes get their own re-check. The security review checks the anonymous surface, antiforgery on every unsafe call, that no error body carries exception details, and that a blocked network reaches only what ADR-0032 allows.
- [ ] `CONTEXT.md`, ADR-0014, ADR-0017, ADR-0018 and ADR-0034 match the final implementation.
- [ ] After the last merge of `main`, the branch builds and every required check passes on the final source, including the full browser suite and `e2e:verify-guards` in CI.
- [ ] The single 9a PR body is written with the pr skill.

## Blocked by

- #T2
