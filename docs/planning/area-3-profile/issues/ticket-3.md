## Parent

#83

## What to build

Replace timestamp arithmetic with durable credential generations and explicit session-store revocation versions. Fence old proofs, stale account snapshots and pending sign-ins through issuance; keep device listings truthful. Add the coordinated Redis recovery incarnation/gate and narrow local-cookie-clearing branch accepted in ADR0026.

A credential transaction must invalidate old credentials even if external stores fail. Explicit sign-out remains external revoke-first, audit-second. The fresh proven browser survives completed sensitive changes except reset; ordinary cookie renewal must never acquire a later revocation version.

## Acceptance criteria

- [ ] Add durable per-account credential generation with both provider migrations. Password, active email, recovery replacement and passkey add/remove advance it atomically with mutation/audit; reset advances it and signs out current too. Cosmetic edits/beginning operations do not advance it.
- [ ] Carry the original generation through every affected local/SSO pending principal, TOTP/recovery/enrollment, full sign-in, passkey, break-glass, trusted SSO, Profile replacement and secret-step-up path. Do not stamp old proof with a newly reread generation; fence consumption/mutation/issuance against the captured value.
- [ ] Use a non-repeating session-store revocation version with memory/Redis parity and preserve per-device tombstones. Capture it before the account snapshot and carry it through proof/issuance; reject changed versions rather than issuing stale roles with a newer version.
- [ ] Migrate every account-wide caller: Profile, administrator role/disable/forced logout/deletion and intrusion response. Sliding expiration, display/avatar renewal and secret step-up preserve the original external version.
- [ ] Missing/expired/evicted store state invalidates old cookies. Initialize fresh state atomically with a non-repeating value; never recreate state using a value supplied by a cookie.
- [ ] Discoverable sign-in keeps Q22's explicit account-unknown boundary: bind generation when the credential resolves its owner before verification, fence completion, and never rescue removed keys or stale named proofs.
- [ ] Fresh current sessions use a new session ID and the mutation's committed generation. Old sessions remain invalid after renewal/response failure; return a known committed/sign-in-required outcome without retrying or undoing the mutation. Device registry recording remains best-effort.
- [ ] Registry observations carry/filter original credential generation, external version and incarnation, so stale in-flight touches cannot resurrect invalid device listings.
- [ ] Implement the coordinated session-store incarnation outside restored Redis and a documented traffic gate across all nodes after possible lost revocation writes. Invalidate old sessions and pending session-bound proofs; retire nodes unable to enforce the fence. Do not promise automatic undisclosed-rollback detection.
- [ ] Implement ADR0026's exact terminal route outside /api/ui before store-backed blacklist/session/auth middleware: GET creates a fresh anonymous no-store antiforgery token; POST validates it anonymously and deletes the configured auth cookie with its CookieManager/path/domain/chunk behavior. No account read, protected data, server revocation or protected-pipeline continuation.
- [ ] Normal account-wide sign-out remains fully validated, clears the local cookie, revokes first, audits confirmed results and truthfully reports revoked_unrecorded or sign_out_incomplete. The fallback preserves incomplete warnings rather than claiming all-device success.
- [ ] Real memory/Redis and isolated Production HTTP tests cover same-instant ordering, concurrent sign-out, missing/expired state, role-change/issuance race, all revokers, retained renewal versions, pending-proof fencing, stale device observations and real store outage.
- [ ] Recovery tests restore populated older Redis state, rotate the external incarnation through the supported gate and prove old sessions/proofs fail across nodes. Cookie tests pin anonymous allowlisting, wrong paths/methods, missing/invalid/authenticated-versus-anonymous CSRF tokens and chunk deletion.
- [ ] Document one-time refusal of generation-less cookies/proofs at rollout, old-node retirement and coordinated Redis recovery. Required Release/format/unit/integration/provider checks pass; obtain a focused security review of these invalidation/outage boundaries.

## Blocked by

- #85

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

