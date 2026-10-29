# Area 3 Profile grill checkpoint

Updated 2026-10-22. Root checkout: `/Users/sabasabashvili/Downloads/EclipseVault`.
Parent T3 thread: `e877e718-6881-4791-bde5-e2c932ae4461`.

## Guardrails

- Planning grill only. No implementation, branches, commits, pushes or issues until the user confirms the final decision table.
- Never read, move, commit or delete the checkout's `docs/research/`. Do not read or copy `docs/internal/`.
- `main` and fetched `origin/main` are `c7b4b1b`; tracked files were clean.
- Preserve Legacy appearance and behavior except named deliberate fixes. Keep Area 8 sign-in migration separate.
- Carry over session-only UI API, no-store, Problem Details, antiforgery, explicit Web response types and generated TypeScript, shared ConfirmationDialog/StatusLine, real failure tests, browser-health Playwright, one eventual branch and PR.
- Record accepted terms in CONTEXT.md and significant tradeoffs in ADRs as decisions settle; leave them uncommitted. Q1–Q31 and the complete decision table/delivery outline are confirmed. Publish the parent specification and eight tickets; implementation belongs to ticket 1.

## Recovered history and reading

Claude stopped during the initial survey before round 1; no answers or settled new decisions were found in the historical run.
Read AGENTS.md, CODING_STANDARDS.md, CONTEXT.md, all ADRs 0001–0021, docs/THREAT_MODEL.md, issue #74 and tickets #75–#81. Claude saved ticket bodies at `references/area-2-tickets.md`.
Read grilling skill and domain-modeling skill. Apply the latter when recording accepted terms/ADRs; CONTEXT is glossary only.

## Legacy inventory

- ProfileController (315 lines) owns Index, Update, avatar upload/removal/image, ChangePassword GET/POST, CheckPasswordBreached, ResetMfa, GenerateRecoveryCodes, RecoveryCodes, SignOutEverywhere, PasskeyRegisterBegin/Complete and RemovePasskey.
- Three views: Profile/Index, ChangePassword, RecoveryCodes. Index displays avatar; fixed username; editable display name/email; read-only clearance/project; password, TOTP reset, recovery count/generation, sign-out-everywhere; passkey add/list/remove. No passkey rename action.
- Password form: current/new/confirmation, length 12–256 in Web, advisory local strength, show/hide new password, 400 ms debounced local-corpus breach check. Server also rejects compromised and unchanged passwords.
- Avatar: file limit 4 MiB, request limit 6 MiB; JPEG/PNG detected from content, dimension bound 6000 per axis, reject incomplete decoding, crop/resize 256x256, re-encode PNG stripping metadata.
- Avatar GET intentionally accepts another account's id for the Legacy Users list; private cache 300 seconds. Self-service mutations always use the session account. Do not mistake the cross-account image read for an unscoped mutation.
- Recovery generation creates 10 independently salted Argon2id hashes, replaces old codes, returns plaintext once. Legacy uses TempData across redirect, displays code list and Print/Done. Direct visit or refresh returns to Profile without codes. Default cookie TempData is protected, not plaintext.
- TOTP enrollment belongs to AccountController's MFA-pending sign-in flow, not Profile. Reset clears TOTP and signs out the current cookie to send the person through that Legacy flow. Passkey sign-in is also Legacy Account and remains Area 8.

## Write/audit trace

- ProfileService.UpdateAsync lines 81–84: Users.UpdateAsync saves details, then ProfileUpdated standalone audit.
- ChangePasswordAsync lines 117–130: saves password hash/salt, raises account-wide external revocation marker, audits PasswordChanged, sends fail-soft password-change notification. Wrong current password is audited as LoginFailed but does not increment lockout.
- SetAvatarAsync/RemoveAvatarAsync lines 141–152: repository saves avatar plus user marker, then AvatarUpdated/AvatarRemoved standalone audit.
- ResetOwnMfaAsync lines 160–165: saves cleared secret/enabled state, separately deletes recovery codes, then SelfMfaReset standalone audit. Does not reset LastTotpStep or revoke other sessions. Controller signs out only its own cookie after success.
- MfaRecoveryService lines 64–73: repository saves replacement set, then RecoveryCodesGenerated standalone audit; no TotpEnabled prerequisite.
- PasskeyService lines 136–138 and 266–271: saves added/deleted credential, then PasskeyRegistered/PasskeyRemoved standalone audit. Removal scopes by account id plus credential id; absent/foreign returns false without audit.
- SignOutEverywhere lines 242–245: raises account kill switch, ILogger only, signs out current cookie. No security audit row today.
- Passkey begin changes server ceremony state; breach check is read-only in memory, no credential persistence.
- AuditSaveChangesInterceptor lines 130–140 and 183–220 covers Secret, TrustedNetwork, DynamicSecretLease, SecretGrant only. None of the above Profile database writes are rescued by an interceptor rule.
- Repositories call SaveChangesAsync internally. Reset's two saves are separate transactions. Recovery replacement is one save but its audit is separate.

## Demonstrated security concerns

1. Audit rejection after any of these Profile database writes leaves the committed mutation unaudited. A rejected recovery generation invalidates the old set and leaves the person without the undisplayed replacement.
2. Existing session alone can reset TOTP, mint replacement recovery codes, add a new passkey, remove passkeys, and change email. A stolen session can install an attacker-controlled passkey and later sign in independently. Password change alone requires the current password.
3. Authenticator reset does not invalidate other sessions; its split saves permit partial reset when deletion fails.
4. Password controller lines 182–183 sets AuthTime to now+1 second to survive its own kill switch; lines 302–312 preserve the session id. A subsequent sign-out-everywhere before that future instant can leave a captured renewed cookie valid. It also refreshes the strong-auth clock after a password-only check.
5. Passkey registration stores only the challenge in a shared browser ceremony session. No account/session/purpose/issued-at binding record; client timeout is 120 seconds but server session expires on six minutes of inactivity, extended by other session use. Sign-in/sign-out do not clear registration state. Sequential completion removes challenge, but Session Get/Remove is not an atomic distributed consume.
6. UserRepository.UpdateAsync line 47, SetAvatar line 79 and RemoveAvatar line 92 use Users.Update on an already tracked entity, marking unrelated columns modified; UserConfiguration has no concurrency token. An overlapping stale profile/avatar write can restore old password, TOTP, role, or enabled values committed by another request. This is a static code scenario, not a runtime reproduction.
7. Profile has no AuthThrottleFilter; Account has it. Wrong current-password attempts bypass the account lockout path. Add throttling to any newly introduced credential proof endpoints.
8. Email is a unique login alias and SSO resolves verified IdP email against it (SsoSignInService line 46), so email changes are security-sensitive, not just contact edits. Duplicate email currently reaches a unique constraint without a domain conflict mapping.

Do not overclaim: a code audit is not a tested exploit; runtime integration and concurrency tests are for implementation. Ordinary lost responses remain possible even after atomicity is fixed.

## Links and coexistence dependencies

- Profile entry: Legacy layout account menu, Angular account menu, command palettes derived from those links.
- Angular My activity, Sign-in history, Signed-in devices, and Security checkup remediation links point at /Profile. Security checkup Profile and BackupCodes both map there.
- Legacy layout and /api/ui/me build avatar URLs with Profile/Avatar; Legacy Admin/Users uses Profile/Avatar with another account id and display-name seed.
- Legacy Admin/CreateUser calls Profile/CheckPasswordBreached; it must still work after Profile controller retirement, with a deliberate shared endpoint handoff.
- Internal views link Profile <-> ChangePassword, recovery Done -> Profile. Reset/sign-out -> Account/Login. Other navigation comes from the shared shell.
- Preserve /Profile and /Profile/Index, /Profile/ChangePassword, /Profile/RecoveryCodes in any letter case. Old unsafe MVC actions must eventually be retired; image/helper endpoints need explicit placement rather than accidental survival.

## Testing inventory

ProfileServiceTests currently has only three password cases with stub repositories/audit. Existing TotpReplayTests, RecoveryCodeFormatTests, WebAuthnVerifierTests and integration PasskeyAuthenticator provide prior art. AuditFailureFixture already proves real database audit rejection and rollback for Area 2. Add meaningful HTTP failure/concurrency and browser tests when implementing; no tests/builds run during this survey.

## Reviewer tracking

- Required Claude reviewer task failed from Claude API quota:
  `node:delegated-task:command%3Amcp%3A9b2a45ff-5a72-4f0b-b543-e9c23fbc3666%3Adelegate-task%3Aprofile-area3-security-survey-r1`
- Fresh Codex security-reviewer fallback (CODING_STANDARDS permits same model in fresh session if another is unavailable):
  `node:delegated-task:command%3Amcp%3A9b2a45ff-5a72-4f0b-b543-e9c23fbc3666%3Adelegate-task%3Aprofile-area3-security-survey-r1-codex-fallback`
  Target confirmed codex / gpt-6.1-sol / reasoningEffort xhigh / serviceTier default. Completed; full findings saved at `reviews/security-survey.md`. Later review rounds require new delegate_task calls with prior findings/responses.

The completed reviewer corroborated the primary survey and added explicit recovery concurrency findings: two first-generation requests can each see no rows and insert separate ten-code sets; two redemptions can read one unused code and both unconditionally mark it used and sign in. Replacement serialization and atomic single-use redemption must be covered when this shared backend is used by Profile proofs. Password persistence also precedes external session revocation, so a revocation-store failure can leave a changed password and unrevoked old sessions.

## External primary guidance

- OWASP MFA: https://cheatsheetseries.owasp.org/cheatsheets/Multifactor_Authentication_Cheat_Sheet.html — reauthenticate with an existing factor before factor changes, do not rely on active session; notify out of band.
- OWASP passkeys: https://cheatsheetseries.owasp.org/cheatsheets/Passkey_Security_Cheat_Sheet.html — protect lifecycle; bind ceremony to intended account, recent authentication, server-side expiry and single use.

## Accepted round 1

The user explicitly accepted all five recommendations. Recorded in CONTEXT.md (Account verification, Authenticator reset) and docs/adr/0022-profile-security-changes-require-account-verification.md, uncommitted on main. No implementation files changed.

1. Sensitive account actions require fresh proof from existing credentials, distinct from secret-reveal step-up. Include email, password, authenticator, recovery-code and passkey changes; exclude display-name/avatar and sign-out-everywhere. Recommend yes; methods, freshness and replay policy follow in the next round.
2. Authenticator UX: preserve reset -> sign out -> existing Legacy enrollment rather than building in-place replacement in Profile. Require fresh proof, atomic retirement of old TOTP and recovery codes, invalidate all existing sessions and relevant pending ceremonies; if every existing proof is lost, use administrator recovery. Recommend this narrow parity-preserving flow.
3. Password success: keep the proven caller signed in, revoke prior sessions, give that browser a truly fresh session; remove future-dated AuthTime. Recommend preserving that UX with real rotation. Detailed failure/revocation design follows.
4. Recovery delivery: retain 10 codes, one-time display, Print and Done; keep plaintext only in current page memory, clear on leaving, no cookie/storage/URL persistence and no automatic mutation retry. Refresh/direct visit returns to Profile; lost response requires explicit regeneration. Recommend yes.
5. Shared backend boundary: fix the shared persistence, single-use and session mechanisms Profile relies on, including regression coverage through remaining Legacy callers. Keep Account sign-in/enrollment pages in Area 8. Do not isolate fixes only in the new UI API or perform a general sign-in rewrite. Recommend this boundary.

## Later frontier to recompute after answers

