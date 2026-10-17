# EclipsVault: instructions for coding agents

Self-hosted secrets manager. ASP.NET Core MVC on .NET 10, Clean Architecture: Web → Infrastructure → Core.
Core references only the BCL: no NuGet packages, so no `ILogger` and no `IMemoryCache` there.

## Build and test

- The solution file is `EclipsVault.slnx`. There is no `.sln`.
- Restore: `dotnet restore --locked-mode`. It fails when a `packages.lock.json` is stale; regenerate the
  lock files whenever you change `Directory.Packages.props`.
- Build: `dotnet build EclipsVault.slnx -c Release`. Warnings, including analyzer findings, fail the build.
- Test (unit, no Docker): `dotnet test EclipsVault.Tests -c Release --no-build --blame-hang-timeout 5m --blame-hang-dump-type mini`
- Integration tests: `dotnet test EclipsVault.IntegrationTests -c Release --no-build --blame-hang-timeout 10m --blame-hang-dump-type mini`.
  First build the Angular UI (`npm ci --ignore-scripts` then `npm run build` in `EclipsVault.AngularUi`,
  on its `.nvmrc` Node version): header tests serve its real document through the route table.
  They need Docker running: each run starts SQL Server with Testcontainers and boots the unmodified
  `Program` against it in Production, signing in through the real sign-in. The image is pinned by
  digest in `VaultFixture`, and a machine without it downloads about 2.5 GB on the first run.
  `dotnet test EclipsVault.slnx` runs both suites, so it needs Docker too.
  The endpoints that throw each domain exception, to pin the UI API's error mapping, live in
  `EclipsVault.IntegrationTests/TestOnlyEndpoints.cs`. Only `VaultFixture` adds them to the host
  under test; never put such an endpoint in `EclipsVault.Web`.
- A model change needs a migration in both sets: SQL Server (`EclipsVault.Infrastructure`) and PostgreSQL
  (`EclipsVault.Migrations.Postgres`). CI checks both; the exact `dotnet ef` commands are in
  `.github/workflows/ci.yml`. For Postgres the startup project is `EclipsVault.Web`.
- Agent guardrail tests: `python3 -I -m unittest discover -s .claude/hooks -p 'test_*.py'`.
- SBOM: `bash .github/sbom/sbom.sh` writes `sbom/sbom.json`, which the release signs and attests to the
  image. Both generators are locked: the CycloneDX dotnet tool in `dotnet-tools.json`, the CycloneDX
  npm tool in `.github/sbom/package-lock.json` (audited by `security-scan`). Change their versions there.

## Angular UI

`EclipsVault.AngularUi` sits outside the solution (ADR-0015). Run these in that folder, on the Node
version in its `.nvmrc`.

- Install: `npm ci --ignore-scripts`. Keep that flag on installs only: setting it in `.npmrc` would
  also skip the pre/post-build gates.
- Lint: `npm run lint`.
- After changing UI API response types or routes, run `npm run generate:api-types` and commit
  `src/app/ui-api/schema.generated.ts`. `npm run check:api-types` regenerates in memory and fails
  if that file differs; the `frontend` CI job runs it. Both commands build the .NET metadata-only
  generator with locked restore; they need the .NET SDK, but no database, KEK or other secret.
  A solution build also writes the document to `EclipsVault.UiApiDocument/obj/ui-api.openapi.json`.
  In Development, a signed-in session can fetch `/openapi/ui.json`; other environments return 404.
- Test: `npm test -- --watch=false` (Vitest; in a terminal it otherwise watches and never exits), and
  `npm run test:build-checks` for the build's own checks.
- Build: `npm run build`. Bundles go to `EclipsVault.Web/wwwroot/ui/`, the index to
  `EclipsVault.Web/AngularUi/index.html`. The build fails on a runtime package outside `@angular/*`,
  `rxjs` and `tslib` (ADR-0003; another one needs a new ADR), and on an inline `<script>`, an `on…=`
  handler, a `<style>` or a `style=` in the index, which the CSP would block.
- Dev server: `npm start`, on http://localhost:4200, proxying `/api` to the vault on https://localhost:7443.
- `npm audit --audit-level=high` fails CI on a high or critical finding in any package, dev or runtime.
  Fix a vulnerable package by upgrading it, or by pinning a patched version in `package.json`
  `overrides`. The finding itself stays visible: never suppress it or lower the audit level.

## Browser end-to-end tests

Run in `EclipsVault.AngularUi` on the Node version in `.nvmrc`. Needs Docker Engine with Compose,
Linux containers (SQL Server uses `linux/amd64`, including emulation on Apple Silicon), and Chromium:

```sh
npm ci --ignore-scripts
npx playwright install --with-deps chromium
export Seed__AdminPassword="E2e-$(openssl rand -hex 24)"
export ECLIPSVAULT_KEK="$(openssl rand -base64 32)"
npm run e2e
npm run e2e:verify-guards
unset Seed__AdminPassword ECLIPSVAULT_KEK
```

