## Parent

#SPEC

## What to build

Activation, New user, Users status and Resend served by the Angular UI (spec Q15, Q19, Q24).

- Route table: `/Account/Activate` anonymous, with the anonymous request token and Angular CSP; the page reads the token from the fragment and never sends it in a URL.
- Activation page with the agreed texts, EnrollTotp's block and Profile's password fields; on success to `/Account/Login?activated=1`, whose sentence Login shows.
- The New user field and its two outcomes already shipped in ticket 2. This ticket adds the unavailable explanation on that form. Users list and user page: status, "Resend invitation" and invitation-address replacement, each with its ConfirmationDialog and StepUpDialog. The replacement's confirmation reads "Use *address* for *username*? The link already sent stops working. Resend to send a new one there." and sends no link itself. Overview's "Provision a user" subtitle.

## Acceptance criteria

- [ ] Vitest: every page state and dialog flow.
- [ ] `scripts/check.sh e2e <affected specs>`: create → SMTP capture → activate → sign in; resend kills the old link; replacing the address kills the old link, and resending delivers to the new one; an expired link; the unavailable state is covered by Vitest and HTTP tests; both themes; keyboard.
- [ ] `scripts/check.sh` passes; `npm run lint`, `npm test -- --watch=false` and `npm run check:api-types` pass. CI runs SQL Server and the full browser suite.

## Blocked by

- #T2
