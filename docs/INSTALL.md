# Production installation

EclipsVault is self-hosted: you run it in your own environment, hold your own keys, and manage your
own database and backups. This is the whole security model — the vendor never has access to your
secrets or your servers. This guide is the production runbook; for feature detail see the README.

## Quickest path: the reference deployment

[`deploy/production/`](../deploy/production/) runs the whole vault on one Linux host with Docker
Compose: Caddy in front with automatic HTTPS, the vault, and PostgreSQL 17. It is the quickest
supported way to install. It is a single node: for more replicas, build your own deployment from
the numbered sections below, which also explain each setting the reference deployment makes.

What it sets up:

- Caddy obtains and renews a certificate for your host name and is the only proxy the vault trusts
  for the client's address (`ForwardedHeaders__KnownProxies`), so sign-in throttling, network rules
  and the audit trail see the real client. Only Caddy and the vault share that network.
- PostgreSQL keeps its data in a named volume, on an internal network only the vault shares. The
  vault signs in as its own login, `eclipsvault_app`, not as the superuser.
- The Data Protection key ring lives in a named volume, sealed with the master key.

What it trades for simplicity, compared with the numbered sections:

- The vault applies its own migrations at startup (`Database__MigrateOnStartup`), so its login owns
  the database. While the vault runs, that login can change any table, the audit trail included;
  there is no separate deploy login as in section 3.
- The vault reaches PostgreSQL without TLS. The traffic never leaves the host, on a network only the
  two containers share.
- Anyone who can run Docker commands on the host, or read `.env`, can read the master key. Treat
  that access as access to every secret.

### Install

1. Point a DNS record for the vault's host name at the host, and let ports 80 and 443 reach it:
   Caddy needs both to obtain the certificate.
2. Take `deploy/production/` from the release you install, and verify that release's image and note
   its digest ([`VERIFYING.md`](VERIFYING.md)). Releases from before `deploy/production/` existed
   cannot run it: their image has no `/keyring` the vault's account can write.
3. Copy `deploy/production/` to the host, and in that folder create `.env`, readable only by you,
   before any secret goes in:

       install -m 600 .env.example .env

4. Replace every `<placeholder>` in `.env`. Each line says how to generate its value. `.env` holds
   the master key and the audit signing key, so the whole file belongs in your secret manager, never
   beside a backup (see [`BACKUP_AND_RECOVERY.md`](BACKUP_AND_RECOVERY.md)). Save it there after the
   first sign-in below, and again after every change to it.
5. Start it:

       docker compose up -d

   `docker compose logs vault` shows startup; the vault waits for PostgreSQL, applies its
   migrations and creates `vault-admin`.

### First sign-in

Open `https://<your host name>/` and sign in as `vault-admin` with the password you put in
`Seed__AdminPassword`, then enroll an authenticator. Then empty the `Seed__AdminPassword=` line in
`.env` and recreate the vault without it:

    docker compose up -d

Now save the whole `.env` to your secret manager. Save it again whenever you change it: a new
`EV_IMAGE` digest, or a rotated master key with its `ECLIPSVAULT_KEK_RETIRED` line.

Adding accounts sends invitations, which need email delivery. Add your SMTP settings (`Email__*`) to
the vault's `environment` in `compose.yaml`; `Identity__PublicUrl` is already set to your host name.

### Back up

Back up the database and the key ring together, onto separate, encrypted backup storage. Never write
them into the folder that holds `.env`, and never put `.env` in the backup: the master key and the
data it unlocks must not be found together. Each backup goes into a new private directory, and is
marked `COMPLETE` only when both parts were written. From the `deploy/production/` folder, with
`BACKUP_ROOT` set to a directory on that storage:

    (
      set -e
      umask 077
      BACKUP_ROOT=/mnt/encrypted-backup/eclipsvault
      BACKUP="$BACKUP_ROOT/$(date -u +%Y%m%dT%H%M%SZ)"
      mkdir -m 700 "$BACKUP"
      docker compose exec -T db pg_dump --username=postgres --format=custom eclipsvault > "$BACKUP/eclipsvault.dump"
      docker compose exec -T vault tar -C /keyring -cf - . > "$BACKUP/keyring.tar"
      touch "$BACKUP/COMPLETE"
    )

