# Demonstration setup and reset / დემონსტრაციის მომზადება და საწყის მდგომარეობაში დაბრუნება

The demonstration is a disposable Development vault. It is not the production install in
`docs/INSTALL.md` or `deploy/production/`, and it is not the Playwright stack in `AGENTS.md`.

The walk below was run on `origin/main` `1298958050e45f83aefeb4b6455d0a02169ab22a` on 24 November
2026, finishing at about 16:03 Asia/Tbilisi. That run was port-shifted: a standalone PostgreSQL
container and Web on `https://localhost:7543`, because the owner's development vault held port 7443
and the development Compose stack was running under its fixed container names. It signed in and
enrolled both seeded accounts, opened `Phoenix_Dev_Database_Password`, revealed it, left that page,
and found `Revealed` and `Viewed` on `/Audit`. The screenshots in [screens/](screens/) are from that
run. The Compose-project setup in this file was last run on `d576dd9` on 21 November and was not
re-run on `1298958`.

დემონსტრაციისთვის გამოიყენება ერთჯერადი საცავი Development გარემოში. `docs/INSTALL.md`-სა და
`deploy/production/`-ში აღწერილი Production განთავსება და `AGENTS.md`-ში აღწერილი Playwright სტეკი
დემონსტრაციის ფარგლებს გარეთაა.

ქვემოთ აღწერილი რეპეტიცია ჩატარდა 2026 წლის 24 ნოემბერს `origin/main`-ის რევიზიაზე
`1298958050e45f83aefeb4b6455d0a02169ab22a` და დაახლოებით 16:03-ზე დასრულდა (Asia/Tbilisi).
ამ გაშვებისთვის სხვა პორტები იყო გამოყოფილი. PostgreSQL ცალკე კონტეინერში გაეშვა, Web კი მისამართზე
`https://localhost:7543`, რადგან პორტი 7443 მფლობელის სამუშაო საცავს ეკავა და სამუშაო Compose
სტეკი უკვე გაშვებული იყო კონტეინერების ფიქსირებული სახელებით. რეპეტიციაზე ორივე წინასწარ შექმნილი
ანგარიშით შესრულდა შესვლა და დარეგისტრირდა ავთენტიფიკატორი. გაიხსნა
`Phoenix_Dev_Database_Password`-ის დეტალები. მნიშვნელობის გამოჩენის შემდეგ დეტალების გვერდი დაიხურა,
ხოლო `/Audit`-ზე მოიძებნა `Revealed` და `Viewed` ჩანაწერები. საქაღალდეში [screens/](screens/)
ამ გაშვების ეკრანის ანაბეჭდებია. ამ ფაილში აღწერილი Compose პროექტი ბოლოს 21 ნოემბერს გაეშვა
`d576dd9`-ზე. `1298958`-ზე ეს გაშვება აღარ გამეორებულა.

## Story the stack must support

| Item | Value | Source |
|---|---|---|
| Project | `PHOENIX` | `DbSeeder.SeedDevelopmentDataAsync` |
| Member | username `dev-user`, display name Dev User, clearance Standard, project `PHOENIX` | same |
| Administrator | username `vault-admin`, display name Vault Administrator, clearance Top Secret, project `GLOBAL` | same |
| Secret | `Phoenix_Dev_Database_Password`, Development, Internal, not a honey-token | same |
| Also on this account's list | `Phoenix_Ephemeral_Deploy_Token`, until it expires five minutes after seeding. Its red expiry badge rounds up to whole hours on the last day, so it read "In 1 hour" right after the seed. Do not open it | same; `shared/expiry.ts` |
| Do not open | `Production_AWS_Root_Key`, `Global_SQL_SA_Password` | same, `IsHoneyToken`. They are omitted from the list |
| After sign-in | `/Secrets`, not `/` | `SignInTarget.Default`; ADR-0032 |

`dev-user` sees two active secrets right after the reset, and one once the ephemeral token expires.
Right after the reset the page says "2 active secrets · 1 expires within 7 days. Opening one records
an audit entry." The recorded entry for opening the details page is `Viewed`, and only after the
access check allows it.

The seeded secret values are synthetic and already in `DbSeeder.cs`. Do not copy them onto a slide
or into a screenshot. The talk needs the name, not the value.

## Credentials

