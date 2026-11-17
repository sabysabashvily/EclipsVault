## Parent

#SPEC

## What to build

The Angular UI serves Dynamic secrets (spec Q2, Q16, Q17):

- `GET /api/ui/dynamic-secrets`, `POST /api/ui/dynamic-secrets/issue` and `POST /api/ui/dynamic-secrets/leases/{leaseId}/revoke` with the spec's problems.
- The page at `/DynamicSecrets` copied word for word from `Views/DynamicSecrets/Index`, with StepUpDialog on issue, ConfirmationDialog and StatusLine with the Legacy texts, the refused-revoke sentence, and the issued credential only in page memory.
- `DynamicSecretsController`, its view and view models go.

## Acceptance criteria

- [ ] Production HTTP: shapes and every code; the role filter and re-check; holder versus administrator revoke and the single `lease_not_found`; anti-forgery and `no-store`; the password in no audit row or log.
- [ ] A retired-routes test pins that `/DynamicSecrets/Issue` and `/DynamicSecrets/Revoke` no longer reach a handler.
- [ ] Vitest: every state and dialog flow, including the credential cleared on departure.
- [ ] Playwright under browser-health, against the run's own SQL Server with ticket 1's two roles (Internal and Secret): viewing the page starts no evaluation window; the saved administrator session, whose proof is stale by then (the `networks.spec.ts` pattern), issuing the Secret role opens StepUpDialog, and a cancelled dialog starts no window and issues nothing; a real TOTP code then issues the credential, starts the 30-day evaluation, and shows username and password once, gone after reload and departure; the login exists inside the `sql` container (password never on host argv); revoke removes it; the Internal role issues without a dialog; keyboard; both themes.
- [ ] `scripts/check.sh` passes (locked restore, Release build with 0 warnings, unit tests); `scripts/check.sh integration "<this ticket's filter>"` passes on PostgreSQL; `scripts/check.sh e2e <affected specs>` passes; `dotnet format --verify-no-changes` is clean. CI runs SQL Server and the full browser suite.

## Blocked by

- #T1