Restore only from a directory that holds `COMPLETE`.

To restore onto a new host, install as above, but take `.env` from your secret manager instead of
filling it in. If you have rotated the master key, its `ECLIPSVAULT_KEK_RETIRED` must hold every
earlier key the restored data or key ring still needs. Then, before the first `docker compose up -d`,
with `BACKUP` set to the newest backup directory that holds `COMPLETE`:

    docker compose up -d --wait db
    docker compose exec -T db pg_restore --username=postgres --dbname=eclipsvault --clean --if-exists --exit-on-error < "$BACKUP/eclipsvault.dump"
    docker compose run --rm -T --no-deps --entrypoint tar vault -C /keyring -xf - < "$BACKUP/keyring.tar"
    docker compose up -d

Then sign in, reveal a secret, and run **Verify integrity** on the **Audit log** page.

### Upgrade

Back up first. Then verify the new release image, set `EV_IMAGE` in `.env` to its digest, read that
release's upgrade notes (sections 8 and 9), and run:

    docker compose up -d

The vault applies the new release's migrations as it starts. To move Caddy or PostgreSQL to a newer
image, change the digest in `compose.yaml` the same way. A new major PostgreSQL version needs a dump
and restore, not just a new image.

## 1. Prerequisites

- A database, which you provide: outside the reference deployment, EclipsVault ships no database.
  We recommend PostgreSQL 17 or later. SQL Server 2017 or later is the fully supported alternative; its migrations use `TRIM`,
  `TRANSLATE` and `STRING_AGG`, which need SQL Server 2017. If you choose SQL Server, you license it
  from Microsoft yourself.
- A TLS-terminating reverse proxy or ingress in front of the app (the container serves plain HTTP on
  port 8080 inside your network).
- A place to hold a persistent Data Protection key ring directory, shared by every replica.

## 2. Required configuration (environment variables)

