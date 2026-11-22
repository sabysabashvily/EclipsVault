# Demonstration setup and reset / დემონსტრაციის მომზადება და დაბრუნება

The demonstration is a disposable Development vault. It is not the production install in
`docs/INSTALL.md`, and it is not the Playwright stack in `AGENTS.md`.

These commands were run on `origin/main` `d576dd93a30bc461183ca7b71c89f038adbff446` on 21 November
2026. The second walk, after reset, finished at 23:39 Asia/Tbilisi. Both walks enrolled the two
seeded accounts, opened `Phoenix_Dev_Database_Password`, revealed it, left that page, and found
`Revealed` and `Viewed` on `/Audit`.

დემონსტრაცია ერთჯერადი Development საცავია. ის არ არის `docs/INSTALL.md`-ის production დაყენება და
არც `AGENTS.md`-ის Playwright სტეკი.

ეს ბრძანებები გაეშვა `origin/main` `d576dd93a30bc461183ca7b71c89f038adbff446`-ზე 21 ნოემბერს 2026.
მეორე გავლა, დაბრუნების შემდეგ, დასრულდა 23:39 Asia/Tbilisi-ზე. ორივე გავლამ ჩარიცხა ორი დათესილი
ანგარიში, გახსნა `Phoenix_Dev_Database_Password`, გამოაჩინა მნიშვნელობა, დატოვა ეს გვერდი და
`/Audit`-ზე იპოვა `Revealed` და `Viewed`.

## Story the stack must support

| Item | Value | Source |
|---|---|---|
| Project | `PHOENIX` | `DbSeeder.SeedDevelopmentDataAsync` |
| Member | username `dev-user`, display name Dev User, clearance Standard, project `PHOENIX` | same |
| Administrator | username `vault-admin`, display name Vault Administrator, clearance Top Secret, project `GLOBAL` | same |
| Secret | `Phoenix_Dev_Database_Password`, Development, Internal, not a honey-token | same |
| Also on this account's list | `Phoenix_Ephemeral_Deploy_Token`, badge `soon`, until it expires about five minutes after the reset. Do not open it | same |
| Do not open | `Production_AWS_Root_Key`, `Global_SQL_SA_Password` | same, `IsHoneyToken`. They are omitted from the list |
| After sign-in | `/Secrets`, not `/` | `AccountController.RedirectAfterSignIn` defaults to `/Secrets` |

`dev-user` sees two active secrets right after the reset, and one once the ephemeral token expires,
about five minutes later. Right after the reset the page says "2 active secrets. Opening one records
an audit entry." The recorded entry for opening the details page is `Viewed`, and only after the access
check allows it.

The seeded secret values are synthetic and already in `DbSeeder.cs`. Do not copy them onto a slide
or into a screenshot. The talk needs the name, not the value.

## Credentials

Copy `EclipsVault.Web/appsettings.Development.json.template` to
`EclipsVault.Web/appsettings.Development.json`. That copy is never committed. Generate the two
seed passwords on the machine that will run the vault and pass `Seed__AdminPassword` and
`Seed__DevPassword` in the process environment. The template's placeholders are not passwords to
type in the room. Keep the generated values in that shell. Do not write them into this document, a
slide, a ticket, or a picture.

The passwords published in `DbSeeder.cs` are used only when `Seed:AdminPassword` and
`Seed:DevPassword` are absent from configuration as a whole. Unsetting the environment variables
is not enough: a copied `appsettings.Development.json` can still set them. Do not display the
published passwords either. Production refuses that published administrator password. Source:
`DbSeeder.cs`; `DbSeederTests.Production_refuses_the_development_password_published_in_this_repository`.

Generate a master key in the shell and leave it there:

```sh
export ECLIPSVAULT_KEK="$(openssl rand -base64 32)"
```

The template turns `Crypto:AllowDevelopmentKekFallback` on, and its `DevelopmentKekBase64` is a
placeholder, not a key. The rehearsal set `ECLIPSVAULT_KEK`. The log then said the master key
loaded, and it did not warn that the development fallback was used. Do not print the variable.

