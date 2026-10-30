# Prepare a Community evaluation

Prepare an isolated instance containing only invented data, then give testers the
[walkthrough](COMMUNITY_WALKTHROUGH.md) and [feedback template](COMMUNITY_FEEDBACK.md). This guide
does not change the product licence or establish production readiness.

## Candidate gate

The dependency blockers are resolved on `main` as of `06790ef` (2026-10-14): ImageSharp was replaced
by SkiaSharp (#38), which needs no vendor licence, and the security fixes (#34), the patched
`System.Security.Cryptography.Pkcs` (#39) and the audit shutdown fix (#37) are merged. A candidate
still has to pass this gate before testers are invited.
The expected behaviour below was checked against source code, including the pending
[security fixes in PR #34](https://github.com/Saba-sabashvili/EclipsVault/pull/34).
**Do not invite testers until a candidate containing those fixes and the dependency update passes
the following gate.** Record the actual candidate commit or image digest; a branch name alone can move.

- [ ] Candidate includes the project/clearance creation checks and permission-filtered dashboard.
- [ ] Candidate includes the SkiaSharp avatar processor (#38) and the patched
      `System.Security.Cryptography.Pkcs` (#39).
- [ ] `dotnet restore --locked-mode` succeeds without suppressing vulnerability findings.
- [ ] `dotnet build EclipsVault.slnx -c Release --no-restore` succeeds.
- [ ] `dotnet test EclipsVault.slnx -c Release --no-build --blame-hang-timeout 5m --blame-hang-dump-type mini`
      succeeds after that build. An incomplete or hanging run is not a pass. The candidate must
      include the audit shutdown fix (#37) and its regression tests.
- [ ] The selected database's migrations apply and the app starts on a fresh evaluation database.
- [ ] The maintainer completes T1–T7 on that candidate and records the results.

This evaluation uses Community features; do not activate premium capabilities to get the basic walkthrough working.
If restore or build is blocked, record the failing command and sanitised error in the session note.
Mark browser tasks **Blocked**, without treating an environment prerequisite as a workflow failure.

## Local setup for a supervised session

For a small disposable setup using cached PostgreSQL/Mailpit images and no database disk volume or
backups, use the [local evaluation lab](../evaluation/README.md). Its database is discarded when the
container stops; it supports the browser walkthrough, not a recovery rehearsal. The manual setup
below is an alternative.

Use a dedicated checkout and a new, disposable database. Follow the [README development setup](../README.md#run-it),
with these details made explicit:

1. Copy `EclipsVault.Web/appsettings.Development.json.template` to the gitignored
   `EclipsVault.Web/appsettings.Development.json` only if that file does not already exist. Fill its
   placeholders locally. Do not overwrite an existing configuration or reuse a real deployment's keys.
2. Set the connection string to a new database such as `EclipsVaultCommunityEval`. For SQL Server,
   the local password must match the development Compose database. Supply a fresh 32-byte dev KEK
   in base64 and test-only seed passwords. Keep the KEK stable while reusing this database; do not
   paste it or the configuration into feedback.
3. Keep the default local AES-GCM engine and Redis disabled. Start only the needed dependencies
   with `docker compose up -d db mailpit`. If the SQL Server container cannot run on your machine,
   use the README's PostgreSQL configuration with a disposable database bound to loopback; record
   which provider you actually tested.
4. Trust the local HTTPS development certificate with `dotnet dev-certs https --trust`. After the
   candidate gate's build, run `dotnet run --project EclipsVault.Web -c Release --no-build --launch-profile https`.
   The profile selects Development and serves `https://localhost:7443`; migrations and sample
   seeding run on startup. Wait for successful startup before opening the browser.
5. Sign in as `vault-admin` using the seed password you configured and enroll MFA. Existing databases
   do not reset accounts or passwords when you change the seed settings.

Development mode, the sample Compose services and their published credentials are for the local
machine. For remote testers, provision a separate evaluation deployment with TLS and proper key
storage using [the deployment runbook](INSTALL.md), bootstrap its administrator, and create the
accounts below. Do not expose the local development stack or use a database containing real secrets.
Remote deployment still uses dummy data; a deployment configuration is not a readiness certificate.

Use a local mail capture service or the Log email transport, without real SMTP or identity-provider
credentials. Do not configure managed rotation or dynamic-secret targets for this walkthrough.

## Create the test accounts and fixtures

Use a separate browser profile for the administrator. In **Users → New user**, create one account
per tester with invented names, a unique test-only password of at least 12 characters, **Standard**
clearance and project **PHOENIX**. Record the chosen username; allow the tester to enroll MFA
themselves. Do not give testers the administrator's password.

Assign a distinct prefix per tester, such as `EVAL_A` or `EVAL_B`. Before they start, use **New secret**
as administrator to create these two records, substituting the assigned prefix:

| Name | Value | Project | Environment | Sensitivity | TTL days |
|---|---|---|---|---|---|
| `EVAL_A_OTHER_PROJECT` | `dummy-other-project` | ORION | Development | Internal | 2 |
| `EVAL_A_HIGH_CLEARANCE` | `dummy-high-clearance` | PHOENIX | Development | Secret | 2 |

Save their details URLs for the direct-access checks. Do not grant the tester access to them. Their
short TTL puts them in the administrator's Overview **Expiring within 7 days** total, so the tester's
Overview can show they are excluded; recreate them if the session occurs
after they expire. The tester creates `EVAL_A_WORKFLOW` during T2. Avoid unrelated writes while
comparing list and dashboard counts. Do not use seeded honey tokens as fixtures: opening those is
intended to trigger intrusion response. Development mode also seeds a few sample PHOENIX secrets; a
Standard PHOENIX tester sees the Internal ones in their list, and that is expected.

In the session note, record the URL, candidate commit/digest, database provider, browser/OS,
tester username/prefix, whether the run is local or remote, and where feedback should be returned.
Transmit the initial password separately from that note. Tell testers explicitly that only invented
secret values may be entered and that blocked steps should be reported rather than worked around.

## Triage and close the session

- Any unexpected access, missing successful-operation audit event, or loss of a stored dummy value
  blocks this candidate. Preserve sanitised reproduction steps and fix/retest the failing path.
- For usability findings, record the task, confusing screen or wording, and whether assistance was
  needed. Prioritise failures to complete the core workflow before adding new features.
- After collecting results, remove the dummy fixtures and disable test accounts through **Users**.
  Stop the evaluation instance when it is no longer needed. Delete only
  storage identified as belonging to that disposable instance; do not use blanket volume cleanup.
- Keep the completed feedback separate from this reusable guide. Never commit login credentials,
  MFA material, raw configuration, or unreviewed logs with a report.

Before any production readiness decision, separately rehearse [backup and recovery](BACKUP_AND_RECOVERY.md)
and an upgrade/rollback on disposable data, with the exact versions and outcomes recorded. Completing
the browser walkthrough does not count as performing those rehearsals.
