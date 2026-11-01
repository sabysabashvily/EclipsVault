## Problem

An administrator creating an account chooses its initial password and gives it to the new person. Whoever signs in first with it enrolls the authenticator, so the administrator can take over a new account before its owner does, and every action it then takes is attributed to that person. The provisioning email also tells the person to use "the password an administrator gave you", which has to travel to them by some other channel.

Found by the Area 5 grill (finding U7). Area 5a copies the current New user form unchanged, so this needs its own design.

## Options to weigh

- A one-time enrollment link, sent to the account's email, that sets the password and the authenticator together and expires.
- Keep an administrator-set password but force the owner to replace it at first sign-in, before enrollment.

Either changes sign-in (Area 8) and needs an ADR.

## Acceptance criteria

- [ ] An ADR records the chosen provisioning flow.
- [ ] No administrator can complete a new account's first sign-in with credentials they chose.
