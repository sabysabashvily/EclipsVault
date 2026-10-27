# Production installation

EclipsVault is self-hosted: you run it in your own environment, hold your own keys, and manage your
own database and backups. This is the whole security model — the vendor never has access to your
secrets or your servers. This guide is the production runbook; for feature detail see the README.

## 1. Prerequisites

- A database: SQL Server 2017 or later, or PostgreSQL 17+ (see `Database:Provider`). The
  migrations use `TRIM`, `TRANSLATE` and `STRING_AGG`, which need SQL Server 2017.
- A TLS-terminating reverse proxy or ingress in front of the app (the container serves plain HTTP on
  port 8080 inside your network).
- A place to hold a persistent Data Protection key ring directory, shared by every replica.

## 2. Required configuration (environment variables)

| Variable | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | Database connection string. Use a least-privilege login and enforce TLS (`Encrypt=True;TrustServerCertificate=False` on SQL Server, `SSL Mode=Require` on PostgreSQL). |
| `Database__Provider` | `SqlServer` (default) or `Postgres`. |
| `ECLIPSVAULT_KEK` | Master key: `openssl rand -base64 32`. Or use a KMS engine — set `Crypto__Engine=VaultTransit` and the `Vault` settings (see `docker-compose.yml` for the shape). |
| `ECLIPSVAULT_AUDIT_SIGNING_KEY` | Base64 PKCS#8 P-256 private key for signing audit checkpoints. |
| `DataProtection__KeyRingPath` | Durable directory shared by all nodes (the keys are sealed at rest with the KEK). |
| `DynamicSecrets__TargetConnectionString` | **Only if you use dynamic secrets or managed rotation.** The database whose logins the vault manages — see [Dynamic secrets: the managed target](#dynamic-secrets-the-managed-target) below. Deliberately separate from `ConnectionStrings__DefaultConnection`. |
| `ECLIPSVAULT_LICENSE` | Your license token (or place it in a `license.key` file in the content root). |
| `ASPNETCORE_ENVIRONMENT` | Must be `Production` (anything other than `Development` disables dev seeding and fallbacks). |
| `ForwardedHeaders__KnownProxies` | The IP(s) of your reverse proxy, so the real client IP is trusted for rate limiting, the IP blacklist, ABAC network rules, and audit. |
| `AllowedHosts` | Your vault's hostname(s), so a spoofed `Host` header is rejected. |

## 3. Apply the schema from your deploy job (not the app)

The running app does not have rights to change the schema. Run migrations once, from your deploy
pipeline, with a login that has DDL rights (which the app's own login does not):

    ConnectionStrings__DefaultConnection="…" \
    dotnet ef database update --project EclipsVault.Infrastructure --startup-project EclipsVault.Web

For PostgreSQL, use the `EclipsVault.Migrations.Postgres` project and set
`ECLIPSVAULT_DESIGN_PROVIDER=Postgres` (the design-time factory also reads
`ECLIPSVAULT_DESIGN_CONNECTION` if you keep the runtime connection string out of that shell). The app
verifies the schema at startup and refuses to start against a mismatched one.

### Dynamic secrets: the managed target

Skip this unless you use **dynamic secrets** or **managed rotation**. Both work by issuing
`CREATE LOGIN`, `DROP LOGIN`, and `ALTER LOGIN` against the database whose credentials you want the
vault to manage. That needs server-level rights there — `ALTER ANY LOGIN` on SQL Server.

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

## 5. Run (Docker)

Released images are published to `ghcr.io/sabysabashvily/eclipsvault` and are **cryptographically
signed** — verify the image and pin it to the digest you verified *before* running it. See
[`VERIFYING.md`](VERIFYING.md), then use the verified `ghcr.io/sabysabashvily/eclipsvault@sha256:…`
reference below in place of a locally-built tag.

    docker run -d --name eclipsvault \
      -e ConnectionStrings__DefaultConnection="…" \
      -e Database__Provider=Postgres \
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

Put your reverse proxy in front, terminating TLS and forwarding `X-Forwarded-For` /
`X-Forwarded-Proto` to the container.

## 6. Verify

- The app starts and logs `License check: Valid — Licensed to …` (or, unlicensed, a warning line —
  it still runs; licensing is soft).
- Sign in, complete TOTP, open a secret.
- On the admin **Audit log** page, run **Verify integrity** — the chain reports intact.

## 7. Backups

Back up the database **and** the Data Protection key ring directory. The key ring is sealed with your
KEK, so a backup is inert without `ECLIPSVAULT_KEK` — keep the KEK in your secret manager, not beside
the backup.

## 8. Upgrading

Every session carries the account's credential generation and session revocation version, and ends
when either moves on: a credential change, or signing out everywhere. Two rules follow when you
upgrade from a release whose sessions carry neither:

- **Everyone signs in again once.** The vault refuses those sessions, and sign-ins left half-finished
  across the upgrade, rather than guess what they would carry.
- **Upgrade every node before relying on this.** A node still running the earlier release keeps its
  own rules, so it would accept sessions the new one has ended. Drain and retire old nodes instead of
  running both releases behind one load balancer.
