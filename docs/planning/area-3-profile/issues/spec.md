Follows #74 (Area 2, merged in #82), after #55/#71 and the Foundation. ADR-0017 puts **Area 3: Profile** next. The owner confirmed all 31 grill decisions and the eight-ticket delivery outline.

## Problem Statement

Opening Profile from the Angular UI still leaves the app for three Legacy pages: Profile, Change password and Recovery codes. Profile owns security-sensitive credential management as well as display-name and avatar edits. Its migration must preserve the existing pages while repairing the shared backend guarantees they depend on.

The grill's source survey and independent security reviews found concrete failure scenarios; these are source deductions, not runtime exploit reproductions:

- Profile details, password, avatar, authenticator reset, recovery replacement and passkey changes save before separate audit writes. Rejecting audit inserts leaves unaudited changes, including a recovery set whose plaintext was never delivered.
- A copied session alone can install a lasting passkey, change the email used by SSO, reset TOTP or generate recovery credentials.
- Whole-row User updates can restore stale password/TOTP/role/disabled/replay state after an overlapping cosmetic edit.
- Reset saves TOTP removal and recovery deletion separately and leaves copied/other sessions alive. Recovery codes can be generated while TOTP is absent and become usable after reenrollment.
- Future-dated password renewal can escape a subsequent same-second sign-out. Timestamp revocation also cannot distinguish a genuinely new same-second session reliably.
- Profile password guesses bypass the sign-in throttle/lockout. TOTP/recovery consumption and failure-counter increments have concurrency gaps.
- Registration challenges lack initiating-account binding, absolute server expiry and atomic consumption.
- Sign out everywhere has no security audit event today.
- Email comparison/uniqueness depends on deployment collation. The combined username/email lookup can select the wrong account if custom provisioning created a cross-account alias collision. The normal administrator form already excludes email-shaped usernames; this is a conditional data/provisioning defect.

## Solution

The Angular UI serves all Profile pages at the existing case-insensitive URLs, preserving layout, cards, icons, labels and behavior except the named fixes below. The whole Area changes ownership in one PR; no Legacy security write handler remains at completion.

Sensitive changes require fresh proof of existing credentials for one frozen operation. Database credential changes, proof consumption, credential-generation advancement and semantic auditing commit together. Explicit session-store revocation stays revoke-first under ADR-0021 and reports partial outcomes truthfully. New email activates only after mailbox ownership confirmation. Shared Legacy sign-in/enrollment pages remain in Area 8; their affected backend callers receive narrow repairs and regression tests here.

## User Stories