- Fresh proof methods for password/TOTP/recovery/passkey/SSO-only accounts, age and scope, whether proof is per action, replay/atomic consume, throttling/lockout, semantics distinct from secret step-up.
- Reset vs verified replacement, recovery when old factor is lost, all-session invalidation and durable identity-change generation, recovery-code and LastTotpStep behavior, pending sign-in/ceremony invalidation.
- Exact audit writer/rules per entity and multi-entity transaction, one semantic row per action, no credentials in audit, ADR-0021 mixed outcomes and current cookie clearing even when audit fails.
- Email ownership and SSO binding, whether verification infrastructure is in scope, duplicate-email conflict without enumeration.
- Registration binding/lifetime/concurrency, no silent custom verifier scope expansion; review malformed inputs and provenance checks.
- Avatar endpoint location/cache/cross-account image access, legacy consumer compatibility.
- Response/route contracts, strength meter CSP implementation, cancellation and lost responses, UI errors/loading/focus, browser/date formatting and truthful security wording.
- Exact tests, both database migration sets if model changes, SQL/Redis failure cases, ticket split and dependencies, final review gates and document recording.

## Round 2 design check

New required security-reviewer design/fact check, fresh Codex fallback after prior Claude quota failure:
`node:delegated-task:command%3Amcp%3A9b2a45ff-5a72-4f0b-b543-e9c23fbc3666%3Adelegate-task%3Aprofile-area3-security-design-round2`
Target confirmed codex / gpt-6.1-sol / xhigh / default. Completed; full result saved at `reviews/security-design-round-2.md`. The prompt includes the original brief, prior findings, accepted Q1–Q5, and all unresolved design hazards.

Round 2 review corroborates proposed questions; Q6–Q12 still await user answers. Additional concrete findings: concurrent TOTP verification can accept the same time step twice; concurrent failure increments can lose counts. A credential generation alone does not solve replay-ledger or lockout races. Also, existing passkey completion resolves the credential's owner rather than accepting an expected session account, so account verification must enforce account ownership server-side; allowCredentials is insufficient. Existing credential verification methods emit sign-in audit events and must not be reused unchanged as Profile verification. These are requirements within the already accepted narrow shared-backend repair scope; exact mechanisms remain open.

Later decisions/tests must cover original-generation propagation through pending sign-in and issuance; discoverable login challenges have no known account at begin; generation-less cookies/ceremonies and older application nodes at rollout; invalidated session metadata disappearing from Signed-in devices; email confirmation versus account-proof expiry; and atomic TOTP/recovery consumption and lockout increments. Existing admin recovery resets only TOTP, not passwords or email. No new decisions were accepted by receiving the review result.

## Accepted round 2

The user explicitly accepted Q6–Q12. Recorded in expanded ADR0022, new ADR0023 (durable credential generations), new ADR0024 (email ownership), and the Credential generation glossary term. All records remain uncommitted on main. Preserve the current-password requirement for password changes in addition to the selected full-MFA method, as stated in the original Q6 recommendation and Legacy behavior.

- Q6: Allow fresh password + TOTP, password + an unused recovery code, or an already registered passkey with UV for account verification. Keep current-password proof additionally mandatory for a password change. Reject TOTP/recovery replay across sign-in, secret step-up and Profile. Recommendation: this full-MFA matrix.
- Q7: No reusable five-minute unlock of the whole Profile. One server-held authorization for one named operation and its target, bound to account, session and credential generation, expires five minutes after verification and is consumed once. The same proof cannot authorize changed operation data. Recommendation: one operation, not a general grace period.
- Q8: Add a durable per-account credential generation to the database; carry it consistently through sessions, MFA-pending proofs and management ceremonies; increment it atomically with credential changes and their audit events. Compare the generation through verification and session issuance, never stamp an old proof with a newly reread generation. Missing-generation cookies require sign-in once at deployment. Keep external sign-out semantics from ADR-0021 separate. Recommendation: durable generation, not future timestamps or cache-only credential invalidation.
- Q9: On successful password change, active email change, recovery-code regeneration, and passkey addition/removal, revoke prior sessions and keep only a genuinely fresh current session. Authenticator reset signs out all including current. Name/avatar edits and merely beginning a change do not end sessions. Recommendation: invalidate other sessions after each completed sensitive credential change. Opening an email-change request alone does not activate email or trigger this invalidation.
- Q10: Activate a new email only after fresh account proof and new-address ownership confirmation; notify old address; retain existing address while pending or mail fails. If real mail delivery is not configured, expose email as read-only with an explanation. Confirmation secrets must not enter logs, audit details, or stored notification bodies (NotificationService currently persists Body). Recommendation: verified replacement, consistent with email's login/SSO role.
- Q11: Apply the existing IP auth budget and shared per-account credential-failure lockout to all actual account-verification attempts, including password guesses and failed existing-passkey proofs. Defaults 11 attempts per 120-second IP window, five account failures then 15-minute lockout; use configured values. Browser cancellation is not a wrong credential. Sign-out remains unmetered and no account lockout is bypassed by Profile. Recommendation: reuse existing policy, no new unlimited guessing path.
- Q12: For an SSO session with no usable local proof, require a separately designed fresh IdP proof or recovery path rather than treating the active session as proof. Recommendation for this Area: do not add a new OIDC reauthentication protocol; block sensitive changes for such an account and explain the restriction, while leaving ordinary use available. Existing local credentials/passkeys work regardless of original sign-in method. This restriction is a named parity exception. No administrator password-reset feature exists today (only TOTP reset), so do not claim that an existing admin button solves every no-proof case. Revisit the exact recovery/bootstrap branch explicitly in the next frontier.

Additional observed facts: MFA-pending cookies presently carry account id/username only and expire after six minutes; OIDC login has no forced-freshness or original-account-bound reauthentication flow. Email Sender defaults to Log, can be suppressed, and ordinary email history stores message Body. These matter to secure email verification and SSO-only account handling.

## Completed round 3 design check

Task `node:delegated-task:command%3Amcp%3A9b2a45ff-5a72-4f0b-b543-e9c23fbc3666%3Adelegate-task%3Aprofile-area3-security-design-round3` completed as fresh Codex fallback xhigh/default. Saved full result at `reviews/security-design-round-3.md`.

Facts: reset retains passkeys; passkey/trusted-IdP MFA sign-in bypasses local TOTP enrollment, while password and untrusted-IdP MFA sign-in enroll when TOTP is absent. Wording must be 'when sign-in requires local TOTP,' not password-only. No TOTP prerequisite for recovery generation or last-passkey safety guard exists today. SmtpEmailSender itself does not persist its body; NotificationService persists both full body and raw delivery error, and IEmailSender bypasses enabled configuration unless a new narrow seam enforces it. Email comparison/index casing is provider-dependent and duplicate conflicts become 500; further namespace check is running. Generic User->ProfileUpdated interception would falsely audit replay/lockout/admin writes and duplicate existing semantic events. Anonymous discoverable login cannot capture per-account generation at begin; capture and fence it when the credential/account becomes known before verification. This does not invalidate every anonymous challenge predating a credential change if its key remains registered; that boundary must be explicit. Device metadata must carry/filter original generations to prevent stale in-flight touches resurrecting invalid listings. Cookie/metadata delivery failure after a committed mutation never rolls back credentials or retries the mutation.

Additional primary-agent finding: old account-wide revocation markers use Unix seconds, so removing future-dated AuthTime alone can make a genuinely fresh session fail against an earlier revocation in the same second. Proposed external session-store revocation versions need a user decision and a subsequent design review. Missing/reset state must not be interpreted as a version that revives old cookies.

## Accepted round 3

The user explicitly accepted Q13–Q24. Recommendations are now recorded in ADR0021–0025 and CONTEXT.md, including Session revocation version and the corrected reset definition. The response to Q13 said 'single-use mailbox valid for 15 minutes'; interpret the obvious omitted word as mailbox code, consistent with the question. All implementation still prohibited until full final confirmation.

- Q13 email confirmation: one pending target per account; 15-minute single-use mailbox code entered in original signed-in Profile; original five-minute account proof must still be valid at activation or be repeated for that exact target. Requesting mail does not consume the authorization needed at final activation. Replacement/resend cancels previous code; cancellation supported; no state-changing email links or code in URLs.
- Q14 mailbox limits: five incorrect attempts invalidate that pending code; first send/resends limited to one per minute and six per hour per account, plus source-IP protection, shared across nodes. Mailbox typos do not lock ordinary vault sign-in. SMTP failure leaves active email unchanged; deliberate resend rotates code.
- Q15 passkey ceremonies: retain 120-second advertised timeout as absolute server deadline, random challenges, account/session/purpose/generation binding for known-account operations, atomic consumption, one active attempt per session and purpose; a new attempt cancels the previous same-purpose attempt. Cancelling the browser prompt is not a bad credential.
- Q16 final passkey: permit removal only after proving another usable local method. The key being deleted cannot prove its own fallback. A stored hash or SSO session does not count as proof of another usable method. Refusal is explicit on server; exact recovery-code-last-use edge remains to be covered.
- Q17 reset boundary: retire only TOTP and its recovery codes (including its replay marker), retain unrelated passkeys/SSO, invalidate all sessions. Local-TOTP-required sign-in reenrolls; passkey/trusted-IdP MFA still signs in. No reset of an unenrolled authenticator or code generation before TOTP enrollment; server supplies availability and reasons.
- Q18 committed mutation vs response: do not undo committed credentials or lower generation, and do not automatically retry writes. A known commit with failed current-session renewal returns a distinct changed/sign-in-required outcome. A lost/ambiguous reply is reported as unknown, followed by a read/sign-in check before deliberate retry. Lost recovery plaintext is not retained and requires explicit regeneration. Metadata failure is best-effort, not credential rollback.
- Q19 explicit account-wide sign-out: use a store-issued revocation version rather than a wall-clock cutoff. New sessions carry the current version; sign-out changes it atomically. Preserve separate durable credential generation and per-device tombstones; missing state must not revive a previous version. In-memory/Redis parity, same-instant/concurrency tests, and preserve original versions on mere cookie renewal.
- Q20 sign-out reporting: always clear the current browser cookie even when revocation/audit fails. Revoke-first/audit-second, confirmed unrecorded ->503 revoked_unrecorded, uncertain/incomplete ->503 sign_out_incomplete, no false all-device success. Explain outcome before leaving for Legacy sign-in, preserving warning in fixed nonsecret flash notice as needed.
- Q21 notifications: notify the trusted existing address after each successful sensitive change, using captured old address for email change. Delivery is best-effort and failures recorded without secrets; it cannot undo/block committed credential changes. Email ownership delivery is different: without the actual code no activation occurs.
- Q22 discoverable passkey login: preserve username-less login. Its anonymous challenge is not an account proof; bind account generation when identifying the credential before verifying, then fence completion against changes. A prior anonymous challenge may authenticate an unchanged still-registered key within its short lifetime; it cannot rescue removed credentials or stale named-account proofs. This is an explicit boundary on Q8, not generation stamping after verification.

Derived correctness requirements, not optional policy: distinct account-verification audit events must not report LoginSucceeded; one intended mutation event per operation (not ten for replacement codes), no false ProfileUpdated for replay/lockout markers, and no secret material in event details. All verification requirements for a password change, including the extra current-password check when a passkey is used, must pass before resetting failure counts; otherwise repeated valid-passkey + wrong-password requests could keep clearing the guessing counter.

## Email identity fact check pending

Task `node:delegated-task:command%3Amcp%3A9b2a45ff-5a72-4f0b-b543-e9c23fbc3666%3Adelegate-task%3Aprofile-area3-email-identity-fact-check` completed, fresh Codex xhigh/default. Saved full result at `reviews/email-identity.md`. Original brief, Q1–Q12 and prior review paths supplied. No Q13+ decisions are accepted yet.

