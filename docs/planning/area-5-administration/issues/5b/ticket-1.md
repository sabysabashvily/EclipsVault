## Parent

#SPEC

## What to build

Lifting an intrusion-defence block is undone when it cannot be recorded (ADR-0028), for every caller that lifts one from the administration console:

- Lift the block, then write `IpRangeUnblocked`. If the audit write fails, restore the exact block record (same reason and time) and report the audit trail as unavailable; the range stays blocked.
- If restoring also fails, report that the lift was not recorded and log it as critical.
- Lifting a range that is not blocked changes nothing and records nothing.
- Break-glass recovery lifting its own range (Area 8) is unchanged.

## Acceptance criteria

- [ ] The finding (a lift survives a failed audit write) is first shown failing, then fixed.
- [ ] Unit tests against both blacklist implementations: success records one row; audit failure restores the record with its original reason and time; restore failure is reported.
- [ ] Redis integration test against real Redis: lift and restore keep the key's original content.
- [ ] AuditFailureFixture through the Legacy Unblock form: the block stays, with no audit row.
- [ ] Locked restore; Release build with 0 warnings; format; unit and integration suites pass.

## Blocked by

None (can start once #SPEC5A's PR has merged).