Generate the two seed passwords on the machine that will run the vault and pass
`Seed__AdminPassword` and `Seed__DevPassword` in the process environment. Keep the generated values
in that shell. Do not write them into this document, a slide, a ticket, or a picture. If you copy
`EclipsVault.Web/appsettings.Development.json.template` to `appsettings.Development.json`, that copy
is never committed, and its placeholders are not passwords to type in the room.

The passwords published in `DbSeeder.cs` are used only when `Seed:AdminPassword` and
`Seed:DevPassword` are absent from configuration as a whole. Unsetting the environment variables
is not enough: a copied `appsettings.Development.json` can still set them. Do not display the
published passwords either. Production refuses that published administrator password. Source:
`DbSeeder.cs`; `DbSeederTests.Production_refuses_the_development_password_published_in_this_repository`.

Generate a master key in the shell and leave it there:

```sh
export ECLIPSVAULT_KEK="$(openssl rand -base64 32)"
```

Do not print the variable. The template's `DevelopmentKekBase64` is a placeholder, not a key.

With `AuditSigning:AllowDevelopmentEphemeralKey` true (the template sets it; the 24 November run
passed `AuditSigning__AllowDevelopmentEphemeralKey=true`), no `ECLIPSVAULT_AUDIT_SIGNING_KEY` is
needed. The audit page then shows a key id that changes when the process restarts. That id is not the
private key. Do not print a signing key if you set one.

Two settings matter on `1298958`:

- `Database__Provider=Postgres` is required. Without `Database:Provider` the process stops at
  startup with "Database:Provider is not set"; an unknown value stops it too. The template sets it,
  and the commands below pass it explicitly. Source: `DependencyInjection`; `DatabaseProvider`.
- `Identity__PublicUrl` is not needed to start. It is the address invitation links are built from,
  and New user and Resend stay unavailable without it and real SMTP. The talk sends no invitation.
  The commands below set it to the vault's own address so the setting is visible. Source:
  `UserAdminService`; `AccountInvitationDelivery`; ADR-0033.

პაროლები და მასტერ-გასაღები მხოლოდ ამ გაშვებისთვის გახსნილი ტერმინალის გარემოში შეინახეთ.
შაბლონში `DevelopmentKekBase64` მხოლოდ შესავსები ადგილის აღმნიშვნელია. `ECLIPSVAULT_KEK`-ის
მნიშვნელობა ტერმინალში შექმენით. გასაღები ან პაროლი ამ დოკუმენტში ნუ ჩაწერთ.
`Database__Provider=Postgres` აუცილებელია. პარამეტრის გარეშე პროცესი გაშვებისას ჩერდება.
`Identity__PublicUrl` გაშვებისთვის აუცილებელი არ არის. საცავი ამ მისამართიდან ადგენს მოწვევის
ბმულს. დემონსტრაციისას მოწვევა არ იგზავნება.

First sign-in enrolls an authenticator. The heading is "Set up two-factor authentication" and the
button is "Confirm enrollment". Do that with the projector off, before the room is watching. Do
not photograph the QR code, the manual key, the recovery codes, the cookie store, or any private
key. A later sign-in shows "Two-factor verification", the field "Authenticator code", and the
button "Verify and sign in".

## Setup

### Compose project, as run on `d576dd9`

Use a compose project name that is not the checkout's default. The default project uses the volume
`eclipsvault-postgres`. A later reset of the talk project must not delete that volume.

`docker-compose.yml` sets fixed `container_name` values, including `eclipsvault-postgres`. Those
names are not project-scoped. A second project cannot start while those containers exist.

```sh
docker compose down
docker compose -p eclipsvault-defence-draft up -d --wait
```

What the 21 November rehearsal observed:

- `docker compose down` exits 0 and does not stop containers that were started with
  `-p eclipsvault-defence-draft`. Run it only to stop a project that was started without `-p`.
  Do not add `-v` to that command.
- `docker compose -p eclipsvault-defence-draft up -d --wait` exits 0. It starts PostgreSQL, Redis,
  Vault, Mailpit, and Keycloak, and waits until their health checks pass. The first run downloads
  the images.

### Standalone database, as run on `1298958`

When the development stack must stay up, start one PostgreSQL container of your own, on a free
port, with a password generated in the shell. The image is the one `docker-compose.yml` pins.
`--rm` removes the container and its anonymous data volume when it stops.

