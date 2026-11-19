# Small local evaluation lab

One PostgreSQL database and Mailpit, bound to loopback. This setup uses cached images and a 256 MiB
RAM filesystem for the database: **no database disk volume and no backups**. Stopping or recreating
the database discards its data, including enrolled MFA accounts. It is for invented values only.
The application runs on the host, so this setup does not build another application image.

## Start the dependencies

From this checkout, initialise once with the path of your patched candidate:

```bash
python3 evaluation/prepare.py /path/to/patched-candidate
docker compose -f evaluation/compose.yml --env-file evaluation/.env config --quiet
docker compose -f evaluation/compose.yml --env-file evaluation/.env up -d --wait
```

The initializer refuses to overwrite an existing development configuration or lab credentials.
Generated settings and login details are private, gitignored files. Open
`evaluation/.local/credentials.txt` locally for the `vault-admin` and `dev-user` passwords; do not
paste them into chat. The database is at `127.0.0.1:55432`; the mail viewer is
<http://127.0.0.1:18025>. No SMTP account or real email credentials are needed.

Both images are pinned by multi-architecture digest, so the lab runs on Apple Silicon and x86-64.
The first start on a machine downloads them (about 175 MB); later starts reuse the cached copies and
never download again.

## Build and run the candidate

In the **candidate checkout**, complete the [candidate gate](../docs/COMMUNITY_EVALUATION_SETUP.md#candidate-gate).
Starting these containers does not replace that gate. Only after restore, the Release build and
tests pass, run:

```bash
dotnet dev-certs https --trust
dotnet run --project EclipsVault.Web -c Release --no-build --launch-profile https
```

macOS asks for your password to trust the certificate. If the browser still rejects
`https://localhost:7443` afterwards, or the app logs "The ASP.NET Core developer certificate is not
trusted", more than one development certificate exists: run `dotnet dev-certs https --clean`, then
`--trust` again, and restart the app.

On the first start against a new database the log shows two `ERR Failed executing DbCommand` lines
for `__EFMigrationsHistory`. That is EF Core looking for a table that does not exist yet; the next
line applies the migrations. Wait for `Application started` before opening the browser.

Open <https://localhost:7443>. Sign in as `vault-admin` and enroll MFA; create a fresh Standard,
PHOENIX test user and the fixtures from [evaluation preparation](../docs/COMMUNITY_EVALUATION_SETUP.md).
Run [T1–T7](../docs/COMMUNITY_WALKTHROUGH.md) and record the results in the
[feedback template](../docs/COMMUNITY_FEEDBACK.md). For an application restart check, stop only the
`dotnet run` process and run it again while PostgreSQL stays running; verify the dummy value and MFA
still work. The audit signing key is ephemeral in this Development lab, so signing-key continuity
across application restarts is not assessed.

## Stop and discard the session

Stop the application with Ctrl+C, then run from this lab checkout:

```bash
docker compose -f evaluation/compose.yml --env-file evaluation/.env down
```

This removes only the lab's containers and network. It discards the RAM database and captured emails;
cached images and the small local settings/key-ring files remain for reuse. The next start creates a
fresh database and requires MFA enrollment again. No backup or restore rehearsal is performed here.
Old authenticator codes belong to the discarded enrollment; enroll again on the new database.
Retire the old authenticator entry once you have confirmed which entry belongs to the new session.
Avoid Docker-wide prune commands: unrelated projects may share the cached images.
