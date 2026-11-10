# Demonstration setup and reset / დემონსტრაციის მომზადება და დაბრუნება

This is a draft for a later, separate run. No command here was executed while writing this folder.
Do not treat a successful reading of these steps as a started vault.

The demonstration is a disposable Development vault. It is not the production install in
`docs/INSTALL.md`, and it is not the Playwright stack in `AGENTS.md`.

ეს მოგვიანო, ცალკე გაშვების მონახაზია. ამ საქაღალდის წერისას აქედან არც ერთი ბრძანება არ გაშვებულა.
ამ ნაბიჯების წაკითხვა გაშვებულ საცავად ნუ ჩათვალოთ.

დემონსტრაცია ერთჯერადი Development საცავია. ის არ არის `docs/INSTALL.md`-ის production დაყენება და
არც `AGENTS.md`-ის Playwright სტეკი.

## Story the stack must support

| Item | Value | Source |
|---|---|---|
| Project | `PHOENIX` | `DbSeeder.SeedDevelopmentDataAsync` |
| Member | username `dev-user`, clearance Standard, project `PHOENIX` | same |
| Administrator | username `vault-admin`, clearance TopSecret, project `GLOBAL` | same |
| Secret | `Phoenix_Dev_Database_Password`, Development, Internal, not a honey-token | same |
| Do not open | `Production_AWS_Root_Key`, `Global_SQL_SA_Password` | same, `IsHoneyToken` |
| Do not use as the talk secret | `Phoenix_Ephemeral_Deploy_Token` | expires about five minutes after seeding |

The seeded secret values are synthetic and already in `DbSeeder.cs`. Do not copy them onto a slide
or into a screenshot. The talk needs the name, not the value.

## Credentials

Generate the two seed passwords on the machine that will run the vault. Keep them in the shell of
that run. Do not write them into this file, a slide, a ticket, or a picture.

If `Seed__AdminPassword` and `Seed__DevPassword` are unset, Development uses the passwords published
in `DbSeeder.cs`. Do not display those either. Production refuses that published administrator
password. Source: `DbSeederTests.Production_refuses_the_development_password_published_in_this_repository`.

First sign-in enrolls an authenticator. Do that with the projector off, before the room is
watching. Do not photograph the QR code, the recovery codes, the cookie store, or any private key.
`ECLIPSVAULT_KEK` and `ECLIPSVAULT_AUDIT_SIGNING_KEY` stay in the environment of the process. Do
not print them. Development can fall back to a local key only in the way `appsettings` and the
seeder already describe; do not invent a key and paste it here.

## Setup draft

Use a compose project name that is not the checkout's default. The default project shares the
volume `eclipsvault-postgres` with ordinary development, and a later reset would delete that data.
Source: `README.md` on named volumes, and the `volumes` block in `docker-compose.yml`.

From the repository root, the shape is:

```sh
docker compose -p eclipsvault-defence-draft up -d --wait
```

Then build the Angular UI from `EclipsVault.AngularUi` with the Node version in its `.nvmrc`,
because some pages are Angular even though the secret and audit pages are still Legacy. Source:
`README.md` "Run it"; `AngularUiRoutes`.

Start Web as Development, with `Database__Provider=Postgres` and the compose file's PostgreSQL
connection string. An unset provider selects SQL Server and will not use this PostgreSQL.
Source: `README.md`; `DependencyInjection` default; `DatabaseProvider`.

Pass disposable `Seed__AdminPassword` and `Seed__DevPassword` in that process's environment.
Development migrates and seeds on startup. Source: `DatabaseMigrator`; `DbSeeder`.

Listen on the Development URL the README already uses, `https://localhost:7443`, after the local
development certificate trust step in the README. This draft does not add a new port or a new
configuration file.

Redis, Mailpit, Vault, and Keycloak come up with the compose file. The committed
`appsettings.json` leaves Redis disabled and the crypto engine on `AesGcmLocal`. Leave them that
way for this talk so the picture matches the default code. Source: `appsettings.json`.

## Live clicks, once a checked vault exists

1. Sign in as `dev-user`. Complete the second factor off camera if enrollment is still pending.
2. Open Secrets, open `Phoenix_Dev_Database_Password`, and reveal it. Confirm the page is the
   Legacy details page.
3. Move to another page before any capture. The value is on that details page. Source:
   `SecretsController.Reveal`.
4. Sign out. Sign in as `vault-admin` and complete that account's second factor off camera.
5. Open `/Audit`. Find the row labelled `Revealed` for `Phoenix_Dev_Database_Password` and
   `dev-user`. A `Viewed` row for the same name is the details load. Source: `AuditDisplay`.
6. Stop. Do not press Verify and call the result a checkpoint proof. Do not press Export.

If the secret is missing, the disposable database was not empty at seed time, or the process was
not Development. Do not point the talk at a database you use for ordinary work in order to "make
the row appear".

## Capture rules

- No screenshot or recording of enrollment, recovery codes, cookies, or key material.
- No screenshot of the revealed value. The audit row is the picture.
- No honey-token, even to show the warning.
- No service-account key issuance. The raw key is shown once by the product.
- No production connection string, and no copy of `appsettings.Development.json`.

These rules are instructions. This draft contains no evidence that a capture was checked, because
no capture was made.

## Reset draft

Reset only the compose project created for the talk:

```sh
docker compose -p eclipsvault-defence-draft down -v
```

That removes the containers and the volumes of `eclipsvault-defence-draft`. It does not remove the
checkout's ordinary `eclipsvault-postgres` volume, provided the project name was used at setup.
Source for the volume behaviour: `README.md`, "`docker compose down` leaves volumes; a command that
removes volumes deletes the data".

Also stop the `dotnet` process you started, and unset `Seed__AdminPassword`, `Seed__DevPassword`,
and `ECLIPSVAULT_KEK` in that shell. Do not delete a Docker image you did not build for this run.

If the talk was pointed at the ordinary development volume by mistake, do not run `down -v` to
"clean the demo". That deletes the development database. Leave it, and say the reset was not done.

## After a real run

Write the revision you actually started, the time, and whether setup and reset succeeded, on slide 7
of [storyboard.md](storyboard.md). Until those cells are filled from a run, the talk says the
journey is a script.
