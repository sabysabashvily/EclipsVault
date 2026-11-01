## Parent

#SPEC

## What to build

Every access-request change commits in one transaction with its audit rows (ADR-0021, ADR-0027), and exactly one decision wins. The Legacy Access requests page keeps working on top of it until ticket 5 serves it.

- `AuditSaveChangesInterceptor` gains `DescribeAccessRequestChange` for filing, approving, rejecting and withdrawing, with today's actions and texts. `AccessRequestService` stops writing these events through `IAuditSink`.
- Approval commits the share, the request's decision, `SecretShared` and `AccessRequestApproved` together, or nothing.
- `AccessRequest.Status` is an EF concurrency token, and every update is field-specific (no `DbSet.Update`). Of approve, reject and withdraw, exactly one wins; the others are refused as "no longer pending".
- Approval shares to `RequesterUserId` with the reviewer from the session as issuer; it never looks the username up again.
- Approval's expiry is 0–3650 days (0 = none), validated in Core. No decision note is accepted from the UI; a stored note still displays.
- The decision email goes after commit, best-effort, with today's text.

## Acceptance criteria

- [ ] Each of the save-then-audit gap, the approve/withdraw race and the unbounded expiry is first shown failing by a test, then fixed.
- [ ] HTTP integration tests (through the Legacy actions): each transition writes exactly the expected audit rows with today's actions and texts; approval creates the share with the reviewer as issuer and it appears on the reviewer's Shared by me.
- [ ] AuditFailureFixture: filing, approving, rejecting and withdrawing each leave the request, any share and the audit trail unchanged and answer the audit failure. No email is recorded.
- [ ] Race tests on SQL Server and real PostgreSQL: approve vs withdraw, approve vs reject, and two approvals each end with one winner and its audit rows, and no share outlives a losing decision.
- [ ] Unit tests: expiry bounds; approval when an active share already exists marks Approved without a second share.
- [ ] No schema change is needed for the concurrency token; if one turns out to be, both provider migration sets are added and checked.
- [ ] Locked restore; Release build with 0 warnings; `dotnet format --verify-no-changes`; unit and integration suites pass.

## Blocked by

- #T1
