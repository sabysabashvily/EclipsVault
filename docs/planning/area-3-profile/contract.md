# Area 3 Profile: confirmed contract and delivery outline

Planning only. Q1–Q31 are accepted. Q25–Q29 accept the contract, UI, compatibility, wording and
test-boundary recommendations below. Q30–Q31 accept the cookie-clearing fallback and coordinated
Redis recovery gate. Security policy remains in CONTEXT and ADR0021–0026.
The user confirmed the complete decision table and eight-ticket outline. Publish the specification
and tickets as the planning handoff. The first implementation ticket creates the branch and commits
the records; this planning task writes no implementation code and creates no branch or commit.

## HTTP behavior

Profile data and writes use session-only UI API endpoints, explicit Web request/response types,
generated TypeScript, no-store and antiforgery on every unsafe request. Identity comes from the
session. Each response maps Core results field by field. Operation identifiers are session-bound
correlation identifiers, not bearer credentials. Request data cannot supply another account.

Proposed route grouping (the exact request and response properties are pinned by HTTP tests):

```text
GET    /api/ui/profile                                      page snapshot and server-supplied action availability
PATCH  /api/ui/profile/details                              display name only
POST   /api/ui/profile/avatar                               validated multipart image
DELETE /api/ui/profile/avatar                               remove own image
POST   /api/ui/profile/operations                           prepare a typed intended security change
DELETE /api/ui/profile/operations/{operationId}             cancel an owned pending operation
POST   /api/ui/profile/operations/{operationId}/verify      password + TOTP or recovery-code proof
POST   /api/ui/profile/operations/{operationId}/passkey/begin
POST   /api/ui/profile/operations/{operationId}/passkey/complete
POST   /api/ui/profile/password                             commit the verified intended password change
POST   /api/ui/profile/authenticator/reset                  commit verified reset, sign out
POST   /api/ui/profile/recovery-codes                       commit verified replacement, return ten codes once
POST   /api/ui/profile/passkeys/registration/begin
POST   /api/ui/profile/passkeys/registration/complete
DELETE /api/ui/profile/passkeys/{passkeyId}                 commit verified removal of that owned key
POST   /api/ui/profile/email/request                        send pending mailbox code after account verification
POST   /api/ui/profile/email/resend                         deliberately rotate and resend a pending code
POST   /api/ui/profile/email/confirm                        atomically consume both proofs and activate address
DELETE /api/ui/profile/email/pending                        cancel the account's pending address
POST   /api/ui/profile/sign-out-everywhere
POST   /api/ui/passwords/check-breached                      shared by Profile and Legacy Create user
GET    /Profile/Avatar                                     shared binary compatibility route, not a Razor page
```

Prepare/verification inputs are explicit tagged Web types, not arbitrary JSON commands. The server
freezes normalized intended data and checks it again at commit. Passwords and current-factor inputs
are never persisted in plaintext; a prepared password may retain only its candidate hash/salt in
short-lived server state. No current password, TOTP, recovery plaintext, raw passkey response or
mailbox code enters a URL, application log, audit detail or recorded notification body. Unverified
preparation has an absolute expiry and finite active-operation bounds, with shared admission and
hashing-concurrency limits applied before expensive work. Cheap size/format validation comes first.

The page snapshot contains only what the page needs: display fields, access labels, enrolled status,
avatar URL, passkey summaries, recovery count, pending email status and allowed methods/actions with
stable refusal reasons. It excludes credential generations, revocation versions, password hashes,
TOTP seeds, recovery hashes, full passkey public keys and existing authorization material. Public
credential IDs occur only where the WebAuthn protocol requires them; page summaries use record IDs.

