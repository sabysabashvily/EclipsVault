# Profile browser acceptance at cutover

Ticket #89 stages components without changing server or Angular route ownership. Its component
and HTTP tests do not prove production browser parity. Ticket #91 must activate the complete Area
with #90's credential controls, then implement and execute this inventory using page objects and
`test` from `e2e/fixtures.ts`. Extra contexts come from `browserHealth.newContext()`.

- Open Profile through both shells, account menus, palettes, My access, Sessions, Data and Security.
  Check `/Profile`, `/Profile/Index`, `/Profile/ChangePassword` and mixed-case bookmarks. Verify the
  three page titles after recovery display is included. No unsafe Legacy write URL remains.
- `visual parity:` compare the cards, fields, labels, icons, order, avatar and password meter in
  both themes. Differences are separate display-name/email actions, accepted security flows,
  compromise-based advice and explicit UTC passkey dates. Check narrow-screen layout.
- Save a display name; check the shell and account menu update. Upload JPEG and PNG; reject wrong
  content, over-4-MiB files, over-6-MiB requests, excess dimensions and incomplete decodes. Verify
  cropped metadata-free 256×256 PNG, generated seeds, Remove confirmation and Legacy Users reads.
- Verify password minimum/maximum, mismatch, unchanged and breached refusals, show/hide and advisory
  meter. Use a disposable account for the real password change; verify retained current browser and
  refusal of the copied old cookie and a second context. Refresh the new antiforgery state before
  another write. Never reuse a revoked saved suite session.
- Exercise authenticator, recovery and UV passkey proof choices on frozen intent, wrong proof,
  expiry, cancellation and duplicate clicks. Cancel defaults, Escape, Tab/Shift+Tab focus cycle and
  opener restoration must work; destructive confirmation closes before verification opens.
- Capture actual SMTP for email request/resend/confirm/cancel. Keep active email unchanged until
  activation. Check expired grant re-verification, wrong/expired/replaced code, resend limits and
  SMTP unavailable/failure. Confirm exactly once; check retained session and second-context refusal.
- Verify all sensitive inputs clear on Cancel, navigation, pagehide, back-forward restoration and
  session loss. Aborted UV prompts cannot fall back or complete later. No secret appears in storage,
  URL, console, application logs, notification history or audit details.
- Reset the authenticator with real account proof; confirm current/copied/other sessions end and
  Legacy sign-in shows its one-shot notice. Local TOTP enrollment follows sign-in policy; existing
  passkeys and SSO remain usable. A crafted sign-in link cannot claim a reset happened.
- Replace recovery codes after TOTP enrollment and verification. Check exactly ten codes, Print,
  Done, refresh and back clearing, including hidden mailbox/factor inputs. A direct or refreshed
  `/Profile/RecoveryCodes` visit returns to Profile with the one-time-display explanation. Lost
  replies and postcommit renewal failures never recover plaintext or replay generation.
- Add, verify with and remove UV virtual passkeys. Check frozen names, UTC dates, browser dismissal,
  the server's two-minute expiry, late completion after departure/session loss, stale capabilities
  and final-key refusal when no proven surviving method remains. Exercise keyboard and both themes.
- Induce known committed/session-renewal failure and unknown response loss: never replay a write.
  Exercise successful, revoked-unrecorded and incomplete account-wide sign-out. Under real store
  outage use the anonymous token/local-cookie fallback and retain its warning into Legacy sign-in.
- Run the complete browser suite and all eight deliberate browser-health guard probes against the
  production image with CSP, Trusted Types, Secure cookies and real authentication active.

## Coverage at cutover (#91)

The final report records executed results for each seam below. Source coverage alone is not
runtime acceptance. Browser tests exercise page behavior and real round trips. A real HTTP test against the
Production SQL Server host (or the Redis/SMTP fixtures) is used where the browser can only reach a
failure by inventing state, timing a race or pausing infrastructure. Vitest covers page-only state
that a browser cannot observe from outside. No pre-cutover browser run is acceptance evidence for the new Profile pages.

Browser, `e2e/profile.spec.ts` through production routes, the production image and browser health:

- Entry from both account menus, both palettes and Security checkup; mixed-case `/Profile/Index`,
  `/Profile/ChangePassword` and `/Profile/RecoveryCodes`; titles; signed-out `ReturnUrl`. The
  My activity, Sign-in history, Signed-in devices and Security checkup links are in `sign-in.spec.ts`
  and `sessions.spec.ts`.
- `visual parity:` card order, field states, Access terms, Security row titles and order, two-column
  and narrow layout, the danger action in both themes, and the Change password fields, hint and meter.
  The Legacy views no longer exist, so the expectations come from their markup at `c7b4b1b`;
  screenshots are evidence, not baselines.
- Display name and live shell; PNG upload, `no-store` read and Remove; a non-image refused.
- Password mismatch, unchanged and breached refusals before any write, show/hide and meter; a real
  change that keeps the current browser and refuses its copied cookie.
- Real SMTP request and confirm; a wrong mailbox code refused; cancel keeps the active email; one
  verification request however often Verify is clicked.
- Ten recovery codes, Print, Done, refresh, back and forward, and the one-time explanation.
- A UV virtual passkey added, used as proof and removed, with UTC dates and copied-cookie refusal.
- Authenticator reset ending current and copied sessions, the one-shot notice and Legacy reenrollment.
- Cancel default, Escape, Tab/Shift+Tab inside the dialog, opener focus and cleared proof fields.
- Wrong account proof and a server-expired operation, with no mutation request.
- Sign out everywhere ending this and copied sessions, then signing in again.
- A reply lost after a committed password change: one request, the unknown-outcome message, the page
  blocked, the copied cookie refused and the new password accepted. Nothing is replayed.

Real HTTP (`EclipsVault.IntegrationTests`), where the browser cannot honestly reach the outcome:

- Avatar file/multipart section limits and cross-account reads: `ProfileApiContractTests`,
  `RetiredProfileRoutesTests`. The decoder boundary is also exercised by `SkiaAvatarProcessorTests`
  (unit tests), rather than being mislabelled as HTTP coverage.
- Resend limits, expired and retired codes, SMTP unavailable and real SMTP failure: `MailboxTests`,
  `MailboxAuditFailureTests`.
- Final-key and surviving-method refusal, the two-minute prompt expiry, replaced and late prompts:
  `ProfilePasskeyTests`, `ProfileFinalConsumeTests`, `PendingProofTests`.
- `revoked_unrecorded`, `sign_out_incomplete` and the anonymous local-cookie branch during a real
  Redis outage: `ProfileSessionOutcomeTests`, `LocalSignOutTests`, `RedisSessionHttpTests`.
- Store failure before and after a password commit: `RedisSessionHttpTests`.
- Role and issuance races, concurrent proofs: `AccountChangeRaceTests`, `ProfileVerificationTests`.
- Retired Legacy writes in any case and method: `RetiredProfileRoutesTests`.

Vitest: `pagehide`, back-forward restoration and session-loss clearing (`profile-writes.spec.ts`),
recovery codes kept out of storage, cookies and the URL (`recovery-codes.spec.ts`), passkey prompt
dismissal and abort (`passkey-prompts.spec.ts`, `verification-dialog.spec.ts`), and refusal to
generate codes without an authenticator.
