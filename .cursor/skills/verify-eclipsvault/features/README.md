# EclipsVault verification map

This directory is the maintained source for verifying the user-facing web UI. Read this index, then use the matching feature file.

## Baseline preconditions

- Launch with `.cursor/skills/verify-eclipsvault/verify-eclipsvault launch` so the vault is at `https://localhost:7443` on a new `evverify…` database.
- Run `verify-eclipsvault doctor` and require `ok vault`, that base URL, and `accounts 2`.
- Sign in once with `verify-eclipsvault sign-in`. The session is `vault-admin`.
- Never drive a vault this run did not start. Ports `7443` and `5080` cannot host a second one.
- A Development vault with no licence file shows "Running unlicensed". That notice is expected.

## Driving conventions

- Start from the signed-in Secrets page unless the feature says otherwise.
- Prefer ARIA roles and accessible names. Pass `--exact` when the feature file does.
- Keep one browser session. A later command sees the sidebar group or palette the previous command opened.
- Treat every command as literal.
- Restore nothing in the shared developer database: this run has its own database, dropped on cleanup.
- Leave files in `logs/verify/evidence/` in place when cleaning up.

## Proof and skip reporting

- Capture the action and the resulting state, not only the final screen.
- UI proof is a screenshot plus an ARIA snapshot under `logs/verify/evidence/<feature>/`.
- The screenshot shows the EclipsVault brand and the page heading.
- A mutation is confirmed from a second user-facing view, not from a direct database edit.
- Record which entry point produced the artifact.
- An entry point that was not driven is not verified. Say which command failed and which precondition was missing.

## Feature entry contract

Each feature file has an H1, one opening paragraph, then four H2 sections: `Sub-features`, `How to get to it (user POV)`, `Driving it with verify-eclipsvault`, and `Gotchas`.

## Features

- [Sign in](./sign-in.md) covers the password step and mandatory authenticator enrollment.
- [Secrets](./secrets.md) covers the seeded secret list and opening one non-decoy secret without revealing it.
- [My access](./my-access.md) covers the administrator's clearance and project.
- [Sign-in history](./sign-in-history.md) covers the sidebar, the bookmark, and the command palette.
- [Security checkup](./security-checkup.md) covers the account's two-step sign-in posture.
