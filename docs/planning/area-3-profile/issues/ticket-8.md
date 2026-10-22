## Parent

#83

## What to build

Make Profile wholly Angular-owned and remove the old page/write surfaces in the same Area PR. Activate existing case-insensitive Profile URLs, update every in/out link and helper consumer, and run the full real-browser, persistence and failure verification.

The shared binary avatar handler and the deliberately anonymous cookie-only branch remain explicit exceptions, not surviving Legacy security-write aliases. Obtain the required whole-Area reviews and prepare one PR body for the owner; do not push or open/merge the PR.

## Acceptance criteria

- [ ] Activate the profile Area in the server route table and Angular sidebar/router as one unit. Preserve /Profile, /Profile/Index, /Profile/ChangePassword and /Profile/RecoveryCodes in any letter case, titles and signed-out return behavior.
- [ ] Both account menus, command palettes, My activity, Sign-in history, Signed-in devices and Security checkup enter Profile inside Angular. Internal page/Done links and Legacy sign-in transitions work on the intended session.
- [ ] Delete retired Profile Razor views, view models, unused helpers and controller page/write actions. Keep /Profile/Avatar in its explicit session-only binary handler, and switch Legacy Create user to /api/ui/passwords/check-breached. HTTP tests prove old unsafe routes/aliases cannot mutate.
- [ ] Update Area-policy pins, intentional anonymous allowlist, authorization-surface and layering tests deliberately. All ordinary UI API endpoints remain session-only/no-store/antiforgery; only the exact ADR0026 branch is anonymous and has zero account authority.
- [ ] Playwright under e2e/fixtures.ts exercises real details/avatar/password/email, real SMTP capture, TOTP reset and Legacy reenrollment, recovery generation/Print/Done/refresh/back, UV virtual passkey add/verify/remove and surviving-method guards, expiry/cancellation and every named failure outcome.
- [ ] Use browserHealth.newContext for additional browsers and verify every sensitive change invalidates prior/copied cookies, reset ends current, stale devices disappear, and no write is retried. Pin genuine new-session/antiforgery renewal, role/issuance race and committed-versus-unknown UX through the appropriate HTTP/browser seams.
- [ ] Parity specs cover the existing cards, fields, labels/icons/order, password controls, both themes and UTC passkey dates, plus keyboard/focus/announcements. Screenshots/style assertions use the visual parity: test-title convention. No undeclared CSP/Trusted Types/console/unhandled errors.
- [ ] Real final suites include SQL Server Production HTTP, all correctly phased AuditFailureFixture cases, focused PG migrations/persistence/concurrency, real Redis/outage/recovery-gate tests and real SMTP transport failure. No credential/audit service replacements or production test endpoints.
- [ ] Run the full final-commit gates: locked restore; zero-warning Release build; format; unit/integration/provider tests; agent guardrails; generated-type check; Angular lint/format/Vitest/build checks/production build; npm audit; e2e and e2e:verify-guards. Rebuild before no-build tests. Report actual outputs and limitations; never infer CI green.
- [ ] The security-reviewer reviews the complete Area diff against ADR0021–0026 and the parent spec; resolve concrete findings. Obtain final Standards and Spec reviews by a different model or disclose the permitted fresh-session fallback.
- [ ] Provide the ticket report with 2–3 representative new-code excerpts and write a concrete PR body to a durable file for the owner. One branch and one PR; do not push, open/merge the PR, close tickets, tag or delete branches.
- [ ] CONTEXT/ADRs and operational rollout/Redis-recovery instructions match implemented behavior. No forbidden-directory content or disposable secret artifacts are staged.

## Blocked by

- #90

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