Confirmed: Web username validation forbids @ and actual admin POST checks model state, so a normal user/admin UI cannot create an email-shaped username. Core creation and separate DB unique indexes permit it via custom/privileged provisioning. If A.Username == B.Email, unordered combined lookup can choose A for B's verified SSO address; trusted/asserted IdP MFA then admits the wrong account. This is conditional corrupted/custom provisioning, not an ordinary-user exploit. Password login still needs the selected account's password. Combined lookup also serves passkey begin and sharing. Email-only SSO lookup is a narrow separate repair from normalization. PG uses varchar and ordinary deterministic collation; SQLServer uses inherited collation, which was not inspected live.

Additional pending round 3 questions:
- Q23: treat trimmed email case-insensitively (since limited to the letters A to Z; see ADR-0025) as a vault identity across providers, preserving entered spelling for display/delivery. One application normalization plus matching database uniqueness, all accounts including disabled included; no provider-specific dot/plus alias rules. Both-provider migration preflight refuses canonical collisions with explicit operator resolution, no auto merge or selected owner. Recheck uniqueness at activation and map races to a generic conflict.
- Q24: SSO looks up by email only. Combined username/email callers must reject ambiguity rather than choose first; Core enforces the same username rules as Web to keep future namespaces disjoint. Do not change username case policy or rewrite OIDC. Preexisting cross-account alias collisions require explicit resolution, not silent reassignment.

Final round 3 numbering is Q13–Q24, all now accepted. The child reviews listed above are completed. Future work: review complete design and external session-store version design, settle HTTP contracts and UI/shared resource compatibility, mail/runtime/concurrency/failure-path test plan and both migrations, then ticket split/whole-design review, final complete decision table and wait for literal confirmed. No implementation before confirmation.

## Whole-design review and round 4 frontier

Accepted Q13–24 are captured in ADR0021–0025, CONTEXT and this checkpoint. Contract/UI/testing
proposal saved at `contract.md`.

Whole-design review task running:
`node:delegated-task:command%3Amcp%3A4d2ab6c8-fa66-428d-aa3b-fe92c0ef4d0c%3Adelegate-task%3Aprofile-area3-whole-design-security-review`
Target confirmed codex/gpt-6.1-sol/xhigh/default. Completed; result saved in
`reviews/whole-design-security.md`. Prompt includes
original brief, all prior finding paths, accepted Q1–24, and unresolved external-version/cookie-
outage/contract/test issues. No child continuation through t3_thread_send.

New concrete outage boundary under review: the full-cookie validator queries account/session stores
before Account.Logout or Profile sign-out controller code. Its exception can prevent local-cookie
clearing even though Logout itself catches revocation-store failures. No general authentication
bypass is authorized. Any special pure-local-cookie clearing path needs explicit owner decision,
antiforgery, zero account authority and a security review; its question waits on review findings.

Independent pending round 4 questions:
- Q25 typed UI API contract in the proposal: server-bound prepared intended operation, common
  verification, action-specific mutation endpoints/outcomes, safe response allowlists and stable
  Problem Details; operation ids are not bearer authorizations; 401 only for unusable session.
- Q26 separate display-name Save from email-change/confirmation controls in existing Details card,
  one reusable accessible verification dialog, shared destructive confirmation first without nested
  dialogs, inline StatusLine, duplicate-submit prevention and sensitive-state disposal.
- Q27 shared authenticated /Profile/Avatar binary compatibility route retained outside retired
  Profile controller, no-store, intended cross-account image lookup retained, existing upload guards;
  shared breach preview moves to /api/ui/passwords/check-breached and Legacy Create user updates;
  old unsafe Profile routes retire; all Profile page URLs retain case-insensitive Angular ownership.
- Q28 deliberate copy/date differences: replace periodic-password-change advice with compromise-based
  advice, show passkey creation dates explicitly in UTC; preserve other text/design except accepted
  security workflow changes.
- Q29 SSO test boundary: reuse existing after-IdP-assertion OIDC correlation-cookie fixture for real
  callback/account-matching/attribution/proof-policy tests; full external IdP browser sign-in remains
  Area8. Existing test seam is explicitly not IdP token validation. All new SMTP/browser/failure/race/
  migration tests in proposal remain required by 'test everything we can'. No service replacement
  for audit/credential checks and no production test-only routes.

None of Q25+ is accepted yet. Updated docs pass git diff --check. No runtime tests were run because
this remains a documentation-only grill. Main remains c7b4b1b; docs/research remains untouched.

## Whole-design review result and current-round supplement

Two new owner decisions are ready; Q25–Q29 still await answers.

- Q30: approve an exact-route anonymous local-cookie-clearing GET/POST outside /api/ui, terminal
  before blacklist/session/authentication store access. GET issues a fresh anonymous antiforgery
  token with no-store; POST validates it anonymously and deletes the configured auth cookie with
  matching cookie manager/domain/path/chunks. It reads no account identity, revokes no server
  sessions, returns no protected data, never continues into protected routes and claims only local
  clearing; failed account-wide outcome remains sign_out_incomplete. Deliberately pin anonymous
  authorization coverage and wrong path/method/CSRF tests. Existing authenticated antiforgery tokens
  cannot be used here because their identity binding would fail anonymous validation. This is a
  narrow exception to keep cookie clearing available during store outages, not full-session proof.
- Q31: require coordinated session-store incarnation rotation outside the restored Redis keyspace
  before recovering Redis resumes vault traffic. Invalidate all existing sessions across all nodes;
  implement and test a durable database/deployment incarnation and document the mandatory recovery
  gate. Missing-state random initialization alone does not prevent a populated old Redis snapshot
  restoring version A after A was revoked to B. The guarantee relies on the coordinated recovery
  step, not automatic detection of arbitrary undisclosed snapshot rollback. Exact coordinator is an
  implementation choice subject to review; do not promise undetectable rollback detection.

Correctness constraints needing no new policy approval: capture external revocation version before
loading account attributes/credentials; carry original version through proof and session issuance;
reject a change rather than attach a newer version to stale role claims. Apply every existing
account-wide caller (role/disable/force logout/delete/intrusion). Preserve original versions on mere
renewal. Non-repeating atomic initialization and fail-closed expired/evicted state never initialize
from a cookie. Bound and throttle unverified preparation before 64-MiB password hashes/state
allocation; cap active operations and hashing concurrency with cheap input checks first. Recheck
last-key surviving recovery eligibility inside deletion transaction serialized with redemption.
Proof-through-recovery uses distinct verification events rather than sign-in-shaped RecoveryCodeUsed.

Failure tests must prepare and verify before enabling RejectAuditWritesAsync for mutation rollback;
test verification rollback separately. Include resend/confirmation/fifth-failure races. Add isolated
real-Redis HTTP host to test authentication outage, and real SMTP capture/failure in E2E (current
compose disables mail), without replacing production credential/audit services. Source-based review,
not executed tests. All child tasks completed; no live delegation remains.

## Accepted Q25–Q29

The user explicitly answered Q25–Q29. The contract proposal now records these as accepted design;
implementation remains prohibited during the grill. ADR0022 captures server-prepared operations,
bounded preparation and the last-passkey recovery race. ADR0021 captures original revocation-version
propagation and initialization rules. Q30–Q31 still need owner decisions.

- Q25: server-prepared operations freeze intended data, use shared account verification and expose
  typed endpoints for each final action. Identifiers alone confer no authority.
- Q26: preserve the existing cards, separating display-name Save from email-change controls.
  The shared accessible verification/confirmation/status behavior remains as recommended.
- Q27: retain /Profile/Avatar as the shared, session-only binary compatibility route with no-store.
  Preserve intentional cross-account avatar consumers and upload protections. The proposed shared
  breach-preview move and retirement of unsafe Legacy Profile handlers remain in the delivery plan.
- Q28: replace periodic-change password advice with compromise-based advice; show passkey creation
  dates explicitly in UTC. Other parity requirements remain.
- Q29: reuse the existing test fixture starting after IdP assertion for real callback and account-
  policy coverage; do not claim full IdP protocol coverage. Full external IdP browser flow stays Area8.

The whole-design reviewer findings are source deductions, not executed tests. Mutation audit-failure
tests must finish preparation and verification before enabling audit rejection, with separate tests
for verification rollback. The plan now names isolated real-Redis HTTP outage coverage and actual
SMTP transport failure, plus resend/confirmation/fifth-incorrect-attempt races.

Next owner frontier: Q30 narrow anonymous local-cookie clearing; Q31 coordinated incarnation rotation
outside restored Redis state before serving recovery traffic. After answers, resolve delivery split
and any genuinely dependent frontier, then present every decision and wait for literal confirmed.

## Accepted Q30–Q31

The user explicitly accepted both recommendations. Q30 is recorded in new ADR0026 and referenced
from ADR0021. Q31 is recorded in ADR0021 and the Session-store incarnation glossary term. The
contract proposal is updated; all documentation remains uncommitted on main at c7b4b1b.

- Q30: exact-route anonymous GET-token/antiforgery-protected POST local-cookie clearing outside the
  UI API and before store-backed middleware; no account identity, protected data, server revocation
  or continuation into protected endpoints. Preserve sign_out_incomplete after a failed account-wide
  attempt. Pin allowlisting, paths/methods, token binding and cookie-manager/chunk behavior in tests.
- Q31: before accepting traffic after Redis recovery that may have lost revocation writes, rotate a
  session-store incarnation outside the restored keyspace across all nodes. Invalidate old sessions
  and prevent pending session-bound proofs from bridging the rotation. Gate traffic until all nodes
  enforce it, with documented and tested recovery. No automatic undisclosed-rollback guarantee.

No additional policy decisions are currently known. Prepare the complete decision table and a
concrete delivery outline for final confirmation after checking documentation closure/readability.

## Grill ready for final confirmation

The focused final security/spec and readability review found no new findings. Claude Opus high
failed again with API rate limit; the completed fallback was a fresh Codex gpt-6.1-sol xhigh/default
session under CODING_STANDARDS. Report saved at
`reviews/final-document-review.md`.

Failed task:
`node:delegated-task:command%3Amcp%3A4d2ab6c8-fa66-428d-aa3b-fe92c0ef4d0c%3Adelegate-task%3Aprofile-area3-final-doc-closure-review-20261022`
Completed fallback task:
`node:delegated-task:command%3Amcp%3A4d2ab6c8-fa66-428d-aa3b-fe92c0ef4d0c%3Adelegate-task%3Aprofile-area3-final-doc-closure-review-codex-20261022`

All 31 accepted decisions, carried-forward requirements and the proposed eight-ticket delivery
outline are saved in `decisions.md` and its HTML
sibling. HTML was previewed at 728px/dark and 360px/light with no horizontal overflow or console
errors. The final table must be rendered before the final reply. No policy questions remain.

Wait for the user's literal confirmed. Do not implement, create a branch, commit, push or open issues
during this grill. No runtime builds/tests were run; all review findings concern source/design text.

## Final confirmation received

The user replied literal confirmed after the complete decision table. The grill is complete.
Prepare, review and publish the parent specification plus eight linked implementation tickets in
GitHub, using #74 and #75–#81 as the format. Native sub-issue and blocked-by links should match that
model. No implementation, branch, commit, push or PR belongs to this planning handoff. The first
ticket owns branch creation and commits the accepted glossary/ADRs. Record every published issue URL
here and in a durable manifest to avoid duplicate creation after a restart.


## Artifact location corrected; publication paused