1. As a vault account, I keep bookmarks for `/Profile`, `/Profile/Index`, `/Profile/ChangePassword` and `/Profile/RecoveryCodes` in any letter case, the existing titles and sign-in return behavior.
2. As a vault account, menus, palettes, My activity, Sign-in history, Signed-in devices and Security checkup open Profile inside Angular; links back to Legacy sign-in still work on the same account.
3. As a vault account, I see the same avatar, fixed username, clearance/project, details and security cards. I can save my display name independently of a pending email change.
4. As a vault account, I can upload/remove my avatar with the existing protections; both shells and Legacy Users still display their intended avatars.
5. As a vault account, I see which sensitive actions and existing proof methods are available and why an action is unavailable.
6. As a vault account, I authorize the exact intended change with current password + TOTP, current password + unused recovery code, or an existing UV passkey. A password change still requires the current password.
7. As a vault account, changing operation data or waiting past authorization expiry requires new verification. Cancellation clears sensitive inputs and restores focus.
8. As a vault account, I retain the existing password form, confirmation, advisory meter, show/hide and breach checks, with compromise-based advice.
9. As a vault account, completed sensitive changes invalidate prior sessions; the verified browser receives a fresh session except reset, which signs everyone out.
10. As a vault account, reset retires TOTP and recovery codes while retaining unrelated passkeys/SSO; local-TOTP-required sign-in performs enrollment.
11. As a vault account, I receive ten replacement recovery codes once, can Print/Done, and understand that losing the response requires deliberate regeneration.
12. As a vault account, I can add/remove passkeys with bounded ceremonies, explicit UTC dates and a proven surviving local method before deleting the last key.
13. As a vault account, my old email remains active until a 15-minute mailbox code and still-fresh account authorization activate the new address on the original signed-in Profile page.
14. As a vault account, resend/cancel and five-wrong-attempt behavior are clear; missing SMTP makes email read-only rather than pretending delivery succeeded.
15. As an SSO user without a usable local proof, I receive an explanation when sensitive changes are unavailable; my ordinary vault use continues.
16. As a vault account, every completed sensitive change attempts a notification to my existing trusted address without putting reusable secrets in history.
17. As a vault account, wrong proof stays on Profile, loading/fetch failures are understandable, duplicate submissions are prevented, and writes are never automatically retried.
18. As a vault account, I learn whether a credential change committed, whether I must sign in again, or whether a lost response left its outcome unknown.
19. As a vault account, sign out everywhere reports confirmed, unrecorded or incomplete outcomes truthfully and clears my local cookie even if the session store is unavailable.
20. As the owner, a restored Redis snapshot cannot revive old sessions after the documented, coordinated recovery gate.
21. As a keyboard/screen-reader user, the shared confirmation, verification dialog and StatusLine announce intent/outcomes, handle Escape/Cancel and restore focus without nested dialogs.
22. As the owner, real HTTP, audit-failure, provider, concurrency, Redis, SMTP and browser tests prove the exact boundaries below.

## Confirmed Decisions

These are the authoritative accepted answers, including named parity exceptions.

