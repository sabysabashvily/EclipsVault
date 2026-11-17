## Parent

#SPEC

## What to build

A browser test through a real identity provider (spec Q25). Test infrastructure only: no change to application code. Suitable for a Cursor cloud agent, started from 8a's pushed HEAD after #T2, on its own branch with a PR into `area-8a-sign-in`.

- Add Keycloak to the e2e stack, pinned by digest, importing a test realm from source (as `docker-compose.yml` does with `keycloak/eclipsvault-realm.json`), and configure the vault's SSO against it for the run.
- One spec, importing `test` from `e2e/fixtures.ts`: an IdP user whose sign-in Keycloak completed with MFA lands on `/Secrets`; one without IdP MFA lands on the Angular Totp page and finishes with a real TOTP code; an IdP user with no vault account sees "Single sign-on did not grant access to this vault. Contact an administrator."

## Acceptance criteria

- [ ] `scripts/check.sh e2e <the new spec>` passes locally; the stack removes its own containers on exit.
- [ ] The spec passes in CI's `e2e` job and `e2e:verify-guards` still passes.
- [ ] No file under `EclipsVault.Web`, `EclipsVault.Core` or `EclipsVault.Infrastructure` changes. If the spec shows an application defect, the agent stops and reports it; the fix returns to Claude.

## Blocked by

- #T2