The owner questioned why the Area 3 decision record was outside the project.
Moved the decision record, contract, review reports, checkpoint, reference ticket dump and issue
drafts into docs/planning/area-3-profile/. All remain uncommitted. Relative links replace the
previous sibling-Downloads artifact paths. GitHub publication is paused; no issues were created.
The publication-draft review was cancelled at the owner's request. Do not resume publication without
further direction. The first implementation ticket must include the confirmed planning records
when committing the glossary/ADRs.

## Ticket publication resumed

The user explicitly requested resume ticket creation. Review the relocated drafts, then publish
the parent specification and eight issues through gh with saved body files, as required by
docs/agents/issue-tracker.md. Keep the records inside the project, save issue URLs in the manifest
after each creation, add native parent/dependency links and verify all published bodies. Do not
start implementation, create a branch, commit, push or open a PR in this planning handoff.

Publication coverage review task:
`node:delegated-task:command%3Amcp%3A4d2ab6c8-fa66-428d-aa3b-fe92c0ef4d0c%3Adelegate-task%3Aprofile-area3-publication-review-after-relocation-20261022`

Fresh Codex gpt-6.1-sol xhigh/default fallback after repeated Claude quota failure. The review is
read-only and checks draft coverage, dependency sequencing and substantive readability. Publication
waits for its result. GitHub native sub-issue/dependency reads and ready-for-agent label were verified.

Publication review completed with one P2 omission: the public spec/ticket4 needed the accepted
proof-input prohibition in URLs/logs/audit/recorded notification bodies and disclosure checks. Both
were corrected. No other substantive security/spec/dependency/readability findings. Report:
reviews/publication-review.md. Publication can proceed through gh from the reviewed body files.

Published 2026-10-22: spec #83, tickets #84-#91. Sub-issues of #83 and the blocked-by chain
#84 <- #85 <- ... <- #91 were added through GraphQL and read back. Nothing pushed, no branch created.

## Ticket #84 implementation (started 2026-10-22)

Branch `area-3-profile` from main c7b4b1b in the root checkout. Design: `EmailIdentity.Normalize`
(.NET Trim + ASCII-only lower-casing, reproducible in both migrations) stored in
`User.NormalizedEmail` (set by the Email setter, ordinal collation, unique index replaces IX_Users_Email).
Migration preflight refuses canonical and username/email cross-account collisions with account ids.
SSO uses `FindByEmailAsync`; combined lookup returns null for two accounts; Profile update refuses a
taken address with `EmailAddressTakenException`, also mapped from the unique-index race in UserRepository.
Core username rules shared with Web (`UsernameRules`). Tests: real-provider upgrade + persistence
fixtures (both providers) and HTTP SSO/password-login tests on VaultFixture.
Status 2026-10-22: implementation staged (explicit paths only; docs/research untouched). Unit + new
integration tests pass. Codex reviews running: clientRequestIds 84-standards-review-r1 and
84-spec-review-r1. Full-suite output goes to ~/Downloads/ev-84-test-run.txt. Not committed yet.

## Ticket #84 resumed after provider limits

2026-10-22: Codex resumed the Claude-authored staged implementation in the same root checkout and
branch. Earlier review attempts all failed on provider usage limits and produced no findings.
The earlier full-suite logs contain 654 passing unit tests and 240 passing integration tests.
Fresh locked restore, Release build (0 warnings, 0 errors), format verification, both provider
pending-model-change checks and 654 unit tests pass. Guardrail tests: 6 pass. A fresh full integration
run is active; logs are `~/Downloads/ev-84-*-resumed.txt`, with the two model checks in
`~/Downloads/ev-84-sqlserver-model-check.txt` and `~/Downloads/ev-84-postgres-model-check.txt`.

Standards review: GPT Sol 6.1 xhigh/default, clientRequestId `84-standards-review-resume-r2`.
Requested Opus 5.5 high Spec review failed again on the Claude API rate limit. Fresh GPT Sol 6.1
xhigh/default Spec fallback: clientRequestId `84-spec-review-resume-r2-sol-fallback`.
Task-id prefix for both: `node:delegated-task:command%3Amcp%3Ac231d0db-8f38-422d-8d04-8b148532e42d%3Adelegate-task%3A`.
No implementation changes made in the resumed run yet. Check the ASCII-only case folding against
the accepted case-insensitive email requirement before committing. Planning records retain their
existing trailing blank lines as the owner requested preserving them. Nothing pushed or committed.

## Ticket #84 review corrections

Both review axes completed with findings. Standards: moved the touched IUserRepository port to
Application/Abstractions, removed historical/what-only comments, and renamed the regex constant to
AllowedCharactersPattern. Spec: replaced ASCII-only email folding with Trim().ToUpperInvariant(),
added shared invariant-Unicode SQL translation for both providers, preserved actual Username column
collation in migration/activation alias checks, and made provisioning skip legacy reserved aliases.
Email spelling remains unchanged in storage; normalized-email columns retain ordinal uniqueness.

Added real-provider Unicode/supplementary backfill and collision tests, accent-insensitive alias
preflight/activation tests, Turkish-collation alias checks and a real-service provisioning regression.
657 unit tests and all 53 focused email-identity integration tests pass (no failures or skips).
The SQL Server collation clause requires an unquoted catalog collation name; a disposable pinned
SQL Server probe confirmed this, and its own container was removed. The PostgreSQL metadata query
uses non-keyword aliases. Failed intermediate runs were corrected before the passing focused run.

Final locked restore, Release build (0 warnings/errors) and format verification pass. The final
full integration suite and both pending-model-change checks are running, logged as
`~/Downloads/ev-84-*-final.txt`. Follow-up review clientRequestIds are
`84-standards-review-r3-fixed` and `84-spec-review-r3-fixed`, under the same task-id prefix above.
Both are fresh GPT Sol 6.1 xhigh/default sessions; disclose the same-model fallback for Codex's
review corrections because the requested Claude Opus 5.5 high reviewer remains rate-limited.
Commit message is saved at `~/Downloads/ev-84-commit-message.txt`. Still no commit, push or PR.


## Ticket #84 shared alias matcher

Further review found whole-string casing representatives insufficient for mixed Turkish aliases
and collation expansions. Replaced the three policy copies with EmailIdentitySql.UsernameAliasesEmail.
It checks the full invariant reverse-casing class independently at each scalar position, retaining
original Username collation. A second username representation exposes Unicode compatibility and
linguistic expansions only when the preserved collation equates the character and expansion.
Original spelling and binary normalized-email uniqueness remain unchanged. New fixtures cover
Turkish sensitive/insensitive rules and case-sensitive expansion comparisons on both providers.
Additional Standards corrections remove conditional lookup fakes, split migration-refusal outcomes,
and name invalid username scenarios explicitly. Forwarding migration helpers are removed.

Release R8 build passes with 0 warnings/errors. Previous R7 focused run passed 87 tests; the R8 run
adds ß/ss and mixed ßSs/SSSS cases and is active at ~/Downloads/ev-84-focused-r8.txt. Unit/format
checks also active. Prior full R4 integration run passed 267, but newer changes need the final run.
Fresh same-model fallback reviewers active: clientRequestIds 84-standards-review-r8-shared-matcher
and 84-spec-review-r8-shared-matcher (same task-id prefix as above). Opus remains rate-limited.
R5 tasks were cancelled before results when the author added the case-sensitive gate; R6 completed
with the mixed/expansion findings addressed by the shared matcher. Historical R1 tasks later resumed
and delivered additional findings; these are addressed too. Draft implementation review report is
reviews/implementation-ticket-84.md and still contains pending placeholders; finish before commit.
No commit/push/PR yet. Commit message remains ~/Downloads/ev-84-commit-message.txt.


## Ticket #84 character graphs

R8 full integration run passed 297, but reviewers found partial expansions and collation-ignored
characters beyond the two complete representations. R9 now builds per-letter graphs: raw or
collation-gated expansions for Username; complete reverse invariant casing classes with independent
raw/expansion paths for email. Intersecting graphs permits partial expansions and ignored-character
transitions. Ignorability compares x+letter+x with xx, avoiding SQL Server's trailing-space padding.
PostgreSQL recursive UNION deduplicates states; SQL Server uses indexed temporary transitions and
a unique visited frontier. All policy SQL is shared by both migration preflights and the guard.
The guard uses raw ToListAsync and in-memory Single because its SQL Server batch is non-composable.

R9 focused run: PostgreSQL 49 passed; SQL Server initially hit VALUES-as-CTE syntax, corrected to
SELECT FROM derived VALUES. Its focused run is active at ~/Downloads/ev-84-sqlserver-focused-r9.txt.
One more negative internal-space case per provider was then added. Release build passes0warnings,
format passes, unit657passes. Final focused/full integration runs still required after SQL Server
passes. Review tasks: 84-standards-review-r9-character-graphs and 84-spec-review-r9-character-graphs.
Original branch/base/restricted-path rules unchanged; no commit/push/PR. Draft report placeholders
must be updated before committing.

## Ticket #84 alias check simplified (2026-10-23, Claude Opus resumed)

The R9 character-graph matcher exhausted SQL Server memory in the full suite (238 failures: the
vault could not start). The owner chose to simplify. EmailIdentitySql now holds only the invariant
Unicode normalizer shared by both backfills, the preflight and activation. A username aliases
another account's email when it equals that address under the Username column's own collation, or
normalizes to the same identity. Exotic collation mixtures (Turkish, expansions, accent-insensitive
custom Username columns) remain protected at sign-in by the combined lookup's refusal, which the
database decides exactly. Those graph-specific tests and fixture helpers were removed.
R10: Release build 0 warnings, format clean, 657 unit and 55 focused email-identity tests pass.
Full suite + model checks log to ~/Downloads/ev-84-integration-r10.txt. Reviews per owner: Claude
Opus 5.5 high and GPT Sol 6.1 xhigh (Codex limited until 2026-10-23 02:12).
R11 (2026-10-23): Opus r10 Standards/Spec reviews and stale resumed GPT/Opus reviews of the graph
version processed. Fixed: SQL Server TRANSLATE remapped surrogate halves (normalization now folds
BMP characters only, app and both databases); restored unrelated comments GPT removed; named the
unsaved-account Guid; corrected FindEmailsWithPrefixAsync doc; why-comments for the provisioning
loop, SQL Server index-name match and THROW limit; HTTP tests for named-passkey options and
sharing with an ambiguous identifier. R10 full suite passed 257/257 before these changes. Final
run logs to ~/Downloads/ev-84-final-r11.txt. Commit message: ~/Downloads/ev-84-commit-message.txt.
GPT Sol 6.1 xhigh Standards review pending its 02:12 limit reset. Not committed.
Committed a14474f. Then an automated security review found that Unicode upper-casing lets SSO
match a look-alike mailbox (dotless i -> I). Normalization now ignores case only for A to Z, in the
app and both backfills; 263 integration, 657 unit and 61 focused tests pass. Follow-up commit.

## Ticket #85 implementation (started 2026-10-23, Claude Opus 5.5)

Branch `area-3-profile` from 73b6a60, root checkout, uncommitted. Design: Core port
`IAccountChangeCommitter.CommitAsync(account, action, details)` saves everything staged since the
account was loaded and records one event through the new operation-aware interceptor rule
`AuditSaveChangesInterceptor.SaveAccountChangeAsync` (no generic User rule). Repositories stage
instead of saving (`StageAvatarAsync`, `StageReplacementAsync`, `StageRemovalOfAllAsync`,
`StageAddition`/`StageRemoval`). A shadow `Users.ConcurrencyStamp` concurrency token (migration
`AddAccountConcurrencyStamp`, both providers) changes on every tracked account save; a stale copy
throws `AccountChangedException`. `UpdateAsync` no longer calls `Users.Update`, so only changed
fields are written. Shared `SecondFactorVerifier` (sign-in, step-up, later Profile verification)
spends TOTP steps and recovery codes with conditional updates; failures count by compare-and-set.
Admin TOTP reset keeps its UserTotpReset event but saves the code deletion in the same save.
Moved the touched ports IMfaRecoveryCodeRepository/IPasskeyCredentialRepository to Abstractions.
Tests: AccountChangeRaceTests (SQL Server + PostgreSQL, 24), ProfileAuditTests (11 HTTP),
ProfileAuditFailureTests (9). Logs in ~/Downloads/ev-85-*.txt. Mutation checks: removing the stamp
bump fails 6 race tests; removing the TOTP step condition fails both same-code tests.
Next: full integration suite, security-reviewer, Standards/Spec reviews, commit (message file in
~/Downloads/ev-85-commit-message.txt). No push, PR or issue closure.