| Ref | Decision | Required behavior |
| --- | --- | --- |
| Q1 | Sensitive changes | Email, password, authenticator, recovery-code and passkey changes require fresh proof from existing credentials. Display-name/avatar edits and signing out everywhere do not require this extra proof. |
| Q2 | Authenticator flow | Keep reset → sign out → Legacy enrollment when sign-in requires local TOTP. Retire the old TOTP and recovery codes atomically; invalidate sessions and affected pending proofs. No in-place authenticator replacement in Profile. |
| Q3 | Password-change session | Keep the verified browser signed in with a genuinely fresh session and new session ID. Invalidate its previous cookie and other sessions; remove future-dated authentication timestamps. |
| Q4 | Recovery-code display | Keep ten codes, Print and Done. Deliver plaintext once into current page memory; clear it on departure or session loss. No cookie, browser storage or URL retention. Refresh/direct visits return to Profile; a lost response requires deliberate regeneration. |
| Q5 | Shared repair boundary | Repair the shared backend and affected Legacy callers narrowly; leave sign-in/enrollment pages for Area 8. Profile database mutations and semantic audit events commit together. Field-specific updates and concurrency controls prevent cosmetic writes from restoring stale credentials or security state. |
| Q6 | Accepted account proof | Accept current password + TOTP, current password + an unused recovery code, or an already registered passkey with user verification. Enforce the session account server-side. Password changes additionally require the current password, including when a passkey supplies verification. |
| Q7 | One operation | A server-held authorization covers one operation with frozen intended data, bound to account, session and original credential generation. It expires five minutes after verification and is consumed once. Changed data requires new verification; no general unlocked Profile period. |
| Q8 | Durable credential generation | Advance a database credential generation atomically with the change, authorization consumption and audit. Carry the original generation through pending proof and issuance; never upgrade stale proof by rereading the latest value. Refuse old generation-less cookies/proofs and retire older application nodes at rollout. |
| Q9 | Completed-change invalidation | Every completed password, active email, recovery-set or passkey add/remove change invalidates prior sessions and retains a fresh current session. Authenticator reset ends all sessions, including current. Cosmetic edits and merely starting a change do not end sessions. |
| Q10 | Email ownership | Require both account verification and new-mailbox ownership before activation. Keep the old address active while pending or delivery fails. Without configured, enabled SMTP, email is read-only with an explanation. Confirmation secrets stay out of logs, audit and recorded notification bodies. |
| Q11 | Throttling and replay | Reuse configured shared IP and account limits across verification methods: defaults 11 requests/120 seconds and five failures/15-minute lockout. Make failure counts, TOTP replay and recovery consumption atomic. Reset failures only after the entire intended proof succeeds. Browser cancellation is not a wrong credential; sign-out remains unmetered. |
| Q12 | SSO without local proof | Keep sensitive changes unavailable with an explanation when no usable local proof exists. Add no fresh-IdP verification protocol in Area 3. An SSO session alone is insufficient; do not imply an existing administrator password-reset feature. |
| Q13 | Mailbox confirmation | Use one pending target and a single-use mailbox code valid for 15 minutes, entered on the original signed-in Profile page. Account authorization must still be within five minutes at activation or be repeated for that exact target. Consume it at activation. Resend/replacement invalidates the old code; cancellation is available. |
| Q14 | Mailbox attempt limits | Five wrong attempts invalidate the pending code. Limit sends/resends to one per minute and six per hour per account, with shared source-IP protection. Mailbox mistakes do not lock ordinary sign-in; delivery failure leaves active email unchanged. |
| Q15 | Passkey ceremony state | Enforce a 120-second server deadline, purpose/session binding and account/generation binding where known. Consume challenges atomically once. Starting a new attempt replaces the prior attempt for that session and purpose. |
| Q16 | Last passkey | Require proof of a different local method that remains usable after deletion. The removed key, an unproved password hash or an SSO session cannot qualify. A spent recovery code cannot count as fallback; recheck surviving recovery capability in the deletion transaction, serialized with redemption. |
| Q17 | Reset scope and guards | Retire TOTP, its replay marker and recovery codes; retain unrelated passkeys and SSO. Reenroll when local TOTP is required at sign-in. Server-side guards refuse reset without enrollment and recovery generation without TOTP; return availability and reasons. |
| Q18 | Committed and uncertain outcomes | Keep a committed change and its invalidation even if cookie renewal or response delivery fails. Never automatically retry the mutation. Distinguish a known commit requiring sign-in from an unknown outcome; check state/sign-in before deliberate retry. Metadata failure is best-effort; lost recovery plaintext is not retained. |
| Q19 | Explicit session revocation | Use atomic session-store revocation versions instead of timestamps, with memory/Redis parity and per-device tombstones. Capture the version before loading the authentication snapshot and carry it through issuance. Preserve it on ordinary renewal/step-up. Refuse missing state; initialize new state atomically with a non-repeating value, never from a cookie. Migrate every account-wide revocation caller. |
| Q20 | Truthful sign-out | Clear the local cookie regardless of revocation/audit outcome. Revoke first, then audit confirmed results. Return revoked_unrecorded for confirmed revocation with failed audit, or sign_out_incomplete for uncertain/incomplete revocation. Preserve the warning through Legacy sign-in. |
| Q21 | Security notifications | After every completed sensitive change, attempt notification to the existing trusted address; capture the old address before email activation. Delivery is best-effort and secret-free, and cannot undo the committed change. Ownership-code delivery remains separate. |
| Q22 | Discoverable passkey boundary | Preserve username-less passkey sign-in. Capture and fence account generation when the credential identifies its owner, before verification. An unchanged registered key may answer an unexpired anonymous challenge begun before another credential change; removed keys and stale named-account proofs remain invalid. |
| Q23 | Email comparison | Trim email identities and ignore case for the letters A to Z only; every other character must match exactly. Preserve spelling for display/delivery. Enforce matching uniqueness in both databases, including disabled accounts. Fail migration on canonical/alias collisions for explicit resolution; never merge/select an owner. Activation races return a generic conflict. No dot/plus alias folding. |
| Q24 | Account lookup | Give SSO an email-only lookup. Combined username/email callers reject matches to different account IDs. Enforce Web username rules in Core, and explicitly resolve preexisting cross-account aliases. Keep username case policy and the OIDC protocol unchanged. |
| Q25 | Prepared typed operations | Prepare frozen intended data on the server, use shared verification and typed final-action endpoints. Operation IDs alone confer no authority. Bound preparation lifetime, active state, admission and hashing concurrency before expensive work. Use safe response allowlists, stable Problem Details and HTTP contract tests; wrong proof stays on Profile, while 401 means an unusable session. |
| Q26 | Profile controls | Keep the existing cards, with separate display-name Save and email-change controls. Use an accessible shared verification dialog, destructive ConfirmationDialog first without nesting, and StatusLine. Cancel/Escape restore focus and clear sensitive input; prevent duplicate submissions and refresh shared account/antiforgery state after renewal. |
| Q27 | Routes and compatibility | Keep /Profile/Avatar as a shared session-only binary route with no-store, intentional cross-account avatar reads and existing upload protections. Preserve case-insensitive Profile page URLs. Move breach preview to the UI API for Profile and Legacy Create user; retire unsafe Legacy Profile write handlers at cutover. |
| Q28 | Named presentation fixes | Replace “Change it periodically” with compromise-based password advice. Show passkey creation dates explicitly in UTC. Preserve other appearance/text except accepted security flows, including CSP-safe meter, show/hide, length/confirmation, unchanged-password refusal and breach checks. |
| Q29 | SSO and verification coverage | Reuse the existing fixture beginning after IdP assertion for the real callback, account matching, attribution and proof policy. Full external IdP/browser protocol coverage remains Area 8; do not claim that the fixture validates IdP tokens. Retain the full real-failure, concurrency and browser test plan. |
| Q30 | Outage-only local clearing | Provide an exact-route terminal anonymous GET-token/antiforgery-protected POST outside the UI API and before store-backed middleware. It only clears the configured local authentication cookie, reads no account identity, performs no server revocation and never enters protected routes. Preserve sign_out_incomplete; test allowlisting, methods, CSRF and cookie chunks. |
| Q31 | Redis recovery gate | Before traffic resumes after recovery that may have lost revocation writes, rotate a session-store incarnation outside the restored Redis keyspace across all nodes. Invalidate old sessions and fence pending session-bound proofs. Document and test the gate; the guarantee requires coordinated recovery and cannot automatically detect undisclosed rollback. |

