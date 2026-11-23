# Security flows / უსაფრთხოების ნაკადები

Each step names the source it was read from, at `origin/main`
`d576dd93a30bc461183ca7b71c89f038adbff446` (21 November 2026, 22:42 Asia/Tbilisi).
The live journey is flow 3. The others are the answers around it. The disposable Development run
is recorded in [demo-setup-reset.md](demo-setup-reset.md).

ყოველი ნაბიჯი ასახელებს წყაროს, წაკითხულს `origin/main`
`d576dd93a30bc461183ca7b71c89f038adbff446`-ზე (21 ნოემბერი 2026, 22:42 Asia/Tbilisi).
ცოცხალი გზა ნაკადი 3-ია. დანარჩენი მის გარშემო პასუხებია. ერთჯერადი Development გაშვება
[demo-setup-reset.md](demo-setup-reset.md)-შია ჩაწერილი.

## 1. Vault account sign-in

Current. A session does not exist after the password alone. These pages are still Razor. Nothing
in this revision is an invitation flow.

| Step | What happens | Source |
|---|---|---|
| 1 | The password is checked. Failure stays on the sign-in page. | `AccountController.Login`; `Views/Account/Login.cshtml` heading "Sign in", button "Continue" |
| 2 | Success signs an `MfaPending` cookie, not the session cookie. | `AccountController.Login`; `Program.cs` scheme `MfaPending` |
| 3 | First use enrolls an authenticator. A later use asks for a code. A recovery code or a passkey are the other current paths. | `AccountController` redirects to `EnrollTotp` or `Totp`; `SessionIssuer.SignInAsync` call sites for TOTP, recovery, enrollment, and passkey |
| 4 | `SessionIssuer.SignInAsync` issues the session and records the success audit. | `SessionIssuer` |
| 5 | SSO, when enabled, proves identity at the IdP. The vault still decides admission, then uses the same issuer. | `Program.cs` SSO block; `AccountController` SSO sign-in; ADR-0002; `CONTEXT.md` identity provider |

Do not photograph step 3. Enrollment material and recovery codes are out of the talk.

Sign-in, the authenticator page, recovery, and the passkey button are the Razor views under
`Views/Account/`. They are not listed in `AngularUiRoutes`. An invitation type is not in this
revision's source.

## 2. Per-request access

Current. `SecretAccessPolicy.Evaluate` is the rule. `SecretAccessHandler` supplies the subject,
the resource, the window, the network, and any share. An API key adds scope rules that can only
narrow access.

| Check | Rule in code | Talk secret `Phoenix_Dev_Database_Password` |
|---|---|---|
| Clearance | Numeric clearance must be at least numeric sensitivity. Standard is 1, Elevated 2, Secret 3, TopSecret 4. Internal is 1, Confidential 2, Secret 3, TopSecret 4. | Standard is not below Internal. |
| Project | Must match, unless clearance is TopSecret or `IsExplicitlyGranted`. | Account project `PHOENIX`, secret project `PHOENIX`. |
| Time | Production environment requires `IsWithinProductionWindow`. | Environment is Development, so the window is not consulted. |
| Network | Sensitivity at or above Confidential requires `IsFromTrustedNetwork`. | Internal, so the network is not consulted. |
| API scope | A project scope or metadata-only scope adds denials. | A session has no API-key scope. |

Sources: `SecretAccessPolicy`, `ClearanceLevel`, `SensitivityLevel`, `DbSeeder.SeedDevelopmentDataAsync`,
`AccessContext` built in `SecretAccessHandler`. The default trusted list, including loopback and the
private ranges, is `appsettings.json` `Abac:TrustedIpCidrs`. The production window hours are
`Abac:ProductionWindowStartUtcHour` 6 and `ProductionWindowEndUtcHour` 22, UTC unless configured
otherwise. Those defaults are why a loopback denial is not part of the live story.

A share lets a recipient view and reveal across the project boundary only. Clearance, window, and
network still apply. Rotate, restore, and delete need the change right: an administrator, or a
member of the secret's project. Source: ADR-0031; `SecretDetailsController.MayChange`;
`SecretChangeRight`; the comment on rule 2 in `SecretAccessPolicy`; `CONTEXT.md` share.

A refused by-id action writes `SecretAccessDenied` with the policy's reasons, not a `Viewed` row.
Source: `SecretDetailsController.OpenAsync`; `SecretsApiController.Get`; `SecretService.RecordAccessDeniedAsync`.

## 3. Reveal, audit before decryption

Current. This is the live journey. The page is Angular.

