# Demonstration setup and reset / დემონსტრაციის მომზადება და საწყის მდგომარეობაში დაბრუნება

Run the demonstration in a disposable Development vault. The production installation in
`docs/INSTALL.md` and `deploy/production/` and the Playwright stack in `AGENTS.md` are outside
the rehearsal scope.

The rehearsal on `origin/main` `1298958050e45f83aefeb4b6455d0a02169ab22a` ended on 24 November
2026 at about 16:03 Asia/Tbilisi. It used a standalone PostgreSQL container and Web at
`https://localhost:7543`. The owner's development vault occupied port 7443, and the development
Compose stack already occupied the fixed container names. The presenter signed in with both
seeded accounts and enrolled their authenticators. The presenter opened and revealed
`Phoenix_Dev_Database_Password`, left the details page, and found `Revealed` and `Viewed` on
`/Audit`. The screenshots in [screens/](screens/) are from that run. The last Compose-project
rehearsal used `d576dd9` on 21 November. The `1298958` rehearsal used the standalone setup only.

დემონსტრაციისთვის გამოიყენება ერთჯერადი საცავი Development გარემოში. `docs/INSTALL.md`-სა და
`deploy/production/`-ში აღწერილი Production განთავსება და `AGENTS.md`-ში აღწერილი Playwright სტეკი
დემონსტრაციის ფარგლებს გარეთაა.

რეპეტიცია ჩატარდა 2026 წლის 24 ნოემბერს `origin/main`-ის რევიზიაზე
`1298958050e45f83aefeb4b6455d0a02169ab22a` და დაახლოებით 16:03-ზე დასრულდა (Asia/Tbilisi).
PostgreSQL ცალკე კონტეინერში გაეშვა, Web კი სხვა პორტზე, მისამართზე
`https://localhost:7543`, რადგან პორტი 7443 მფლობელის სამუშაო საცავს ეკავა და სამუშაო Compose
სტეკი უკვე გაშვებული იყო კონტეინერების ფიქსირებული სახელებით. მომხსენებელი ორივე წინასწარ შექმნილი
ანგარიშით შევიდა და მათთვის ავთენტიფიკატორები დაარეგისტრირა. გახსნა `Phoenix_Dev_Database_Password`-ის
დეტალები, გამოაჩინა მნიშვნელობა და გვერდიდან გამოვიდა. შემდეგ `/Audit`-ზე იპოვა `Revealed` და
`Viewed`. ამ გაშვების ეკრანის ანაბეჭდები მოთავსებულია [screens/](screens/)-ში. Compose პროექტით ბოლო
რეპეტიცია 21 ნოემბერს `d576dd9`-ზე ჩატარდა. `1298958`-ზე მხოლოდ ცალკე კონტეინერი გაეშვა.

## Demo accounts and secrets

| Item | Value | Source |
|---|---|---|
| Project | `PHOENIX` | `DbSeeder.SeedDevelopmentDataAsync` |
| Member | username `dev-user`, display name Dev User, clearance Standard, project `PHOENIX` | same |
| Administrator | username `vault-admin`, display name Vault Administrator, clearance Top Secret, project `GLOBAL` | same |
| Secret | `Phoenix_Dev_Database_Password`, Development, Internal, not a honey-token | same |
| Also on this account's list | `Phoenix_Ephemeral_Deploy_Token`, until it expires five minutes after seeding. Its red expiry badge rounds up to whole hours on the last day, so it read "In 1 hour" right after the seed. Do not open it | same; `shared/expiry.ts` |
| Do not open | `Production_AWS_Root_Key`, `Global_SQL_SA_Password` | same, `IsHoneyToken`. They are omitted from the list |
| Default redirect after sign-in | `/Secrets`, not `/` | `SignInTarget.Default`; ADR-0032 |

`dev-user` sees two active secrets after the reset and one once the ephemeral token expires.
Before the expiry the page says "2 active secrets · 1 expires within 7 days. Opening one records
an audit entry." The recorded entry for opening the details page is `Viewed`, and only after the
access check allows it.

The seeded secret values are synthetic and already in `DbSeeder.cs`. Do not copy them onto a slide
or into a screenshot. Show the secret name on slides.

## Credentials

Generate the two seed passwords on the machine that will run the vault and pass
`Seed__AdminPassword` and `Seed__DevPassword` in the process environment. Keep the generated values
in that shell. Do not write them into this document, a slide, a ticket, or a picture. If you copy
`EclipsVault.Web/appsettings.Development.json.template` to `appsettings.Development.json`, that copy
is never committed, and its placeholders are not passwords to type in the room.