## Implementation Decisions

### UI API boundary and contract

Use the cookie scheme plus Session policy, session-derived account ID via the existing claim helpers, `no-store`, RFC 9457 Problem Details and antiforgery on every unsafe request. Administrators receive no broader Profile authority. Added account/username fields cannot redirect ownership. Explicit Web DTOs map safe fields from Core; regenerate TypeScript and pin complete HTTP shapes.

```text
GET    /api/ui/profile
PATCH  /api/ui/profile/details
POST   /api/ui/profile/avatar
DELETE /api/ui/profile/avatar
POST   /api/ui/profile/operations
DELETE /api/ui/profile/operations/{operationId}
POST   /api/ui/profile/operations/{operationId}/verify
POST   /api/ui/profile/operations/{operationId}/passkey/begin
POST   /api/ui/profile/operations/{operationId}/passkey/complete
POST   /api/ui/profile/password
POST   /api/ui/profile/authenticator/reset
POST   /api/ui/profile/recovery-codes
POST   /api/ui/profile/passkeys/registration/begin
POST   /api/ui/profile/passkeys/registration/complete
DELETE /api/ui/profile/passkeys/{passkeyId}
POST   /api/ui/profile/email/request
POST   /api/ui/profile/email/resend
POST   /api/ui/profile/email/confirm
DELETE /api/ui/profile/email/pending
POST   /api/ui/profile/sign-out-everywhere
POST   /api/ui/passwords/check-breached
GET    /Profile/Avatar
```

The snapshot contains display/access labels, avatar URL, enrolled status, recovery count, passkey summaries, pending-email status and server-supplied action/method availability. Exclude credential generations/revocation versions, password/TOTP/recovery material, passkey public keys and authorization secrets. Public credential IDs appear only where WebAuthn requires them; summaries use record IDs.