## Ticket #85 resumed in Codex (2026-10-23)

HEAD remains 73b6a60 on area-3-profile; all ticket work is uncommitted. Original full integration
run: 305 passed, 2 failed because EmailIdentityUpgradeTests assumed its migration remained latest.
That assertion now checks the preserved email values. Sol Standards/Spec r1 found: administrator
reset was still save-before-audit and kept LastTotpStep; ledger/counter updates preceded their audit;
break-glass reset failures before clearance; combined tests and redundant comments/naming.
Corrections add AccountTransaction (per-account advisory lock before chain lock), AuthenticationAttempt
(Core port; refresh account under lock, stage events through interceptor, commit with all bookkeeping),
shared AuthenticationFailures, atomic administrator reset with preserved actor, and a dedicated
break-glass verification boundary. Passkey counters use conditional field updates; genuine bad
signatures/clone counters count against lockout. Profile tests are split into individual scenarios.

Security-reviewer Sol r3 found staging/start/rollback cleanup gaps and passkey failure bypass. Reset
now loads recovery rows before changing User; AccountTransaction catches start failure and clears
tracking even when rollback reports failure. Added real-provider transaction fault tests, real SQL
Server/Postgres rejected-audit ledger tests, and Production AuthenticationAuditFailureTests plus
PasskeyLockoutTests. Focused recovery r3: 83 passed. Newer r5 run is in
~/Downloads/ev-85-focused-recovery-r5.txt; build r5 passed with zero warnings/errors. Further provider
failure tests were added after that build, so build and final verification remain necessary.

Owner requires delegates only on Sol 6.1 xhigh or Opus 5.5 high: authoritative rule in AGENTS.md;
CLAUDE.md and local agent definitions agree. The Astra security attempt was canceled at the owner's
request. Its replacement Sol security review completed (two findings above). Prior native Opus
security review failed quota. Final fresh Standards/Spec/security reviews still required after fixes;
use the approved models and disclose same-model fresh-session review for Codex corrections if needed.
Do not push, create a PR or close #85. Stage only explicit paths; restricted directories untouched.

## Ticket #85 resumed in Claude (2026-10-23 evening)

Codex stopped on its usage limit after the passkey reload-under-lock fix (Spec r6 P2) and a clean
r7 build; its r7 reviews failed on the limit. Gates rerun on the current tree (r8): locked restore,
Release build 0 warnings/0 errors, format clean, 657 unit, 6 guardrail, both model checks clean.
Docker had stopped (first r8 integration attempt: 339 failed, no Docker socket); restarted it and
reran to ~/Downloads/ev-85-integration-r8.txt. Final reviews on Opus 5.5 high (different model from
the Sol corrections): clientRequestIds 85-security-final-r8-opus-high,
85-standards-final-r8-opus-high, 85-spec-final-r8-opus-high. Commit message:
~/Downloads/ev-85-commit-message.txt (63-character subject). Not committed, pushed or closed.

## Ticket #85 final recovery in Codex

Opus r8 Standards reported no hard violations; its nine judgment calls are answered in
`~/Downloads/ev-85-review-resolutions.md`. Documentation/test naming fixes were applied in r9.
Opus r8 Spec found an account-state disclosure in passkey errors and missing losing-request
assertions in the concurrent HTTP TOTP test. Both are corrected in r10, with two new Production
HTTP passkey regressions. Enrollment/step-up shared failure accounting remains as authorized by
confirmed Q11. Account transaction errors retain the underlying cause and fail-closed operation
outcome; the quoted acceptance criterion does not require audit-only diagnostic classification.

R10 Release build: zero warnings/errors; format clean; 657 unit tests and all 341 integration tests
pass, with no failures or skips. The full run includes 38 focused provider race/fault cases (19 per
provider), 17 Profile event tests, 17 Production audit-rejection tests and 6 passkey accounting tests.
Logs: `~/Downloads/ev-85-{build,format,unit,integration}-r10.txt`. Locked restore, both model checks
and 6 guardrail tests passed r8; Angular prerequisite install/build passed on Node 24.22.0.

Opus security r8 failed quota. Fresh Sol 6.1 xhigh final review clientRequestIds:
`85-security-final-r10-sol-xhigh`, `85-standards-final-r10-sol-xhigh` and
`85-spec-final-r10-sol-xhigh`, under task prefix
`node:delegated-task:command%3Amcp%3Adfb4d046-1762-4313-8c29-5d67ea34fc48%3Adelegate-task%3A`.
Spec reports no unresolved findings and accepts both responses above. Standards reports no hard
violations and one remaining summary-wording judgment, now corrected to describe event/bookkeeping
atomicity without promising an event on every guard exit. Security is pending. Implementation is
uncommitted; delegate model policy is already committed as `16e4b4a`. No push, PR or issue closure.

Security r10 then found a SQL Server authentication/share deadlock: authentication could hold a
User row before waiting for the chain, while sharing held the chain before its foreign-key check.
The correction acquires the chain after the account lock and before authentication writes, through
`AuditChain.AcquireAsync`. `AuthenticationSharingTests` coordinates this exact interleaving and
passes with the correction. Removing the early chain acquisition made that regression fail with
an audit-chain lock refusal (1 failed). The correction is restored, and the r13 rebuild passes
with zero warnings/errors. Mutation log: `~/Downloads/ev-85-sharing-mutation.txt`.

Final r13 locked restore, format, unit and full integration checks pass, logged at
`~/Downloads/ev-85-*-r13.txt`: Release build 0 warnings/errors, 657 unit and 342 integration tests,
all with zero failures/skips. The suite includes 39 provider cases (20 SQL Server, 19 PostgreSQL).
Both pending-model checks and 6 agent guardrails passed r8; model/hooks have not changed since.
Fresh Sol 6.1 xhigh security/Standards/Spec follow-up reviews use the same
task prefix above and clientRequestIds `85-{security,standards,spec}-final-r13-sol-xhigh`.
All three report no remaining actionable findings. These are fresh-session same-model fallbacks
for the final Sol corrections because Opus is quota-limited. Final reports:
`~/Downloads/ev-85-final-reviews.md`; all prior findings/responses:
`~/Downloads/ev-85-review-resolutions.md`. The implementation and sharing regression are staged
explicitly for the final commit. Subject: "Save each Profile change with its one audit event or not
at all" (63 characters), message file `~/Downloads/ev-85-commit-message.txt`. Separate model-policy
commit: `16e4b4a`, "Use Sol 6.1 and Opus 5.5 for delegated agents" (45 characters).
No push, PR or issue closure. Final report goes to coordinator `e877e718-6881-4791-bde5-e2c932ae4461`.

## Ticket #86 implementation (started 2026-10-24, Claude Opus 5.5)

Root checkout, branch `area-3-profile` from `d786a89`; logs and reviews in `logs/ticket-86/`.
Design, to resume from if interrupted:

- `User.CredentialGeneration` (bigint, both providers). Password, active email (normalized change),
  recovery replacement, passkey add/remove, self and administrator authenticator reset advance it in
  the same `IAccountChangeCommitter` transaction; each service takes the session's expected
  generation and returns the committed one. Mismatch throws `StaleProofException`.
- `SessionStamp(CredentialGeneration, RevocationVersion)` travels in claims `vault:cg`/`vault:rv`
  of the pending-MFA and session cookies; registry observations store it and listings filter on the
  current stamp (`SignedInDevices`). Cookies without both claims are refused (one-time sign-in).
- `ISessionRevocationService`: `CaptureAsync` (atomic init with random value), `FindAsync` (null when
  missing; Redis GETEX refreshes TTL), `RevokeAsync` (new random value). No timestamps.
- Capture the version after resolving the account id and before the locked reload/snapshot
  (password stage, discoverable passkey, SSO via `ReloadAsync`). Verification steps fence the pending
  generation under the account lock; `SessionIssuer` refuses issuance unless the captured version is
  current. Renewal/step-up copy claims, so they keep the original version.
- Redis session keys are scoped by `Redis:SessionIncarnation` (config, outside Redis); recovery gate:
  stop traffic, set a new value on every node, restart all, retire nodes that cannot.
- Sign out everywhere: Core `AccountSignOut` revoke-first, audit `SessionsRevokedSelf` second;
  outcomes SignedOut / RevokedUnrecorded / Incomplete; Legacy controller always clears the cookie.
- ADR-0026 branch `/local-sign-out` mapped before the blacklist/session/auth middleware.
Progress 2026-10-24: production code, both `AddCredentialGeneration` migrations and docs
(BACKUP_AND_RECOVERY Redis gate, INSTALL §8 Upgrading, README) are written, uncommitted; Web builds.
Next: fix existing tests, add new tests, full verification, reviews, commit.

## Ticket #86 resumed in Codex (2026-10-24 evening)

Still root checkout, `area-3-profile`, HEAD `d786a89`, all ticket work uncommitted. Restricted
folders untouched. Every new artifact/log/review is under `logs/ticket-86/` (gitignored).

Added production proof fencing at pending-cookie validation and locked factor verification, retained
external versions for named passkeys, rechecked durable generation at issuance/replacement, bound
registration to its original account/session/stamp, and preserved SSO's initially matched generation
and email across reload. Committed-change replacement failures now return 503
`changed_sign_in_required`; account-wide sign-out failures return the specified 503 codes and keep
Legacy warnings. Local clearing reports only `sign_out_incomplete`. Session replacement atomically
retires its old ID against per-device revocation (memory lock and Redis Lua); metadata stays best-effort.

Added real SQL Server/PostgreSQL generation upgrade/persistence/stale-proof/rollback tests, generation
rollback assertions throughout AuditFailureFixture, real Redis version/expiry/stale-observation and
retirement tests, Production Redis hosts for outage/recovery (two rotated nodes against restored
populated state), local-cookie paths/methods/CSRF/chunks, every revoker on memory/Redis, stale factor
POSTs, named/discoverable keys, and renewal/issuance races. Redis outage fixtures pause the container:
a stop/start remaps Docker's random host port, confirmed in `redis-port-probe.txt` (own probe removed).

Reviews r1: Opus security failed quota; fresh Sol 6.1 xhigh security found a per-device retirement
race (fixed atomically). Spec found SSO generation upgrading and missing sign-out statuses (fixed).
Standards had six grouped findings plus typed-state advice (fixed). R2 Standards has two naming/comment
findings (fixed) and named-state grouping advice (fixed with PasskeySignInState). R2 security and Spec
are still running. All tasks use provider codex, model gpt-6.1-sol, reasoningEffort xhigh, serviceTier
default, task prefix `node:delegated-task:command%3Amcp%3A4052bddb-f2dc-4f35-9891-468614ccfa1c%3Adelegate-task%3A`,
clientRequestIds `86-{security,standards,spec}-r2-sol-xhigh`. Read reports under logs/ticket-86/.