The template also sets `AuditSigning:AllowDevelopmentEphemeralKey` to true. This rehearsal did not
set `ECLIPSVAULT_AUDIT_SIGNING_KEY`. The audit page shows a key id. That id changes when the
process restarts, and it is not the private key. Do not print a signing key if you set one.

პაროლები და მასტერ-გასაღები ამ გაშვების გარსში დარჩეს. შაბლონის `DevelopmentKekBase64`
ჩანაცვლებული ტექსტია, არა გასაღები, ამიტომ `ECLIPSVAULT_KEK` გარსში შექმენით. ნამდვილი გასაღები
ან პაროლი ამ დოკუმენტში ნუ ჩაწერთ.

First sign-in enrolls an authenticator. The heading is "Set up two-factor authentication" and the
button is "Confirm enrollment". Do that with the projector off, before the room is watching. Do
not photograph the QR code, the manual key, the recovery codes, the cookie store, or any private
key.

## Setup that worked

Use a compose project name that is not the checkout's default. The default project uses the volume
`eclipsvault-postgres`. A later reset of the talk project must not delete that volume.

`docker-compose.yml` sets fixed `container_name` values, including `eclipsvault-postgres`. Those
names are not project-scoped. A second project cannot start while those containers exist.

```sh
docker compose down
docker compose -p eclipsvault-defence-draft up -d --wait
```

What this rehearsal observed:

- `docker compose down` exits 0 and does not stop containers that were started with
  `-p eclipsvault-defence-draft`. Run it only to stop a project that was started without `-p`.
  Do not add `-v` to that command.
- `docker compose -p eclipsvault-defence-draft up -d --wait` exits 0. It starts PostgreSQL, Redis,
  Vault, Mailpit, and Keycloak, and waits until their health checks pass. The first run downloads
  the images.

Build the Angular UI before starting Web. On this machine Node was already `24.22.0`, the version
in `EclipsVault.AngularUi/.nvmrc`. `npm run build` stops to ask about Angular analytics unless
`CI=true` is set. With that variable the build exits 0 and writes
`EclipsVault.Web/AngularUi/index.html`. Without that file the Angular routes throw.

```sh
cd EclipsVault.AngularUi
CI=true npm ci --ignore-scripts
CI=true npm run build
cd ..
dotnet restore --locked-mode
dotnet build EclipsVault.slnx -c Release
```

`dotnet dev-certs https` had already created a localhost certificate here. The sign-in page is
served on `https://localhost:7443` with that certificate.

Start the built dll from `EclipsVault.Web`, as Development. This rehearsal did not execute
`dotnet run`; it used the Release dll so the start would not rebuild. Pass the provider, the
compose connection string, the two seed passwords, and `ECLIPSVAULT_KEK` in that process's
environment. Development migrates and seeds on startup. The log line to wait for is
`Seeded staff accounts vault-admin and dev-user`, then `Now listening on: https://localhost:7443`.

The copied template sets `Database:Provider` to `Postgres`, `Redis:Enabled` to false, and
`Email:Sender` to `Smtp`. The committed `appsettings.json` still has `Crypto:Engine` `AesGcmLocal`,
`Redis:Enabled` false, and `Email:Sender` `Log`. This run kept the template's Smtp setting.
Mailpit was up and no message was sent. Redis was not enabled. To match the committed email
default instead, set `Email__Sender=Log` in the same process environment before starting.

## Live clicks

Light theme: the account card says "Switch to dark" when the page is already light. Set the
`EclipsVault.Theme=light` cookie before the room if the machine opens dark. The captures in
[screens/](screens/) are light, 1280 px wide.

1. Open `https://localhost:7443/Account/Login`. The heading is "Sign in". The text says step 1 of
   2 is the password and an authenticator code is always required next. The buttons are "Continue"
   and "Sign in with a passkey". Sign in as `dev-user`.