Prepare/verify accepts explicit tagged types, not arbitrary JSON commands. Store only bounded, expiring server-side operation state. A prepared password can retain its candidate hash/salt, never plaintext. Freeze normalized targets and recheck them at final commit. An operation ID is only an account/session-bound correlation ID. Bound admission and hashing concurrency before expensive work. Final writes consume the authorization in the same database transaction as their mutation/event. Email consumes it at activation, not mail request.

Current/new passwords, TOTP inputs, recovery plaintext, raw passkey responses and mailbox codes must never enter URLs, application logs, audit details or recorded notification bodies. Validation, exception and transport diagnostics must not echo these inputs. Real HTTP success, wrong/malformed/expired proof and audit-failure tests inspect diagnostics and stored events/history for disclosure as well as checking response shapes.

| Outcome | Contract |
| --- | --- |
| Bad input or wrong proof | 400 with stable safe field/code information; stay on Profile |
| Unusable full session | 401 `unauthenticated` / `stale_session`; use existing sign-in handling without request replay |
| Unknown or foreign operation/key | 404 with the same safe result |
| Expired/stale operation, missing proof, unavailable method/fallback or duplicate email | 409 with the relevant stable code |
| Admission/attempt limit | 429 `throttled`, with the applicable retry behavior |
| Audit transaction rejected | 503 `audit_unavailable`; no mutation, generation or usable authorization escapes rollback |
| Known committed change, replacement session failed | 503 `changed_sign_in_required`; change and invalidation stand |
| Confirmed revocation, audit failed | 503 `revoked_unrecorded`; revocation stands |
| Uncertain/incomplete external sign-out | 503 `sign_out_incomplete`; preserve warning and clear local cookie |

Pin codes including `validation_failed`, `verification_failed`, `operation_not_found`, `passkey_not_found`, `operation_expired`, `operation_changed`, `verification_required`, `verification_expired`, `method_unavailable`, `alternate_method_required`, `authenticator_not_enrolled`, `email_conflict`, `email_code_invalid`, `email_code_expired`, `mail_unavailable` and existing audit/session/antiforgery codes. Pin final success DTOs/statuses, including deliberate empty responses, in HTTP tests. Recovery generation alone returns new recovery plaintext; never retain it for replay.

### Audit, persistence and authentication

Use an operation-aware persistence seam and interceptor rules, not a generic User→ProfileUpdated rule. Each intended operation has one semantic mutation event, even if it replaces ten code rows. Replay/lockout/admin bookkeeping must not create false Profile edits or duplicate existing events. Separate Profile verification from login-specific events, especially recovery-code use that currently establishes sign-in location.

Protect field-specific updates and concurrency for user/security fields, recovery replacement/redemption, TOTP step consumption, failure counting, grants and mailbox attempts. A stale in-flight request cannot upgrade its original generation/version or resurrect registry metadata. Recheck last-key fallback under the same serialization as recovery consumption.

Carry original generation and external revocation state through local/SSO pending principals, TOTP/recovery/enrollment, named passkey and management ceremonies, full sign-ins, break-glass, trusted SSO, Profile replacement and secret step-up. Preserve ordinary-renewal versions. For discoverable sign-in, apply Q22's explicit account-unknown boundary. Capture the external revocation version before the account snapshot; do not bless stale role claims with a later value. Include existing role-change, disable, forced-logout, deletion and intrusion-response revokers.

Keep the durable credential transaction independent of session-store availability for its invalidation guarantee. Preserve per-device tombstones and revoke-first auditing for external session removals. Registry observations carry/filter original generation/version/incarnation; cleanup failure cannot restore authentication validity.

### Compatibility and UI

