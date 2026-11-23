## Parent

#SPEC

## What to build

Require `Database:Provider` at startup, as #SPEC says:

- Remove the SQL Server fallback in `EclipsVault.Infrastructure/DependencyInjection.cs`. An unset or empty value throws `InvalidOperationException("Database:Provider is not set. Set it to 'SqlServer' or 'Postgres' (environment variable Database__Provider).")` before any database use, in every environment.
- `EclipsVault.AngularUi/e2e/compose.yml`: `Database__Provider: SqlServer` on the vault service.
- `docs/INSTALL.md`: the setting is required; add "Upgrading to 2.0". Update `README.md` and `.claude/skills/run-vault/SKILL.md`.
- Leave `EclipsVaultDbContextFactory` unchanged. Never read or edit `EclipsVault.Web/appsettings.Development.json`.

## Acceptance criteria

- [ ] A unit test shows startup configuration without the setting accepted today (failing first), then refused with exactly the message above; an empty value is refused too; `SqlServer` and `Postgres` still register.
- [ ] `scripts/check.sh` passes; CI's integration (both databases) and full browser suite pass on the PR head.
- [ ] The PR body (pr skill) has a release-note paragraph for v2.0.0 and says the PR merges only after part 9a.

## Blocked by

None to build. Merge after #SPEC9A merges.