```sh
export EV_DEMO_PG_PASSWORD="$(openssl rand -hex 16)"
docker run -d --rm --name ev-defence-refresh-pg -p 127.0.0.1:55432:5432 \
  -e POSTGRES_USER=eclipsvault -e POSTGRES_PASSWORD="$EV_DEMO_PG_PASSWORD" -e POSTGRES_DB=evdefence \
  postgres:17@sha256:2d2b8998d31037bf721cfdf764d76ba74171b4fab3431b7f72c27c56ddbdf9e3
```

### Build

Build the Angular UI before starting Web, on the Node version in `EclipsVault.AngularUi/.nvmrc`
(`24.22.0`; the 24 November run used it through nvm). `npm run build` stops to ask about Angular
analytics unless `CI=true` is set. With that variable the build exits 0 and writes
`EclipsVault.Web/AngularUi/index.html`. Without that file the Angular routes throw "The Angular UI
is not built".

```sh
cd EclipsVault.AngularUi
CI=true npm ci --ignore-scripts
CI=true npm run build
cd ..
dotnet restore --locked-mode
dotnet build EclipsVault.slnx -c Release
```

On 24 November the Release build finished with 0 warnings and 0 errors.

`dotnet dev-certs https` had already created a localhost certificate on this machine. Kestrel serves
`https://localhost:7443`, or the port in `ASPNETCORE_URLS`, with that certificate.

### Start Web

Start the built dll from `EclipsVault.Web`, as Development, so the start does not rebuild.
Development migrates and seeds on startup. For the Compose project, use port 5432, the database and
the password from `appsettings.Development.json.template`. For the standalone database, use the
values from the `docker run` above:

```sh
cd EclipsVault.Web
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=https://localhost:7443 \
AllowedHosts=localhost \
Database__Provider=Postgres \
ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=55432;Database=evdefence;Username=eclipsvault;Password=$EV_DEMO_PG_PASSWORD" \
Identity__PublicUrl=https://localhost:7443 \
AuditSigning__AllowDevelopmentEphemeralKey=true \
Email__Sender=Log \
Redis__Enabled=false \
dotnet bin/Release/net10.0/EclipsVault.Web.dll
```

`Seed__AdminPassword`, `Seed__DevPassword`, and `ECLIPSVAULT_KEK` come from the shell's exported
variables. The 24 November run used port 7543 in both URLs because 7443 was taken; use 7443 when it
is free. The log lines to wait for are
`Seeded staff accounts vault-admin and dev-user (TOTP enrollment happens on first sign-in)`, then
`Now listening on: https://localhost:7443`. The same log also says it seeded four dynamic-secret
roles: `phoenix_db_reader`, `global_db_writer`, `phoenix_pg_reader`, and `global_pg_writer`.

`Email__Sender=Log` keeps the committed email default, so nothing reaches a Mailpit that belongs to
another stack. The copied template sets `Smtp` instead. Redis stays off either way.

## Live clicks

Light theme: the sign-in page has a moon button, "Switch color theme". Inside the app, the account
menu says "Switch to dark" when the page is already light. Set the `EclipsVault.Theme=light` cookie
before the room if the machine opens dark. The captures in [screens/](screens/) are light, 1280 px
wide.

1. Open `https://localhost:7443/Account/Login`. The page is Angular. It says "Step 1 of 2", the
   heading is "Sign in", and the text is "Enter your password first. An authenticator code is always
   required next." The fields are "Username or email" and "Password". The buttons are "Continue"
   and "Sign in with a passkey". Sign in as `dev-user`.
2. Complete enrollment off camera if the heading "Set up two-factor authentication" appears. The
   next page is `/Secrets`, heading "Secrets".
3. The filter's accessible name is "Filter secrets" and its placeholder is "Filter by name,
   project, environment…". Filter to `Phoenix_Dev_Database_Password`. One row remains: the name as
   a link, `PHOENIX` under it, environment `Development`, sensitivity `Internal`, expires
   "No expiry". There is no separate Open button.
4. Click the name. The URL is `/Secrets/Details/{id}`. The id changes after a reset. The page shows
   the round "Back to list" arrow, the chips `Development`, `Internal`, `PHOENIX`, and the controls
   "Reveal value", "Rotate value", "Share access", and "Delete". The value is not on the page yet.
   Press "Reveal value". For this Internal secret the page does not ask for a code.