Problem outcomes: validation/malformed proof 400 with stable code and safe field errors;
unauthenticated or stale session 401; unknown/foreign operation or passkey 404 without enumeration;
stale operation, expired authorization, unavailable alternate method and email conflict 409;
throttled 429; failed audit transaction 503 audit_unavailable with no mutation; a known committed
change whose replacement session failed 503 changed_sign_in_required; explicit revocation failures
retain revoked_unrecorded/sign_out_incomplete. Proposed stable codes include validation_failed,
verification_failed, operation_not_found, passkey_not_found, operation_expired, operation_changed,
verification_required, verification_expired, method_unavailable, alternate_method_required,
authenticator_not_enrolled, email_conflict, email_code_invalid, email_code_expired, throttled,
mail_unavailable and the existing audit/session codes. Wrong proof stays on the page; 401 is reserved
for an unusable session so the global interceptor does not turn an ordinary wrong code into logout.
Successful own-avatar removal, cancellation and passkey deletion can use 204; security mutations
return the small typed outcome required to refresh /api/ui/me and the page. Recovery generation is
the only response carrying newly generated recovery plaintext, and is never replayed from storage.

## UI and compatibility

- Keep /Profile, /Profile/Index, /Profile/ChangePassword and /Profile/RecoveryCodes in any case.
  Keep titles, layout, icons, ordering and established text except named security differences.
- Details has separate display-name Save and email-change/confirmation controls so the name can
  save without activating an unconfirmed email. Preserve the card's existing layout and styling.
- Use one accessible account-verification dialog for the accepted proof methods, showing the
  intended action. Destructive actions first use the shared ConfirmationDialog; dialogs do not nest.
  Cancel/Escape restore focus and clear sensitive inputs. StatusLine reports outcomes; no toast.
- Disable duplicate submissions, freeze the intended change while proving it, and clear sensitive
  page state on cancellation, navigation, pagehide/back-forward restoration and session loss.
  Browser cancellation remains cancellation; it cannot silently switch to a weaker proof method.
- Refresh the shared CurrentAccount snapshot after retained-session changes so avatar/name and
  antiforgery state stay consistent. Device listings filter invalid original versions immediately.
- Keep /Profile/Avatar as a shared authenticated binary route for both shells and Legacy Users;
  move its handler out of the retired Profile controller and make its responses no-store. Preserve
  its intentional account-id image lookup and generated-image seed behavior. Retain all upload
  validation/limits. Update the shared breach checker in Legacy Create user to the UI API; retire
  the old unsafe Profile URLs instead of retaining write aliases.
- Port the advisory password meter/show-hide/breach feedback using CSP-safe Angular templates and
  finite CSS classes. Replace 'Change it periodically' with compromise-based advice. Keep the
  minimum length, maximum length, unchanged-password refusal and breached-password check.
- Show passkey creation dates explicitly in UTC, consistent with the migrated pages, rather than
  silently switching between server and browser local zones. Keep existing short date wording.

## Audit and failures

One semantic mutation event per operation, supplied through operation-aware interceptor rules;
multi-row resets/code replacement do not produce one row per code. Existing action names remain
where they describe the same event. Account verification and email-request/activation receive
distinct events. Sign-in history excludes Profile verification success; activity and intrusion
classification explicitly handle new security events. All proof consumption and failure-counter
writes are atomic with their intended event. Viewing, breach preview, rejected validation, a missing
or foreign resource and a true no-op do not fabricate successful mutation events.

Metadata/notification failure after a committed credential change is best-effort, not a rollback
or an invitation to repeat the write. A server-known commit with failed cookie renewal has a
distinct sign-in-required outcome; unknown response loss is reported as unknown. Mailbox delivery
errors cannot activate email. Confirmation and failure diagnostics contain no reusable secrets.

Accepted authentication-outage boundary (ADR0026): an exact-route terminal branch outside /api/ui,
before store-backed middleware, provides anonymous GET-token/antiforgery-protected POST local-cookie
clearing. It reads no account identity, revokes no server sessions and cannot enter protected routes.
It preserves the actual sign_out_incomplete result when account-wide revocation failed. Normal
protected reads/writes and account-wide revocation remain fully validated.

Accepted recovery policy (ADR0021): recovered Redis that may have lost revocation writes cannot
resume traffic until a coordinated incarnation rotation outside its restored keyspace invalidates
prior sessions across all nodes. Include pending session-bound proofs in the fence. Document and
test the mandatory recovery gate; undisclosed rollback is not automatically detectable.

## Verification and delivery proposal