The passwords published in `DbSeeder.cs` are used only when `Seed:AdminPassword` and
`Seed:DevPassword` are absent from configuration as a whole. Unsetting the environment variables
can leave values set in a copied `appsettings.Development.json`. Keep the published passwords off
screen as well. Production refuses the published administrator password. See `DbSeeder.cs` and
`DbSeederTests.Production_refuses_the_development_password_published_in_this_repository`.

Generate a master key in the shell and leave it there:

```sh
export ECLIPSVAULT_KEK="$(openssl rand -base64 32)"
```

Keep the generated value out of terminal output. The template's `DevelopmentKekBase64` is a placeholder.

With `AuditSigning:AllowDevelopmentEphemeralKey` true (the template sets it; the 24 November run
passed `AuditSigning__AllowDevelopmentEphemeralKey=true`), no `ECLIPSVAULT_AUDIT_SIGNING_KEY` is
needed. The audit page shows a public key ID that changes when the process restarts. Keep any
signing key you set out of terminal output.

Settings for `1298958`:

- Set `Database__Provider=Postgres`. A missing `Database:Provider` stops startup with
  "Database:Provider is not set". An unknown value also stops startup. The template and the
  startup command both set the provider. See `DependencyInjection` and `DatabaseProvider`.
- Startup can proceed without `Identity__PublicUrl`. The vault builds invitation links from this
  address. New user and Resend both require it and real SMTP. The demo sends no invitation. The
  startup command uses the vault's address to show where to set it. See `UserAdminService`,
  `AccountInvitationDelivery`, and ADR-0033.

პაროლები და მასტერ-გასაღები მხოლოდ ამ გაშვებისთვის გახსნილი ტერმინალის გარემოში შეინახეთ.
ტერმინალში შექმენით `ECLIPSVAULT_KEK`-ის მნიშვნელობა. `DevelopmentKekBase64` შაბლონში შესავსები
ველის აღმნიშვნელია. გასაღები ან პაროლი ამ დოკუმენტში ნუ ჩაწერთ.
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

Choose a separate Compose project name for the rehearsal so its reset leaves the default project's
`eclipsvault-postgres` volume intact.

`docker-compose.yml` sets fixed `container_name` values, including `eclipsvault-postgres`. Those
names are not project-scoped. A second project cannot start while those containers exist.

```sh
docker compose down
docker compose -p eclipsvault-defence-draft up -d --wait
```

Commands tested on 21 November:

- `docker compose down` exits 0 and does not stop containers that were started with
  `-p eclipsvault-defence-draft`. Run it only to stop a project that was started without `-p`.
  Do not add `-v` to that command.
- `docker compose -p eclipsvault-defence-draft up -d --wait` exits 0. It starts PostgreSQL, Redis,
  Vault, Mailpit, and Keycloak, and waits until their health checks pass. The first run downloads
  the images.

### Standalone database, as run on `1298958`

When the development stack must stay up, start one PostgreSQL container of your own, on a free
port, with a password generated in the shell. The image is the one `docker-compose.yml` pins.
When the container stops, `--rm` removes it and its anonymous data volume.

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

Run the built DLL from `EclipsVault.Web` in Development to reuse the existing build.
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
variables. Use port 7443 when it is free. On 24 November the rehearsal used port 7543 in both
URLs because 7443 was occupied. Wait for the log lines
`Seeded staff accounts vault-admin and dev-user (TOTP enrollment happens on first sign-in)`, then
`Now listening on: https://localhost:7443`. The same log also says it seeded four dynamic-secret
roles: `phoenix_db_reader`, `global_db_writer`, `phoenix_pg_reader`, and `global_pg_writer`.

`Email__Sender=Log` keeps the committed email default, so nothing reaches a Mailpit that belongs to
another stack. The copied template sets `Smtp` instead. Redis stays off either way.

## Demo steps

Use the light theme. The sign-in page has a moon button, "Switch color theme". Inside the app, the account
menu says "Switch to dark" when the page is already light. Set the `EclipsVault.Theme=light` cookie
before the audience arrives if the machine opens in dark mode. The screenshots in [screens/](screens/) use the light theme and are 1280 px
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

- Keep enrollment, recovery codes, cookies, and key material out of screenshots and recordings.
- Capture the audit row and leave the revealed value off camera.
- Skip honey-tokens and their warning.
- Skip service-account key issuance because the vault shows the raw key once.
- Keep the invitation link and activation page off screen. The link fragment holds the token.
- Keep production connection strings and copies of `appsettings.Development.json` out of the capture.
- The audit page shows a public key ID. Keep `ECLIPSVAULT_AUDIT_SIGNING_KEY` out of the capture.

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
exited 0 and left those containers running.

Standalone database, as run on 24 November:

```sh
docker stop ev-defence-refresh-pg
```

On 24 November that removed the container and its anonymous volume. Neither appeared in
`docker ps -a` or `docker volume inspect`. The development stack kept running.

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
