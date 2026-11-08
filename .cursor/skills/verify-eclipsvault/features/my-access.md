# My access

My access shows what the signed-in account can open right now: clearance, project, and the rules that follow from them.

## Sub-features

- `access-sidebar` opens My access from the Access group.
- `access-bookmark` opens the same page at `/Access`.
- `access-admin` shows Top Secret clearance and project GLOBAL for `vault-admin`.

## How to get to it (user POV)

- In the sidebar, open Access, then choose My access.
- Open `https://localhost:7443/Access` after signing in.
- Choose the Search… button, type `my access`, and choose the My access result.

## Driving it with verify-eclipsvault

Preconditions:

- `verify-eclipsvault doctor` prints `ok vault`.
- `verify-eclipsvault sign-in` has completed in this session.

- **Sidebar.** Start from Secrets. Run `verify-eclipsvault browser goto /Secrets`, then `verify-eclipsvault browser click --text "Access" --exact`, then `verify-eclipsvault browser click --role link --name "My access" --exact`.
- **Administrator facts.** Run `verify-eclipsvault browser expect --role heading --name "My access" --exact`, `verify-eclipsvault browser expect --text "Top Secret"`, and `verify-eclipsvault browser expect --text "GLOBAL"`.
- **Bookmark.** Run `verify-eclipsvault browser goto /Secrets`, then `verify-eclipsvault browser goto /Access`, then `verify-eclipsvault browser expect --role heading --name "My access" --exact`.
- **Proof.** Run `verify-eclipsvault browser screenshot --path logs/verify/evidence/my-access/access.png` and `verify-eclipsvault browser snapshot --path logs/verify/evidence/my-access/access.aria.txt`. Both show EclipsVault, My access, Top Secret, and GLOBAL.

## Gotchas

- The Access group is a disclosure. Chromium exposes its summary as the text Access, not as a button. Click that text before the My access link is visible.
- This session is `vault-admin`. A Standard account would show a different clearance and project PHOENIX. Do not report this page as `dev-user`'s access.
- The palette entry is a separate path. Driving the sidebar and the bookmark does not verify the palette.