- Preserve cards/text/order/icons and the three page titles; separately save display name and email. Keep immutable account/access fields.
- Preserve avatar limits: JPEG/PNG by content, file ≤4 MiB, request ≤6 MiB, ≤6000 pixels per axis, complete decoding, crop/re-encode 256×256 PNG without metadata. Keep generated seeds and intended account-ID image reads for Legacy Users; no-store replaces the old private avatar cache.
- Retain `/Profile/Avatar` outside the retired Profile controller as an explicitly session-only binary handler. Move the shared breach preview to the UI API and update Legacy Create user. Retire old unsafe Profile aliases.
- Reuse Area 2 ConfirmationDialog/StatusLine. Show the intended operation in shared accessible account verification; destructive confirmation comes first with no nesting. Clear sensitive state on cancel, navigation, pagehide/back-forward restoration and session loss. Prevent duplicate submits; browser cancellation never falls back silently to weaker proof.
- Refresh CurrentAccount and antiforgery state after retained-session changes. Use signals/existing request patterns and CSP-safe templates/finite classes; add no runtime dependency.
- After a failed account-wide attempt, Q30's exact-route anonymous fallback gets a fresh anonymous antiforgery token and clears only the configured local auth cookie before store-backed middleware. Deliberately allowlist and isolate it; no unvalidated identity can revoke account-wide access.

### Email and operational recovery

Use a narrow verification-delivery seam that requires enabled SMTP and excludes codes from EmailLog bodies, audit and diagnostics; ordinary NotificationService currently records full bodies. SMTP acceptance is delivery evidence, not ownership proof. Best-effort completed-change notices are separate and use the captured old trusted address.

Apply one email normalization and matching unique constraints in both providers; preserve display spelling. Migration checks disabled accounts and fails on canonical/cross-account alias collisions for explicit resolution. Recheck at activation and map the uniqueness race safely. SSO gets email-only lookup, other combined callers fail ambiguous matches, and Core username rules match Web.

Before serving session traffic after Redis recovery that may have lost revocation writes, coordinate an incarnation rotation outside the recovered keyspace across every node. Old sessions and pending session-bound proofs must fail; retire incapable nodes. Ship the recovery procedure/gate and rollout tests. This is an explicit operator coordination requirement, not automatic rollback detection.

## Testing Decisions

Tests accompany their behavior; ticket 8 runs the full final suite.

