# Profile security changes require fresh proof of an existing credential

A stolen session must not be able to install a lasting sign-in method. Changing email, password,
authenticator enrollment, recovery codes or passkeys therefore requires account verification using
existing account credentials, separately from the step-up used for secret reveals. Display-name and
avatar edits keep their existing behavior; signing out everywhere remains available without this
additional proof.

Accepted proof is the current password plus TOTP, the current password plus an unused recovery
code, or an already registered passkey with user verification. Password changes also retain the
current-password requirement. Every proof is checked against the account from the session; a valid
passkey belonging to another account cannot authorize the change. TOTP and recovery-code consumption
is atomic and shared with sign-in and secret step-up, so concurrent requests cannot reuse a proof.

Verification authorizes one specific operation and its intended data, not a grace period for every
security setting. Authorization stays server-side, is bound to the account, session and credential
generation, expires five minutes after verification, and is consumed once. A different operation or
changed data needs new verification; a recent sign-in or secret step-up does not supply it.

The UI API prepares and freezes typed intended data on the server before shared verification. Each
final action has a typed endpoint that commits that prepared operation. An operation identifier is
bound to its owning session and supplies no authority by itself. Preparation has an absolute expiry,
a bounded number of active operations and shared admission limits. Cheap input checks and hashing
concurrency limits apply before preparing a candidate password hash; preparation cannot become an
unlimited hashing or pending-state allocation endpoint. Plaintext passwords are not retained.

Actual verification attempts share the configured source-IP budget and account lockout across
methods. Profile cannot provide a separate allowance or bypass an existing lockout. Failure counting
must remain correct under concurrency, and complete verification is required before failures reset.
Cancelling a browser passkey prompt is not a wrong credential; signing out remains available.

Authenticator reset keeps the existing reset, sign-out and enrollment-at-sign-in flow. Retirement of
the old authenticator and recovery codes is atomic with its audit event, and existing sessions and
affected pending ceremonies are invalidated. Losing every existing proof requires administrator
recovery rather than a session-only bypass, where the existing recovery flow can establish access.
An in-place replacement ceremony in Profile was rejected to keep the migration within its existing
page boundary.

Reset retires TOTP, its replay marker and its recovery codes, while retaining unrelated passkeys and
SSO. Enrollment occurs when a subsequent sign-in requires local TOTP; retained passkeys and trusted
IdP MFA remain usable alternatives. Reset is unavailable without an enrolled authenticator, and
recovery-code generation requires TOTP enrollment. Availability and its explanation come from the
server.

Removing the last passkey requires proof of a different local method that remains usable afterward.
The key being removed cannot establish its own fallback, and a recovery code spent during that
proof cannot itself count as the surviving method. A stored password hash or an existing SSO
session does not establish that the person can use another method.

The deletion transaction rechecks the surviving method, serialized with recovery redemption. A
different request consuming the final remaining recovery code after verification must not permit
the last passkey to disappear with no proven usable fallback.

Passkey ceremonies enforce the advertised two-minute deadline on the server. Challenges are
single-use, bound to their purpose and session, and to the account and credential generation where
known. Consumption is atomic. Starting another attempt replaces the previous attempt for that
session and purpose; expiry and cancellation have explicit UI outcomes.

A successful password change keeps the verified browser signed in with a genuinely fresh session,
while invalidating prior sessions. A future authentication timestamp is not a substitute for a new
session: it can escape an account-wide revocation that happens before that future instant.

Recovery generation keeps ten codes, the one-time display, Print and Done. The response delivers
plaintext directly to the current page, which retains it only in memory and clears it on departure
or session loss. Cookies, browser storage and URLs do not carry the codes. A refresh or direct visit
returns to Profile; generation is never retried automatically, and a lost response requires explicit
regeneration. This gives up recovery of the original response rather than retaining a retrievable
copy of sign-in material.

The security repairs apply narrowly to the shared persistence and authentication mechanisms Profile
relies on, with regression coverage through affected Legacy callers. Database changes and their
audit events follow ADR-0021; ordinary Profile edits must not overwrite unrelated security state,
and recovery codes must remain single-use under concurrency. The sign-in and enrollment pages stay
in the Legacy UI until Area 8. Keeping separate fixes only in the new UI API would leave another
caller able to violate the same account guarantees.

Account verification records verification events rather than pretending a new sign-in happened.
Each mutation has one intended semantic audit event, including a multi-row recovery-code replacement
or authenticator reset. Replay and lockout bookkeeping must not become Profile edits. Verification
requirements for the entire intended action pass before failure counts reset, including the current
password check when a passkey authorizes a password change.

Using a recovery code for Profile verification records that purpose; it does not emit a recovery
sign-in event or establish a successful sign-in location.

Every completed sensitive change attempts a notification to the existing trusted address; email
changes use the old address captured before activation. Notification delivery is best-effort and
failure is recorded without secrets. It cannot undo a committed credential change. Mailbox ownership
confirmation remains a separate requirement under ADR-0024.

An SSO session alone is not fresh account verification. If the person has no usable local proof,
sensitive Profile changes remain unavailable with an explanation, while ordinary vault use remains
available. Area 3 adds no new OIDC verification protocol. The existing administrator action resets
TOTP only; it cannot reset a forgotten password. Recovery for an account with no usable proof needs
a separately designed path, not an implied existing administrator feature.