R10 locked restore and six guardrails passed. R10 Release build: zero warnings/errors; full integration
and unit suites plus both pending-model checks are running (`*-r10.txt`). Last earlier focused run:
79/80 passed; failure was the now-diagnosed Redis port remapping. Failure-path r9: 15/16 passed; its
one failure was the test's @@SPID int16/int32 mismatch, corrected with an explicit SQL cast in r10.
Angular prerequisite npm ci --ignore-scripts and production build passed on pinned Node 24.22.0;
install reports one moderate advisory (no high/critical). New source edits since the r10 build group
named passkey state and rename the retirement helper, so final build/format/unit/integration gates
must run again after any remaining review corrections. No commit, push, PR or issue closure.

## Ticket #86 finished in Claude (2026-10-27)

Resumed on Opus 5.5 after Codex hit its quota. Fixed the one r10 integration failure (an admin MFA
reset now ends the target's sessions), added the step-up renewal regression Spec r2 asked for, and
ran the security-reviewer plus Standards/Spec reviews on Opus high. Findings and resolutions:
`logs/ticket-86/review-resolutions.md` (Final round). Open owner decisions: a Redis
`maxmemory-policy` startup check, auditing sessions refused at issuance, and the role-change
revoke-before-audit ordering (pre-existing). Committed as one commit; not pushed.

### Owner decisions on #86 review findings (2026-10-28)

- An administrator's MFA reset ends the target's sessions: confirmed.
- Auditing a sign-in that `SessionIssuer` refuses after the success event was committed (and
  break-glass lifting the IP block before issuance): deferred to #93.
- A role change that commits, then revokes in Redis, then audits, so a Redis failure leaves it
  unaudited: deferred to #92.
- Redis eviction: enforced. With Redis enabled the vault reads `maxmemory_policy` from `INFO memory`
  on every server at startup (a short-lived admin connection; the vault's own connection stays
  non-admin) and refuses to start unless it is `noeviction`. Second commit on `area-3-profile` after
  `f12ba30`; logs under `logs/ticket-86/` (`*-r13.txt`).

## Ticket #87 implementation (started 2026-10-28, Claude Opus 5.5)

Root checkout, branch `area-3-profile` from `6dc23c7`; logs and reviews in `logs/ticket-87/`.
Design, to resume from if interrupted:

- New table `ProfileOperations` (entity `ProfileOperation`, both providers, migration
  `AddProfileOperations`): account, session id, kind (ChangePassword, ResetAuthenticator,
  ReplaceRecoveryCodes, AddPasskey, RemovePasskey), frozen target (candidate password hash/salt,
  passkey nickname, target passkey id), the session stamp it was prepared under, created/expires
  (10 minutes unverified), verified-at (grant lasts 5 minutes), proof method plus the proving passkey,
  and one passkey challenge (purpose, value, 120-second deadline). Cascade-deleted with the account.
- Preparation: one operation per session (preparing replaces the session's previous one and its
  challenge), at most five unexpired per account, shared IP throttle (AuthThrottleFilter) before any
  work, size/format/breach checks, then a bounded candidate-hash gate. No audit event.
- Verification runs in IAuthenticationAttempt: refresh the operation under the account lock, fence
  its stamp, check the method is offered, current password, then (password change) the unchanged
  check, then TOTP/recovery through SecondFactorVerifier or the passkey assertion (challenge consumed
  first by a conditional update). Failures count through AuthenticationFailures and record
  AccountVerificationFailed; success resets failures, marks the grant and records AccountVerified.
  Neither event is in sign-in history.
- Final endpoints take only the operation id. The grant is consumed by deleting the row in the same
  IAccountChangeCommitter transaction as the mutation, generation and event. Last-passkey removal
  rechecks the surviving method under the account lock (new committer overload with a check run
  after the lock), so it serializes with recovery redemption.
- Web: Ui/ProfileDetailsController (snapshot, details, avatar, sign-out everywhere),
  Ui/ProfileOperationsController (prepare/cancel/verify/passkey), Ui/ProfileCredentialsController
  (password, reset, recovery codes, passkey registration/removal). Problem Details via
  ProfileProblemResponse; UI API invalid model state becomes validation_failed with field names only.

## Ticket #87 resumed in Codex (2026-10-28)

Root checkout remains `area-3-profile` at `6dc23c7`; all work is uncommitted. No restricted folders
read or changed, no worktree, push or PR. Logs/reports are under `logs/ticket-87/`.

Completed Production HTTP coverage for route authorization/antiforgery/shapes/scoping, prepared
operation replacement/expiry/binding, verification/lockout/foreign and malformed passkeys, recovery
proof/replacement, session rotation/reset, and all five credential audit-failure paths. Added real
SQL Server/PostgreSQL operation persistence/concurrent admission/challenge/final-consume tests,
last-key removal serialized with final recovery redemption, and delayed final-consumption expiry
and revocation regressions. The focused r7 run passed 89/89 before the final review fixes.

Security r1 (fresh Sol 6.1 xhigh after attempted Opus high hit API quota) found delayed final grant
revocation/expiry gaps and missing non-password change notices. Fixed all: locked final rechecks,
atomic challenge consumption with audited changes, and fail-soft secret-free notices. Security r2
is reviewing the whole final diff. Standards r1 found test branching/composite scenarios, a narrative
comment, and two passkey naming/duplicate-parser smells; corrected each. Spec r1 is running.

Latest `build-final.txt`: Release zero warnings/errors. Locked restore, generated-type checks,
Angular prerequisite production build and six guardrails passed. Full integration, unit/format
verification and local Docker E2E are running; their final logs are `integration-final.txt`,
`unit-final.txt`, `format-verify-final.txt`, `e2e.txt`. Do not claim completion before reading results.
Final report must list counts/reviews, commit SHA/subject length and three code excerpts, and be
sent to coordinator e877e718-6881-4791-bde5-e2c932ae4461 if tool available. Commit with message file,
explicit paths only; do not push or close issue.

Final-source verification restarted as r12 after Spec review added the shared Legacy ceremony
store and new HTTP 503/final-consume/disclosure tests. Security r2 inspected those late additions
and has no unresolved findings. Standards r2's four remaining test-readability findings were fixed
locally (literal request transport, explicit nine-code setup, isolated Redis/provider scenarios).
Spec r2 remains pending. Focused r11 passed 20/20 before fixture-name/outbox-expectation corrections;
r12 unit passed 698/698, Release build has zero warnings/errors, both provider model checks match.
Full r12 integration is running. The earlier integration/E2E runs were superseded, cancelled with
SIGINT, and retained only as diagnostic logs. The E2E runner removed its own containers/image.
The final E2E/guard run is `e2e-final.txt` / `e2e-guards-final.txt` on the frozen production source.
Commit message is prepared at `logs/ticket-87/commit-message.txt`, subject length 66. Still no commit.

Spec r2 closed its four original findings and found one inaccurate audit phrase after a
password-first check. Removed the claim of a valid passkey and added a Production HTTP regression
with an unchecked assertion. Build/locked restore/unit/format r13 pass (zero warnings, 698 units).
All findings now have fixes recorded in logs/ticket-87/review-resolutions.md; no owner decision remains.
Full r12 integration was cancelled after SQL fixture startup/login timeouts under Docker-heavy
parallel gates. Run the full r13 suite after E2E with `--settings logs/ticket-87/serial.runsettings`
(collection serialization only; race tests retain their explicit concurrent requests). Current final
E2E session is running `npm run e2e`, followed on success by `npm run e2e:verify-guards`; both logs use
`*-final.txt`. All intended source paths are staged via the explicit manifest in logs/ticket-87/.
Restage this checkpoint before commit. The final audit-wording correction is covered by r13 and the
new HTTP test; it does not change the browser authentication/session behavior exercised in E2E.

Browser gates completed: final E2E 74/74 passed (8.8 minutes); guard verification correctly detected
all eight intentional failures and exited zero. Each runner removed its own disposable stack/image.
Full integration r13 is now running with collection serialization, log `integration-r13.txt` (native
exec session 40529). Final source is frozen; only report/checkpoint updates remain. npm audit exited
zero at the required high threshold (one existing moderate fast-uri advisory remains visible).

## Ticket #87 complete (2026-10-28, Codex)

Final full integration r13 passed 533/533, zero skipped, in 4m16s with collection serialization;
explicit race tests still run their concurrent requests. Final Release build: zero warnings/errors;
locked restore, format verification, 698/698 units, SQL Server/PostgreSQL pending-model checks,
generated-type check, Angular build/lint (six lint guard tests), 162 Vitest tests, 15 build-check
tests, six agent guardrails and npm audit at high threshold passed. Local browser suite: 74/74;
guard harness verified all eight deliberate failures. npm audit retains one existing moderate
fast-uri finding, no high/critical. Logs and complete review resolutions: logs/ticket-87/.

Security r2 has no unresolved findings. All Standards r1/r2 and Spec r1/r2 findings are corrected,
including the final audit wording pinned by a real HTTP test. Reviews used fresh Sol 6.1 xhigh
sessions after an attempted Opus 5.5 high security review failed quota; Claude authored the initial
production implementation and Codex completed it. No remaining owner decision. The final audit
wording correction was validated by the full final HTTP suite; it changes no browser/session flow.

Commit follows `6dc23c7`, subject "Verify one frozen Profile change with existing account credentials"
(66 characters), using the message file and explicit staged paths. No push, PR or issue closure.
The root checkout remains on area-3-profile. Legacy unsafe handlers remain for the planned #91
cutover; this ticket does not claim those routes require the new Profile verification workflow.

## Ticket #88 implementation (2026-10-28, Codex)

Started from exactly `a1d92486513f4a689a906bfda57ff0930b7fb238` on `area-3-profile` in the root
checkout. Evidence is under `logs/ticket-88/`. Neither excluded documentation directory was read
or changed. No push, PR, issue closure, worktree or new implementation thread.

The frozen ChangeEmail operation owns its target, SHA-256 digest of a random 128-bit mailbox code,
15-minute absolute code deadline, delivered flag and five-attempt counter. A filtered unique index
in both providers permits one email operation per account. Account transactions serialize request,
resend, confirmation, cancellation and proof refresh; active email, generation, both proof
consumptions and the EmailChanged event commit together through the audit interceptor. The existing
session stamp also fences the operation against revocation and session-store incarnation changes.

Delivery reserves shared database account/IP minute and sliding-hour budgets before SMTP. It clears
the grant and leaves the code unusable until the real SMTP transport succeeds; success restores only
the original grant deadline. Failed delivery needs fresh account verification before deliberate
resend. Neither codes, bodies nor relay exception text enters ordinary notification history.
Completed-change notices use captured trusted addresses, with email activation notifying its old
address. Shared Legacy credential callers also attempt these notices pending their planned removal.

Typed session-only request/resend/confirm/cancel routes, capability explanations and pending status
are exposed and TypeScript regenerated. The E2E stack now includes digest-pinned Mailpit and a
browser flow for activation, single use and copied-session rejection. No Profile UI cutover occurred.

Focused r3 passed 37/37; expanded r5 passed 94/94 before the last provider tests and final naming
cleanup. Release builds through r10 have zero warnings/errors. r11 locked restore/build/format and
local E2E/guard gates are in progress. Angular lint with six guard probes, 162 Vitest tests, 15 build
checks, six agent guardrails and npm audit at high threshold have passed; the existing moderate
fast-uri advisory remains visible. Full final integration, final model checks and complete-diff
Standards/Spec/security reviews remain required. Do not report completion from this checkpoint.

The first #88 implementation commit is `237c00dfc3ab0242a6024f08515aca8e0e96b752` (subject 64
characters). Initial independent reviews used fresh Sol 6.1 xhigh after all three attempted Opus
5.5 high reviews failed API rate limits. Standards r1's three groups (stale comments, conditional
test expectations, missing page objects/named browser actions) are corrected locally. Spec and
security each found the same P1: email activation also needed to advance Users.ConcurrencyStamp.
A two-provider regression demonstrated the stale credential write before the fix; AuthenticationAttempt
now rotates the stamp on tracked account changes. Both regressions passed in full integration r13/r14.