| Step | What happens | Source |
|---|---|---|
| 1 | A session opens `/Secrets/Details/{id}`. The Angular page calls `GET api/ui/secrets/{id}`. | `secret-details-page`; `AngularUiRoutes` lists `/Secrets/Details/{id:guid}`; `SecretDetailsController.Get` |
| 2 | `OpenAsync` calls `GetDetailsAsync`, which loads the envelope and trips a honey-token if this row is one. That load writes no audit row. | `SecretDetailsController.OpenAsync`; `SecretService.GetDetailsAsync`; ADR-0031 |
| 3 | `AuthorizeAsync` runs flow 2. A denial writes `SecretAccessDenied` and returns 403 `access_denied`. It does not reveal. | `SecretDetailsController.OpenAsync`; `SecretService.RecordAccessDeniedAsync` |
| 4 | After allow, `Get` calls `RecordViewedAsync`, which commits `SecretMetadataViewed`. The screen label is `Viewed`. | `SecretService.RecordViewedAsync`; `AuditDisplay` |
| 5 | `POST api/ui/secrets/{id}/reveal` runs `OpenAsync` again. Sensitivity below Secret skips step-up. Secret and above, with a stale strong sign-in, returns 403 `step_up_required` and does not decrypt. That challenge writes no denial row. | `SecretDetailsController.Reveal`; `StepUpRequiredAsync`; `appsettings.json` `StepUp:MinimumSensitivity` `Secret` |
| 6 | `RevealAsync` commits `SecretRevealed` through `AuditSink` and returns only after that commit. | `SecretService.RevealAsync`; `AuditSink` |
| 7 | `DecryptToDtoAsync` calls `UnsealAsync`, copies the plaintext into the response, and zeroes the buffer. The Angular page shows it once. | `SecretService.DecryptToDtoAsync`; `AesGcmCryptoEngine.UnsealAsync`; `secret-details-page.html` |

The audit action stored for step 6 is `SecretRevealed`. The page shows `Revealed`. Source: `AuditDisplay`.

If step 6 fails, step 7 does not run. Source: the call order in `RevealAsync` and `AuditSink`'s
contract that it throws `AuditWriteFailedException` rather than returning an unaudited success.

The button on the details page is "Reveal value". Do not capture the page after it succeeds.

## 4. Envelope

Current. Default engine `AesGcmLocal`. Master-key rotation and the Encryption keys page are on
this revision. This talk does not rotate a key.

| Step | What happens | Source |
|---|---|---|
| 1 | Seal generates a fresh 32-byte data key, seals the payload with it, and wraps the data key with the master key. | `AesGcmCryptoEngine.SealAsync` |
| 2 | The row stores ciphertext, wrapped data key, key id, and algorithm. | `SealedSecret` returned by `SealAsync`; `SecretService.CreateAsync` copies those fields |
| 3 | The payload's associated data is the row identity, so an envelope copied onto another row does not open. | `AesGcmCryptoEngine` class comment; `SecretBinding.ForCurrentValue` |
| 4 | Unseal unwraps the data key with the master key named by the row, then opens the payload. | `AesGcmCryptoEngine.UnsealAsync` |
| 5 | When `Crypto:Engine` is `VaultTransit`, the master key stays in Vault and the data key is still local to the payload. | `VaultTransitCryptoEngine` class comment; `appsettings.json` `Vault` section |
| 6 | Rotation runs in resumable batches and does not decrypt a value. The Angular page is `/Encryption`, `AdminOnly`. | `KekRotationService`; `EncryptionController`; ADR-0029; `AngularUiRoutes` `administratorPaths` |

## 5. Checkpoint and export

Current. ADR-0030 is on this revision. Verify compares the trail with checkpoints signed by the
current key. It still does not prove every deletion, whatever the latest checkpoint is.

| Step | What happens | Source |
|---|---|---|
| 1 | Every audit insert is stamped into a hash chain at the interceptor, with a monotonic sequence. | `AuditSaveChangesInterceptor` class comment; rows carry `PreviousHash`, `EntryHash`, and `Sequence` |
| 2 | Opening `/Audit` calls `GET api/ui/audit`. That action writes `AuditLogViewed` before it reads the page. The Angular heading is "Audit log". | `AuditController.Get`; `AngularUiRoutes` `/Audit` |
| 3 | Verify streams the trail and checks checkpoints signed by the current key: signature, head hash at that sequence, and no missing entries up to the latest such checkpoint. It then writes `AuditChainVerified`, critical when the outcome is tampered. The button is "Verify integrity". One verification at a time; another answers 409 `audit_busy`. | `AuditController.Verify`; `AuditCheckpointService.VerifyAsync`; `AuditChainVerifier.VerifyAsync`; ADR-0030 |
| 4 | Create checkpoint signs the current head and saves the checkpoint in the same transaction as `AuditCheckpointCreated`. A failed save leaves no checkpoint. | `AuditCheckpointService.CreateCheckpointAsync`; `AuditSaveChangesInterceptor.DescribeCheckpointChange` |
| 5 | Export requires a fresh step-up, signs the committed head, writes `AuditBundleExported`, then streams rows up to that head. No bytes if the audit write fails. The button is "Export signed bundle". One export at a time. Schema stays version 2. | `AuditController.Export`; `AdministratorStepUp`; `AuditCheckpointService.ExportAsync`; `AuditBundleExport.WriteToAsync`; ADR-0030 |
| 6 | The offline verifier re-walks the bundle, checks the checkpoint against that bundle's head, and checks the signature. An optional pinned key checks that the embedded public key is the expected one. | `AuditBundleVerifier` |
| 7 | Formats `eclipsvault.audit-bundle/1` and `/2` verify. An unknown format is refused and is not called an edited row. | `AuditBundleSchema`; `AuditBundleVerifier` |
| 8 | The 1.0.0 verifier predates that refusal and can report `INVALID — "the row was edited"` for a later bundle. That is the old verifier, not tampering. | `README.md` signed-checkpoint paragraph |

