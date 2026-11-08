# Sign in

Sign in asks for the account password and then a one-time authenticator code. A new account enrolls an authenticator before it reaches the vault.

## Sub-features

- `sign-in-password` accepts the username and password and moves to the authenticator step.
- `sign-in-enroll` shows an authenticator key, accepts one code, and opens the vault.
- `sign-in-land` arrives on Secrets, not on the page named in a return URL.

## How to get to it (user POV)

- Open `https://localhost:7443/Account/Login`.
- Follow a signed-out redirect from any signed-in page. The redirect returns to sign-in; after the code step the vault still opens Secrets.

## Driving it with verify-eclipsvault

Preconditions:

- `verify-eclipsvault doctor` prints `ok vault` and `accounts 2`.
- `vault-admin` has never signed in on this database.

- **Open sign-in.** The harness opens Sign in. Run `verify-eclipsvault sign-in`. The command finishes on Secrets.
- **Password and enrollment.** The same command submits `vault-admin`, enrolls the authenticator key shown on the page, and submits one code. It prints `signed in vault-admin at https://localhost:7443/Secrets`.
- **Landed state.** The Secrets heading is visible. Run `verify-eclipsvault browser expect --role heading --name "Secrets" --exact`. The command prints `saw heading Secrets`.
- **Proof.** Capture Secrets, which is the state sign-in produced. Run `verify-eclipsvault browser screenshot --path logs/verify/evidence/sign-in/secrets.png` and `verify-eclipsvault browser snapshot --path logs/verify/evidence/sign-in/secrets.aria.txt`. Both show EclipsVault and the Secrets heading.

## Gotchas

- The harness will not print or screenshot the enrollment page. The authenticator key is on that page.
- A code works once per 30-second step. The harness waits when the step is about to change. A second sign-in in the same step fails.
- Eleven sign-in posts per 120 seconds per address is the throttle, and each sign-in spends two. This journey spends one sign-in. Do not loop it.
- After the code step the return URL is dropped. Prove a later page by navigating to it, not by expecting the redirect to land there.
- An already enrolled `vault-admin` makes `sign-in` fail. That account is not on a fresh `evverify…` database.
