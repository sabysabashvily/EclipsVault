## Parent

#83

## What to build

Email is a sign-in identity and SSO matching key. Apply trimmed comparison that ignores case for the letters A to Z only consistently in application code and both databases while preserving spelling for display/delivery. SSO resolves email only; combined username/email callers refuse different matching account IDs.

This ticket creates `area-3-profile` from main at c7b4b1b or later, preserves the owner's existing uncommitted documents, and commits CONTEXT.md, modified ADR0021, new ADR0022–0026 and the confirmed records in `docs/planning/area-3-profile/`. The existing administrator form already forbids @ in usernames; the ambiguous SSO scenario requires old/custom provisioning. Do not describe it as an ordinary-user provisioning exploit.

## Acceptance criteria

- [ ] One application email-normalization policy and matching database uniqueness are applied to lookup, provisioning/bootstrap and Profile activation; preserve entered spelling and perform no dot/plus folding. Keep username case behavior unchanged.
- [ ] Migration preflight checks all existing accounts, including disabled accounts, for canonical email and cross-account alias collisions. Refuse the upgrade with actionable safe diagnostics for explicit resolution; never merge accounts or choose an owner automatically.
- [ ] SQL Server and PostgreSQL migrations/backfills enforce uniqueness with semantics matching application lookup. New/updated case variants cannot create separate identities regardless of raw-column collation.
- [ ] SSO uses email-only lookup. Password login, named-passkey options and sharing/other combined lookup callers refuse ambiguous different-account matches. Core enforces the same ASCII username rules as Web for new provisioning.
- [ ] A duplicate activation/database-constraint race maps to a generic conflict without identifying the other account; the later email endpoint uses this result.
- [ ] Real upgrade-data tests for both providers cover preserved spelling, trimmed/case variants, disabled collisions, existing cross-alias collisions, noncolliding data, and refusal without partial identity reassignment. Focused persistence tests prove lookup and constraints agree.
- [ ] HTTP regression tests use the existing post-IdP-assertion OidcCorrelation fixture for the real callback and email-only matching/ambiguity refusal. Assert the selected account and attribution; do not claim external token-validation coverage.
- [ ] The branch is based on main at c7b4b1b or later. Commit the explicit glossary, ADR and planning-record paths listed above; docs/research/ and docs/internal/ remain untouched.
- [ ] Locked restore, zero-warning Release build, formatting, relevant unit/HTTP/migration tests and both-provider migration checks pass.

## Blocked by

None (first implementation ticket).

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