2. Complete enrollment off camera if the heading "Set up two-factor authentication" appears. The
   next page is `/Secrets`, heading "Secrets".
3. The filter's accessible name is "Filter secrets" and its placeholder is "Filter by name,
   project, environment…". Filter to `Phoenix_Dev_Database_Password`. One row remains: project
   `PHOENIX`, environment `Development`, sensitivity `Internal`, expires `—`, button "Open".
4. Open it. The URL is `/Secrets/Details/{id}`. The id changes after a reset. The visible controls
   are "Back to list", "Reveal value", "Rotate value", "Delete", and "Share access". The value is
   not on the page yet. Press "Reveal value". For this Internal secret the page does not ask for a
   code.
5. Press "Back to list" before any capture. The filter clears, so the short-lived token is visible
   again unless it has expired. Do not open it.
6. Open the account card "Dev User" / "Standard" and press "Sign out". The next page is
   `/Account/Login`.
7. Sign in as `vault-admin` and complete that account's enrollment off camera. Open `/Audit`. The
   heading is "Audit log". The sidebar group is "Administration". A notice says the vault is
   running unlicensed and that this changes nothing about how the vault runs. "Latest checkpoint"
   says "none yet — create one to attest the current trail". The buttons are "Verify integrity",
   "Create signed checkpoint", and "Export signed bundle". Do not press them.
8. The filter's accessible name is "Filter audit entries". Filter to
   `Phoenix_Dev_Database_Password`. The rows, newest first, are `Revealed` by `dev-user`, `Viewed`
   by `dev-user`, and `Created` by `system` with details `EntityState=Added`. The source address on
   this machine was `::1`. The `Revealed` and `Viewed` rows have an empty details cell. Point at
   `Revealed`.

If the secret is missing, the disposable database was not empty at seed time, or the process was
not Development. Do not point the talk at a database you use for ordinary work.

## Capture rules

- No screenshot or recording of enrollment, recovery codes, cookies, or key material.
- No screenshot of the revealed value. The audit row is the picture.
- No honey-token, even to show the warning.
- No service-account key issuance. The raw key is shown once by the product.
- No production connection string, and no copy of `appsettings.Development.json`.
- The audit page's key id is a public id. It is not `ECLIPSVAULT_AUDIT_SIGNING_KEY`.

The captures from the second walk are in [screens/](screens/).

## Reset that worked

Stop the Web process you started, by its process id. Then reset only the compose project created
for the talk:

```sh
docker compose -p eclipsvault-defence-draft down -v
```

That command removed the containers and the volumes
`eclipsvault-defence-draft_eclipsvault-postgres` and
`eclipsvault-defence-draft_eclipsvault-redis`. It does not remove a volume belonging to a project
started without `-p`.

`docker compose down` without `-p`, run while the named project was up, exited 0 and left those
containers running. It is not the reset.

Start the named project again with `up -d --wait`, start the Web process again with the same
environment, and wait for the seed line. The second walk enrolled both accounts again and opened a
new secret id. Unset `Seed__AdminPassword`, `Seed__DevPassword`, `ECLIPSVAULT_KEK`, and
`ECLIPSVAULT_AUDIT_SIGNING_KEY` in that shell when the day is over. Do not delete a Docker image
you did not build for this run.

ასევე გააჩერეთ Web პროცესი მისი პროცესის ნომრით და ამ გარსში მოხსენით `Seed__AdminPassword`,
`Seed__DevPassword`, `ECLIPSVAULT_KEK` და `ECLIPSVAULT_AUDIT_SIGNING_KEY`. დაბრუნება არის
`docker compose -p eclipsvault-defence-draft down -v`. `-p`-ის გარეშე `docker compose down` ამ
პროექტს არ აჩერებს.

If the talk was pointed at the ordinary development volume by mistake, do not run `down -v` to
"clean the demo". That deletes the development database. Leave it, and say the reset was not done.
