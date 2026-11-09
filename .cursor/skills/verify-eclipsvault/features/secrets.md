# Secrets

Secrets lists the credentials the signed-in account is allowed to see and opens one secret's page. The stored value stays hidden until the user chooses Reveal value.

## Sub-features

- `secrets-list` shows the seeded secret names.
- `secrets-open` opens a non-decoy secret and still offers Reveal value.
- `secrets-back` returns to the list with the same name visible.

## How to get to it (user POV)

- Sign in. The code step lands here.
- Choose Secrets in the sidebar.
- Open `https://localhost:7443/Secrets`.

## Driving it with verify-eclipsvault

Preconditions:

- `verify-eclipsvault doctor` prints `ok vault`.
- `verify-eclipsvault sign-in` has completed in this session.

- **List.** Open Secrets. Run `verify-eclipsvault browser goto /Secrets` and `verify-eclipsvault browser expect --role heading --name "Secrets" --exact`. The page contains `Phoenix_Dev_Database_Password`.
- **Seeded name.** Run `verify-eclipsvault browser expect --text "Phoenix_Dev_Database_Password"`. The command prints `saw Phoenix_Dev_Database_Password`.
- **Open a real secret.** Choose Open on that row. Run `verify-eclipsvault browser click --role link --name "Open" --exact --row "Phoenix_Dev_Database_Password"`. The heading is the secret name. Run `verify-eclipsvault browser expect --role heading --name "Phoenix_Dev_Database_Password" --exact`.
- **Value still sealed.** Run `verify-eclipsvault browser expect --role button --name "Reveal value" --exact`. Do not click it.
- **Back to the list.** Run `verify-eclipsvault browser click --role link --name "Back to list" --exact` and `verify-eclipsvault browser expect --text "Phoenix_Dev_Database_Password"`.
- **Proof.** Run `verify-eclipsvault browser screenshot --path logs/verify/evidence/secrets/list.png` and `verify-eclipsvault browser snapshot --path logs/verify/evidence/secrets/list.aria.txt`. Both show EclipsVault, Secrets, and `Phoenix_Dev_Database_Password`.

## Gotchas

- `Production_AWS_Root_Key` and `Global_SQL_SA_Password` are honey tokens. Opening either one trips intrusion response and can block this machine. The journey never clicks their Open links.
- `Phoenix_Ephemeral_Deploy_Token` expires about five minutes after launch. Do not require it to still be listed.
- Reveal value decrypts the secret onto the page. A screenshot of that page is a copy of the value. Stop at the sealed details page.
- Every row's Open link has the same name. `--row` is what picks `Phoenix_Dev_Database_Password`.
