## Parent

#SPEC

## What to build

Export signs the committed head, writes `AuditBundleExported`, then streams the entries up to that head (ADR-0030). A failed audit write sends no bytes. Schema v2, the file name and indentation are unchanged. Export runs one at a time, and needs a sign-in or step-up within `StepUp:MaxAuthAgeMinutes` (ADR-0028), refused with `403 step_up_required` when stale; the Legacy Export follows the same rule until ticket 4 retires it.

## Acceptance criteria

- [ ] Each finding (A2 whole trail in memory for Export, A5 no fresh proof) is first shown failing, then fixed.
- [ ] HTTP integration tests on SQL Server and PostgreSQL: the streamed bundle passes `AuditBundleVerifier` pinned to the host's public key; its checkpoint matches the last row; stale proof is refused and fresh proof allowed; a concurrent export is refused as busy through a seam.
- [ ] AuditFailureFixture: export sends no bytes and writes no row.
- [ ] A v2 bundle still verifies with the current offline tool; `AuditBundleVerifierTests` pass unchanged.
- [ ] Locked restore; Release build with 0 warnings; format; unit and integration suites pass.

## Blocked by

- #T2
