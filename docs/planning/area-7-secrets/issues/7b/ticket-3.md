## Parent

#SPEC

## What to build

The Angular UI serves Secret details (spec Q2, Q9, Q14, Q17):

- Every `/api/ui/secrets/{id}` endpoint in the spec's contract, with its problems, including the managed-rotation codes and evaluation reads failing open.
- The page at `/Secrets/Details/{id}`, copied word for word from `Views/Secrets/Details`, with the spec's deliberate changes: StepUpDialog for reveal, version reveal and share; change controls and the sharing panel from `canChange` and `canShare`, and Restore hidden for a managed secret; ConfirmationDialog and StatusLine with the Legacy texts; a revealed value only in page memory.
- `access_denied` opens `/Account/Denied?secretId=` with the reasons in navigation memory (the Denied page itself is ticket 4; until then it stays Legacy and shows its generic text).
- Create's Legacy flash bridge from 7a is replaced by the Angular StatusLine; Delete returns to the list with the Legacy success text.
- The Details actions of `SecretsController` and `Views/Secrets/Details` go.

## Acceptance criteria

- [ ] Production HTTP: every shape and code; the authorization matrix on every endpoint; reveal answers `no-store`; anti-forgery on every write; a decoy's answer is identical to an unknown id's and trips the trap.
- [ ] AuditFailureFixture: reveal, version reveal, rotate, restore, share, revoke share and delete answer 503 and change or disclose nothing.
- [ ] Unit: an evaluation read that throws leaves the note out and the page served.
- [ ] Vitest: every state and dialog flow.
- [ ] Playwright under browser-health: reveal, step-up reveal with a real TOTP code, version reveal, rotate, restore, share and revoke, delete, a recipient seeing no change controls, keyboard, both themes.
- [ ] `scripts/check.sh` passes (locked restore, Release build with 0 warnings, unit tests); `scripts/check.sh integration "<this ticket's filter>"` passes on PostgreSQL; `scripts/check.sh e2e <affected specs>` passes; `dotnet format --verify-no-changes` is clean. CI runs SQL Server and the full browser suite.

## Blocked by

- #T2
