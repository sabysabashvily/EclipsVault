Reviewed `c7b4b1b` as a fresh same-model fallback using the security-reviewer instructions. This was a read-only source survey: no files changed, excluded directories accessed, tests run, or further agents spawned. The scenarios below are deductions from the code, not runtime reproductions.

1. **[P1] Profile database writes are not atomic with their audit events.**

   The interceptor injects events for `Secret`, `TrustedNetwork`, `DynamicSecretLease` and `SecretGrant`; it has no rules for `User`, `UserAvatar`, `MfaRecoveryCode` or `PasskeyCredential. See [AuditSaveChangesInterceptor.cs:130](../../../../EclipsVault.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs:130).

   Profile details, password changes, avatar changes and MFA reset save first, then audit separately ([ProfileService.cs:81](../../../../EclipsVault.Core/Application/Profile/ProfileService.cs:81), [120](../../../../EclipsVault.Core/Application/Profile/ProfileService.cs:120), [141](../../../../EclipsVault.Core/Application/Profile/ProfileService.cs:141), [160](../../../../EclipsVault.Core/Application/Profile/ProfileService.cs:160)). Recovery generation and passkey addition/removal do likewise ([MfaRecoveryService.cs:64](../../../../EclipsVault.Core/Application/Mfa/MfaRecoveryService.cs:64), [PasskeyService.cs:136](../../../../EclipsVault.Infrastructure/Security/WebAuthn/PasskeyService.cs:136), [266](../../../../EclipsVault.Infrastructure/Security/WebAuthn/PasskeyService.cs:266)).

   **Concrete result:** reject audit inserts while permitting ordinary writes. A new password or passkey persists, or an existing passkey/recovery set disappears, despite the request reporting failure. Recovery generation can invalidate the previous set without returning the new plaintext. These are database changes, so ADR-0021 requires atomicity; its session-store exception does not apply. There are no interceptor-protected Profile fields to distinguish from these separate semantic events.

2. **[P1] A stolen session can create a persistent passwordless credential.**

   Registration requires the current session and antiforgery token, but no current password, existing-factor assertion or freshness check ([ProfileController.cs:250](../../../../EclipsVault.Web/Controllers/ProfileController.cs:250)). User verification applies to the **new** authenticator ([PasskeyService.cs:108](../../../../EclipsVault.Infrastructure/Security/WebAuthn/PasskeyService.cs:108)).

   **Concrete result:** an attacker holding a copied full-session cookie fetches their own antiforgery token for that session, registers their authenticator, and later signs in after the stolen session is revoked. Passkey sign-in grants a full session without the victim’s password or TOTP ([AccountController.cs:259](../../../../EclipsVault.Web/Controllers/AccountController.cs:259)). Verification by the attacker’s new authenticator does not prove possession of an existing account credential.

   Email changes have a related, configuration-dependent consequence: Profile can change the email without credential proof, while SSO matches accounts by that mutable email. With trusted IdP MFA enabled, changing it to the attacker’s verified IdP address provides another persistent sign-in route ([ProfileService.cs:81](../../../../EclipsVault.Core/Application/Profile/ProfileService.cs:81), [SsoSignInService.cs:46](../../../../EclipsVault.Core/Application/Sso/SsoSignInService.cs:46)).

3. **[P1] An ordinary Profile save can restore stale credentials or security state.**

   `UserRepository.UpdateAsync` calls `DbSet.Update`, marking the whole user row for update; avatar writes do the same ([UserRepository.cs:45](../../../../EclipsVault.Infrastructure/Persistence/Repositories/UserRepository.cs:45), [78](../../../../EclipsVault.Infrastructure/Persistence/Repositories/UserRepository.cs:78)). Neither the entity nor its mapping defines a concurrency token ([User.cs:9](../../../../EclipsVault.Core/Domain/Entities/User.cs:9), [UserConfiguration.cs:9](../../../../EclipsVault.Infrastructure/Persistence/Configurations/UserConfiguration.cs:9)).

   **Concrete interleaving:** request A loads a user for a display-name/avatar edit; request B changes the password, resets MFA, disables the account or advances `LastTotpStep`; A saves afterward. A writes its old password hash, MFA state, enabled state and replay marker back over B’s changes. The Profile audit event describes an innocuous edit while security state is reverted.

4. **[P1] MFA reset leaves copied and other sessions live, allowing recovery credentials to survive re-enrollment.**

   Reset clears TOTP and deletes codes, then clears only the requesting browser’s authentication cookie. It never invokes either revocation service ([ProfileService.cs:155](../../../../EclipsVault.Core/Application/Profile/ProfileService.cs:155), [ProfileController.cs:201](../../../../EclipsVault.Web/Controllers/ProfileController.cs:201)). Cookie validation checks account existence and `IsDisabled`, not `TotpEnabled` ([Program.cs:98](../../../../EclipsVault.Web/Program.cs:98)).

   **Concrete result:** a copied cookie—even a copy of the resetting session—continues working after reset. It can generate recovery codes while TOTP is disabled because generation only checks that the user exists ([MfaRecoveryService.cs:39](../../../../EclipsVault.Core/Application/Mfa/MfaRecoveryService.cs:39)). Those codes become usable when the owner completes new TOTP enrollment, which does not replace recovery codes ([VaultAuthenticationService.cs:184](../../../../EclipsVault.Core/Application/Authentication/VaultAuthenticationService.cs:184)). An attacker with the password can then bypass the replacement authenticator.

   Reset also commits TOTP removal and recovery-code deletion separately, creating a partial-reset outcome if the second save fails.

5. **[P2] Password renewal can escape a subsequent account-wide revocation.**

   Password change preserves the session ID and sets `AuthTime` to the next Unix second ([ProfileController.cs:177](../../../../EclipsVault.Web/Controllers/ProfileController.cs:177), [302](../../../../EclipsVault.Web/Controllers/ProfileController.cs:302)).

   **Concrete timing:** password change at `1000.2` produces `AuthTime=1001`; “Sign out everywhere” at `1000.8` stores a revocation before `1001` and creates no session-ID tombstone ([ProfileController.cs:239](../../../../EclipsVault.Web/Controllers/ProfileController.cs:239)). A copy of the renewed cookie remains accepted afterward: both revocation implementations reject only timestamps at or before the marker ([InMemorySessionRevocationService.cs:29](../../../../EclipsVault.Infrastructure/Security/Defense/InMemorySessionRevocationService.cs:29), [RedisSessionRevocationService.cs:52](../../../../EclipsVault.Infrastructure/Security/Defense/RedisSessionRevocationService.cs:52)).

   Preserving the session ID alone does **not** keep an ordinary pre-change cookie valid; its old `AuthTime` is rejected. The demonstrated problem is the future timestamp.

6. **[P2] Change-password supplies an unthrottled current-password guessing endpoint.**

   An incorrect current password writes `LoginFailed`, but does not increment/check account lockout state ([ProfileService.cs:90](../../../../EclipsVault.Core/Application/Profile/ProfileService.cs:90)). The authentication throttle decorates `AccountController`, not `ProfileController` ([AccountController.cs:24](../../../../EclipsVault.Web/Controllers/AccountController.cs:24), [ProfileController.cs:19](../../../../EclipsVault.Web/Controllers/ProfileController.cs:19)).

   **Concrete result:** a stolen session can repeatedly submit candidate current passwords with a valid replacement password, receiving a distinct wrong-password result without exhausting either the Account IP budget or account lockout. Audit visibility does not impose a guessing limit.

7. **[P2] Recovery-code replacement and consumption have concurrency gaps.**

   Replacement deletes the rows observed by a query, then inserts ten new rows in one `SaveChanges` ([MfaRecoveryCodeRepository.cs:21](../../../../EclipsVault.Infrastructure/Persistence/Repositories/MfaRecoveryCodeRepository.cs:21)). That individual replacement is transactional; the defect is concurrent replacement.

   **Concrete interleaving:** two first-generation requests both query an empty set, then each inserts ten codes. Both sets remain valid, contradicting replacement semantics.

   At the shared redemption seam, two requests can both read the same unused code before either marks it used. `MarkUsedAsync` performs an unconditional update, with no concurrency token or `UsedAtUtc IS NULL` condition ([VaultAuthenticationService.cs:126](../../../../EclipsVault.Core/Application/Authentication/VaultAuthenticationService.cs:126), [MfaRecoveryCodeRepository.cs:29](../../../../EclipsVault.Infrastructure/Persistence/Repositories/MfaRecoveryCodeRepository.cs:29)). Both can complete sign-in using one “single-use” code. This is a shared backend dependency relevant to Profile recovery, not an Area 8 migration recommendation.

8. **[P2] Passkey registration state is not bound to its initiating account or an absolute expiry.**

   Begin stores only a challenge string. Complete retrieves it and supplies the **current** user ID ([ProfileController.cs:252](../../../../EclipsVault.Web/Controllers/ProfileController.cs:252)).

   **Concrete result:** begin as account A, retain its registration response, log out, sign in as B in the same browser, and complete using B’s antiforgery token. Logout and sign-in do not clear ceremony state; the service stores the credential under B ([AccountController.cs:345](../../../../EclipsVault.Web/Controllers/AccountController.cs:345), [PasskeyService.cs:125](../../../../EclipsVault.Infrastructure/Security/WebAuthn/PasskeyService.cs:125)).

   The advertised 120-second timeout is only sent to the client; the server checks no creation timestamp, so a response submitted after three minutes can still pass within the six-minute session idle lifetime ([PasskeyService.cs:66](../../../../EclipsVault.Infrastructure/Security/WebAuthn/PasskeyService.cs:66), [Program.cs:300](../../../../EclipsVault.Web/Program.cs:300)). Consumption is a separate session read/remove, not an atomic consume operation. Sequential replay is rejected; concurrent completion needs explicit protection.

9. **[P2] Profile “Sign out everywhere” never writes an audit event.**

   The action revokes, writes an ordinary application log, clears the cookie and reports success ([ProfileController.cs:239](../../../../EclipsVault.Web/Controllers/ProfileController.cs:239)). Thus even healthy-store revocations have no audit row. ADR-0021 permits revocation to stand when auditing fails; it still requires attempting the audit and reporting an unrecorded outcome, as the existing sessions API does ([SessionsController.cs:64](../../../../EclipsVault.Web/Controllers/Ui/SessionsController.cs:64)).

The existing boundary controls are present: inherited session authorization, global antiforgery validation, caller-derived ownership for writes, and owner-scoped passkey removal. Password checks screen the replacement server-side; recovery codes persist as salted hashes; avatar uploads are size/format/dimension checked and re-encoded. Recovery plaintext is carried through TempData and rendered in HTML covered by `no-store` ([ProfileController.cs:215](../../../../EclipsVault.Web/Controllers/ProfileController.cs:215), [SecurityHeadersMiddleware.cs:34](../../../../EclipsVault.Web/Middleware/SecurityHeadersMiddleware.cs:34)).

Recommended design questions before implementation:

- Which existing credential must be proved for email changes, MFA reset, recovery generation and passkey addition/removal—including passkey-only and SSO users?
- How will database mutations and their precise audit events commit atomically, with field-specific updates and concurrency enforcement?
- Can durable credential/session generations replace timestamp arithmetic? Password persistence currently precedes external revocation, so a revocation-store failure can leave the new password committed while old sessions become usable again after recovery.
- Will ceremony and recovery-display state be bound to user, session and absolute expiry, with atomic consumption? TempData consumption alone should not be assumed to provide replay-resistant, one-time disclosure.
