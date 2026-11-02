# Production installation

EclipsVault is self-hosted: you run it in your own environment, hold your own keys, and manage your
own database and backups. This is the whole security model — the vendor never has access to your
secrets or your servers. This guide is the production runbook; for feature detail see the README.

## 1. Prerequisites

- A database, which you provide: EclipsVault ships no database. We recommend PostgreSQL 17 or
  later. SQL Server 2017 or later is the fully supported alternative; its migrations use `TRIM`,
  `TRANSLATE` and `STRING_AGG`, which need SQL Server 2017. A customer who chooses SQL Server
  licenses it from Microsoft themselves.
- A TLS-terminating reverse proxy or ingress in front of the app (the container serves plain HTTP on
  port 8080 inside your network).
- A place to hold a persistent Data Protection key ring directory, shared by every replica.

## 2. Required configuration (environment variables)

| Variable | Purpose |
|---|---|
| `Database__Provider` | `Postgres` for PostgreSQL, or `SqlServer`. **Set it on a PostgreSQL install:** when it is unset the vault uses SQL Server, and a PostgreSQL connection string then fails at startup. |
| `ConnectionStrings__DefaultConnection` | Database connection string, for a least-privilege login with TLS enforced. PostgreSQL: `Host=db.example.internal;Port=5432;Database=eclipsvault;Username=eclipsvault_app;Password=…;SSL Mode=Require`. `Require` encrypts but does not check the server's certificate; `SSL Mode=VerifyFull` also verifies the certificate and the server's host name. SQL Server: `Server=db.example.internal;Database=eclipsvault;User Id=eclipsvault_app;Password=…;Encrypt=True;TrustServerCertificate=False`. |
| `ECLIPSVAULT_KEK` | Master key: `openssl rand -base64 32`. Or use a KMS engine — set `Crypto__Engine=VaultTransit` and the `Vault` settings (see `docker-compose.yml` for the shape). |
| `ECLIPSVAULT_AUDIT_SIGNING_KEY` | Base64 PKCS#8 P-256 private key for signing audit checkpoints. |
| `DataProtection__KeyRingPath` | Durable directory shared by all nodes (the keys are sealed at rest with the KEK). |
| `DynamicSecrets__TargetConnectionString` | **Only if you use dynamic secrets or managed rotation.** The database whose logins the vault manages — see [Dynamic secrets: the managed target](#dynamic-secrets-the-managed-target) below. Deliberately separate from `ConnectionStrings__DefaultConnection`. |
| `ECLIPSVAULT_LICENSE` | Your license token (or place it in a `license.key` file in the content root). |
| `ASPNETCORE_ENVIRONMENT` | Must be `Production` (anything other than `Development` disables dev seeding and fallbacks). |
| `ForwardedHeaders__KnownProxies` | The IP(s) of your reverse proxy, so the real client IP is trusted for rate limiting, the IP blacklist, ABAC network rules, and audit. |
| `AllowedHosts` | Your vault's hostname(s), so a spoofed `Host` header is rejected. |

## 3. Apply the schema from your deploy job (not the app)

The running app does not have rights to change the schema. Run the migrations for each release from
your deploy pipeline, with a login that has DDL rights (which the app's own login does not). They
run from a checkout of the release you deploy, with the .NET 10 SDK and the EF Core tool
(`dotnet tool install --global dotnet-ef`).

PostgreSQL:

    ECLIPSVAULT_DESIGN_PROVIDER=Postgres \
    ECLIPSVAULT_DESIGN_CONNECTION="Host=…;Port=5432;Database=eclipsvault;Username=<deploy login>;Password=…;SSL Mode=Require" \
    dotnet ef database update -p EclipsVault.Migrations.Postgres -s EclipsVault.Web -c EclipsVaultDbContext

SQL Server:

    ECLIPSVAULT_DESIGN_CONNECTION="Server=…;Database=eclipsvault;User Id=<deploy login>;Password=…;Encrypt=True;TrustServerCertificate=False" \
    dotnet ef database update -p EclipsVault.Infrastructure -s EclipsVault.Web -c EclipsVaultDbContext

The tool chooses the engine from `ECLIPSVAULT_DESIGN_PROVIDER`, not `Database__Provider`, and unset
it targets SQL Server. Each engine has its own migrations project. Without
`ECLIPSVAULT_DESIGN_CONNECTION`, the tool reads `ConnectionStrings__DefaultConnection`. The app
verifies the schema at startup and refuses to start against a mismatched one.

### Dynamic secrets: the managed target

Skip this unless you use **dynamic secrets** or **managed rotation**. Both work by issuing
`CREATE LOGIN`, `DROP LOGIN`, and `ALTER LOGIN` against the database whose credentials you want the
vault to manage. That needs server-level rights there — `ALTER ANY LOGIN` on SQL Server.

The managed target must be SQL Server, whichever database the vault itself runs on: those are SQL
Server statements, and SQL Server is the only target the vault supports. A vault on PostgreSQL can
manage a SQL Server target.

Those rights are why this is a **second, separate connection string**:

    DynamicSecrets__TargetConnectionString="Server=app-db;Database=app;User Id=eclipsvault_credmgr;Password=…;Encrypt=True;TrustServerCertificate=False"

- **Point it at the database you want managed, not at the vault's own.** Minting credentials on the
  vault's database is almost never what you want, and it is not the default.
- **Use a dedicated login on that server**, granted only `ALTER ANY LOGIN` (plus whatever your role
  statements need). Do not reuse `ConnectionStrings__DefaultConnection`.
- **Why they are separate:** `ALTER ANY LOGIN` can re-password *any* principal on the instance that
  holds it. If the vault's own login held it, a compromise of the running application would reach
  the server that stores the audit trail — so the vault never asks for it. See
  `docs/THREAT_MODEL.md` → *Invariants that bound the blast radius*.
- **If it is unset, minting and rotation refuse**, with an error naming this setting. There is no
  fallback to the vault's connection: falling back would quietly require the privilege above.
- The target may be a different server, and in most deployments it should be.

## 4. First administrator

On an empty vault, create the first admin once (screened like any password), then remove the setting:

    Seed__AdminPassword='<a password unique to this deployment>' dotnet EclipsVault.Web.dll

With Docker, pass it to the first run as `-e Seed__AdminPassword=…` instead.

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
