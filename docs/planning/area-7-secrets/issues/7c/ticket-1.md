## Parent

#SPEC

## What to build

Issuing and revoking dynamic credentials follow ADR-0031, through the existing Legacy actions (spec Q7, Q9, Q16):

- Issuing for a role at or above `StepUp:MinimumSensitivity` needs a sign-in or step-up within `StepUp:MaxAuthAgeMinutes`; a refused issue writes 7a's `SecretAccessDenied` on the role; a step-up challenge writes none. Access and step-up checks run before the licence gate, so a refused issue never starts the evaluation window.
- Any failed or uncertain mint (an error, a lost acknowledgement or a cancelled request, during any of the recipe's statements; `SqlServerBackend` runs the recipe without a transaction) runs the role's revocation statements for the known login, ignoring the request's cancellation and bounded by the target's command timeout. Cleanup succeeded: `502 mint_refused`. Cleanup failed or timed out: `502 mint_incomplete` with the spec's text, a critical log line naming the role and login, and the operator check added to `docs/INCIDENT_RESPONSE.md`.
- If the lease cannot be recorded, the mint is undone, ignoring the request's cancellation; if the undo fails, the same critical log line.
- No password in any row, log, cache or error message: backend exceptions are logged by type and error number, never by a message that could quote the statement.
- No lease issued after this ticket exceeds 1440 minutes: the effective TTL, the request or the role's default when omitted, is clamped to the role's maximum and to 1440 (spec Q26). Leases issued before keep their expiry.
- A revoke reports whether the backend destroyed the credential; a destroyed credential whose close cannot be recorded is reported as such, and its lease shows Active until its original expiry, when the sweep runs the revocation again: Expired if that retry succeeds and its close is recorded, RevocationFailed with its fixed note if the retry fails, and still Active while auditing stays unavailable (an accepted delay, ADR-0031).
- `RevocationError` holds a fixed sentence per cause; the raw backend error goes only to the log.
- The page's evaluation-days read fails open.
- The browser-test setup, found feasible by a read-only probe (`logs/area-7/cursor-e2e-dynamic-probe-result.md` in the coordinator's checkout), e2e-only: `e2e/compose.yml` sets `DynamicSecrets__TargetConnectionString` on the `vault` service to the run's `sql` container, in a separate empty database (never `EclipsVault`); a helper beside `e2e/palette.ts` creates that database and inserts two role rows (Development, project `GLOBAL`, enabled, the Development seed's reader recipe; one Internal, one Secret) through in-container `sqlcmd`, called from the spec only; this ticket's spec drives the still-Legacy page with the Internal role, and ticket 2 adds the Secret role's step-up flow; `run.mjs` unchanged. The Production e2e vault seeds no roles and is unlicensed, so the first issue starts the 30-day evaluation and viewing only peeks. The probe never started the stack: prove `CREATE LOGIN` and `DROP LOGIN` on the pinned SQL Server image first, and stop and report before ticket 2 if they fail.

## Acceptance criteria

- [ ] F5 (issue half), F7, F8 and F11 are first shown failing, then fixed.
- [ ] Production HTTP against a real SQL Server login on Testcontainers: issue and revoke succeed; a stale session issuing a Secret+ role is refused for step-up and an Internal one is not; a refused role writes the denial row and starts no evaluation window; a recipe whose second statement (`CREATE USER`) fails leaves no login and answers `mint_refused`; a role whose revocation statements also fail answers `mint_incomplete`, logs the role and login at critical level, and leaves no password in any row or log; an omitted TTL on a role whose default is 2880 issues a 1440-minute lease, an omitted TTL on a role whose default is 30 and maximum 2880 issues a 30-minute lease, an explicit 1440 on a role whose maximum is 2880 issues 1440, and 1441 answers `400`; a 2880-minute lease already stored before the change keeps its expiry, stays Active past 1440 minutes and is closed by the sweep only once it expires.
- [ ] AuditFailureFixture: issue undoes the mint and stores nothing; a revoke whose close fails is reported as destroyed but unrecorded, the lease stays Active until its expiry, a sweep while writes are still rejected leaves it Active, a sweep after they are restored closes it as Expired, and a sweep whose retry fails closes it as RevocationFailed with its fixed note.
- [ ] Unit, with fake backends: a `CREATE LOGIN` that fails and one that runs but loses its acknowledgement; a `CREATE USER` that fails and one that loses its acknowledgement; a request cancelled mid-mint; each runs the cleanup without the request's token and answers `mint_refused`, or `mint_incomplete` when the cleanup fails or times out; undo-failure log without the password; fixed notes; evaluation read failing open.
- [ ] `scripts/check.sh e2e <the new spec>` issues one Internal credential through the still-Legacy page, proves the login exists inside the `sql` container (password never on host argv), revokes it and proves the login is gone. The browser step-up, evaluation and one-time-display flow is ticket 2's.
- [ ] `docs/INCIDENT_RESPONSE.md` says how an operator finds and drops a login named in a `mint_incomplete` log line.
- [ ] `scripts/check.sh` passes (locked restore, Release build with 0 warnings, unit tests); `scripts/check.sh integration "<this ticket's filter>"` passes on PostgreSQL; `dotnet format --verify-no-changes` is clean. CI runs SQL Server and the full browser suite.

## Notes

- The current `RevokeAtBackendAsync` catch also catches an audit failure on the success close and re-closes the lease as RevocationFailed, so a destroyed credential can be labelled as not destroyed. Keep the two causes apart, as the fixed note per cause already requires; the security reviewer checks it.

## Blocked by

None (the branch starts from `main` after 7a merges).
