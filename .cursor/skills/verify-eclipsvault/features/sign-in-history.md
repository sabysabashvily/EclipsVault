# Sign-in history

Sign-in history lists the signed-in account's own sign-in attempts, newest first, including the sign-in that opened this session.

## Sub-features

- `history-sidebar` opens the page from the Security group.
- `history-bookmark` opens `/SignIns` directly.
- `history-palette` opens it from Search… and from Ctrl K.
- `history-row` shows the sign-in this session just completed.

## How to get to it (user POV)

- In the sidebar, open Security, then choose Sign-in history.
- Open `https://localhost:7443/SignIns` after signing in.
- Choose Search…, or press Ctrl K, type `sign-in history`, and choose Sign-in history.

## Driving it with verify-eclipsvault

Preconditions:

- `verify-eclipsvault doctor` prints `ok vault`.
- `verify-eclipsvault sign-in` has completed in this session, so one successful sign-in exists.

- **Sidebar.** Start from Secrets. Run `verify-eclipsvault browser goto /Secrets`, then `verify-eclipsvault browser click --text "Security" --exact`, then `verify-eclipsvault browser click --role link --name "Sign-in history" --exact`.
- **The sign-in is listed.** Run `verify-eclipsvault browser expect --role heading --name "Sign-in history" --exact`, `verify-eclipsvault browser expect --text "Successful sign-ins"`, and `verify-eclipsvault browser expect --text "Signed in"`.
- **Bookmark.** Run `verify-eclipsvault browser goto /Secrets`, then `verify-eclipsvault browser goto /SignIns`, then `verify-eclipsvault browser expect --role heading --name "Sign-in history" --exact`.
- **Search button.** Run `verify-eclipsvault browser goto /Secrets`, then `verify-eclipsvault browser click --role button --name "Search secrets or jump to a page" --exact`, then `verify-eclipsvault browser fill --role combobox --name "Search secrets, or jump to a page" --value "sign-in history"`, then `verify-eclipsvault browser click --role option --name "Sign-in history"`. Run `verify-eclipsvault browser expect --role heading --name "Sign-in history" --exact`.
- **Keyboard.** Run `verify-eclipsvault browser goto /Secrets`, then `verify-eclipsvault browser press --key Control+k`, then `verify-eclipsvault browser fill --role combobox --name "Search secrets, or jump to a page" --value "sign-in history"`, then `verify-eclipsvault browser press --key Enter`. Run `verify-eclipsvault browser expect --role heading --name "Sign-in history" --exact`.
- **Proof.** Run `verify-eclipsvault browser screenshot --path logs/verify/evidence/sign-in-history/history.png` and `verify-eclipsvault browser snapshot --path logs/verify/evidence/sign-in-history/history.aria.txt`. Both show EclipsVault, Sign-in history, and Signed in.

## Gotchas

- Chromium exposes the Security summary as the text Security, not as a button. Security checkup is a separate link. `--exact` keeps the summary click on Security.
- The Sign-in history link stays hidden until the Security group is open. A bookmark or the palette does not need that click.
- Ctrl K is the chord on this Linux host. The button label shows Ctrl K. macOS shows ⌘K; press `Meta+k` there instead of `Control+k`.
- "Signed in" is the successful password sign-in. The page subtitle talks about signing in and is not that row.
- Do not treat a sidebar visit as proof of the bookmark or the palette. Each step above is its own entry.
