## Parent

#SPEC

## What to build

Checkpoints, viewing and verifying are recorded fail-closed (ADR-0030), through the existing Legacy actions:

- A checkpoint commits with its `AuditCheckpointCreated` row through an interceptor rule; if the row cannot be written, no checkpoint exists.
- Opening the audit log writes `AuditLogViewed`; Verify writes `AuditChainVerified` with its outcome, critical when tampering is found. If the row cannot be written, the page or result is not shown.
- Both new `AuditAction` values get `AuditDisplay` labels ("Audit log viewed", "Audit chain verified", muted) and `ActivityDescriber` descriptions.

## Acceptance criteria

- [ ] Each finding (A3 checkpoint without audit row, A4 unrecorded access) is first shown failing, then fixed.
- [ ] AuditFailureFixture: checkpoint, view and verify each fail with nothing changed and no checkpoint row.
- [ ] HTTP integration tests: one view row per page load; one verify row with its outcome; tampering marks it critical (on the tampering host).
- [ ] Activity tests cover the two new actions.
- [ ] Locked restore; Release build with 0 warnings; format; unit and integration suites pass.

## Blocked by

None (can start once #SPEC6A's PR has merged).