r13 full integration: 591/593 passed, two fixture-null notice failures corrected by wiring a real
fail-soft notification composer into AccountRequest. r14: 594/595 passed, one false-positive short
TOTP disclosure match inside a random trace ID. The assertion now matches short numeric values
with alphanumeric boundaries, without excluding log fields/files; four literal matcher cases pin
actual disclosures and the trace nonmatch. These final helper/tests and readability fixes still
need a fresh build/full run. Unit r12/r14 passed 700/700. Source model checks passed both providers.
The first E2E run was deliberately superseded for the concurrency fix and cleaned up its own stack
and image; it is not final passing evidence. Full final gates and fresh complete-diff rechecks are
still required. Reviews and their resolution ledger are under logs/ticket-88/.

## Ticket #88 final gates (2026-10-28, Claude Opus 5.5 resumed after the Codex usage limit)

Full integration on `82d28ef` passed 599/599. Standards r2 found three stale comments (corrected);
Spec r2 found nothing unresolved. The Sol security r2 attempt hit the Codex usage limit, so a fresh
Opus 5.5 high security-reviewer session rechecked the complete diff: r1's concurrency-stamp P1 is
fixed, with no new findings. Its one carried observation: Legacy `POST /Profile/Update` still
changes email without mailbox proof until the #91 cutover retires it. The e2e mailbox helper now
sends `RequestVerificationToken`, the header Program configures. Final local browser suite: 75/75
including the real-SMTP mailbox spec. Guard verification confirmed all eight deliberate failures.
Full evidence, the ledger and the final report are in logs/ticket-88/. No push, PR or #88 closure.

## Ticket #89 staged implementation (2026-10-28, Codex)

Resumed Claude's read-only start on exactly `d5b1565`, root checkout `area-3-profile`; only the
excluded untracked research directory existed. Neither excluded directory was accessed. No new
branch, worktree, push, PR or issue closure. Evidence and bounded check runner: `logs/ticket-89/`.

Staged Profile/details, email and Change password components use the existing typed APIs, shared
StatusLine/ConfirmationDialog and a reusable native account-verification dialog. Profile ownership
and Angular routes remain Legacy until #91. The security cards hand back to Legacy Profile until
#90 supplies their complete controls. The cutover acceptance inventory is `browser-acceptance.md`.

The snapshot adds only HasCustomAvatar; the shared breach preview now has an explicit session-only,
no-store UI API endpoint used by both Legacy password fields. TypeScript was regenerated. No model
or mutation persistence changes. Existing AuditFailureFixture coverage must pass in the full suite.
A fixed local-cookie warning renders at Legacy sign-in after anonymous fallback, without accepting
arbitrary warning text. Page-scoped requests never replay, clear sensitive state on departure/session
loss, and handle known commits separately from unknown replies.

Angular lint (six lint guards), formatting, 200 Vitest tests, 15 build checks, generated-type check,
production build and high-threshold audit passed before reviews; the existing moderate fast-uri
advisory remains visible. Initial Release build had zero warnings/errors. Final bounded backend
restore/build/format/unit/full integration and production E2E/guard runs are in progress. Independent
complete-diff Standards/Spec/security reviews remain pending. Do not report completion yet.

## Ticket #89 reconnection and review corrections

Initial implementation commit: `f09e1ef` (subject 62 characters). Opus Standards, Spec and security
reviews all failed API rate limits. Fresh Sol xhigh fallback sessions were then interrupted by the
connection loss. Standards saved five documented finding groups plus one duplication judgment;
corrections and the one justified HTTP wire-contract assertion are in
`logs/ticket-89/review-resolutions.md`. The Spec/security preliminary observations led to deferred
opener focus restoration and a stale-401 logout-order correction, each with regression coverage.

The initial integration run passed 601/605: three SQL connection failures during the outage and one
new test-data collision, corrected with a unique account suffix. Browser failure was explicitly
ERR_NETWORK_CHANGED: 6 passed, 1 failed, 68 not run. Both full runs are restarted; original output
is retained. Corrected components pass 210 Vitest tests. Latest Release build remains zero warnings.
All final gates and fresh complete-diff reviews still need confirmed results before completion.

## Ticket #89 final review outcomes and serialized verification

Completed fresh Sol 6.1 xhigh security r2 found no defects, including focused logout-order and late
passkey-disposal probes. Standards r2 verified prior fixes and accepted the Legacy wire-contract
assertions; its only remaining minor stale hidden-meter rule/comment was removed. Spec r2 found
four P2 UI state issues, all accepted: disabled confirmation openers lost focus, expired verified
replies kept parent forms locked, removed email operations kept stale controls, and email failure
coverage was incomplete. Narrow fixes plus 16 regressions now pass 226/226 Vitest tests. Detailed
responses are `logs/ticket-89/spec-resolutions-r2.md`; original review reports remain unchanged.

