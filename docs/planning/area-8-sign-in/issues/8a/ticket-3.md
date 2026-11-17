## Parent

#SPEC

## What to build

Break-glass recovery served by the Angular UI at `/Account/Recover` (spec Q2, Q4, Q10, Q12, ADR-0016, ADR-0032).

- In the same change that deletes the Legacy Recover POST, the IP-block exemption narrows to the `/Account/Recover` document and `POST /api/ui/auth/recover`. Until that deletion, ticket 1 has left the path exemption in place so the Legacy form still posts. The page carries its own anonymous token and makes no other call. `/local-sign-out` and static files still answer from a blocked address and still grant no account authority.
- Exact copy of the page; the outcome ("The intrusion-defence block on your network was lifted and your session restored." / "Access restored. Your network was not blocked.") shows on the page with "Continue to the vault" to `/`.
- Ticket 1 owns the fail-closed block-lift correction. This page handles `recovery_incomplete` without claiming access restored; the already-recorded session is not falsely described as absent. No additional auth or block exemption is introduced.
- Delete the Recover view, its model and the Legacy actions. The access-blocked page keeps linking `/Account/Recover`.

## Acceptance criteria

- [ ] Production HTTP: from a blocked address, the auth surfaces that answer are the Recover document and the recover call. `/local-sign-out` still answers and grants nothing. #127's rows and order (`LoginSucceeded`, `BreakGlassRecovery`, `IpRangeUnblocked`) and a refused issuance leaving the block in place still pass.
- [ ] Vitest: the page and its outcomes, including `recovery_incomplete` and audit rejection without a false access-restored message.
- [ ] `scripts/check.sh e2e <the new spec>`: the run's own client is blocked through `E2E_COMPOSE_PROJECT`, sees the access-blocked page, recovers with password and a real TOTP code, and continues into the vault.
- [ ] `scripts/check.sh` and `scripts/check.sh integration "<this ticket's filter>"` pass. CI runs SQL Server and the full browser suite.

## Blocked by

- #T2
