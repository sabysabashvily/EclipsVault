## Parent

#83

## What to build

Build the Angular authenticator reset, recovery-code generation/display/Print/Done and passkey add/list/remove flows on the verified backend. Preserve accepted reset/last-key safeguards and two-minute WebAuthn behavior.

Keep Profile's ownership unchanged until ticket 8. Vitest proves the staged UI here; ticket 8 exercises these complete flows through real production routes under browser-health. Do not add test-only production routes or prematurely expose an incomplete Area.

## Acceptance criteria

- [ ] Authenticator card keeps reset → sign out → Legacy sign-in/enrollment when local TOTP is required, with proof/confirmation and truthful outcomes. Retain unrelated passkeys/SSO; show server reasons for absent enrollment or unavailable proof.
- [ ] Recovery replacement requires enrolled TOTP and one verified frozen action. Show exactly ten returned codes in current-page memory with Print and Done; no cookie/TempData/local/session storage/URL, refetchable response or automatic mutation retry.
- [ ] Clear recovery plaintext and factor inputs on Done, cancellation, navigation, pagehide/back-forward restoration and session loss. Refresh/direct visit returns to Profile; lost/ambiguous response explains deliberate regeneration. Do not recover plaintext after postcommit renewal failure.
- [ ] Passkey add/list/remove preserves naming and summaries, requires existing-account proof before addition/removal, uses server-bound 120-second ceremonies, handles browser cancellation separately, and displays creation dates explicitly in UTC. No passkey rename feature.
- [ ] Delete-last-key UI explains a missing proven surviving method, but the server remains authoritative. Spent recovery codes/deleted key/SSO session cannot count as fallback; handle stale capability/conflict safely.
- [ ] Successful sensitive changes refresh shared account/antiforgery state with a genuine new session; reset signs out current. Show changed_sign_in_required or unknown outcomes without replaying the write.
- [ ] Reuse ConfirmationDialog, the shared verification dialog and StatusLine; no nested/native confirm dialogs, unsafe HTML, inline styles or new runtime packages. Controls and printed recovery view remain accessible and match Legacy except accepted fixes.
- [ ] Vitest covers every security flow, expiry/cancellation, capability explanations, codes-only-once and clear-on-leave behavior, duplicate submits, verification errors and postcommit outcomes.
- [ ] Ticket 8 runs real browser-health coverage after whole-Area cutover: real password/TOTP/recovery, UV virtual passkey add/verify/remove, final-key guards, two contexts/copied cookie refusal, Print/Done/refresh/back clearing, both themes and keyboard. No production test-only routes or premature ownership switch.
- [ ] Angular lint/format/Vitest/build checks/build, generated-type check, npm audit and relevant .NET/HTTP regressions pass.

## Blocked by

- #89

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

