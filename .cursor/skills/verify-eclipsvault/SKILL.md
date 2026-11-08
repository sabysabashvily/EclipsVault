---
name: verify-eclipsvault
description: Launch a disposable EclipsVault web vault and drive the signed-in UI in Chromium. Use when verifying a user-visible change to sign-in, secrets, My access, sign-in history, or security checkup.
---

# Verify EclipsVault

EclipsVault's primary surface is the web UI on one host: Legacy MVC pages such as Secrets, and the Angular UI on the same origin (My access, Sign-in history, Security checkup, and the other routes in `EclipsVault.AngularUi/src/app/app.routes.ts`). The UI API at `/api/ui` is the same session, not a second app. The licence forge and audit verifier are separate CLIs; this skill does not drive them.

Read `features/README.md` before driving, then follow the feature file. Run the commands below from the repository root. The harness is `.cursor/skills/verify-eclipsvault/verify-eclipsvault`.

## Launch

Launch creates an empty PostgreSQL database named `evverify` plus 8 hex characters, migrates it, and seeds the Development demo world. It does not touch `EclipsVaultUmbraDb`. Ports `7443` and `5080` are the vault's fixed dev ports: if either is already taken, launch refuses. Two vaults cannot share them.

Docker must already be running (`docker info`). Launch starts only the compose `db` service and leaves it up.

```bash
.cursor/skills/verify-eclipsvault/verify-eclipsvault launch
```

Ready means `https://localhost:7443/Account/Login` returns the Sign in page. That page is served only after migrations and seeding finish. Launch prints the base URL, pid, and database name. It writes the administrator password, the KEK, and the audit signing key to `logs/verify/instance/env` and does not print them.

The Development host uses a throwaway KEK and a throwaway audit signing key generated for this process. The dev HTTPS certificate is created with `dotnet dev-certs https` when the machine does not have one. The Angular UI is built into `EclipsVault.Web/AngularUi/index.html` before the process starts; without that file the Angular routes cannot load.

## Doctor

Run this first whenever the vault looks wrong. It only reads. It checks that the pid still runs `EclipsVault.Web.dll`, that this pid owns port `7443`, that Sign in is being served, that the Angular document exists, and that the disposable database has both seeded accounts.

```bash
.cursor/skills/verify-eclipsvault/verify-eclipsvault doctor
```

A healthy run prints `ok vault`, the base URL, the pid, the `evverify…` database name, `accounts 2`, and `login Sign in`. Anything else means do not drive it.

## Drive

Sign in once, through the real password form and the authenticator enrollment page. The harness reads the key the page shows, submits one authenticator code, and lands on Secrets. It keeps that Chromium session open so later commands see the same page, including an opened sidebar group or the command palette.

```bash
.cursor/skills/verify-eclipsvault/verify-eclipsvault sign-in
.cursor/skills/verify-eclipsvault/verify-eclipsvault browser goto /Secrets
.cursor/skills/verify-eclipsvault/verify-eclipsvault browser click --text "Security" --exact
.cursor/skills/verify-eclipsvault/verify-eclipsvault browser click --role link --name "Sign-in history" --exact
.cursor/skills/verify-eclipsvault/verify-eclipsvault browser expect --role heading --name "Sign-in history" --exact
.cursor/skills/verify-eclipsvault/verify-eclipsvault browser fill --role combobox --name "Search secrets, or jump to a page" --value "sign-in history"
.cursor/skills/verify-eclipsvault/verify-eclipsvault browser press --key Control+k
.cursor/skills/verify-eclipsvault/verify-eclipsvault browser screenshot --path logs/verify/evidence/sign-in-history/history.png
.cursor/skills/verify-eclipsvault/verify-eclipsvault browser snapshot --path logs/verify/evidence/sign-in-history/history.aria.txt
```

`--row <text>` limits a click to the table row whose accessible name contains that text. Use it on Secrets, where every row has an Open link. The feature files list the exact sequence for each journey.

The account is `vault-admin` (Top Secret, project GLOBAL). `dev-user` is seeded and is not signed in by this harness. A journey that needs `dev-user` fails here; do not borrow the administrator session and call it the Standard one.

## Evidence

Proof lives in `logs/verify/evidence/<feature>/`. Capture the user action and the state it produced. A screenshot must show the EclipsVault brand and the page heading. An ARIA snapshot from `browser snapshot` is the accessible tree of that same page. Also record the command output (`saw …`, the URL).

For a change that writes something, prove it from a second view the user can open: the sign-in row on Sign-in history, the secret name back on the Secrets list, the clearance on My access. Do not reveal a secret value, and do not screenshot the authenticator enrollment page. The harness refuses a screenshot or snapshot of that page because it shows the authenticator key.

Mocks are not used. Mailpit, Redis, Keycloak, and HashiCorp Vault are not started; email, shared revocation, SSO, and Transit are outside this harness. Honey-token secrets are real rows. Opening `Production_AWS_Root_Key` or `Global_SQL_SA_Password` trips intrusion response and can block this machine.

## Cleanup

Cleanup stops the Chromium session and the vault pid this run started, then drops only the `evverify…` database. It does not stop PostgreSQL, and it does not delete `logs/verify/evidence`.

The browser daemon starts at launch and exits on its own after 30 minutes with no request, so a vault nobody signs in to stops too. On that idle exit it first stops the vault pid recorded in `logs/verify/instance/state.json`, and only when that process's command line contains the exact `dll` path stored there. It then closes Chromium and exits. Idle exit does not drop the database or remove `logs/verify/instance`; run cleanup afterwards so the next launch is not blocked by the leftover directory.

```bash
.cursor/skills/verify-eclipsvault/verify-eclipsvault cleanup
```

Run cleanup after a failed launch too, so the next launch is not blocked by the leftover instance directory. Never kill a vault by process name. If doctor says the pid on port `7443` is not the verification vault, leave it alone.

## Helpers

`.cursor/skills/verify-eclipsvault/verify-eclipsvault` is the only command. It launches, checks, signs in, drives the browser, and cleans up. `.cursor/skills/verify-eclipsvault/browser.mjs` is the Chromium session it starts; call it only through the shell script so `VERIFY_ROOT` is set.
