---
name: run-vault
description: Run EclipsVault locally and sign in to it. Start the vault on 7443, get a real session for curl or the browser preview, make a throwaway account. Use when verifying a change in the running app.
---

The local PostgreSQL credentials are public in `docker-compose.yml`. The rest lives in
`EclipsVault.Web/appsettings.Development.json` (never committed): `vault-admin`'s password is
`Seed:AdminPassword`, `dev-user`'s is `Seed:DevPassword`, and a machine on SQL Server has its `sa`
password in `ConnectionStrings:DefaultConnection`. Pass them through environment variables;
never print them or write them into a file.

## 1. Database

`docker compose up -d --wait` starts PostgreSQL as `eclipsvault-postgres`, with user `eclipsvault`,
password `dev-postgres-password` and database `EclipsVaultUmbraDb`. Start Docker Desktop first if
needed (`open -a Docker`). Ready means `psql` answers `SELECT 1`: an open port is not enough.

```bash
docker exec eclipsvault-postgres pg_isready -h 127.0.0.1 -U eclipsvault -d EclipsVaultUmbraDb
PGPASSWORD=dev-postgres-password docker exec -e PGPASSWORD eclipsvault-postgres psql -h 127.0.0.1 -U eclipsvault -d EclipsVaultUmbraDb -c 'SELECT 1'
```

A machine may instead run its own container (check `docker ps`), or SQL Server. These variables tell
`signin.py` (step 4) where the accounts are:

