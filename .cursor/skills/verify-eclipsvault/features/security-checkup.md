# Security checkup

Security checkup scores the signed-in account's own protections and lists each control, including two-step sign-in.

## Sub-features

- `checkup-sidebar` opens the page from the Security group.
- `checkup-bookmark` opens `/Security` directly.
- `checkup-totp` shows two-step sign-in as protecting the account after enrollment.

## How to get to it (user POV)

- In the sidebar, open Security, then choose Security checkup.
- Open `https://localhost:7443/Security` after signing in.
- Choose Search…, type `security checkup`, and choose Security checkup.

## Driving it with verify-eclipsvault

Preconditions:

- `verify-eclipsvault doctor` prints `ok vault`.
- `verify-eclipsvault sign-in` has completed, so an authenticator is enrolled.

- **Sidebar.** Run `verify-eclipsvault browser goto /Secrets`, then `verify-eclipsvault browser click --text "Security" --exact`, then `verify-eclipsvault browser click --role link --name "Security checkup" --exact`.
- **Two-step sign-in.** Run `verify-eclipsvault browser expect --role heading --name "Security checkup" --exact` and `verify-eclipsvault browser expect --text "Two-step sign-in"`. The same page includes `An authenticator app is protecting your sign-in.` Run `verify-eclipsvault browser expect --text "An authenticator app is protecting your sign-in."`.
- **Bookmark.** Run `verify-eclipsvault browser goto /Secrets`, then `verify-eclipsvault browser goto /Security`, then `verify-eclipsvault browser expect --role heading --name "Security checkup" --exact`.
- **Proof.** Run `verify-eclipsvault browser screenshot --path logs/verify/evidence/security-checkup/checkup.png` and `verify-eclipsvault browser snapshot --path logs/verify/evidence/security-checkup/checkup.aria.txt`. Both show EclipsVault, Security checkup, and Two-step sign-in.

## Gotchas

- Chromium exposes the Security summary as the text Security, not as a button. Click that text before Security checkup is visible. The bookmark does not need that click.
- The palette path is listed above and is not part of this drive. A sidebar run does not verify it.
- Backup codes and passkeys are still outstanding after a fresh enrollment. The page can recommend them while two-step sign-in already passes. Do not require an all-clear score.
