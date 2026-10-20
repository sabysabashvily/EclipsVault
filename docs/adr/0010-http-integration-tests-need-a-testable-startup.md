# Test the UI API over HTTP, against the app's real startup

_Originally titled "…which means making Program.cs testable". See the amendment at the end._

The UI API's guarantees can only be proven at the HTTP boundary:
- a session is refused on the Public API, and an API key on the UI API,
- an unsafe request without an antiforgery token is rejected,
- an audit failure becomes a 503 Problem Details response.

So a new integration test project hosts the app with `WebApplicationFactory`, against a real SQL Server started per run with Testcontainers.

This reverses a July 2026 decision. `WebApplicationFactory` does not currently work against this `Program.cs`: the app boots fully, then the test host is disposed before it can be used. That attempt was abandoned because finishing it needed a change to startup code. With a whole new API surface to protect, the change is now worth making: the composition and the pipeline move into methods a test host can call. Because this touches startup in a secrets vault, the `security-reviewer` agent reviews it. A real database replaces the SQLite workarounds the earlier attempt needed (SQLite cannot order by `DateTimeOffset`, which the audit chain does).

## Amended while implementing it (#43)

The restructure turned out to be unnecessary, so `Program.cs` was left unchanged. `WebApplicationFactory<Program>` boots the unmodified entry point once the vault is configured the way a container deployment configures it: through environment variables (connection string, KEK, audit signing key, key-ring path, bootstrap password, `Database__MigrateOnStartup`), in Production, against a real SQL Server. The July failure was not reproduced, and its cause was never found. The likeliest difference is what that attempt replaced to avoid a database: SQLite, a migrations assembly with nothing pending, a no-op chain locker and the removal of the hosted services.

This is stronger than the planned restructure. The test host runs the real entry point, so every test also runs the real startup tasks in their real order, and production startup code carries no seam that exists only for tests. Configuration has to reach the app as environment variables, because the composition reads parts of it while registering services, before any test-host override applies. The integration project is `EclipsVault.IntegrationTests`.

## Testing an unavailable audit store

`AuditFailureFixture` owns a second `VaultFixture`, with its own SQL Server and the same unmodified
`Program` in Production. Startup and test setup, including real sign-ins, finish with auditing
available. `RejectAuditWritesAsync` then installs a SQL Server check constraint that rejects every
new audit row without checking existing rows. Both interceptor-injected rows and standalone group
commits reach this constraint, so failures exercise real persistence and transaction rollback.
Each test removes the constraint in `finally` with `RestoreAuditWritesAsync`.

Failure mode begins after startup because bootstrap and hosted-service audit writes need a working
store. Keeping that setup intact also preserves the committer's startup order and avoids waiting
for an audit writer that has not started. No production service or authentication path is replaced.
The collection runs alone because both hosts use process-wide environment variables. Its separate
database keeps audit failures out of ordinary HTTP tests; the extra database and host make the full
integration suite slower.
