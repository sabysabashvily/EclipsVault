# Rehearsal checklist / რეპეტიციის საკონტროლო სია

One run of the 24 November talk, on `origin/main` `1298958050e45f83aefeb4b6455d0a02169ab22a`.
The presentation cap is 30 minutes. The click path was walked on this revision on 24 November
2026, finishing at about 16:03 Asia/Tbilisi, on a port-shifted disposable vault. Exact setup is
[demo-setup-reset.md](demo-setup-reset.md).

24 ნოემბრის მოხსენების ერთი რეპეტიცია `origin/main`-ის რევიზიაზე
`1298958050e45f83aefeb4b6455d0a02169ab22a`. პრეზენტაციისთვის გამოყოფილია მაქსიმუმ 30 წუთი.
დემონსტრაციის ნაბიჯები სხვა პორტზე გაშვებულ ერთჯერად საცავში შემოწმდა 2026 წლის 24 ნოემბერს.
რეპეტიცია დაახლოებით 16:03-ზე დასრულდა (Asia/Tbilisi). მომზადების ინსტრუქცია მოცემულია
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

1. **Problem.** No product. One sentence: a shared file cannot say who opened the password.
2. **Access.** Four rules in `SecretAccessPolicy`. This secret is Internal and Development, so
   network and the production window do not apply. Do not invent a denial on loopback.
3. **Architecture.** Context, then container, then the Web component picture in
   [diagrams.html](diagrams.html). The browser UI, including sign-in, runs on Angular. Core and
   Infrastructure are modules inside Web.
4. **Order.** Access, then `Viewed`, then "Reveal value", then `Revealed`, then decryption.
5. **Live.**
   - `dev-user` → Continue → authenticator code → Verify and sign in → `/Secrets`.
   - Filter `Phoenix_Dev_Database_Password` → click the name → Reveal value → Back to list.
   - Account card → Sign out.
   - `vault-admin` → Continue → authenticator code → Verify and sign in → Administration → Audit log.
   - Filter the same name. Point at `Revealed` for `dev-user`, then `Viewed` under it.
6. **Boundary.** Latest checkpoint is "none yet". Proof to an outsider is a bundle they already
   hold. Do not say every deletion is detectable. Do not press Verify integrity, Create signed
   checkpoint, or Export signed bundle.
7. **Revision.** Read only the filled cells in [storyboard.md](storyboard.md) slide 7.
8. **Stop.** Three sentences, then questions. [questions.md](questions.md).

## If a step fails / თუ რომელიმე ნაბიჯი ვერ შესრულდა

| What you see | Do |
|---|---|
| "Set up two-factor authentication" | Stop the projector, enroll, return to the empty sign-in page, then continue. |
| A step-up form on this secret | You are not on the Internal secret. Stop. Do not type a code on camera. |
| The secret is missing | The database was not empty, or the process is not Development. Talk the five steps. Do not point at another database. |
| Web stops with "Database:Provider is not set" | Add `Database__Provider=Postgres` to the process environment. Skip the live clicks if it is too late to restart. |
| A page says "The Angular UI is not built" | The index file is missing. Skip the live clicks. |
| A plain "not found" card on a signed-in path | That path is not in the route table. Use the sidebar. |
| Audit log is refused | You are still `dev-user`. Sign out and use `vault-admin`. |
| You pressed Verify | Read the sentence on screen. With no checkpoint it says a database writer could still delete the newest rows. Do not add "so we are safe". |
| You revealed and the value is on screen | Press Back to list before anyone records. |

## Backup recording / სარეზერვო ჩანაწერი

Do this the day before, not in the room. Same vault, same clicks, light theme, 1280 px wide.

- Record from the empty sign-in page through the filtered audit log.
- Cut out enrollment. No QR code, no manual key, no recovery codes, no password field with a value.
- Cut out the page after "Reveal value" and before "Back to list".
- Keep the `Revealed` row, the `Viewed` row, and "Latest checkpoint: none yet".
- Do not record Verify or Export.
- The stills in [screens/](screens/) are the same frames, from the 24 November walk:
  `01-sign-in.png`, `02-secrets-list.png`, `03-secret-details.png`, `04-secrets-after-reveal.png`,
  `05-audit-log.png`. No recording has been made.

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
