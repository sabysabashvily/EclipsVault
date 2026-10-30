## Parent

#83

## What to build

Build the Angular Profile/details, avatar, email and Change password components with the existing card structure and named changes only. Add the reusable accessible account-verification dialog using Area 2 ConfirmationDialog and StatusLine, and truthful sign-out/committed-outcome handling.

Keep the Profile Area marked Legacy until ticket 8 changes ownership as one unit. This ticket proves staged components with Vitest and backend/Legacy regression tests; the complete browser acceptance below is exercised by ticket 8 once production routes expose the complete Area. Do not add production test-only routes.

## Acceptance criteria

- [ ] Build Profile/details and Change password with the existing titles, cards, fields, icons/order/labels and immutable username/clearance/project. Display-name Save is separate from pending email controls; only the approved advice/date/security-flow differences apply.
- [ ] Use the typed page snapshot and server-supplied action/method availability with clear refusal reasons. SSO without usable local proof cannot silently use its session as verification. No new runtime package or relaxed CSP/Trusted Types.
- [ ] Build one reusable accessible verification dialog showing frozen intent and the accepted methods. Destructive ConfirmationDialog comes first with no nesting; Cancel/Escape clear sensitive fields and restore focus. Use shared StatusLine announcements, not toasts.
- [ ] Wire display-name/avatar changes, new-mailbox request/resend/confirm/cancel and password change to the typed endpoints. Prevent duplicate submissions. Clear sensitive state on cancellation, navigation, pagehide/back-forward restoration and session loss; never automatically replay mutations.
- [ ] Preserve avatar JPEG/PNG detection, 4 MiB file/6 MiB request/dimension/complete-decode limits and metadata-free 256x256 re-encoding. Keep the shared binary /Profile/Avatar route, generated seeds and intentional Legacy Users image reads with no-store.
- [ ] Preserve current/new/confirmation password controls, length 12–256, unchanged/breached refusal, show/hide and advisory meter through CSP-safe templates/finite classes. Replace periodic-change advice with compromise-based advice.
- [ ] Handle ordinary wrong proof inline, session loss through existing 401 behavior without replay, known committed changes requiring sign-in, unknown response loss and explicit sign-out partial outcomes truthfully. Use ADR0026's local-cookie fallback if store-backed sign-out cannot run; preserve warnings into Legacy sign-in.
- [ ] Refresh CurrentAccount/avatar/name and antiforgery state after genuine retained-session replacement. Email state never shows a pending alias as active. Loading/fetch retry does not retry a write.
- [ ] Vitest covers fields/forms, capabilities/reasons, all proof choices, dialog focus/default/Escape/cancel, duplicate-submit handling, timers, inline failures, email states and sensitive-data disposal. Existing backend/Legacy tests remain green.
- [ ] Ticket 8 owns production route activation and complete browser execution: this ticket supplies the acceptance inventory for card/password/email/avatar parity, real SMTP activation, two-context invalidation, keyboard and both themes. No temporary production test-only route or false claim of pre-cutover browser coverage.
- [ ] Angular lint/format/Vitest/build checks/build, generated-type check, npm audit and relevant .NET checks pass.

## Blocked by

- #88

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

