## Parent

#SPEC

## What to build

Secrets are changed only by those who may share them, never over a newer value, and shared only with fresh proof (spec Q5, Q6, Q7):

- Rotate, managed rotation, restore and delete need the change right: an administrator, or a member of the secret's project who passes the access check. A share recipient is refused, and the refusal writes `SecretAccessDenied` ("a share grants read access only"), fail-closed.
- Rotate, managed rotation and restore write only if the wrapped data key is still what they read, assigning the version number inside that write; the loser is told the secret changed, and managed rotation puts the upstream password back.
- Every write to a managed secret's value (managed rotation, rotate, shred) first takes a cluster-wide lock on its backend and principal (the existing `ISessionLocker` behind a narrow Core port), without waiting, held from reading the current value through commit or put-back, so one writer's put-back can never undo another's change; another writer gets `409 change_in_progress` before the backend is touched.
- Restoring a version of a managed secret answers `409 restore_managed` ("This secret is the live password of '{principal}'. Rotate it upstream instead; earlier versions stay readable.") and changes nothing; Legacy restored only the stored copy (a named fix).
- The lifecycle worker shreds only if the wrapped data key and the past expiry are still what it read (`ShredAsync` today overwrites whatever it loaded), and skips a locked secret until its next sweep.
- Sharing a secret at or above `StepUp:MinimumSensitivity`, from the sharing panel or by approving an access request, needs a sign-in or step-up within `StepUp:MaxAuthAgeMinutes`. Area 4's Access requests page handles `403 step_up_required` with StepUpDialog. Revoking never asks.

## Acceptance criteria

- [ ] F2, F4, F5 (sharing half) and the managed restore are first shown failing, then fixed.
- [ ] Production HTTP: restoring a managed secret's version answers `409 restore_managed`, writes no event, and leaves the stored value and the backend login's password unchanged.
- [ ] Production HTTP: a share recipient's rotate, managed rotation, restore and delete are refused, change nothing and each write one `SecretAccessDenied`; a project member's and an administrator's succeed.
- [ ] Deterministic races through a seam on SQL Server and real PostgreSQL: two rotations, rotation against restore, a secret rotation during a master key rotation; two managed rotations of one principal, and a managed rotation against a rotate, ending with the backend at the stored value; rotation and restore against shredding, in both orders, never writing onto a shredded row nor shredding a renewed one; every value stays decryptable and in history; the loser gets a conflict, never an audit failure.
- [ ] Production HTTP: a stale session sharing or approving a Secret+ secret gets `403 step_up_required`; a fresh one succeeds; an Internal secret never asks.
- [ ] Vitest: Access requests' approve with StepUpDialog.
- [ ] Unit: the change right and step-up matrix as literal tables.
- [ ] `scripts/check.sh` passes (locked restore, Release build with 0 warnings, unit tests); `scripts/check.sh integration "<this ticket's filter>"` passes on PostgreSQL; `scripts/check.sh e2e <affected specs>` passes; `dotnet format --verify-no-changes` is clean. CI runs SQL Server and the full browser suite.

## Blocked by

- #T1
