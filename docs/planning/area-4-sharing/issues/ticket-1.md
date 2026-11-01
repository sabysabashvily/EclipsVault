## Parent

#SPEC

## What to build

Sharing a secret follows ADR-0027 in Core, so the Legacy sharing panel obeys it as soon as this ticket lands:

- A recipient whose clearance is below the secret's sensitivity is refused: "That account's clearance is below this secret's sensitivity, so a share would not let them open it."
- A new share to someone whose share has expired replaces the expired one in the same transaction, recorded as one `SecretShared` row whose text says it replaced an expired share. No "revoked" row. An active duplicate is still refused.
- A save that fails because a share for that recipient now exists answers a conflict ("They already have a share on this secret"), not an audit failure. Every other failed save still fails closed.

This ticket also starts the Area 4 branch `area-4-sharing` from `main` and commits the grill's records: `CONTEXT.md` (Expired share, Access request, Requester, Reviewer), ADR-0017's amendment, ADR-0027 and `docs/planning/area-4-sharing/`.

## Acceptance criteria

- [ ] Each behaviour above is first shown failing by a test against `main`'s code, then fixed.
- [ ] Unit tests in the existing share-service tests: under-cleared recipient refused; sufficient clearance accepted; expired share replaced; active duplicate refused; self-share still refused.
- [ ] HTTP integration tests through the Legacy sharing panel: under-cleared recipient refused with the message; re-sharing after expiry works and writes exactly one `SecretShared` row and no `SecretShareRevoked` row.
- [ ] AuditFailureFixture: replacing an expired share while audit writes are rejected leaves the expired share in place, adds no share and no audit row, and answers the audit failure.
- [ ] Race tests on SQL Server and real PostgreSQL (Testcontainers, images pinned by digest): two concurrent shares to the same recipient produce one share, one `SecretShared` row, and a conflict (not an audit failure) for the loser.
- [ ] The records listed above are committed.
- [ ] Locked restore; Release build with 0 warnings; `dotnet format --verify-no-changes`; unit and integration suites pass.

## Blocked by

None (can start immediately).
