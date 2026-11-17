## Parent

#SPEC

## What to build

Secret actions record what happened, through the existing Legacy actions and the Public API (spec Q3, Q4, Q15):

- Looking a secret up by id writes no row; a decoy still trips first. A served details page writes `SecretMetadataViewed`; a refused by-id action, Legacy and Public API alike, writes 7a's `SecretAccessDenied` row with the reasons, not critical; a step-up challenge (with its own step-up events), an unknown id and a list write none. Both fail closed.
- Each action writes only its own event in its transaction: restore commits with `SecretVersionRestored`, managed rotation with `SecretUpstreamRotated`, instead of `SecretUpdated`; no Viewed row first.
- Managed rotation's outcomes: upstream refusal reported truthfully; the upstream password put back only when storing failed before commit (a commit, even one followed by a failed cache eviction, is never undone), ignoring the request's cancellation; drift recorded critical, or logged critical if that row fails too.

## Acceptance criteria

- [ ] F1 and F3 are first shown failing, then fixed.
- [ ] Production HTTP: a refused read writes one `SecretAccessDenied` and no `SecretMetadataViewed`, on the Legacy page and `GET /api/v1/secrets/{id}`; an allowed GET writes one Viewed; a stale session's step-up reveal writes no denial row; a reveal writes only `SecretRevealed`; restore and managed rotation each write their one event; My activity, Overview and the data export omit the denied secret's name.
- [ ] AuditFailureFixture: the viewed and denial rows fail closed; restore and managed rotation change nothing (upstream put back).
- [ ] Unit: put-back and drift paths with fake backends, including a commit followed by a failed eviction that puts nothing back; managed rotation against a real SQL Server login on Testcontainers.
- [ ] `scripts/check.sh` passes (locked restore, Release build with 0 warnings, unit tests); `scripts/check.sh integration "<this ticket's filter>"` passes on PostgreSQL; `dotnet format --verify-no-changes` is clean. CI runs SQL Server and the full browser suite.

## Blocked by

None (the branch starts from `main` after 7a merges).
