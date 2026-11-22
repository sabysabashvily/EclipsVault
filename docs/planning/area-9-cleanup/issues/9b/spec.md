This spec is **Area 9 part 9b, Database provider required** (ADR-0017 amendment). It closes #68. It may be built from `main` at any time; its PR merges only after part 9a (#SPEC9A), so that nothing on `main` breaks a v1.x upgrade before the cleanup does. The owner accepted all 19 grill decisions and both delivery outlines, and confirmed them on 2026-11-11.

## Problem Statement

v1.x falls back to SQL Server when `Database:Provider` is unset (`EclipsVault.Infrastructure/DependencyInjection.cs`). The owner decided on 2026-10-18 (#68) that v2.0 has no default: flipping the default to PostgreSQL would make an upgraded SQL Server install read its connection string as PostgreSQL and fail, so v2.0 refuses to start until the setting is chosen.

The survey found launchers that rely on the default (F9): the browser suite's `EclipsVault.AngularUi/e2e/compose.yml` sets no `Database__Provider`, and `docs/INSTALL.md`, `README.md` and `.claude/skills/run-vault/SKILL.md` say an unset provider means SQL Server.

## Solution

At startup, an unset or empty `Database:Provider` stops the vault with exit code 1 and the message "Database:Provider is not set. Set it to 'SqlServer' or 'Postgres' (environment variable Database__Provider)." in every environment, Development included. An unrecognised value already fails and keeps its message. The design-time factory (`EclipsVaultDbContextFactory`, `ECLIPSVAULT_DESIGN_PROVIDER`) is unchanged.

## User Stories

1. As an operator upgrading to v2.0 without the setting, the vault refuses to start and the log names the setting and its two values.
2. As an operator installing v2.0, `docs/INSTALL.md` tells me to set it and explains the upgrade.
3. As a developer, the browser suite, the integration suites and the documented local run still start.

## Implementation Decisions

- Remove the fallback in `AddEclipsVaultInfrastructure`; throw with the message above before any database use, so `Program`'s existing handler logs it and exits 1.
- `e2e/compose.yml` sets `Database__Provider: SqlServer` for the vault service.
- `docs/INSTALL.md`: the `Database__Provider` row says it is required; a new "Upgrading to 2.0" section says to set it to the engine already in use. `README.md` and the `run-vault` skill drop "unset means SQL Server".
- The PR body carries a release-note paragraph for the owner's v2.0.0 tag message. Cutting the release stays with the owner.
- The owner adds `"Database": { "Provider": "…" }` to their own untracked `appsettings.Development.json`; this part never reads or edits that file.

## Testing Decisions

- Unit: configuration without the setting (and with an empty value) throws with exactly that message; both known values still register.
- The integration fixtures already set the provider; the full browser suite in CI proves the compose change.
- `scripts/check.sh` locally; CI runs both database suites and the browser suite.

## Delivery

One ticket on `cursor/area-9b-database-provider` from `main`, by an owner-started Cursor cloud agent (ordinary startup configuration, no protected code). If Cursor cannot take it, Claude Opus 5.5 (high) does it on `area-9b-database-provider`. Reviews: GPT Sol 6.1 (xhigh), Thermos, CI on the exact head, owner merge after 9a merges.

## Out of Scope

- The design-time factory's selection.
- The reference deployment (#69).
- Cutting v2.0.0.

## Records

`docs/planning/area-9-cleanup/`; ADR-0017 amended.
