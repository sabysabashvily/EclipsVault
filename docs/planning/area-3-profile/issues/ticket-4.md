## Parent

#83

## What to build

Expose the accepted Profile UI API through explicit Web DTOs: bounded server-prepared operations freeze intended changes; common verification proves one operation; typed final endpoints commit it. No operation ID, active session or new authenticator proves an existing credential by itself.

Implement details/avatar/password/authenticator/recovery/passkey backend endpoints and sign-out behavior using tickets 2–3. Email request/activation builds on this in ticket 5. Share account-verification machinery narrowly with affected Legacy callers; keep their sign-in/enrollment pages until Area 8.

## Acceptance criteria

- [ ] Profile read/details/avatar/password/reset/recovery/passkey/sign-out and prepared-operation routes from the parent contract exist with explicit cookie/Session authorization, no-store, antiforgery on writes, stable Problem Details and safe field-by-field Web DTO mapping. Identity comes only from the session; administrator status widens nothing.
- [ ] Preparation accepts explicit tagged actions, freezes normalized intended data and remains finite: absolute unverified expiry, active-operation limits, shared admission and hashing-concurrency bounds, cheap input checks before expensive hashes. Retain candidate password hash/salt only; no plaintext persistence.
- [ ] A grant is bound to account, session, original generation/revocation/incarnation and frozen operation/target. It expires five minutes after verification and is consumed once with the final mutation/audit/generation transaction. Changing data or replaying a grant cannot authorize another action.
- [ ] Accept current password + TOTP, current password + unused recovery code, or an existing passkey with UV. Passkey proof enforces expected-account ownership on the server. Password change additionally requires the current password before success/failure reset.
- [ ] Configured shared IP and account lockout cover all actual proof attempts, including current-password and failed passkey checks. A valid passkey with wrong required current password does not reset failures; browser cancellation does not count as wrong proof. Sign-out remains unmetered.
- [ ] Management/assertion/registration challenges enforce 120-second absolute server deadlines, purpose/session/known-account generation binding and atomic consumption. One active attempt per session/purpose; replacement cancels the previous attempt.
- [ ] Reset is refused without enrolled TOTP and recovery generation is refused while TOTP is absent. Last-passkey removal proves another usable local method and rechecks surviving fallback in a transaction serialized with recovery redemption, excluding the removed key and spent code.
- [ ] Profile verification records truthful verification events, never login success/location or the sign-in-shaped RecoveryCodeUsed event. All ledger/counter/grant writes are atomic with their intended events.
- [ ] Wrong proof returns a page-local 400, unusable session alone produces 401; unknown/foreign IDs share 404; stale/expired/unavailable/conflict results use the pinned 409 codes, throttling 429, and audit/committed/revocation failures retain the parent's distinct 503 outcomes. Pin final success DTOs/empty statuses in HTTP tests.
- [ ] Snapshot exposes only display/capability fields; never generations/versions, password/TOTP/recovery secrets, public-key blobs or authorization material. Recovery generation alone returns ten new plaintext codes once, never stores a retrievable response and never automatically retries.
- [ ] Current/new passwords, TOTP inputs, recovery plaintext, raw passkey responses and mailbox codes never enter URLs, application logs, audit details or recorded notification bodies. Validation, exception and transport diagnostics do not echo these inputs.
- [ ] Two-account real Production HTTP tests pin every route's anonymous/API-key refusal, session scoping, overposting resistance, antiforgery, no-store, full shapes, methods, event counts and failure outcomes. Cover foreign/changed/expired/replayed operations, cross-account passkeys and concurrent consume.
- [ ] Real HTTP tests inspect captured diagnostics, audit details, URLs and recorded notification bodies for proof-input disclosure on success, wrong/malformed/expired proof and audit-failure paths; safe response shapes alone do not satisfy this check.
- [ ] AuditFailureFixture mutation tests prepare and verify before rejection; separate verification/counter/ledger rejection tests prove no usable grant or proof state escapes rollback. Test the last-recovery deletion race and wrong-password-plus-valid-passkey counting.
- [ ] Regenerate and commit TypeScript; document finite preparation limits. Required .NET/type-generation and relevant provider/HTTP tests pass.

## Blocked by

- #86

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