| Variable | Default | Meaning |
|---|---|---|
| `EV_DB_ENGINE` | `postgres` | `postgres`, or `sqlserver` for a SQL Server database |
| `EV_PG_CONTAINER` | `eclipsvault-postgres` | the PostgreSQL container |
| `EV_PG_USER` | `eclipsvault` | its user |
| `EV_PG_PASSWORD` | `dev-postgres-password` | its password |
| `EV_SQL_CONTAINER` | `eclipsvault-db` | the SQL Server container (README.md's command names it `eclipsvault-sqlserver`) |
| `EV_SQL_PASSWORD` | none | its `sa` password |

The compose container trusts connections from inside itself, so `EV_PG_PASSWORD` matters only for a
container that asks for one.

On SQL Server, ready means `sqlcmd` answers `SELECT 1`. It asks `master`, because the vault creates
`EclipsVaultUmbraDb` on its first start:

```bash
docker exec <container> /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$EV_SQL_PASSWORD" -C -Q "SELECT 1"
```

## 2. Build, then start

Build with `dotnet build EclipsVault.slnx -c Release`. Any path the Angular UI owns also needs
`npm run build` in `EclipsVault.AngularUi`, or it answers with the error page.

Start the built dll from `EclipsVault.Web` (the content root), self-limiting so it cannot outlive the session:

```bash
cd EclipsVault.Web && (ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="https://localhost:7443;http://localhost:5080" \
  perl -e 'alarm shift; exec @ARGV' 3600 dotnet bin/Release/net10.0/EclipsVault.Web.dll > /tmp/vault.log 2>&1 &)
```

macOS has no `timeout`, hence `perl`. Running the dll rather than `dotnet run` matters: `dotnet run`
starts the app as a child process that survives when the parent is killed. Ready when
`curl -sk -o /dev/null https://localhost:7443/` succeeds; stop with `pkill -f EclipsVault.Web.dll`.

The vault finds its database in `appsettings.Development.json`. That file must set
`Database:Provider` to `Postgres` or `SqlServer`; the template uses `Postgres` for the compose
database. The vault refuses to start when the setting is missing. To override it, add
`Database__Provider=Postgres` and
`ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=EclipsVaultUmbraDb;Username=eclipsvault;Password=dev-postgres-password"`
to the start command's variables.

## 3. An account

`vault-admin` (Top Secret clearance, project GLOBAL) and `dev-user` (Standard clearance, project
PHOENIX) are seeded. For any other clearance, clone `dev-user` once it has signed in (step 4), so
the clone shares its password and authenticator secret. Clearance is 1 (Standard) to 4 (Top Secret),
and `NormalizedEmail` is the email in upper case, the form the vault compares.

PostgreSQL, with the SQL on standard input because it holds both kinds of quote:

```bash
PGPASSWORD=dev-postgres-password docker exec -i -e PGPASSWORD eclipsvault-postgres psql -h 127.0.0.1 -U eclipsvault -d EclipsVaultUmbraDb -v ON_ERROR_STOP=1 <<'SQL'
INSERT INTO "Users" ("Id","Username","Email","NormalizedEmail","PasswordHash","PasswordSalt","TotpSecret","TotpEnabled","Clearance","ProjectKey","CreatedAtUtc","DisplayName","IsDisabled","AvatarUpdatedAtUtc","FailedAccessCount","LockedUntilUtc","LastTotpStep","ConcurrencyStamp","CredentialGeneration")
SELECT gen_random_uuid(),'agent-admin','agent-admin@example.invalid','AGENT-ADMIN@EXAMPLE.INVALID',"PasswordHash","PasswordSalt","TotpSecret",true,4,'GLOBAL',now(),'Agent Admin',false,NULL,0,NULL,NULL,gen_random_uuid(),0 FROM "Users" WHERE "Username"='dev-user';
SQL
```

SQL Server, where `-I` turns on quoted identifiers; without it the delete below fails on the vault's
filtered indexes:

```bash
docker exec <container> /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$EV_SQL_PASSWORD" -C -I -d EclipsVaultUmbraDb -Q "INSERT INTO Users (Id,Username,Email,NormalizedEmail,PasswordHash,PasswordSalt,TotpSecret,TotpEnabled,Clearance,ProjectKey,CreatedAtUtc,DisplayName,IsDisabled,AvatarUpdatedAtUtc,FailedAccessCount,LockedUntilUtc,LastTotpStep,ConcurrencyStamp,CredentialGeneration)
SELECT NEWID(),'agent-admin','agent-admin@example.invalid','AGENT-ADMIN@EXAMPLE.INVALID',PasswordHash,PasswordSalt,TotpSecret,1,4,'GLOBAL',SYSDATETIMEOFFSET(),'Agent Admin',0,NULL,0,NULL,NULL,NEWID(),0 FROM Users WHERE Username='dev-user'"
```

Delete the clone when done, the same way: `DELETE FROM "Users" WHERE "Username"='agent-admin';` on
PostgreSQL, and `DELETE FROM Users WHERE Username='agent-admin'` on SQL Server.

## 4. Sign in

For curl, `signin.py` (next to this file) runs the real sign-in, password then a real TOTP code, and
writes a cookie jar. An account that has never signed in lands on the enrollment page instead; the
script enrolls it with the secret that page creates. That secret is then the account's
authenticator secret, held only in `Users.TotpSecret`; never print it.

```bash
EV_PASSWORD=… python3 -I .claude/skills/run-vault/signin.py dev-user /tmp/vault.jar
curl -sk -b /tmp/vault.jar https://localhost:7443/api/ui/me
```

On SQL Server, add `EV_DB_ENGINE=sqlserver EV_SQL_PASSWORD=… EV_SQL_CONTAINER=<container>` to the
first command.

In the browser preview, compute the code the same way the script does. Click each field before
`preview_type` (with `clear: true`) and confirm the values with `preview_evaluate` before submitting:
typing into a page still loading submits an empty form. Read page text with `preview_evaluate`
(`document.body.innerText`); `preview_snapshot` returns the whole network and action log on every
call, so take one at the end for its `consoleEntries`, where CSP violations appear as errors.

The sign-in budget:
- **Throttle:** 11 sign-in POSTs per 120 s per IP, and a sign-in spends 2. Restarting the vault
  clears it in Development.
- **One code per step:** an authenticator code works once per account per 30-second step, and
  enrolling spends one too. Two sign-ins as the same account (curl, then browser) need a new step
  between them.
- **Return URL:** the sign-in drops `ReturnUrl` at the TOTP step and lands on `/Secrets`, so
  navigate to the page under test afterwards.