The concurrent rerun hit SQL Server startup crashes/readiness failures before product tests.
Docker reported only 4 GB total memory and severe CPU contention across several SQL containers.
Stopped only this run's integration process group; Testcontainers and the browser runner cleaned
up their own resources. Restarted the complete integration suite with an ignored local runsettings
file disabling collection parallelism (tests' own explicit concurrency remains active). Run E2E
and guard verification separately after that suite; do not count startup-failed runs as passing.
No product code or fixture was weakened for resource limits. Latest restore/build/format/unit and
agent guardrails passed again; Release build has zero warnings/errors and unit tests remain 700/700.

## Ticket #89 runtime gates completed; final guard run

The final runtime source is `e725f9d`, following `f09e1ef` and `213c3a4`. Commit subjects are 62,
63 and 68 characters. All changes stay local on area-3-profile. Latest checks: locked restore,
Release build (zero warnings/errors), dotnet format verification, 700/700 unit tests, 605/605 full
integration tests with collection parallelism disabled locally, 6/6 agent guardrails, Angular lint
with six lint guards, formatting, 226/226 Vitest tests, 15/15 build checks, generated-type check,
production build and npm audit at high (the existing moderate fast-uri advisory remains visible).

Running the Docker-heavy suites separately avoided the earlier startup failure. The final full
production E2E passed 75/75 in 9.4 minutes, including real SMTP activation and copied-session refusal.
These are regression checks of existing production routes and the mailbox API flow, not a claim
that staged Angular Profile pages have browser parity before #91 activates them. Final guard
verification passed: all eight intentional failures were confirmed. Both final browser runners
removed their own containers, networks and temporary images. All required gates are complete.
The final report and review ledgers are under logs/ticket-89/. No push, PR or issue closure.

## Ticket #90 continuation (2026-10-28, Codex)

Claude authored the staged authenticator reset, recovery display and passkey controls from exactly
`1778ee2`, then hit API quota. Root checkout remains `area-3-profile`; all changes are uncommitted.
Neither excluded documentation directory was accessed. No branch/worktree/push/PR/issue closure.
Evidence and bounded check runner: `logs/ticket-90/`. Profile ownership remains Legacy until #91.

Initial Standards review (Sol xhigh) found two misleading/redundant names/comments and one naming
judgment, all corrected. Spec r1 found lost-response reloads erasing warnings, destroyed EmailChange
instances retaining mailbox inputs, and missing passkey lifecycle cases. Unknown outcomes now block
writes and show explicit sign-in without protected reload; cleanup registrations clear/unregister on
component destruction; new regressions cover retained mailbox state, late UV completion after
pagehide/session loss, server expiry and duplicate submissions. Native Opus security-reviewer r1's
SEC1 (forgeable reset notice) is fixed with the existing protected one-shot TempData flash and real
HTTP notice-once/forgery-refusal tests. Client now navigates to plain Legacy sign-in.

Final Opus Standards/Spec/security attempts failed API quota; fresh Sol 6.1 xhigh fallback sessions
are rechecking the complete diff. Initial r1 backend gates passed 608 integration/700 unit tests;
final backend r2 is running after the notice fix (focused HTTP passed 16/16). Angular final r4 passes
lint (six guard probes), format, 267 Vitest tests, 15 build checks, generated-type check, build and
high-threshold npm audit (the existing moderate fast-uri advisory remains visible). Backend r2
restore/build/format/700 units/six guardrails pass, zero Release warnings/errors; full integration
is still running. Run browser and all eight guard probes separately after integration. Do not report
completion or commit before remaining reviews and runtime gates have been checked.

Ticket #90 final backend r2 passed 607/607 integration tests, zero skipped, in 6m13s with only
collection parallelism disabled locally (explicit race requests still concurrent). Final Angular
final2 gates pass: 272 Vitest tests, 15 build checks, lint/six lint guards, format, generated-type
check, production build and high-threshold audit. No API route or response shape changed, so the
generated schema remains unchanged. Unit tests remain 700/700; Release has zero warnings/errors.

Standards r2 found no new issues. Spec r2 found ambiguous nonzero HTTP errors and blocked local
cookie clearing; corrected with five regressions. Two existing email mocks were corrected from
503 to the actual mail_unavailable 409 contract (pinned by real HTTP tests). Security r2 inspected
these current corrections, closed its matching SEC2/SEC3 observations and SEC1, and found no
unresolved demonstrated defect. All rechecks used fresh Sol 6.1 xhigh sessions after final Opus
attempts failed API quota; disclose the fresh-session fallback for Codex's continuation edits.
Full review reports and resolutions remain in logs/ticket-90/. Browser final is in progress,
followed by all eight guard probes. Still no commit/push/PR/issue closure.

Final production E2E passed 75/75 in 10.1 minutes and removed its disposable stack/image. This
checks existing production routes and SMTP/session regressions; new Angular credential controls
remain staged and have component coverage until #91's ownership cutover. Final guard verification
is running against a separate fresh vault; all eight deliberate failures must be detected before
completion. Expected commit subject: "Stage verified authenticator, recovery code and passkey controls"
(64 characters), message file logs/ticket-90/commit-message.txt; stage only the explicit 26-path
manifest logs/ticket-90/stage-paths.txt. No commit yet.

## Ticket #90 complete (2026-10-28, Codex continuation)

All runtime gates passed on the final source. Browser-health verification correctly detected all
eight intentional failures and exited zero; its own stack/image were removed. Final production
E2E is 75/75; Vitest 272/272; unit 700/700; full integration 607/607, zero skipped. Both six-test
guard sets and 15 build checks passed. Restore/build/format/type checks/audit passed as above.
All review findings are resolved, including both late Spec/security findings; security r2 inspected
the current corrections. Full reports, resolution ledger and command evidence are logs/ticket-90/.

The completion commit follows 1778ee2 and uses the counted 64-character behavior subject recorded
above. Source remains frozen; only this checkpoint changed after the checks. The commit SHA and
three required code excerpts will be in logs/ticket-90/final-report.md and the coordinator report.
No push, PR, issue closure, branch/worktree or excluded-directory access.

## Ticket #91 cutover in progress (2026-10-28, Codex)

Started on area-3-profile at exactly 2f2b1ea723d217c338e032f95d94b94eeaf1f764, with only the
pre-existing excluded untracked directory entry. Validated c7b4b1bbfed73013a21c51bea90b4d2537bae83d
as an ancestor. Neither excluded directory was accessed. No branch/worktree, push, PR or closure.
Authoritative #83/#91 bodies/comments are saved under logs/ticket-91/.

Profile now owns the four case-insensitive Angular routes. Menus, palette and self-service links
stay inside Angular. Removed the Legacy Profile controller/views/models and registration JavaScript;
Avatar has a separate explicit session-only/no-store handler. Removed the obsolete Core direct-email
helper. HTTP retirement tests exercise valid sessions and antiforgery, including Update and aliases.
Shared integration setup now uses real prepared/verified operations rather than deleted forms.
Redundant unverified credential audit tests were removed in favor of the existing correctly phased
VerifiedProfileAuditFailureTests and exact UI API success-event tests; cosmetic/admin/no-rescue tests
remain. No model changes.

Current checks are preliminary: Release build r4 passed with zero warnings/errors; units 698/698;
frontend r4 passed 280 Vitest tests, 15 build checks, lint/six lint probes, format, generated types,
production build and high-threshold npm audit. Later source changes still need reruns. Initial full
integration r1 is running on earlier binaries; it found deleted-HTML-token setup, an added verification
activity event and a repeated TOTP proof in second-session setup. Fixes are in current source; r1 is
not passing evidence. Its real Redis password-outage case originally paused during verification;
current setup prepares/verifies before taking its mutation-phase blocker.

New Profile browser specs/page objects cover live details/avatar/password/SMTP/recovery/passkey/reset,
copied-cookie refusal, Cancel/Escape and actual server operation expiry. They have not yet run.
Browser evidence now supports E2E_EVIDENCE_DIR so new artifacts stay inside logs/ticket-91/.
Run full integration and browser separately for Docker's 4 GB; retain explicit concurrency tests.
All final gates, whole-Area security/Standards/Spec reviews and the owner PR body remain outstanding.
Review the entire git diff c7b4b1bbfed73013a21c51bea90b4d2537bae83d...HEAD, including every new commit.
Do not claim the Area shipped or push. Coordinator reporting is authorized only for the final report.

## Ticket #91 continuation after Claude quota (2026-10-29, Codex)

HEAD is the local cutover snapshot 3c163423a714390f94fca0693c2d526a9aaea069. Claude's subsequent
edits were recovered in the same root checkout. All remain local; no push, PR or issue closure.
Original whole-Area Opus 5.5 high reports are logs/ticket-91/reviews/{standards,spec,security}-r1.md.
Their finding-by-finding ledger is logs/ticket-91/review-resolutions.md. All artifacts stay under
logs/ticket-91; excluded directories remain untouched.

Security F1 distinguished a real failed session-store recheck from auditing. Keep the commit
fail-closed; session_store_unavailable means no change. Both RedisException and RedisTimeoutException
are classified. The real Redis HTTP regression separates precommit refusal from postcommit renewal
failure using different database gates. Angular now names its write coordinator ProfileWrites.
No model change. API generation still produces the existing schema.

Integration r6: 615/616; only the old outage phase failed. Backend r14 is now running with the two
outage phases plus new avatar size/route/consumer coverage; build/format/698 units/six guardrails pass.
Frontend r15: all gates pass, 282 Vitest tests, 15 build checks, six lint probes. Later source needs
final reruns. Browser r7 failed 429 (25 passing); the fresh-window placement was corrected. Browser r8
passed the corrected test and existing navigation (at least 44 passing), then was superseded for the
expanded acceptance coverage. Its runner removed only its own stack/image.

The expanded E2E stack includes authenticated, digest-pinned Redis under this run's incarnation;
real SMTP and Redis failures, actual SQL audit rejection and postcommit notification gates are used
without service replacement. Live Profile tests now cover mailbox/grant expiry, avatar rejection,
all proof methods, UV cancellation, last-key guard after reset, recovery storage clearing, and
truthful committed/unknown outcomes. Full browser and eight guard probes must run separately after
integration. Keep source frozen while a runner executes; don't claim diagnostic runs as acceptance.

Next: final source commit, complete all required checks, fresh whole-Area Standards/Spec/security
rechecks against c7b4b1bbfed73013a21c51bea90b4d2537bae83d...HEAD including every #91 commit, fix/answer
findings, prepare PR body via pr skill, final report with exact counts/subjects/excerpts, then send
that same report to the authorized coordinator. Never mark the Area shipped or push.

## Ticket #91 final-source checks (2026-10-29, Claude Opus 5.5 continuation)

The third #91 commit carries the r2 review fixes, the ported Legacy form styles and the browser-test
corrections recorded in logs/ticket-91/review-resolutions.md. On that source: backend r19 (locked
restore, zero-warning Release build, format, unit 698/698, integration 628/628, guardrails 6/6,
both pending-model checks), frontend r37 (lint, format, Vitest 283/283, build checks, generated
types unchanged, production build, npm audit) and browser r36 (e2e 106/106; e2e:verify-guards
verified all eight deliberate failures) passed. Run browser suites under caffeinate: a sleeping Mac
froze one run. Remaining: fresh whole-Area security/Standards/Spec rechecks on the final commit,
the final report and the owner PR body. No push, PR or issue closure.


## Ticket #91 final review corrections (2026-10-29, GPT Sol 6.1 xhigh)

On frozen 7fbbca5, fresh whole-Area r4 security found no demonstrable defect; Spec supported
functional/browser acceptance with P8 (incorrect coverage attribution). Standards found S18–S20:
loop-driven assertions, unnamed mailbox controls and missing visual-parity title prefixes. Original
reports are preserved under logs/ticket-91/reviews/. Opus r3 hit its provider quota without a final
report; r4 used the explicitly permitted fresh GPT Sol 6.1 xhigh sessions.

The follow-up changes only tests and documentation. Static assertion loops are explicit cases,
GET/HEAD document assertions are separate, ProfilePage names resend/confirm controls, and visual
helper tests use the required title prefix. The raw-locator suppression count falls from 137 to 129;
it was not raised. The coverage inventory now names the actual HTTP, provider, service/issuance and
Vitest seams. P8 and S18–S20 responses are in logs/ticket-91/review-resolutions.md.

Next: final-source gates (r49 backend/frontend/models, r50 browser/guard probes), fresh whole-Area
rechecks including this test/documentation commit, PR body and completion report under logs/ticket-91/.
Production application source remains 7fbbca5. Do not reuse the r36 browser result as execution of
later changed test source. No push, PR, issue closure, merge, tag, branch/worktree or shipped claim.


## Ticket #91 C# readability follow-up (2026-10-29, GPT Sol 6.1 xhigh)

On fe7abc6, r49 frontend/backend/models all passed: unit 698, integration 628 (14m12s), six guardrails,
283 Vitest, 15 build checks, zero-warning/error Release build and both pending-model checks. Browser
r50 is running on the unchanged production/browser source; its output is logs/ticket-91/e2e-r50.log.
The runner's final command is e2e:verify-guards. Do not start Docker integration while it is active.

Fresh whole-Area r5 security and Spec reported no remaining actionable finding. Standards confirmed
S18–S20 but found S21 inherited assertion loops in SignInReturnUrlTests, SelfServiceScopingTests and
CsrfAndSignOutTests. The parent fixed these: two target tables become 34/18 theory cases, malformed/
expired cases are explicit, scoping reads/assertions name each account, and bounded redirect/budget
transport loops return observed results for assertions. Expected statuses/counts remain. An unused
Legacy recovery-display regex was removed. No production or browser source changed. The integration
total should grow by 50 to 678; only execution can confirm it.

Backend-fast r51b passes on this source: locked restore, zero-warning/error Release build, format,
698 units, six guardrails. The earlier r51 build caught local names colliding after removing a loop
scope; r51b fixes those names. All diagnostics remain in logs/ticket-91/.

Next: commit this C# test/documentation follow-up; fresh whole-Area r6 reviews including it; finish
r50 browser/guards; then run the full backend/provider suite separately (suggest r52) and final
model/frontend gates. Record actual counts, all commit subject lengths, findings/resolutions and
provenance, complete PR body/final report, send the same report to the authorized coordinator.
No push, PR, closure, shipped claim, excluded-directory access or broad Docker cleanup.


## Ticket #91 r6 corrections (2026-10-29, GPT Sol 6.1 xhigh)

Fresh Claude Opus 5.5 high r6 reviews identify one reliability defect and one owner coverage gap,
with no production security defect. Expanded return-target theories overlapped other classes' frozen
credential budgets. Their clients now use the dedicated 2001:db8:91:: range; no limiter/fixture change.
A separate Fact owns failed passkey/retry assertions instead of repeating them in all 18 cases.
Comments accompany encoded-target data, names/summary arguments are clearer, and two inherited
unit assertion loops use Assert.All. The three fixed endpoint helper reads remain explicit (H4
judgment declined with the response in the ledger).

AccountRevokersTests adds the requested genuine HTTP role race for memory and Redis: password POST,
admin EditUser POST, factor POST refusal without session/cookie authority or factor spending, then
fresh sign-in with the new role/project. No production route/service replacement. The service-level
within-request issuance pin remains. Security r6 corrects Spec's suggested omission of LastTotpStep:
SessionCookieValidation and RequireCurrentProofAsync refuse the retired version before spending it.

Backend-fast r53 passes locked restore, zero-warning/error Release build, format, unit 698 and six
guardrails. Expected integration discovery is now 681 (the added failure/retry Fact plus both real
HTTP race variants); runtime proof is still pending. Browser r50 is at least 94/106 on unchanged
production/browser sources, followed by all eight guard probes. Don't overlap integration Docker.

Next: commit the r6 test/doc changes, fresh whole-Area r7 reviews including every commit and full
#83/#91/CONTEXT/ADR sources, complete r50/guards, then full backend r54 with final frontend/models.
All logs/reports/PR body remain under logs/ticket-91; send final coordinator report only when done.
No push, PR, closure, shipped claim, excluded-directory access or broad Docker cleanup.


## Ticket #91 final selector/documentation corrections (2026-10-29, GPT Sol 6.1 xhigh)

Browser r50 passed 106/106 in 50.8 minutes, and all 8 browser-health guard failures were detected
on purpose; both runs removed their own stacks and images. Backend r54 then ran separately: 681/681
integration tests with none failed or skipped (12 minutes), plus 698 unit tests, 6 guardrails, a
locked restore, a Release build with 0 warnings or errors, and format. This proves both real-HTTP
role-race fixtures. Frontend and model checks r54 followed that backend run.

Security r7 (Claude Opus 5.5 high) found no actionable defect. The Opus r7 Standards and Spec tasks
failed on quota, so fresh GPT Sol 6.1 xhigh r8 reviews ran instead. They found two Low items: R8-1,
two raw avatar-picker selectors despite the existing page-object field, and SP-3, recovery guidance
that said a Redis failure never commits. Both are corrected in 2ef5787: the two scenarios reuse
avatarFile, and the guidance distinguishes session_store_unavailable, changed_sign_in_required and
email_request_incomplete. No C# or application source changed after the 681-pass backend run.

## Ticket #91 final run and reviews on 2ef5787 (2026-10-30, Claude Opus 5.5 continuation)

Browser r56 on 2ef5787 stopped when Docker Desktop exited mid-run; it proves nothing either way.
Its own leftover stack was removed by name, and r58 reruns the full suite under caffeinate. Final
r9 rechecks (Claude Opus 5.5 high, one fresh task per axis) cover 0ff55d2...2ef5787 on top of the
whole-Area r7 and r8 reviews. Standards r9 confirmed R8-1 fixed and raised two documentation items,
both fixed: this section's spacing, and naming the 503 status for changed_sign_in_required.
Remaining: the r58 result, the security and Spec r9 reports, the final report and the PR body
under logs/ticket-91, and the coordinator report. Never push, open a PR, close #91 or mark the Area
shipped.
