## Parent

#83

## What to build

Add verified email replacement and secret-safe delivery. The old address remains active until the exact pending target has both a valid mailbox code and an unexpired, one-operation account authorization at activation. The original signed-in Profile page accepts the code; no secret or state-changing action goes into an email URL.

Use a narrow enabled-SMTP verification sender rather than ordinary notification history, which stores bodies. Completed sensitive changes separately attempt best-effort notifications to the captured existing trusted address.

## Acceptance criteria

- [ ] Add one pending target per account, bound to the initiating account/session/generation/incarnation/exact address, with an unpredictable single-use mailbox code valid for 15 minutes. Store verification material safely; no plaintext code in persistent notification/audit/log/diagnostic state.
- [ ] Requesting mail requires the prepared account proof but does not spend the authorization needed for activation. At activation, require that authorization still within five minutes or fresh verification for the same target, and atomically consume both proofs with email/generation/audit. Keep the old alias active until commit.
- [ ] Expose the parent's typed email request/resend/confirm/cancel endpoints with Session policy, no-store, antiforgery and stable outcomes. Generic uniqueness conflicts do not identify another account. Use ticket 1's canonical identity and database race protection.
- [ ] Resend/replacement invalidates previous codes; cancellation removes pending authority. Five incorrect mailbox attempts invalidate the code. Shared limits enforce one send/minute and six/hour/account plus source-IP protection; mailbox mistakes do not lock ordinary sign-in.
- [ ] Require configured, enabled real SMTP. Log/disabled transport produces a server-supplied unavailable/read-only capability with explanation. Actual code entry proves ownership; sender acceptance or a recorded send does not.
- [ ] Use a narrow verification-delivery seam over the real SMTP sender, excluding code/body/error leakage from ordinary EmailLog/NotificationService recording. Delivery failure never activates the address or reuses old authority; deliberate resend rotates the code.
- [ ] Every completed sensitive change attempts a separate secret-free notification to the existing trusted address; email activation uses the captured old address. Notification delivery/failure cannot roll back committed credentials or leave old sessions valid.
- [ ] Real SQL Server and focused PostgreSQL tests cover pending/activation state, exact target/session binding, stale authorization re-verification, canonical conflict races, concurrent resend/confirm/fifth-wrong attempts, one-time consumption and cancellation/expiry.
- [ ] AuditFailureFixture completes account verification before rejecting activation; assert old alias/generation/proofs unchanged and no usable grant escapes rejection. Test request/verification failure phases separately.
- [ ] Use a disposable real SMTP capture server and induce failure in the real transport. Pin digest images, configure the E2E stack for real delivery instead of today's disabled email, and clean only run-owned resources. Assert code absent from EmailLog, audit and diagnostics on success/failure.
- [ ] Use the post-IdP-assertion fixture to verify SSO uses only the active email after activation and no-proof SSO accounts remain restricted. Do not add an IdP reauthentication or recovery protocol.
- [ ] Regenerate TypeScript and run required .NET/type-generation, provider, real-SMTP and HTTP checks.

## Blocked by

- #87

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