`e2e` builds the root production Dockerfile, starts a fresh SQL Server and a TLS proxy on a random
loopback port, bootstraps `vault-admin` through the normal Production startup, enrolls MFA through
the browser, then signs in with a real TOTP code. It removes its own containers and image on exit.
The first run downloads SQL Server (~2.5 GB) and the build images. `E2E_IMAGE=<local-tag>` skips the
build to test an already-built production image, as CI does.
Specs get `E2E_COMPOSE_PROJECT`, naming the run's own disposable stack, for state no production screen
can create: `e2e/palette.ts` marks a honey-token decoy in that run's database this way.
The runner always picks that name itself, with a random suffix; `E2E_PROJECT_PREFIX` sets only the prefix.
An image you tag for E2E_IMAGE reuse is yours to remove: delete it with docker rmi when the ticket is
done. Never remove images or containers you did not create.
The suite uses the proxy's disposable
certificate with `ignoreHTTPSErrors`; production CSP, Secure cookies and authentication stay active.
Always import `test` from `e2e/fixtures.ts` in browser specs: its automatic fixture fails every test
on CSP violations, console errors or uncaught browser exceptions, including other pages in its context.
`e2e:verify-guards` intentionally runs eight failing Playwright probes against another fresh vault;
it succeeds only when the shared fixture reports the corresponding failures. A test that causes a
console error on purpose (a 401 the browser logs) declares it with `browserHealth.expectConsoleError`;
the error must then occur, and nothing else is excused. A second browser context comes from
`browserHealth.newContext()`, which holds it to the same checks.
The Trusted Types sink test declares its single deliberate violation with
`browserHealth.expectCspViolation`, matching directive, sink, sample, document URI and disposition exactly;
a missing or extra violation fails.

Failures retain traces/screenshots in `test-results/` and an HTML report in `playwright-report/`;
open with `npx playwright show-report`. Guard evidence is in `guard-results/`. Both runs also save
`docker.log`. These ignored artifacts can contain disposable passwords, MFA enrollment material and
session cookies; use only throwaway test credentials. CI uploads them on failure with short retention.

## Known traps

- Errors in files you did not touch (RZ1021, `CS0246` naming `summary` or `button`): run
  `dotnet build-server shutdown` and rebuild before debugging code. Deleting `obj/` does not help.
- A test run that stops with "The specified inactivity time of 5 minutes has elapsed" (10 minutes for the
  integration tests) is a hang.
  The long-standing `AuditGroupCommitterTests` hang was fixed in #37, so a hang now is a regression
  to investigate, not a known flake. Test output is buffered: the last test printed is not the one
  that hung. The aborted run's blame `Sequence_*.xml` names the test still in flight.
- A newly published advisory can turn CI red with no code change. Against a direct dependency it
  fails restore and build (`NU1902`/`NU1903` are errors here). Against a transitive one, restore and
  build can still pass and only CI's `security-scan` job fails; reproduce it with
  `dotnet list package --vulnerable --include-transitive`. Either way that is the gate working:
  upgrade the package, or pin the transitive one in `Directory.Packages.props` (transitive pinning
  is on), rather than suppress the warning. Microsoft's monthly security release often triggers this.
- `dotnet test --no-build` runs whatever binaries are already in `bin/`, possibly from another
  branch. Build first, or the result proves nothing.
- Never report CI as green unless you have seen the check results yourself (`gh pr checks <n>`).

## Architecture (pinned by `EclipsVault.Tests/Architecture/LayeringTests.cs`)

- Interface in `Core/Application/<Feature>/`: the implementation lives in Core.
  Interface in `Core/Application/Abstractions/`: the implementation lives in Infrastructure.
- Web may name Infrastructure only in `Program.cs`. A controller never takes an infrastructure options
  type; add a narrow read-only port in `Core/Application/Abstractions` instead (see `ISsoAvailability`).
- One enum per file.

## Conventions

- Never construct an `AuditLog` by hand. Standalone events go through `IAuditSink.WriteAsync`
  (fail-closed). Events that must be atomic with a data change go through `AuditSaveChangesInterceptor`:
  add a `Describe<Entity>Change` rule.
- A startup audit write runs from an `IHostedService` registered after `AuditGroupCommitter`. Written
  inline before `app.Run()` it waits forever.
- Read claims through `ClaimsPrincipalExtensions` or the `VaultController` helpers, never by hand with
  `FindFirstValue`.
- IP and CIDR logic lives in `NetworkRules`. Check it before writing any parsing.
- For a marker-only write to an audited entity, set `Entry(x).Property(...).IsModified = true`.
  `DbSet.Update` marks every column modified, so the change gets audited as a real edit.

## Security invariants: never weaken these

- Auditing is fail-closed: if the audit row cannot be written, the action does not happen.
- A licence problem never stops the vault serving secrets, and licensing lookups fail open.
- A signed perpetual licence never expires into Community on date grounds.
- Viewing a page never starts a capability's evaluation window.
- Dynamic secrets never store the minted password.
- `EclipsVault.Web/appsettings.Development.json` holds the dev KEK and seed passwords and is never
  committed. CI fails if it is.
- Never read, print, or paste a private key. Pass key files to tools by path only.

## Working rules

- Do not merge pull requests, push tags, force-push, delete branches, or rewrite history unless the
  owner asks for that specific action. Before any branch deletion, prove the branch is contained in
  `main` with `git merge-base --is-ancestor`.
- In commercial and legal documents (`LICENSE`, `docs/COMMERCIAL_TERMS.md`, `docs/PRIVACY.md`, pricing),
  never fill in or change business values: prices, dates, legal-entity status, email addresses,
  response times. List what is missing and ask.
- `docs/internal/` is private and gitignored. Never copy its contents into a tracked file.
- Commit messages are one sentence describing the change in behaviour, for example
  "Refuse a mistyped mint flag instead of minting a perpetual licence".
