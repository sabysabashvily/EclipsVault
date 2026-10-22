## Parent

#83

## What to build

Make every Profile database mutation atomic with its intended semantic audit event, including existing Legacy callers. Replace whole-row User updates with field-specific/concurrency-safe persistence so an overlapping cosmetic edit cannot restore stale security fields.

Provide the narrow operation-aware transaction seam later tickets use for generation and authorization state. Repair shared TOTP/recovery replay consumption and lockout accounting; retain true login versus Profile-verification meanings. This is backend repair, not migration of Account sign-in/enrollment pages.

## Acceptance criteria

- [ ] Details, password, avatar set/remove, TOTP reset, recovery replacement and passkey add/remove commit their database changes with one intended semantic event through operation-aware AuditSaveChangesInterceptor rules. Remove the corresponding separate success IAuditSink writes; never construct AuditLog directly.
- [ ] Do not use generic User→ProfileUpdated interception. Sign-in replay/lockout markers, secret step-up and existing administrator changes retain their true purposes/events. Multi-row reset/replacement is one mutation event, not one per code.
- [ ] Reset atomically clears TOTP, enrollment/replay marker and recovery codes. Recovery replacement is serialized per account and cannot leave two first-generated sets valid.
- [ ] Use field-specific updates and concurrency protection for User/avatar/security fields. A delayed display-name/avatar save cannot restore password, TOTP, role, enabled state or LastTotpStep from a stale snapshot.
- [ ] TOTP acceptance and recovery redemption are atomic single-use operations shared by sign-in, Profile verification and secret step-up. Concurrent attempts cannot both succeed using one proof; failures increment reliably under concurrency.
- [ ] Complete intended authentication/verification precedes failure reset. Keep existing lockout behavior across affected password, TOTP/recovery, enrollment, break-glass, step-up and passkey callers; later Profile verification cannot bypass it.
- [ ] The operation-aware transaction seam can include later generation/grant changes without a repository independently committing them. Preserve Core BCL-only and the existing interface-location rules.
- [ ] Real Production HTTP and focused real-provider tests assert exactly one correct audit event for successful mutations, no fabricated events for no-op/missing/foreign/validation cases, and concurrent cosmetic/security and replay behavior.
- [ ] AuditFailureFixture rejects inserts for each reachable database mutation and proves complete rollback, old recovery set retained and no replacement plaintext delivered. Restore writes in finally; no later SaveChanges may rescue a rejected mutation. When ticket 4 adds verification, update mutation tests to verify before enabling rejection and test verification rejection separately.
- [ ] Zero-warning Release build, format, unit/integration and focused SQL Server/PostgreSQL persistence/race tests pass.

## Blocked by

- #84

## Standing rules

- Read the parent spec, `AGENTS.md`, `CODING_STANDARDS.md`, `CONTEXT.md` and ADR0009, 0012, 0013, 0016, 0017, 0020–0026 before starting. Reuse the Foundation and Areas 1–2.
- Work one ticket at a time in the shared root checkout on `area-3-profile`, cut from `main` at `c7b4b1b` or later. Ticket 1 creates it; later tickets continue it. Do not use `angular-migration` or an Area 2 branch. Start only after native blockers are closed by the owner.
- Never read, move, commit or delete `docs/research/`; never read/copy `docs/internal/`. Stage only explicit intended paths. Save restart records outside `/tmp`.
- Core references only the BCL. Web names Infrastructure only in `Program.cs`. Respect the existing ports/layering rules, claim helpers, strict CSP and supply-chain constraints.
- Tests land with the behavior they prove. Build before `--no-build` tests. Real audit-failure tests use `AuditFailureFixture` and the unmodified Production host; do not replace credential/audit services or add production test endpoints.
- Every API route/type change regenerates and commits `schema.generated.ts`. Every model change has SQL Server and PostgreSQL migrations and appropriate real-provider tests.
- Run the ticket's relevant required checks. Obtain Standards/Spec reviews by a different model, or disclose a fresh-session fallback if unavailable. Report 2–3 short new-code excerpts under CODING_STANDARDS.
- Commit with a one-sentence behavior subject of at most 72 characters, using a message file. **Do not push or close the issue.** The owner verifies, pushes and runs CI. One PR opens after ticket 8; prepare its body for the owner. Never merge, tag, delete branches or rewrite history.
- Browser specs import `test` from `e2e/fixtures.ts`; extra contexts come from browserHealth. Expected deliberate errors must be declared exactly. Use disposable credentials, digest-pinned fixtures and only clean up resources created by this run.

