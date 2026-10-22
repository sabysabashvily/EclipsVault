**The alias-collision claim needs qualification: Web validation prevents it; Core and database constraints permit it.**

1. **Username equal to another account’s email**

   **Fact:** Web usernames exclude `@`, and `AdminController` rejects invalid models before calling Core. Profile email validation requires `@`. Therefore ordinary Profile changes and the shipped administrator creation form cannot produce this overlap. See [AdminViewModels.cs:34](../../../../EclipsVault.Web/Models/AdminViewModels.cs:34), [AdminController.cs:55](../../../../EclipsVault.Web/Controllers/AdminController.cs:55), [ProfileService.cs:168](../../../../EclipsVault.Core/Application/Profile/ProfileService.cs:168).

   **Fact:** Core creation only trims/checks minimum username length and checks existing **usernames**. Separate username/email unique indexes impose no cross-column exclusion. Thus a direct Core caller can create `A.Username = owner@example.test` while `B.Email = owner@example.test`. This requires bypassing Web validation through a custom caller or privileged data provisioning; **even the trusted administrator’s normal form rejects it**. See [UserAdminService.cs:76](../../../../EclipsVault.Core/Application/Users/UserAdminService.cs:76), [UserConfiguration.cs:14](../../../../EclipsVault.Infrastructure/Persistence/Configurations/UserConfiguration.cs:14).

   **Conditional concrete result:** the combined lookup uses unordered `FirstOrDefault(Username == identifier || Email == identifier)`, with no email preference. A verified IdP assertion for B’s mailbox can consequently resolve to A. Trusted/asserted IdP MFA permits a full A session; otherwise A’s local MFA/enrollment flow applies. Password login instead checks the selected account’s password—ambiguity alone does not bypass it. See [UserRepository.cs:20](../../../../EclipsVault.Infrastructure/Persistence/Repositories/UserRepository.cs:20), [SsoSignInService.cs:46](../../../../EclipsVault.Core/Application/Sso/SsoSignInService.cs:46), [AccountController.cs:433](../../../../EclipsVault.Web/Controllers/AccountController.cs:433), [VaultAuthenticationService.cs:65](../../../../EclipsVault.Core/Application/Authentication/VaultAuthenticationService.cs:65).

   This is a conditional account-selection defect, **not a demonstrated ordinary-user provisioning exploit**. Malicious administrator/operator activity is outside the documented threat boundary ([THREAT_MODEL.md:85](../../../THREAT_MODEL.md:85)).

2. **Actual email case behavior**

   **Fact:** Profile trims email but preserves case; lookup uses database equality; uniqueness indexes the original email column.

   PostgreSQL creates ordinary `character varying(256)` and a unique email index, without `citext`, a case-folded expression or an explicit collation. Under ordinary deterministic PostgreSQL collation, `Alice@example.test` and `alice@example.test` can coexist and resolve separately. An IdP casing difference can therefore produce no match. See [InitialPostgres.cs:183](../../../../EclipsVault.Migrations.Postgres/Migrations/20260716154841_InitialPostgres.cs:183), [InitialPostgres.cs:524](../../../../EclipsVault.Migrations.Postgres/Migrations/20260716154841_InitialPostgres.cs:524).

   SQL Server creates `nvarchar(256)` without an explicit column collation; its unique index and equality inherit that collation. A case-insensitive deployment matches/rejects case variants; a case-sensitive deployment distinguishes them. See [InitialCreate.cs:82](../../../../EclipsVault.Infrastructure/Migrations/20260709175152_InitialCreate.cs:82), [AddUniqueEmailIndex.cs:13](../../../../EclipsVault.Infrastructure/Migrations/20260709185006_AddUniqueEmailIndex.cs:13).

   **Neither live collation was inspected.**

3. **Narrow SSO repair versus shared normalization**

   **Recommendation, unaccepted:** an email-only lookup used by SSO can remove username matching without changing username comparison or the OIDC protocol. Email-only lookup and case normalization are separate decisions.

   Changing the existing combined lookup has wider consequences: password login, named passkey options and sharing also call it. See [PasskeyService.cs:150](../../../../EclipsVault.Infrastructure/Security/WebAuthn/PasskeyService.cs:150), [SecretGrantService.cs:50](../../../../EclipsVault.Core/Application/Secrets/SecretGrantService.cs:50). These callers retain ambiguity if persisted email-shaped usernames exist.

4. **Constraints if case-insensitive email identity is chosen**

   **Conditional recommendation, unaccepted:**

   - Define one canonicalization policy and apply it consistently to email lookup, provisioning, bootstrap and Profile activation.
   - Enforce uniqueness on that canonical identity in both databases, with comparison semantics matching lookup. Query-time folding plus the existing raw-column index is insufficient.
   - Backfill/check **all existing accounts**, including disabled accounts. **Fail migration on canonical collisions** and require explicit resolution; never merge accounts or silently select an owner.
   - Keep activation race-safe through database uniqueness and explicit conflict handling.
   - For combined lookup, establish disjoint persisted username/email namespaces or refuse matches to different account IDs. Username normalization is unnecessary for either approach.

Source-only review completed. No edits, tests, branches, issues, further agents or reads of either excluded directory.
