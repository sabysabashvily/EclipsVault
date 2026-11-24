# Rehearsal checklist / რეპეტიციის საკონტროლო სია

The 24 November rehearsal ran on `origin/main` `1298958050e45f83aefeb4b6455d0a02169ab22a`.
The presentation limit is 30 minutes. The demo used a disposable vault on a different port and
finished on 24 November 2026 at about 16:03 Asia/Tbilisi. See
[demo-setup-reset.md](demo-setup-reset.md).

24 ნოემბრის რეპეტიცია ჩატარდა `origin/main`-ის რევიზიაზე
`1298958050e45f83aefeb4b6455d0a02169ab22a`. პრეზენტაციისთვის მაქსიმუმ 30 წუთია გამოყოფილი.
დემონსტრაციისთვის ერთჯერადი საცავი სხვა პორტზე გაეშვა. რეპეტიცია 2026 წლის 24 ნოემბერს,
დაახლოებით 16:03-ზე დასრულდა (Asia/Tbilisi). მომზადების ინსტრუქცია მოცემულია
[demo-setup-reset.md](demo-setup-reset.md)-ში.

## Pre-flight / რეპეტიციამდე შესამოწმებელი საკითხები

- [ ] Revision is `1298958` or the commit you will name on slide 7.
- [ ] The talk's PostgreSQL is up and healthy: the Compose project `eclipsvault-defence-draft`, or
      the standalone container `ev-defence-refresh-pg`.
- [ ] `CI=true npm run build` in `EclipsVault.AngularUi` has written `EclipsVault.Web/AngularUi/index.html`.
- [ ] Web is Development on `https://localhost:7443` or the free port chosen for the walk, with
      `Database__Provider=Postgres` (required),
      the connection string, `Identity__PublicUrl`, `Seed__AdminPassword`, `Seed__DevPassword`, and
      `ECLIPSVAULT_KEK`.
- [ ] `/Account/Login` shows "Step 1 of 2" and "Sign in". Both accounts are already enrolled, off camera.
- [ ] Theme is light. The account menu says "Switch to dark".
- [ ] Projector is on the sign-in page, with both fields empty.

## Run order / დემონსტრაციის თანმიმდევრობა

1. Explain in one sentence why a shared file cannot show who opened a password. Keep the product
   off screen.
2. Explain the four rules in `SecretAccessPolicy`. The time and network rules do not apply to
   this Internal Development secret. Skip any improvised denial on loopback.
3. Show the context, container, and Web component diagrams in that order in
   [diagrams.html](diagrams.html). The browser UI and sign-in run on Angular. Core and
   Infrastructure are modules inside Web.
4. Explain that the server checks access before writing `Viewed`. After "Reveal value", it
   writes `Revealed` before decryption.
5. Run the demo.
   - Enter `dev-user`, press Continue, enter the authenticator code, and press Verify and sign in.
     The vault opens `/Secrets`.
   - Filter for `Phoenix_Dev_Database_Password`. Click the name, press Reveal value, and press Back to list.
   - Open the account card and press Sign out.
   - Enter `vault-admin`, press Continue, enter the authenticator code, and press Verify and sign in.
     Under Administration, open Audit log.
   - Filter the same name. Point at `Revealed` for `dev-user`, then `Viewed` under it.
6. Point out that Latest checkpoint says "none yet". An outside auditor can verify a bundle they
   already hold. Some deletions can escape detection. Leave Verify integrity, Create signed
   checkpoint, and Export signed bundle alone.
7. Read only the filled cells on slide 7 of [storyboard.md](storyboard.md).
8. Give the three closing points and take questions. See [questions.md](questions.md).

## If a step fails / თუ რომელიმე ნაბიჯი ვერ შესრულდა

| What you see | Do |
|---|---|
| "Set up two-factor authentication" | Stop the projector, enroll, return to the empty sign-in page, then continue. |
| A step-up form on this secret | Stop and check which secret you opened. The Internal secret does not require step-up. Keep code entry off camera. |
| The secret is missing | The database was not empty, or the process is not Development. Talk the five steps. Do not point at another database. |
| Web stops with "Database:Provider is not set" | Add `Database__Provider=Postgres` to the process environment. Skip the live clicks if it is too late to restart. |
| A page says "The Angular UI is not built" | The index file is missing. Skip the live clicks. |
| A plain "not found" card on a signed-in path | That path is not in the route table. Use the sidebar. |
| Audit log is refused | You are still `dev-user`. Sign out and use `vault-admin`. |
| You pressed Verify | Read the sentence on screen. With no checkpoint it says a database writer could still delete the newest rows. Do not add "so we are safe". |
| You revealed and the value is on screen | Press Back to list before anyone records. |

## Backup recording / სარეზერვო ჩანაწერი

Record the day before the talk. Use the same vault and demo steps, with the light theme at 1280 px.

- Record from the empty sign-in page through the filtered audit log.
- Cut out enrollment and any frames with a QR code, manual key, recovery code, or populated password field.
- Cut out the page after "Reveal value" and before "Back to list".
- Keep the `Revealed` row, the `Viewed` row, and "Latest checkpoint: none yet".
- Do not record Verify or Export.
- The stills in [screens/](screens/) are the same frames, from the 24 November walk:
  `01-sign-in.png`, `02-secrets-list.png`, `03-secret-details.png`, `04-secrets-after-reveal.png`,
  `05-audit-log.png`. The backup recording is still pending.

## Reset before the day / საწყისი მდგომარეობის აღდგენა მოხსენებამდე

Stop the Web process by its process id. Then reset only the talk's database:

```sh
docker compose -p eclipsvault-defence-draft down -v
docker compose -p eclipsvault-defence-draft up -d --wait
```

or, for the standalone container, `docker stop ev-defence-refresh-pg` and the `docker run` in
[demo-setup-reset.md](demo-setup-reset.md) again.

Start Web again and enroll both accounts off camera. `docker compose down` without `-p` does not
stop the Compose project.

Web-ის პროცესი მისი იდენტიფიკატორით გააჩერეთ და ზემოთ მოცემული შესაბამისი ბრძანებებით მხოლოდ
სადემონსტრაციო ბაზა აღადგინეთ საწყის მდგომარეობაში. გაუშვით Web ხელახლა და ორივე ანგარიშისთვის
ავთენტიფიკატორი კამერის გარეშე დაარეგისტრირეთ. `docker compose down` პარამეტრ `-p`-ის გარეშე ამ
Compose პროექტს არ აჩერებს.
