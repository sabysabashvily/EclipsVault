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

Copy `EclipsVault.Web/appsettings.Development.json.template` to
`EclipsVault.Web/appsettings.Development.json`. That copy is never committed. Generate the two
seed passwords on the machine that will run the vault, and put them in that file as
`Seed:AdminPassword` and `Seed:DevPassword`, or pass `Seed__AdminPassword` and `Seed__DevPassword`
in the shell. Keep them in that file or that shell. Do not write them into this document, a slide,
a ticket, or a picture.

The passwords published in `DbSeeder.cs` are used only when `Seed:AdminPassword` and
`Seed:DevPassword` are absent from configuration as a whole. Unsetting the environment variables
is not enough: a copied `appsettings.Development.json` can still set them. Do not display the
published passwords either. Production refuses that published administrator password. Source:
`DbSeeder.cs`; `DbSeederTests.Production_refuses_the_development_password_published_in_this_repository`.

Generate a master key in the shell and leave it there, for example
`export ECLIPSVAULT_KEK="$(openssl rand -base64 32)"`. The committed `appsettings.json` turns off
the development key fallbacks (`Crypto:AllowDevelopmentKekFallback` is false), so the process
needs this variable. Do the same for `ECLIPSVAULT_AUDIT_SIGNING_KEY` if you set one. Never write
a real key or password into `appsettings.Development.json` beyond the disposable seed passwords
for this run, and never paste a key into this document.

პაროლები და მასტერ-გასაღები ამ გაშვების ფაილში ან გარსში დარჩეს. `DbSeeder.cs`-ის გამოქვეყნებული
პაროლები მხოლოდ მაშინ გამოიყენება, როცა `Seed:AdminPassword` და `Seed:DevPassword` კონფიგურაციაში
საერთოდ არ არის. გარემოს ცვლადების მოხსნა საკმარისი არ არის, თუ
`appsettings.Development.json` მათ აყენებს. მასტერ-გასაღები გარსში შექმენით, მაგალითად
`export ECLIPSVAULT_KEK="$(openssl rand -base64 32)"`, რადგან ჩადებული `appsettings.json`
განვითარების გასაღების სარეზერვო გზებს თიშავს. ნამდვილი გასაღები ან პაროლი ამ დოკუმენტში ნუ
ჩაწერთ.

First sign-in enrolls an authenticator. Do that with the projector off, before the room is
watching. Do not photograph the QR code, the recovery codes, the cookie store, or any private key.
Do not print `ECLIPSVAULT_KEK` or `ECLIPSVAULT_AUDIT_SIGNING_KEY`.

## Setup draft

Use a compose project name that is not the checkout's default. The default project uses the
volume `eclipsvault-postgres`. A later reset of the talk project must not delete that volume.
Source: `README.md` on named volumes, and the `volumes` block in `docker-compose.yml`.

`docker-compose.yml` sets fixed `container_name` values, including `eclipsvault-postgres` and the
other services. Those names are not project-scoped. A second project started with
`-p eclipsvault-defence-draft` fails while the ordinary containers exist. Stop the ordinary
project first, without deleting its volume:

```sh
docker compose down
docker compose -p eclipsvault-defence-draft up -d --wait
```

`docker compose down` has no `-v`, so the ordinary volume is kept. Do not add `-v` to that first
command.

`docker-compose.yml` აყენებს ფიქსირებულ `container_name` მნიშვნელობებს, მათ შორის
`eclipsvault-postgres`-ს. ეს სახელები პროექტზე არ არის მიბმული. სანამ ჩვეულებრივი კონტეინერები
დგას, `-p eclipsvault-defence-draft` ვერ გაეშვება. ჯერ გააჩერეთ ჩვეულებრივი პროექტი `docker
compose down`-ით, `-v`-ის გარეშე, რომ მისი ტომი შენარჩუნდეს, და მხოლოდ შემდეგ გაუშვით
სახელდებული პროექტი.

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
5. Open `/Audit`. The log lists the newest row first. Find the row labelled `Revealed` for
   `Phoenix_Dev_Database_Password` and `dev-user`. The `Viewed` row below it is the details load.
   Source: `AuditLogReader.ListRecentAsync`; `AuditDisplay`.
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
`ECLIPSVAULT_KEK`, and `ECLIPSVAULT_AUDIT_SIGNING_KEY` in that shell. Do not delete a Docker image
you did not build for this run.

ასევე გააჩერეთ `dotnet` პროცესი და ამ გარსში მოხსენით `Seed__AdminPassword`, `Seed__DevPassword`,
`ECLIPSVAULT_KEK` და `ECLIPSVAULT_AUDIT_SIGNING_KEY`.

If the talk was pointed at the ordinary development volume by mistake, do not run `down -v` to
"clean the demo". That deletes the development database. Leave it, and say the reset was not done.

## After a real run

Write the revision you actually started, the time, and whether setup and reset succeeded, on slide 7
of [storyboard.md](storyboard.md). Until those cells are filled from a run, the talk says the
journey is a script.