- **Production HTTP / SQL Server:** real sign-ins and two distinct accounts, including administrator self-service. Pin every route's session/API-key/anonymous authorization, antiforgery, no-store, full DTO allowlist, ownership/overposting refusal, stable errors, success statuses and exact audit semantics. Cover all sensitive writes, wrong/expired/foreign proofs, preserved Legacy callers and retired write URLs. Reuse `VaultFixture`; no production test routes.
- **AuditFailureFixture:** prepare and verify before enabling rejection for mutation/activation tests so they actually reach the mutation. Separately reject verification/ledger transactions. Assert unchanged credentials/generation/pending proof/grant state, no new plaintext, no later SaveChanges rescue and exactly one intended successful event. Explicit sign-out still revokes and reports failed auditing. Always restore the fixture in finally.
- **Both providers:** real SQL Server/PostgreSQL upgrade-data tests for email normalization/uniqueness, disabled and canonical/alias collisions; migrations for every model change. Focused real-PG persistence/concurrency tests cover new transactional behavior; do not take over #66's broader PG HTTP work.
- **Concurrency:** same TOTP step, same recovery code, overlapping first recovery generation, concurrent lockout failures, cosmetic/security overlap, generation changes during proof/issuance, role demotion during issuance, competing operation/passkey consumption, last-key deletion versus final recovery redemption, mailbox resend/confirm/fifth-wrong races, and rollback leaving no usable grant.
- **Memory and real Redis:** non-repeating initialization, missing/expired/evicted state, same-instant revocation, concurrent sign-out, retained renewal versions, per-device tombstones, stale device observations and all account-wide callers. Use an isolated real-Redis Production HTTP host for store outages. Restore populated older state, perform the coordinated incarnation rotation and prove old sessions/proofs fail across nodes.
- **Local-cookie branch:** real store outage, exact paths/methods/allowlist, no account authority or protected data, missing/invalid/authenticated-versus-anonymous antiforgery tokens, correct cookie path/domain/chunk deletion and preserved incomplete warning.
- **SMTP:** real sender against disposable capture/failure transport, including E2E configuration (today's compose disables email). Test actual code delivery, no SMTP/read-only, delivery failure, expiry, wrong/reused/cancelled/replaced codes, attempt/resend limits and absence of reusable secrets from EmailLog/audit/diagnostics. Use digest-pinned test containers and clean only this run's resources.
- **SSO:** reuse the existing protected OidcCorrelation-cookie fixture starting after IdP assertion for the real callback, email-only matching, ambiguity refusal, attribution, generation/version fencing and local-proof availability. Do not claim IdP token validation; full IdP browser/protocol coverage stays Area 8.
- **Vitest:** every page/form loading/error/empty/status state, accessible dialogs/focus/Cancel/Escape, proof choices/expiry, capabilities/reasons, duplicate-submit handling, errors and sensitive-state disposal.
- **Playwright:** import the browser-health fixture; parity screenshots in both themes; real password/TOTP/recovery changes, UV virtual-authenticator registration/verification/removal, captured-SMTP email activation, two browser contexts and copied/revoked-cookie refusal, recovery Print/Done/refresh/back behavior, keyboard access, navigation and all shared links. Declare only exact deliberate browser errors; no blanket suppression.
- **Final gates:** locked restore, Release build with zero warnings, format verification, unit/integration/provider tests, guardrail tests, generated-type check, Angular lint/format/Vitest/build checks/production build, npm audit, E2E and E2E guard verification on the final commit. Never report unseen CI results as green.

## Delivery

One branch `area-3-profile` from main at c7b4b1b or later; one ticket at a time, one final PR. Ticket 1 commits CONTEXT, ADR0021–0026 and the confirmed records in `docs/planning/area-3-profile/`. Preserve uncommitted design records until then. Owner pushes/verifies CI/closes tickets; no agent push or issue closure. The last ticket prepares the PR body for the owner. Each ticket gets Standards/Spec review; the whole Area requires security-reviewer before merge.

| Order | Ticket | Blocked by |
| --- | --- | --- |
| 1 | #84 — Make email identity consistent across both databases | None |
| 2 | #85 — Commit Profile changes and audit events together | #84 |
| 3 | #86 — Invalidate stale sessions and proofs without timestamp arithmetic | #85 |
| 4 | #87 — Verify one prepared Profile change at a time | #86 |
| 5 | #88 — Confirm mailbox ownership before activating email | #87 |
| 6 | #89 — Serve Profile details, avatar and password through Angular | #88 |
| 7 | #90 — Serve authenticator, recovery-code and passkey flows through Angular | #89 |
| 8 | #91 — Retire Legacy Profile handlers and verify the complete Area | #90 |

Tickets are native sub-issues with native blocked-by edges, matching Area 2. All publish actions belong to planning; implementation starts only in ticket 1.

## Out of Scope

- Migrating Account sign-in/enrollment UI, adding fresh OIDC reauthentication or full external IdP browser/token-validation coverage (Area 8).
- A new administrator password-reset/recovery feature, SSO-session-only credential changes, in-place Profile TOTP replacement, or passkey rename.
- Broad PostgreSQL HTTP conversion (#66), unrelated authentication rewrites, redesigns, relaxed CSP or new runtime packages.
- Changing username case policy, provider-specific email dot/plus folding, silently repairing/merging identity collisions, or changing unrelated API/export schemas.
- Automatic detection of undisclosed Redis rollback; the accepted guarantee requires the documented coordinated recovery gate.
- Reading or touching docs/research/ or docs/internal/.

## Records

The confirmed glossary and ADR0021–0026 remain uncommitted in the root checkout for ticket 1. The final planning security/spec and readability review found no new findings; Claude quota failure required a disclosed fresh Codex fallback. Reviews concern source/design text, not executed runtime tests. Implementation must still pass its own required reviews and gates.

