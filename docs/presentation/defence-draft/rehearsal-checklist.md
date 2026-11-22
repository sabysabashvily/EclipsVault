# Rehearsal checklist / რეპეტიციის სია

One run of the 24 November talk, on `origin/main` `d576dd93a30bc461183ca7b71c89f038adbff446`.
Thirty minutes. The click path was walked twice on 21 November 2026; the second time finished at
23:39 Asia/Tbilisi. Exact setup is [demo-setup-reset.md](demo-setup-reset.md).

24 ნოემბრის მოხსენების ერთი გავლა, `origin/main`
`d576dd93a30bc461183ca7b71c89f038adbff446`-ზე. ოცდაათი წუთი. დაწკაპუნების გზა ორჯერ გაიარა
21 ნოემბერს 2026; მეორედ დასრულდა 23:39 Asia/Tbilisi-ზე. ზუსტი მომზადება
[demo-setup-reset.md](demo-setup-reset.md)-შია.

## Pre-flight / წინასწარი

- [ ] Revision is `d576dd9` or the commit you will name on slide 7.
- [ ] `docker compose -p eclipsvault-defence-draft up -d --wait` is up. PostgreSQL is healthy.
- [ ] `CI=true npm run build` in `EclipsVault.AngularUi` has written `EclipsVault.Web/AngularUi/index.html`.
- [ ] Web is Development on `https://localhost:7443`, with `Database__Provider=Postgres`, the compose
      connection string, `Seed__AdminPassword`, `Seed__DevPassword`, and `ECLIPSVAULT_KEK`.
- [ ] `/Account/Login` shows "Sign in". Both accounts are already enrolled, off camera.
- [ ] Theme is light. The account card says "Switch to dark".
- [ ] Projector is on the sign-in page, with both fields empty.

## Run order / რიგი

1. **Problem.** No product. One sentence: a shared file cannot say who opened the password.
2. **Access.** Four rules in `SecretAccessPolicy`. This secret is Internal and Development, so
   network and the production window do not apply. Do not invent a denial on loopback.
3. **Architecture.** Context, then container, then the Web component picture in
   [diagrams.html](diagrams.html). Core and Infrastructure are modules inside Web.
4. **Order.** Access, then `Viewed`, then "Reveal value", then `Revealed`, then decryption.
5. **Live.**
   - `dev-user` → Continue → authenticator code → `/Secrets`.
   - Filter `Phoenix_Dev_Database_Password` → Open → Reveal value → Back to list.
   - Account card → Sign out.
   - `vault-admin` → Continue → authenticator code → `/Audit`.
   - Filter the same name. Point at `Revealed` for `dev-user`, then `Viewed` under it.
6. **Boundary.** Latest checkpoint is "none yet". Proof to an outsider is a bundle they already
   hold. Do not say every deletion is detectable. Do not press Verify integrity, Create signed
   checkpoint, or Export signed bundle.
7. **Revision.** Read only the filled cells in [storyboard.md](storyboard.md) slide 7.
8. **Stop.** Three sentences, then questions. [questions.md](questions.md).

## If a step fails / თუ ნაბიჯი ჩავარდა

| What you see | Do |
|---|---|
| "Set up two-factor authentication" | Stop the projector, enroll, return to the empty sign-in page, then continue. |
| A step-up form on this secret | You are not on the Internal secret. Stop. Do not type a code on camera. |
| The secret is missing | The database was not empty, or the process is not Development. Talk the five steps. Do not point at another database. |
| `/Secrets` is the error "The Angular UI is not built" | The index file is missing. Skip the live clicks. |
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
- The stills in [screens/](screens/) are the same frames, from the second walk: `01-sign-in.png`,
  `02-secrets-list.png`, `03-secret-details.png`, `04-secrets-after-reveal.png`, `05-audit-log.png`.

## Reset before the day / დაბრუნება დღემდე

Stop the Web process by its process id. Then:

```sh
docker compose -p eclipsvault-defence-draft down -v
docker compose -p eclipsvault-defence-draft up -d --wait
```

Start Web again and enroll both accounts off camera. `docker compose down` without `-p` does not
stop this project.
