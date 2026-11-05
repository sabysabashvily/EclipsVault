## Parent

#SPEC

## What to build

Administrator changes to an account commit in one transaction with their one audit row (ADR-0028, #92), in Core, so the Legacy Users pages obey it as soon as this ticket lands:

- Create, role change, enable, disable, unlock and delete each commit with exactly one audit row (`UserCreated`, `UserRoleChanged`, `UserEnabled`, `UserDisabled`, `AccountUnlocked`, `UserDeleted`). An audit failure leaves the account unchanged and surfaces as the audit trail being unavailable. The provisioning email follows the commit, best-effort.
- Role change and disabling advance the account's credential generation in that transaction and no longer call the session store (ADR-0023 amendment). The per-request account read then refuses every older session, and re-enabling revives none.
- Force logout keeps ADR-0021's order: revoke, then audit; a failed audit leaves the revocation in place and reports that it was not recorded.
- An unchanged role, enabling an enabled account and unlocking an unlocked one record nothing and end no session.
- A lost race on the unique username answers a conflict with the pre-check's message, not an audit failure.

This ticket also starts the branch `area-5a-accounts` from `main` (after #103 has merged) and commits the grill's records: `CONTEXT.md`, the ADR-0017 and ADR-0023 amendments, ADR-0028 and `docs/planning/area-5-administration/`.

## Acceptance criteria

- [ ] Each behaviour above is first shown failing by a test against `main`'s code, then fixed.
- [ ] New `UserAdminService` unit tests: one audit row per change, in the same commit; unchanged changes record nothing; role change and disable advance the generation; Force logout revokes before auditing.
- [ ] HTTP integration tests through the Legacy forms: a demoted administrator's earlier cookie is refused on its next request with no session-store revocation; a disabled-then-re-enabled account's earlier cookie stays refused; a duplicate username created concurrently answers the conflict.
- [ ] AuditFailureFixture, set up first and then rejecting: create, role change, enable, disable, unlock and delete each leave the account and the audit trail unchanged; Force logout still signs the account out.
- [ ] Race tests on SQL Server and real PostgreSQL (digest-pinned images): two concurrent creates of one username produce one account and one conflict.
- [ ] The records listed above are committed.
- [ ] Locked restore; Release build with 0 warnings; `dotnet format --verify-no-changes`; unit and integration suites pass.

## Blocked by

None (can start once #103 has merged).