What Verify still does not prove: someone who can delete checkpoints, or change entries written
after the latest checkpoint and rehash them, can leave Verify quiet. A bundle an auditor already
holds is unchanged by a later edit of the live database. Source: ADR-0030 consequences;
`docs/THREAT_MODEL.md` item 12; the message built in `AuditChainVerifier.VerifyAsync` when the
outcome is anchored.

`docs/THREAT_MODEL.md` item 12 also says an insider who can read `ECLIPSVAULT_AUDIT_SIGNING_KEY`
from the host can re-sign. Key pinning does not catch the vault's own key. This draft repeats that
limit and does not test it.

The live journey does not press Verify or Export. Verify is easy to over-read, and Export
downloads the trail and asks for a fresh code.

## 6. Dynamic credential

Current, and not a live click. The page is Angular at `/DynamicSecrets`. Issuing one needs the
separate SQL Server target connection and the licence feature gate. This rehearsal did not issue one.

| Step | What happens | Source |
|---|---|---|
| 1 | The role is refused when disabled, or when the licence feature is not allowed, before any password exists. | `DynamicSecretService.IssueAsync`; `RequireAsync(LicenseFeatures.DynamicSecrets)` |
| 2 | A name and a password are minted. The backend creates the login first. | `CredentialMint`; `DynamicSecretService.IssueAsync` |
| 3 | The lease row stores identity, expiry, and status. It has no password field. | `DynamicSecretLease` class comment and properties |
| 4 | The password is returned once in `IssuedCredentialDto`. | `DynamicSecretService.IssueAsync` return |
| 5 | If the lease insert fails, removal is attempted. When that removal also fails, the caller gets `MintUnrecordedException` and `DynamicSecretsController` logs the role and login at critical. A mint that fails after the login may exist, and whose cleanup also fails, throws `MintIncompleteException`; the controller answers 502 `mint_incomplete` and logs the same way. The password is not in that log. | `DynamicSecretService.IssueAsync`; `DynamicSecretsController` catch of `MintIncompleteException` and `MintUnrecordedException`; `LogLoginMayRemain` |
| 6 | `DynamicLeaseWorker` destroys due leases through `ReapDueLeasesAsync`. A failed destroy closes the lease as `RevocationFailed` with a fixed note, audited as critical. | `DynamicSecretService.DestroyAsync`; `AuditSaveChangesInterceptor` lease rule; ADR-0031 |

An attempted revocation is not proof that the temporary credential was removed. A failed cleanup
can leave a live credential without a recorded lease. The page says so; it does not make the
removal certain.

The Development seed creates two role recipes, `phoenix_db_reader` and `global_db_writer`. They are
not part of the live journey. Source: `DbSeeder.SeedDynamicSecretRolesAsync`.

## 7. What the live journey deliberately skips

| Skipped path | Why | Source |
|---|---|---|
| Honey-tokens `Production_AWS_Root_Key` and `Global_SQL_SA_Password` | A by-id read trips intrusion response and bypasses the access policy. They are omitted from the secrets list. | `DbSeeder` comments; `SecretService.TripHoneyTokenIfNeededAsync`; `SecretService.ListAsync` |
| `Phoenix_Ephemeral_Deploy_Token` | Seeded to expire about five minutes after seeding. | `DbSeeder.SeedDevelopmentDataAsync` |
| `Phoenix_Staging_Api_Key` | Confidential, so the network rule applies. Loopback is trusted by default, so it is a poor denial demo. | `DbSeeder`; `appsettings.json` `Abac:TrustedIpCidrs` |
| Service-account reveal | Same policy, different credential. Saying it is enough; the raw key would be on screen. | `SecretsApiController` |
| Verify, Create signed checkpoint, and Export signed bundle | Easy to overclaim. Slide 6 explains the boundary. Export also downloads the trail and demands step-up. | `AuditController`; ADR-0030 |
| A denial and an access request | Real, and recorded as `SecretAccessDenied`. Not rehearsed on loopback, because the trusted-network default would not be what failed. | ADR-0031; `SecretDetailsController.OpenAsync` |
| Dynamic secret issuance | Needs a SQL Server target and the licence gate. The minted password is shown once. | `DynamicSecretService.IssueAsync` |
