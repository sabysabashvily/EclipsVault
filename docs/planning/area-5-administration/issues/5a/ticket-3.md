## Parent

#SPEC

## What to build

Service-account and API-key changes commit in one transaction with their one audit row, and key options are checked in Core:

- Create, enable, disable, delete, key issue and key revoke each commit with exactly one audit row (`ServiceAccountCreated`, `ServiceAccountEnabled`, `ServiceAccountDisabled`, `ServiceAccountDeleted`, `ApiKeyIssued`, `ApiKeyRevoked`). An audit failure leaves nothing changed, so no key exists unaudited.
- Key options: expiry 0–3650 days (0 = none, negatives refused); a clearance ceiling must be a defined level strictly below the account, or none; allowed ranges stay one free-text field, split and checked once in Core; service-account clearance must be a defined level. A refusal names the field.
- A key is revoked under its own service account; another account's key is not found. Revoking a revoked key records nothing.
- A lost race on the unique service-account name answers a conflict, not an audit failure.
- The raw key is returned only to the issuing caller and never written to a log or audit row (prefix only).

## Acceptance criteria

- [ ] Each finding (save-then-audit, unknown ceiling becoming "no limit", unbounded or negative expiry, revoke ignoring the account) is first shown failing, then fixed.
- [ ] New `ServiceAccountService` unit tests: key options as a literal table; one audit row per change; unchanged changes record nothing; key status.
- [ ] AuditFailureFixture: each write leaves the account, its keys and the audit trail unchanged.
- [ ] Race tests on SQL Server and real PostgreSQL: two concurrent creates of one name produce one account and one conflict.
- [ ] An issued key authenticates on the Public API and is refused once revoked (HTTP integration).
- [ ] Locked restore; Release build with 0 warnings; format; unit and integration suites pass.

## Blocked by

- #T2
