## Parent

#SPEC

## What to build

Requests are filed and decided under the sharing rule (ADR-0027), in Core, with the Legacy pages as the callers for now.

- **Reviewer:** an administrator or member of the secret's project who passes the secret's access check at decision time. The queue lists only requests the caller could decide now, never their own. Any other request id is refused exactly like an unknown one.
- **Filing:** allowed only when the requester's clearance suffices, they are outside the secret's project and hold no active share; network and time are ignored. Refusals: clearance too low (`share_cannot_help`), already able to open it, already pending. The server writes the denial reasons; the posted `deniedReasons` is ignored. The honey-token trap stays; filing writes one `AccessRequested` row and no `SecretMetadataViewed` row.
- **Approval** re-checks the filing rule and refuses an ineligible request with the reason. Reject still works on it.
- **Existing data:** a migration in both provider sets clears `DeniedReasons` on pending requests.
- **Legacy Denied page:** the Request access form appears only when the request would be eligible, otherwise one sentence says why (the check writes no audit row). Help text becomes "Ask a reviewer, an administrator or a member of this secret's project who can open it, to share it with you." The hidden `deniedReasons` field goes.

## Acceptance criteria

- [ ] Each finding (reviewer who cannot read the secret, queue disclosure, requester-written reasons, unsatisfiable request) is first shown failing by a test, then fixed.
- [ ] Unit tests: the filing-eligibility rule as a literal table (clearance, project, TopSecret, active share, network, time) and the reviewer rule.
- [ ] HTTP integration authorization matrix: administrator below the sensitivity, low-clearance project member, other-project member, the requester themselves and an off-network reviewer can neither see nor decide; a qualified reviewer can. Undecidable and unknown ids get identical refusals.
- [ ] HTTP tests: a posted `deniedReasons` is ignored and the stored reasons are the server's; filing writes no metadata-viewed row; filing on a honey token trips it; the Denied page shows or hides the form with the right sentence.
- [ ] Migration tests on SQL Server and PostgreSQL: pending rows lose `DeniedReasons`, decided rows keep it.
- [ ] Locked restore; Release build with 0 warnings; `dotnet format --verify-no-changes`; unit, integration and migration tests pass; both pending-model checks pass.

## Blocked by

- #T2
