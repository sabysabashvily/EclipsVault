Fresh same-model fallback because Claude was rate-limited. Reviewed `c7b4b1b` and the uncommitted design documents. These are source-based scenarios, not runtime reproductions. No edits, tests, further agents, or access to either excluded directory.

1. **[P1] Fence revocation-version issuance against stale authentication results.**

   [ADR-0021:19](../../../adr/0021-removing-access-and-fail-closed-auditing.md:19) says new sessions receive the current version but does not specify capture ordering. Concrete race: authentication loads TopSecret attributes; an administrator demotes the account and revokes its sessions; issuance then fetches the new version and attaches it to the old attributes. Credential generation alone does not prevent this role-change race. Existing issuance accepts an already-produced `UserDto` ([AccountController.cs:448](../../../../EclipsVault.Web/Controllers/AccountController.cs:448)); role changes depend on external revocation ([UserAdminService.cs:182](../../../../EclipsVault.Core/Application/Users/UserAdminService.cs:182)).

   **Recommendation:** capture the revocation version before loading the account snapshot used for authentication, carry it with the original credential generation through verification and issuance, and reject changed versions. A replacement session must not acquire a later version merely because issuance happens later. Preserve original versions during sliding expiration, display/avatar renewal and secret step-up.

   Migrate every existing account-wide caller: role change, disable, forced logout and deletion (`UserAdminService.cs:189,226,244,290`), plus [intrusion response:46](../../../../EclipsVault.Infrastructure/Security/Defense/IntrusionResponseService.cs:46). Preserve per-device tombstones.

   **Another owner decision: no.** This is required to preserve existing revocation guarantees.

2. **[P1] Missing-state rejection does not protect against restored, populated Redis state.**

   Suppose cookie version A is revoked by replacing A with B. Restoring Redis from a snapshot containing A makes that cookie match again; credential generation has not changed. The accepted missing-state rule never runs. Redis persistence can lose recent writes during recovery, including with its usual once-per-second AOF synchronization. [Redis persistence documentation](https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/)

   **Recommendation:** initialization must atomically issue a non-repeating version; validation must never recreate missing state from a cookie. Expiry, eviction, memory restart and metadata cleanup must leave old versions invalid. Separately, require a coordinated store-incarnation change outside the restored keyspace before serving recovered Redis, or explicitly document the narrower recovery guarantee. Merely retaining the existing TTL machinery is insufficient ([RedisSessionRevocationService.cs:19](../../../../EclipsVault.Infrastructure/Security/Defense/RedisSessionRevocationService.cs:19); [ADR-0023:25](../../../adr/0023-credential-generations-invalidate-old-proofs.md:25)).

   **Another owner decision:** confirm the recovery policy. Non-repeating initialization and fail-closed missing-state handling are already correctness constraints.