- HTTP tests with the real Production host and real sign-ins: every route's authorization,
  antiforgery, no-store, response shape, overposting refusal, two-account scoping and exact outcomes.
  Use existing test-owned OIDC correlation-cookie technique for SSO callback/business-policy tests;
  full external IdP browser integration remains Area 8.
- SQL Server and PostgreSQL tests for normalized-identity backfill/collisions and data constraints;
  transactional proof consumption, replay races, lockout increments, generation fencing and
  overlapping innocuous/security changes. Existing HTTP suite stays SQL Server; focused real-PG
  tests cover new persistence semantics without silently taking over the separate PG HTTP task.
- AuditFailureFixture tests each Profile mutation, verification/ledger event and email activation:
  unchanged database/generation on rollback, no code/plaintext delivery, no later SaveChanges rescue;
  explicit sign-out still ends confirmed sessions and reports audit failure. For mutation/activation
  rollback, prepare and verify before enabling audit rejection so the request reaches the intended
  transaction. Reject verification separately and assert ledger/counter/authorization rollback.
- Real Redis tests for revocation versions, same-instant ordering, missing/expired state and
  concurrent sign-out; registry tests filter old observations and resist stale in-flight touches.
  Fence issuance against revocation between loading account attributes and issuing a session.
  Use an isolated real-Redis HTTP host for authentication/store-outage behavior.
  Restore a populated older snapshot and prove the coordinated incarnation change rejects its old
  sessions/proofs on every node. Exercise the anonymous local-cookie fallback with real store
  failure, invalid/missing/authenticated antiforgery tokens, wrong paths/methods and chunked cookies.
- Test-owned SMTP capture/failure transport over the real sender, shared with browser E2E as needed,
  with disposable messages and no production service replacement. Prove actual code delivery,
  expired/incorrect/replayed code, resend/cancel limits, SMTP-off/read-only behavior, and that no
  confirmation code reaches EmailLog/audit/diagnostics. Images/containers are digest-pinned and only
  the run's disposable resources are cleaned up. Exercise concurrent resend/confirmation/fifth-wrong-
  attempt outcomes and fail the real SMTP transport rather than replacing its production sender.
- Vitest: loading/error/retry, capability explanations, form and dialog accessibility, all outcomes,
  method selection/cancel, timers and sensitive-state disposal. Playwright under browser-health:
  visual parity, real password/TOTP/recovery actions, virtual UV authenticator add/remove/verification,
  mailbox confirmation through captured SMTP, two browser contexts and revoked-session refusal,
  one-time recovery display including refresh/back, both themes and keyboard use.
- Full required .NET/Angular/audit/format/type-generation/e2e/guard gates on final commit; security
  reviewer plus Standards/Spec reviews, one branch from main and one PR. The first ticket commits
  accepted glossary/ADRs. Push/PR-opening authority follows the owner's final instruction; never
  merge, tag or delete branches without the specific requested action.

Tentative ticket order: identity comparison/backfill; atomic Profile persistence/proof ledgers;
durable credential generation and session-store revocation versions; account-verification workflow;
email verification/delivery; Angular Profile/details/avatar/password; authenticator/recovery/passkey
flows; Legacy retirement and complete verification. Keep one branch; exact split/dependencies will
be reviewed after the remaining policies are accepted.

## Prepared-operation limits

Unverified operations expire ten minutes after preparation; verified grants expire five minutes
from verification. Each session holds one operation, and each account holds at most five live
operations across sessions. Preparing a replacement cancels that session's earlier operation and
passkey prompt. Admission is serialized by the database account lock on both providers.
Preparation and proof submissions share the configured sign-in IP budget (by default eleven
requests per 120 seconds). Candidate password hashing is limited to two simultaneous hashes per
node, with at most two seconds waiting for a slot. Password size/breach and action-field checks
precede hashing. Proof bodies are limited to 64 KiB; passkey prompts expire after 120 seconds.

Legacy passkey sign-in/registration callers retain their pages but share a server ceremony store:
absolute 120-second lifetime, one state per browser session and purpose, replacement cancellation,
and atomic take. Single-node memory uses one lock around cache access; multi-node Redis uses GETDEL
and keys under the current session-store incarnation. Reading a ceremony never extends its deadline.