5. Press "Back to list" before any capture. The filter clears, so the short-lived token is visible
   again unless it has expired. Do not open it.
6. Open the account card "Dev User" / "Standard" at the bottom of the sidebar and press "Sign out".
   The next page is `/Account/Login`.
7. Sign in as `vault-admin` and complete that account's enrollment off camera. Open "Audit log"
   under "Administration", or `/Audit`. A notice says the vault is running unlicensed and that this
   changes nothing about how the vault runs. Beside the heading "Audit log" a badge says
   "Not checked". "Latest checkpoint" says "none yet — create one to attest the current trail". The
   buttons are "Verify integrity", "Create signed checkpoint", and "Export signed bundle". Do not
   press them.
8. The filter's accessible name is "Filter audit entries". Filter to
   `Phoenix_Dev_Database_Password`. The rows, newest first, are `Revealed` by `dev-user`, `Viewed`
   by `dev-user`, and `Created` by `system` with details `EntityState=Added`. "When (UTC)" shows
   full timestamps. The source address on this machine was `::1`. The `Revealed` and `Viewed` rows
   have an empty details cell. Point at `Revealed`.

The Attestation panel's own sentence says an outside auditor can prove the trail "even if rows were
deleted and the hashes rewritten". Do not read it aloud as covering deleted checkpoints or rows after
the latest checkpoint. Slide 6 states the boundary.

If the secret is missing, the disposable database was not empty at seed time, or the process was
not Development. Do not point the talk at a database you use for ordinary work.

## Capture rules

- No screenshot or recording of enrollment, recovery codes, cookies, or key material.
- No screenshot of the revealed value. The audit row is the picture.
- No honey-token, even to show the warning.
- No service-account key issuance. The raw key is shown once by the product.
- No invitation link or activation page. Its fragment holds the token.
- No production connection string, and no copy of `appsettings.Development.json`.
- The audit page's key id is a public id. It is not `ECLIPSVAULT_AUDIT_SIGNING_KEY`.

## Reset

Stop the Web process you started, by its process id. Then reset only what was created for the talk.

Compose project, as run on 21 November:

```sh
docker compose -p eclipsvault-defence-draft down -v
```

That command removed the containers and the volumes
`eclipsvault-defence-draft_eclipsvault-postgres` and
`eclipsvault-defence-draft_eclipsvault-redis`. It does not remove a volume belonging to a project
started without `-p`. `docker compose down` without `-p`, run while the named project was up,
exited 0 and left those containers running. It is not the reset.

Standalone database, as run on 24 November:

```sh
docker stop ev-defence-refresh-pg
```

On 24 November that removed the container and its anonymous volume; `docker ps -a` and
`docker volume inspect` found neither afterwards. The development stack kept running.

Start again with the same environment and wait for the seed line. Each reset needs both accounts
enrolled again and gives the secret a new id. Unset `Seed__AdminPassword`, `Seed__DevPassword`,
`ECLIPSVAULT_KEK`, `EV_DEMO_PG_PASSWORD`, and `ECLIPSVAULT_AUDIT_SIGNING_KEY` in that shell when the
day is over. Do not delete a Docker image you did not build for this run.

რეპეტიციისთვის გაშვებული Web პროცესი მისი იდენტიფიკატორით გააჩერეთ. დღის ბოლოს ტერმინალის
გარემოდან ამოშალეთ `Seed__AdminPassword`, `Seed__DevPassword`, `ECLIPSVAULT_KEK`,
`EV_DEMO_PG_PASSWORD` და `ECLIPSVAULT_AUDIT_SIGNING_KEY`. Compose პროექტის საწყის მდგომარეობაში
დასაბრუნებლად გაუშვით `docker compose -p eclipsvault-defence-draft down -v`. `docker compose down`
პარამეტრ `-p`-ის გარეშე ამ პროექტს არ აჩერებს. ცალკე გაშვებული ბაზისთვის გამოიყენეთ
`docker stop ev-defence-refresh-pg`. `--rm` პარამეტრის გამო კონტეინერიც წაიშლება და მისი მონაცემების
ტომიც.

If the talk was pointed at the ordinary development volume by mistake, do not run `down -v` to
"clean the demo". That deletes the development database. Leave it, and say the reset was not done.
