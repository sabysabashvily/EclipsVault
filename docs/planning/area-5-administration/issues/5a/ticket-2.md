## Parent

#SPEC

## What to build

Core guards that every caller obeys:

- **The vault keeps an enabled administrator.** Demoting (self included), disabling or deleting an account is refused with "last administrator" when no other enabled Top Secret account would remain. Disabled accounts do not count. The check runs under a vault-wide lock inside the change's transaction, so two concurrent changes cannot both pass. Self-demotion is allowed while another enabled administrator exists.
- **No administrator action on your own account that Profile governs.** Resetting your own MFA, disabling or deleting yourself is refused with "own account"; the reset message points to Profile.
- **Input rules live in Core:** every rule the Legacy New user and Edit user forms enforced, plus defined clearance values only, "First Last" at most 64 characters, and a project key of 1–64 characters after trimming. A refusal names the field.

## Acceptance criteria

- [ ] Each finding (disabled administrators counted, self-demotion to zero administrators, concurrent demotions, undefined clearance accepted, long name overflowing, own MFA reset allowed) is first shown failing, then fixed.
- [ ] Unit tests as literal tables: last-administrator cases (enabled/disabled, self/other, demote/disable/delete) and own-account cases; input rules per field.
- [ ] HTTP integration tests through the Legacy forms: each refusal shows its message and changes nothing.
- [ ] Race tests on SQL Server and real PostgreSQL: two administrators demoting each other, and a demotion against a disable, leave at least one enabled administrator; the loser is refused.
- [ ] Locked restore; Release build with 0 warnings; format; unit and integration suites pass.

## Blocked by

- #T1
