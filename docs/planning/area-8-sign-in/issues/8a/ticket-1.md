## Parent

#SPEC

## What to build

The sign-in rules on the server and the UI API calls, while the Legacy pages keep working on the same services (spec Q4–Q9, Q11). This ticket commits every Area 8 grill record (`docs/planning/area-8-sign-in/`, ADR-0032, ADR-0033, the ADR-0017 amendment, `CONTEXT.md`).

- Every `/api/ui/auth/*` call in the spec's contract, with its problems, token reissue and the throttle (sign-out exempt by method).
- `AuthorizationSurfaceTests`: a named entry for each anonymous and pending-sign-in auth call. The IP-block exemption keeps today's `/Account/Recover` path, so the Legacy form's POST still works, and adds `POST /api/ui/auth/recover`. Ticket 3 narrows that path when it deletes the Legacy POST. `/local-sign-out` is not an exemption: it already terminates before the blacklist and the test shows a blocked address gains nothing from it.
- The pending-proof table in the spec. The pending scheme's UI API challenge is Problem Details `401 sign_in_expired`, not the HTML login redirect. Each token reissue replaces `HttpContext.User` before `UiAntiforgery.IssueToken`, and the next POST validates.
- F2: SSO's default is the literal `/Secrets`; break-glass's is `/`.
- F4: passkey sign-in ignores and no longer accepts a username; Profile registration asks `residentKey: "required"`.
- F5: the password stage hashes before it checks lockout.
- F6: sign-out reports `503 sign_out_incomplete` when the revoke fails, the cookie cleared; the Legacy `/Account/Logout` uses the same implementation.
- Harden break-glass block lifting here, with the new recover API, for both that API and the still-Legacy form. Preserve `LoginSucceeded`, `BreakGlassRecovery`, `IpRangeUnblocked` and their order. Retain exact removed ranges and metadata; restore on rejected result row without request cancellation, preserving concurrent changes. A source guard is in place before removal can grant access and stays effective if restoration cannot be confirmed. It is shared with a shared blacklist, is not removed by the address-wide operation, and cannot expire into an unrecorded grant. See the spec's Audit section and ADR-0032. This is the owner's 2026-11-13 revision to no compensation for block lifting only; do not retire an already-recorded session.
- Remove the five leftover Area 3 methods and their interface members; move their tests to the verified services or direct seeding.

## Acceptance criteria

- [ ] F2, F4, F5 and F6 are first shown failing, then fixed.
- [ ] Production HTTP (PostgreSQL locally, both databases in CI): every call's shape and code; anonymous and pending allowlists; a blocked network still completing the Legacy Recover form and the new recover call, and still being refused elsewhere; `/local-sign-out` from a blocked address clearing only the cookie; throttle per POST, not on sign-out or GETs; the pending-proof table; the next POST validating the reissued token; enumeration parity for unknown, locked and disabled usernames.
- [ ] `SignInRefusalAuditTests` and the break-glass tests run through the UI API with every assertion kept. Selectively reject each break-glass row: a refused session leaves no cookie or lift; a failed recovery row leaves the recorded session and block; a failed lift row restores the exact ranges and keeps access refused. Check a later real protected request, not only the response status. AuditFailureFixture covers every auth call.
- [ ] In-memory and real Redis tests cover overlapping ranges, cancellation and concurrent changes; a failed or unconfirmed restoration keeps the source refused, including another replica for Redis. Confirmed restoration returns `503 audit_unavailable`; unconfirmed restoration returns `503 recovery_incomplete` without claiming network access restored. The Legacy Recover form still works with the shared helper until ticket 3 removes it.
- [ ] The five methods are gone; no test calls them; the unit fakes drop them.
- [ ] `scripts/check.sh` passes (locked restore, Release build with 0 warnings, unit tests); `scripts/check.sh integration "<this ticket's filter>"` passes on PostgreSQL; `dotnet format --verify-no-changes` is clean; `npm run check:api-types` passes. CI runs SQL Server and the full browser suite.

## Blocked by

None (the branch starts from `main` after 7a merges).