| Variable | Purpose |
|---|---|
| `Database__Provider` | Required. `Postgres` for PostgreSQL, or `SqlServer`. The vault does not start until this is set. |
| `ConnectionStrings__DefaultConnection` | Database connection string, for a least-privilege login with TLS enforced. PostgreSQL: `Host=db.example.internal;Port=5432;Database=eclipsvault;Username=eclipsvault_app;Password=…;SSL Mode=Require`. `Require` encrypts but does not check the server's certificate; `SSL Mode=VerifyFull` also verifies the certificate and the server's host name. SQL Server: `Server=db.example.internal;Database=eclipsvault;User Id=eclipsvault_app;Password=…;Encrypt=True;TrustServerCertificate=False`. |
| `ECLIPSVAULT_KEK` | Master key: `openssl rand -base64 32`. Or use a KMS engine — set `Crypto__Engine=VaultTransit` and the `Vault` settings (see `docker-compose.yml` for the shape). |
| `ECLIPSVAULT_AUDIT_SIGNING_KEY` | Base64 PKCS#8 P-256 private key for signing audit checkpoints. |
| `DataProtection__KeyRingPath` | Durable directory shared by all nodes (the keys are sealed at rest with the KEK). |
| `DynamicSecrets__TargetConnectionString` | **Only if you use dynamic secrets or managed rotation on SQL Server.** The database whose logins the vault manages — see [Dynamic secrets: the managed target](#dynamic-secrets-the-managed-target) below. Deliberately separate from `ConnectionStrings__DefaultConnection`. |
| `DynamicSecrets__PostgresTargetConnectionString` | **Only if you use dynamic secrets on PostgreSQL.** The PostgreSQL database whose login roles the vault mints — see the same section. Also separate from `ConnectionStrings__DefaultConnection`. |
| `ECLIPSVAULT_LICENSE` | Your license token (or place it in a `license.key` file in the content root). |
| `ASPNETCORE_ENVIRONMENT` | Must be `Production` (anything other than `Development` disables dev seeding and fallbacks). |
| `ForwardedHeaders__KnownProxies` | The IP(s) of your reverse proxy, so the real client IP is trusted for rate limiting, the IP blacklist, ABAC network rules, and audit. |
| `AllowedHosts` | Your vault's hostname(s), so a spoofed `Host` header is rejected. |

## 3. Apply the schema from your deploy job (not the app)

The running app does not have rights to change the schema. Run the migrations for each release from
your deploy pipeline, with a login that has DDL rights (which the app's own login does not). They
run from the repository root of a checkout of the release you deploy, with the .NET 10 SDK.
Restore the EF Core tool pinned in `dotnet-tools.json` before applying the migrations:

    dotnet tool restore

PostgreSQL:

    ECLIPSVAULT_DESIGN_PROVIDER=Postgres \
    ECLIPSVAULT_DESIGN_CONNECTION="Host=…;Port=5432;Database=eclipsvault;Username=<deploy login>;Password=…;SSL Mode=Require" \
    dotnet ef database update -p EclipsVault.Migrations.Postgres -s EclipsVault.Web -c EclipsVaultDbContext

SQL Server:

    ECLIPSVAULT_DESIGN_CONNECTION="Server=…;Database=eclipsvault;User Id=<deploy login>;Password=…;Encrypt=True;TrustServerCertificate=False" \
    dotnet ef database update -p EclipsVault.Infrastructure -s EclipsVault.Web -c EclipsVaultDbContext

The tool chooses the engine from `ECLIPSVAULT_DESIGN_PROVIDER`, not `Database__Provider`; when that
variable is unset, it targets SQL Server. Each engine has its own migrations project. Without
`ECLIPSVAULT_DESIGN_CONNECTION`, the tool reads `ConnectionStrings__DefaultConnection`, and with
neither set it targets a development database on localhost, so always set one in a deploy job. The
app verifies the schema at startup and refuses to start against a mismatched one.

### Dynamic secrets: the managed target

Skip this unless you use **dynamic secrets** or **managed rotation**. Both work by creating,
dropping, or re-passwording principals on the database whose credentials you want the vault to
manage. That needs server-level rights there: `ALTER ANY LOGIN` on SQL Server, `CREATEROLE` on
PostgreSQL.

Each dynamic role names its backend, and each backend has its own target, whichever database the
vault itself runs on:

- **SQL Server** roles and managed rotation use `DynamicSecrets__TargetConnectionString`.
- **PostgreSQL** roles use `DynamicSecrets__PostgresTargetConnectionString`. Managed rotation is
  SQL Server only.

Those rights are why each target is a **separate connection string**:

    DynamicSecrets__TargetConnectionString="Server=app-db;Database=app;User Id=eclipsvault_credmgr;Password=…;Encrypt=True;TrustServerCertificate=False"
    DynamicSecrets__PostgresTargetConnectionString="Host=app-pg;Port=5432;Database=app;Username=eclipsvault_credmgr;Password=…;SSL Mode=VerifyFull"

- **Point it at the database you want managed, not at the vault's own.** Minting credentials on the
  vault's database is almost never what you want, and it is not the default.
- **Use a dedicated login on that server**, granted only `ALTER ANY LOGIN` (plus `VIEW SERVER STATE`
  and `ALTER ANY CONNECTION` for SQL Server dynamic-secret session termination, and whatever your
  role statements need). Do not reuse `ConnectionStrings__DefaultConnection`.
- **On PostgreSQL, the target must be a distinct PostgreSQL cluster from the vault's database**, not
  another database in the same cluster. Roles and the catalogs that hold them are shared by every
  database in a cluster, and `pg_read_all_data` and `pg_write_all_data` reach every database: a lease
  on the vault's own cluster could read or change the vault's data. Revoking `CONNECT` on the vault's
  database does not separate them. The vault does not yet refuse a target on its own cluster, so this
  is yours to keep.
- **The PostgreSQL target must run PostgreSQL 16 or later.** Its login is set up like this, which the
  integration tests use:

      CREATE ROLE eclipsvault_credmgr LOGIN PASSWORD '…' CREATEROLE;
      GRANT pg_read_all_data, pg_write_all_data TO eclipsvault_credmgr WITH ADMIN TRUE, INHERIT FALSE, SET FALSE;
      ALTER ROLE eclipsvault_credmgr SET createrole_self_grant = 'inherit';

  `ADMIN` lets it grant the roles your recipes name (the sample recipes name those two), plus whatever
  else your role statements need. `createrole_self_grant` gives it the privileges of the roles it
  creates, which revocation needs to end their sessions; without it every lease lands in
  `RevocationFailed`. Through the roles it mints, this login can effectively read and write every
  database on the target cluster, which is one more reason that cluster holds only what you want
  managed.
- **Minted roles are for existing objects.** The sample recipes read and write what is already there.
  Revocation never moves, drops or re-grants anything a minted role owns or was granted directly, so
  ownership never crosses to the target's login. If something still depends on the role (a table it
  created, a privilege granted to it), its `DROP ROLE` fails and the lease lands in `RevocationFailed`
  with a critical audit row; the role keeps its objects, cannot sign in, and has no sessions. Decide
  what happens to those objects, then drop the role (below). Revoking `CONNECT` from `PUBLIC` on the
  target cluster's other databases (for example `REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;`)
  keeps minted roles from creating objects there.
- **Revoking a PostgreSQL credential ends its sessions before the role is dropped.** The vault disables
  sign-in, checks that pending startups have cleared, then ends the role's sessions and confirms none
  remain. It runs the role's statements only after those checks and then confirms the role is gone.
  The startup check has a deadline of the target's `authentication_timeout` plus five seconds. If
  startup or session termination stays unconfirmed, the role stays disabled and the lease lands in
  `RevocationFailed` with a critical audit row. Follow the
  [incident runbook](INCIDENT_RESPONSE.md#a-minted-login-may-remain-on-the-target-operators) to verify
  pending startups and session termination before dropping the role.
- **A PostgreSQL role's statements must double-quote `"{{name}}"`**: the minted name has capitals,
  which PostgreSQL folds to lower case when unquoted.
- **A failing statement reaches the target's own log, password included**, because PostgreSQL logs a
  failing statement by default (`log_min_error_statement = error`), and so does `log_statement` when
  set. A failed mint's role is rolled back, so that password never worked, but restrict who reads the
  target's logs as you would its credentials.
- **Why they are separate:** `ALTER ANY LOGIN` can re-password *any* principal on the instance that
  holds it. If the vault's own login held it, a compromise of the running application would reach
  the server that stores the audit trail — so the vault never asks for it. See
  `docs/THREAT_MODEL.md` → *Invariants that bound the blast radius*.
- **If it is unset, minting and rotation refuse**, with an error naming the setting. There is no
  fallback to the vault's connection: falling back would quietly require the privilege above.
- The target may be a different server, and in most deployments it should be.

## 4. First administrator

On an empty vault, create the first admin once (screened like any password), then remove the setting:

    Seed__AdminPassword='<a password unique to this deployment>' dotnet EclipsVault.Web.dll

With Docker, pass `-e Seed__AdminPassword=…` to the first `docker run` only. The value stays in the
container's configuration, where `docker inspect` shows it, so once `vault-admin` exists, remove the
container and start a new one without that variable.

## 5. Run (Docker)

Released images are published to `ghcr.io/sabysabashvily/eclipsvault` and are **cryptographically
signed** — verify the image and pin it to the digest you verified *before* running it. See
[`VERIFYING.md`](VERIFYING.md), then use the verified `ghcr.io/sabysabashvily/eclipsvault@sha256:…`
reference below in place of a locally-built tag.

    docker run -d --name eclipsvault \
      -e Database__Provider=Postgres \
      -e ConnectionStrings__DefaultConnection="Host=…;Port=5432;Database=eclipsvault;Username=eclipsvault_app;Password=…;SSL Mode=Require" \
      -e ECLIPSVAULT_KEK="…" \
      -e ECLIPSVAULT_AUDIT_SIGNING_KEY="…" \
      -e DataProtection__KeyRingPath=/keyring \
      -e ECLIPSVAULT_LICENSE="EVLIC1.…" \
      -e ASPNETCORE_ENVIRONMENT=Production \
      -e ForwardedHeaders__KnownProxies="10.0.0.2" \
      -e AllowedHosts="vault.example.com" \
      -v /srv/eclipsvault/keyring:/keyring \
      -p 8080:8080 \
      eclipsvault:local

On SQL Server, set `Database__Provider=SqlServer` and its connection string instead.

Put your reverse proxy in front, terminating TLS and forwarding `X-Forwarded-For` /
`X-Forwarded-Proto` to the container.

The image carries the licence notices of the third-party software it redistributes:
`/app/THIRD-PARTY-NOTICES.txt` (the NuGet packages, including the native libraries inside SkiaSharp),
`/app/wwwroot/ui/3rdpartylicenses.txt` (the Angular UI's packages), `/app/wwwroot/fonts/OFL.txt` (the
fonts), `/usr/share/dotnet/LICENSE.txt` and `/usr/share/dotnet/ThirdPartyNotices.txt` (.NET), and
`/usr/share/doc/*/copyright` (the Ubuntu base packages). Read one with
`docker run --rm --entrypoint cat <image> /app/THIRD-PARTY-NOTICES.txt`.

Portions of this software are copyright © The FreeType Project (www.freetype.org). All rights reserved.

## 6. Verify

- The app starts and logs `License check: Valid — Licensed to …` (or, unlicensed, a warning line —
  it still runs; licensing is soft).
- Sign in, complete TOTP, open a secret.
- On the admin **Audit log** page, run **Verify integrity** — the chain reports intact.

## 7. Backups

Back up the database **and** the Data Protection key ring directory. The key ring is sealed with your
KEK, so a backup is inert without `ECLIPSVAULT_KEK` — keep the KEK in your secret manager, not beside
the backup. [`BACKUP_AND_RECOVERY.md`](BACKUP_AND_RECOVERY.md) has the PostgreSQL commands and the
restore procedure.

## 8. Upgrading

Every session carries the account's credential generation and session revocation version, and ends
when either moves on: a credential change, or signing out everywhere. Two rules follow when you
upgrade from a release whose sessions carry neither:

- **Everyone signs in again once.** The vault refuses those sessions, and sign-ins left half-finished
  across the upgrade, rather than guess what they would carry.
- **Upgrade every node before relying on this.** A node still running the earlier release keeps its
  own rules, so it would accept sessions the new one has ended. Drain and retire old nodes instead of
  running both releases behind one load balancer.
- **Redis must not evict.** With `Redis:Enabled=true` the vault refuses to start unless Redis reports
  `maxmemory-policy noeviction` and allows `INFO`. Many managed services default to `volatile-lru`;
  change that parameter before upgrading. Every configured server must answer at startup: a vault
  that used to start while one endpoint was down now refuses until it is back. See
  [`BACKUP_AND_RECOVERY.md`](BACKUP_AND_RECOVERY.md).
- **Enroll the bootstrap administrator before upgrading.** The upgrade moves every never-used account
  (no enrolled authenticator, no passkey, and no `LoginSucceeded` or `PasskeyLogin` row in the audit
  trail) to awaiting activation, advances its credential generation, and clears any pending
  authenticator secret. The migration never invites the last enabled administrator: if every enabled
  administrator would otherwise be invited, `vault-admin` (or otherwise the earliest created enabled
  administrator) stays on its existing password and enrolls at first sign-in. Configure real SMTP
  delivery and `Identity__PublicUrl` so you can replace any generated addresses on migrated accounts
  and resend their invitations. Drain and retire old nodes before serving traffic after the upgrade.

## 9. Upgrading to 2.0

v1.x started on SQL Server when `Database:Provider` was unset. v2.0 has no default. An upgraded
vault that still has the setting unset stops at startup, exits 1, and the log names the setting
and its two values.

Set `Database:Provider` (environment variable `Database__Provider`) to the engine this install
already uses, and leave the connection string
as it is:

- `SqlServer` when the vault runs on SQL Server, including an install that never set the variable
  and so used the v1.x default.
- `Postgres` when the vault runs on PostgreSQL.

Choosing the other engine would read that connection string as the wrong database. The migration
command is unchanged: it follows `ECLIPSVAULT_DESIGN_PROVIDER`, and an unset value there still
targets SQL Server.
