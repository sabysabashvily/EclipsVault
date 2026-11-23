## Parent

#SPEC

## What to build

Account activation on the server (spec Q15, Q21).

- `POST /api/ui/users` takes `email` instead of `password`; generated addresses and `Identity:EmailDomain` retired; README updated. This is the same ticket as the activation calls, the helper switch and the form.
- `POST /api/ui/auth/activation` (inspect: seed generated once, nothing spent) and `POST /api/ui/auth/activation/complete` (password under Profile's rules, authenticator enrolled, token spent, generation advanced, `AccountActivated`, one transaction). Anonymous, throttled, named in the allowlist tests; one `404 invitation_invalid`.
- The New user form replaces "Initial password" with the Email field and hint, and shows both creation outcomes. The browser suite posts `email`.
- Integration and e2e helpers that create accounts with a password switch to invitation plus activation (read the token from the test SMTP capture).

## Acceptance criteria

- [ ] F3 and F7 are first shown failing, then impossible.
- [ ] Production HTTP: the email create body and the activation shapes and codes; a wrong code or refused password keeps the invitation; expired, used and replaced tokens answer the same `404 invitation_invalid`, including the old link after ticket 1's address replacement; two concurrent activations and activation against resend leave exactly one outcome; after activation the person signs in with password and TOTP.
- [ ] Vitest: the New user form posts `email` and shows both outcomes.
- [ ] AuditFailureFixture: a failed `AccountActivated` row leaves the account invited and the token unspent.
- [ ] Every suite that created accounts with a password passes on the new helpers.
- [ ] `scripts/check.sh` and `scripts/check.sh integration "<this ticket's filter>"` pass; `npm run lint`, `npm test -- --watch=false` and `npm run check:api-types` pass. CI runs SQL Server and the full browser suite.

## Blocked by

- #T1