3. **[P1] Resolve logout’s outage boundary with a separate, anonymous cookie-clearing path.**

   The acknowledged [proposal:103](../contract.md:103) hole is real: revocation/registry reads occur before the action’s catch ([Program.cs:96](../../../../EclipsVault.Web/Program.cs:96), [111](../../../../EclipsVault.Web/Program.cs:111); [AccountController.cs:354](../../../../EclipsVault.Web/Controllers/AccountController.cs:354)). `[AllowAnonymous]` alone would not prevent default authentication middleware from attempting authentication. [ASP.NET Core authentication middleware](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Security/Authentication/Core/src/AuthenticationMiddleware.cs#L53)

   **Concrete narrow arrangement:** retain fully validated, session-only Profile endpoints and account-wide revocation. Add an exact-route terminal branch, outside `/api/ui`, before store-backed blacklist/session/authentication middleware:

   - GET issues only an anonymous antiforgery request token, with `no-store`.
   - POST validates that token with an anonymous principal, then deletes the configured authentication cookie using its cookie manager and matching path/domain/chunk handling.
   - It reads no account identity, invokes no revocation service, returns no protected data, and never continues into protected endpoints. After an unsuccessful account-wide attempt, report local clearing with `sign_out_incomplete`, preserving the warning.

   A fresh anonymous token is necessary: an existing authenticated request token contains identity binding and fails anonymous validation. [ASP.NET Core antiforgery validation](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Antiforgery/src/Internal/DefaultAntiforgeryTokenGenerator.cs#L127) Place the branch before blacklist access too; that is another earlier store read ([IpBlacklistMiddleware.cs:69](../../../../EclipsVault.Web/Middleware/IpBlacklistMiddleware.cs:69)). Explicitly pin this anonymous surface in authorization coverage.

   **Another owner decision: yes**, for this cookie-only exception. No unvalidated principal should authorize account-wide sign-out.

4. **[P2] Bound preparation before it performs password hashing or allocates pending state.**

   [Proposal:21,41–44](../contract.md:41) permits preparing candidate password hashes before verification, but gives preparation no admission limit or active-operation bound. A valid or stolen session could repeatedly create operations without submitting a credential attempt. Each ordinary hash currently uses 64 MiB by default ([Argon2idPasswordHasher.cs:15](../../../../EclipsVault.Infrastructure/Security/Cryptography/Argon2idPasswordHasher.cs:15)).

   **Recommendation:** specify absolute expiry for unverified preparation, bounded active operations and hashing concurrency, shared admission limits, and inexpensive size/validation checks before hashing. Operation IDs remain correlation identifiers; server records must independently enforce account/session/generation/target binding.

   **Another owner decision: no.** Exact limits are implementation choices within the accepted bounded-state requirement.

5. **[P2] Recheck surviving recovery capability when deleting the last passkey.**

   Concrete race: password plus recovery code A authorizes deletion, leaving code B as the surviving fallback. Another sign-in consumes B before deletion commits. Recovery consumption need not advance credential generation, so a generation check alone still permits removal.

   [ADR-0022:38](../../../adr/0022-profile-security-changes-require-account-verification.md:38) already requires a surviving usable method. **Recommendation:** preserve which method established verification and recheck fallback eligibility inside the deletion transaction, serialized with recovery redemption. Password-plus-recovery cannot qualify when that would leave no unused recovery code and no independently proven surviving method.

   **Another owner decision: no.** This resolves the accepted last-recovery edge rather than changing Q16.

6. **[P2] Make failure tests reach the intended phase and real transport.**

   The [verification proposal:119](../contract.md:119) needs explicit fixture sequencing. Enabling audit rejection before account verification makes a mutation test stop during verification; unchanged credentials then prove nothing about mutation rollback. `AuditFailureFixture` rejects every audit insert ([AuditFailureFixture.cs:16](../../../../EclipsVault.IntegrationTests/AuditFailureFixture.cs:16)).

   **Recommendation:** prepare and verify before enabling rejection for mutation/activation tests; separately reject verification transactions and assert rollback of proof consumption, counters and authorization creation. Include concurrent resend/confirmation/fifth-wrong-code cases and assert that no rejected transaction leaves a usable grant.

   SMTP browser coverage also requires a configured disposable capture server: [E2E Compose:31](../../../../EclipsVault.AngularUi/e2e/compose.yml:31) disables email. Keep the real SMTP sender and induce transport failure. Authentication-outage HTTP tests need an isolated real-Redis host; the ordinary fixture uses default in-memory stores.

   The existing [OIDC correlation-cookie technique:311](../../../../EclipsVault.IntegrationTests/SelfServiceScopingTests.cs:311) is suitable for post-assertion callback policy, without claiming IdP protocol coverage. Focused real-PostgreSQL persistence tests fit the proposed boundary; expanding PostgreSQL HTTP coverage is unnecessary.

   **Another owner decision: no.**

The accepted audit and proof rules need no reopening. Pin their less obvious consequences in tests: valid passkey plus wrong current password must not reset failures; Profile recovery verification must not emit the existing sign-in-shaped `RecoveryCodeUsed` event, which currently counts as successful authentication and establishes an IP location ([SignInEventClassifier.cs:36](../../../../EclipsVault.Core/Application/SignInHistory/SignInEventClassifier.cs:36), [SignInHistoryBuilder.cs:40](../../../../EclipsVault.Core/Application/SignInHistory/SignInHistoryBuilder.cs:40)). Verification, mailbox activation and mutation events must retain their distinct, truthful meanings.
