# Security flows / უსაფრთხოების პროცესები

Source revision: `origin/main` `1298958050e45f83aefeb4b6455d0a02169ab22a`
(24 November 2026, 15:18 Asia/Tbilisi). The demo follows flow 4. The other sections cover the
surrounding security checks and related operations. See [demo-setup-reset.md](demo-setup-reset.md)
for the disposable Development run.

წყაროების რევიზიაა `origin/main`
`1298958050e45f83aefeb4b6455d0a02169ab22a` (2026 წლის 24 ნოემბერი, 15:18 Asia/Tbilisi).
დემონსტრაცია მე-4 ნაწილს მიჰყვება. დანარჩენ ნაწილებში აღწერილია წვდომის შემოწმება და მასთან
დაკავშირებული მოქმედებები. ერთჯერადი გაშვება Development გარემოში აღწერილია
[demo-setup-reset.md](demo-setup-reset.md)-ში.

## 1. Vault account sign-in

Every sign-in page runs in Angular (ADR-0032). A password alone does not create a session.

| Step | What happens | Source |
|---|---|---|
| 1 | `/Account/Login` is an anonymous route that serves the Angular document with an antiforgery token and `no-store`. A signed-in visitor is sent on to their target. | `AngularUiRoutes.ServeLogin`; ADR-0032 |
| 2 | `POST api/ui/auth/password` checks the password. A failed check returns 400 `sign_in_failed` with "Invalid username or password." An unknown account gets the same response. | `AuthController.Password`; ADR-0032 "Nothing tells an anonymous caller that an account exists" |
| 3 | After a successful password check, the server signs only an `MfaPending` cookie and returns the next step, `enroll` or `totp`. It issues the session cookie later. | `AuthController.Password`; `AuthSchemes.MfaPending` |
| 4 | The server serves `/Account/EnrollTotp`, `/Account/Totp`, and `/Account/RecoveryCode` only to a pending sign-in. The pages call `enrollment`, `enrollment/confirm`, `totp`, and `recovery-code`. | `AngularUiRoutes` `SecondFactorPaths`; `PendingSignInController` |
| 5 | A discoverable passkey satisfies both factors without a username. Only discoverable passkeys work for this flow. | `AuthController` `passkey/begin`, `passkey/complete`; ADR-0032 |
| 6 | `SessionIssuer.SignInAsync` issues the session and records the success audit. The server alone picks where sign-in lands: `/Secrets` by default. | `SessionIssuer`; `SignInTarget.Default`; ADR-0032 |
| 7 | SSO, when enabled, proves identity at the IdP. The vault still decides admission, then uses the same issuer. `AccountController` keeps only those two redirects. | `AccountController` `/Account/ExternalLogin` and `/Account/ExternalCallback`; `ISsoSignInService`; ADR-0002 |

Keep step 4 off camera so enrollment material and recovery codes stay out of the talk.

Break-glass recovery requires all factors at once and Top Secret clearance. The vault audits
each recovery. From a blocked network, the only authentication paths available are the Angular
page `/Account/Recover` and `POST api/ui/auth/recover`. If recovery returns 503
`recovery_incomplete`, an administrator can release the remaining guards from Networks
(`NetworksController` `blocks/release-guard`). See `AuthController` and ADR-0032.

For a path outside the route table, the server returns its not-found page to signed-in callers
and redirects signed-out callers to sign-in. The access-blocked, error, and not-found server pages
carry no script. See `AngularUiRoutes.MapAngularUi`, `ServerPage`, and ADR-0034.

## 2. Account activation by invitation

The recipient sets their own password through the invitation (ADR-0033). The demo skips activation.

| Step | What happens | Source |
|---|---|---|
| 1 | New user asks the administrator for the person's email address. The recipient chooses the password during activation. Account creation needs fresh proof and writes `UserCreated` with the invitation address. | ADR-0033; ADR-0028 |
| 2 | The token is 256 random bits. Only its SHA-256 hash is stored. It is valid for 72 hours. | `InvitationToken.Generate`, `Hash`, `Lifetime` |
| 3 | The link is built only from `Identity:PublicUrl` and carries the token in its fragment, which the browser omits from the page request. The page sends the token to the activation API. It goes out only through real SMTP, never the notification outbox, a log, or audit details. | `AccountInvitationDelivery`; `InvitationLink`; `AngularUiRoutes` `/Account/Activate`; ADR-0033 |
| 4 | Without real SMTP or `Identity:PublicUrl`, New user and Resend are unavailable with an explanation. No link is shown to the administrator instead. | `UserAdminService`; ADR-0033 |
| 5 | Opening the link displays the username and an authenticator seed and leaves the token usable. | `AuthController.InspectActivation`; `AccountActivationService` |
| 6 | One call sets the password under Profile's rules, enrolls the authenticator, spends the token, and writes `AccountActivated`, in one transaction. A wrong code keeps the invitation. | `AuthController.CompleteActivation`; ADR-0033 |
| 7 | Until then the account cannot sign in by any route. Resending replaces the token. | ADR-0033 |

The seeded Development accounts skip invitations and enroll at first sign-in. See the `DbSeeder`
log line "TOTP enrollment happens on first sign-in".

## 3. Per-request access

`SecretAccessPolicy.Evaluate` decides access. `SecretAccessHandler` supplies the subject,
the resource, the window, the network, and any share. An API key adds scope rules that can only
narrow access.

| Check | Rule in code | Talk secret `Phoenix_Dev_Database_Password` |
|---|---|---|
| Clearance | Numeric clearance must be at least numeric sensitivity. Standard is 1, Elevated 2, Secret 3, TopSecret 4. Internal is 1, Confidential 2, Secret 3, TopSecret 4. | Standard and Internal both have level 1. |
| Project | Must match, unless clearance is TopSecret or `IsExplicitlyGranted`. | Account project `PHOENIX`, secret project `PHOENIX`. |
| Time | Production environment requires `IsWithinProductionWindow`. | Environment is Development, so the window is not consulted. |
| Network | Sensitivity at or above Confidential requires `IsFromTrustedNetwork`. | Internal, so the network is not consulted. |
| API scope | A project scope or metadata-only scope adds denials. | A session has no API-key scope. |

Sources: `SecretAccessPolicy`, `ClearanceLevel`, `SensitivityLevel`, `DbSeeder.SeedDevelopmentDataAsync`,
`SecretAccessHandler` and `AccessContextProvider`. The default trusted list, including loopback
and the private ranges, is `appsettings.json` `Abac:TrustedIpCidrs`. The default production window
is 06:00 to 22:00 UTC, set by `Abac:ProductionWindowStartUtcHour` and `ProductionWindowEndUtcHour`.
`Abac:TimeZoneId` can change the time zone used for those hours. Those defaults are why a loopback denial is not part of the live story.

A share lets a recipient view and reveal across the project boundary only. Clearance, window, and
network still apply. Rotate, restore, and delete need the change right: an administrator, or a
member of the secret's project. Source: ADR-0031; `SecretDetailsController.MayChange`;
`SecretChangeRight`; the comment on rule 2 in `SecretAccessPolicy`; `CONTEXT.md` share.

A refused by-ID action records the policy's reasons in `SecretAccessDenied`. Only an allowed
details request produces `Viewed`.
Source: `SecretDetailsController.OpenAsync`; `SecretsApiController.Get`; `SecretService.RecordAccessDeniedAsync`.

## 4. Reveal, audit before decryption

The demo opens the Angular secret details page and reveals the value.

| Step | What happens | Source |
|---|---|---|
| 1 | A session opens `/Secrets/Details/{id}`. The Angular page calls `GET api/ui/secrets/{id}`. | `secret-details-page`; `AngularUiRoutes` lists `/Secrets/Details/{id:guid}`; `SecretDetailsController.Get` |
| 2 | `OpenAsync` calls `GetDetailsAsync` to load the envelope. For an ordinary secret, the load writes no audit row. A decoy trips the intrusion response. | `SecretDetailsController.OpenAsync`; `SecretService.GetDetailsAsync`; ADR-0031 |
| 3 | `AuthorizeAsync` runs flow 3. A denial writes `SecretAccessDenied` and returns 403 `access_denied`. It does not reveal. | `SecretDetailsController.OpenAsync`; `SecretService.RecordAccessDeniedAsync` |
| 4 | After access is allowed, `Get` calls `RecordViewedAsync`, which commits `SecretMetadataViewed`. The screen label is `Viewed`. | `SecretService.RecordViewedAsync`; `AuditDisplay` |
| 5 | `POST api/ui/secrets/{id}/reveal` runs `OpenAsync` again. Sensitivity below Secret skips step-up. Secret and above, with a stale strong sign-in, returns 403 `step_up_required` and does not decrypt. That challenge writes no denial row. | `SecretDetailsController.Reveal`; `StepUpRequiredAsync`; `appsettings.json` `StepUp:MinimumSensitivity` `Secret` |
| 6 | `RevealAsync` waits for `AuditSink` to commit `SecretRevealed` before it decrypts. | `SecretService.RevealAsync`; `AuditSink` |
| 7 | `DecryptToDtoAsync` calls `UnsealAsync`, copies the plaintext into the response, and zeroes the buffer. The Angular page shows it once. | `SecretService.DecryptToDtoAsync`; `AesGcmCryptoEngine.UnsealAsync`; `secret-details-page.html` |

The audit action stored for step 6 is `SecretRevealed`. The page shows `Revealed`. Source: `AuditDisplay`.

If the audit write in step 6 fails, `AuditSink` throws `AuditWriteFailedException` and
`RevealAsync` never reaches decryption in step 7.

The button on the details page is "Reveal value". Do not capture the page after it succeeds.

## 5. Envelope

`AesGcmLocal` is the default engine. This revision includes master-key rotation and the
Encryption keys page. The demo leaves the master key in place.

| Step | What happens | Source |
|---|---|---|
| 1 | Seal generates a fresh 32-byte data key, seals the payload with it, and wraps the data key with the master key. | `AesGcmCryptoEngine.SealAsync` |
| 2 | The row stores ciphertext, wrapped data key, key id, and algorithm. | `SealedSecret` returned by `SealAsync`; `SecretService.CreateAsync` copies those fields |
| 3 | The payload's associated data is the row identity, so an envelope copied onto another row does not open. | `AesGcmCryptoEngine` class comment; `SecretBinding.ForCurrentValue` |
| 4 | Unseal unwraps the data key with the master key named by the row, then opens the payload. | `AesGcmCryptoEngine.UnsealAsync` |
| 5 | When `Crypto:Engine` is `VaultTransit`, the master key stays in Vault and the data key is still local to the payload. The Encryption keys page lists the engine's master keys; the Transit token needs `read` on `transit/keys/<key>` for that. | `VaultTransitCryptoEngine`; `ICryptoEngine.GetMasterKeysAsync`; `README.md` KMS paragraph |
| 6 | Rotation runs in resumable batches and does not decrypt a value. If a pass fails after committing batches, its response includes the counts it rewrapped. An audit failure gives `audit_unavailable`. Other failures after a committed batch give `rotation_interrupted`. The Angular page is `/Encryption`, `AdminOnly`. | `KekRotationService`; `EncryptionController`; ADR-0029; `AngularUiRoutes` `administratorPaths` |

## 6. Checkpoint and export

Under ADR-0030, Verify compares the trail with checkpoints signed by the current key.
Some deletions can escape detection even when a checkpoint exists.

| Step | What happens | Source |
|---|---|---|
| 1 | Every audit insert is stamped into a hash chain at the interceptor, with a monotonic sequence. | `AuditSaveChangesInterceptor` class comment; rows carry `PreviousHash`, `EntryHash`, and `Sequence` |
| 2 | Opening `/Audit` calls `GET api/ui/audit`. That action writes `AuditLogViewed` before it reads the page. The Angular heading is "Audit log". | `AuditController.Get`; `AngularUiRoutes` `/Audit` |
| 3 | Verify streams the trail and checks checkpoints signed by the current key: signature, head hash at that sequence, and no missing entries up to the latest such checkpoint. It then writes `AuditChainVerified`, critical when the outcome is tampered. The button is "Verify integrity". One verification at a time; another answers 409 `audit_busy`. | `AuditController.Verify`; `AuditCheckpointService.VerifyAsync`; `AuditChainVerifier.VerifyAsync`; ADR-0030 |
| 4 | Create checkpoint signs the current head and saves the checkpoint in the same transaction as `AuditCheckpointCreated`. A failed save leaves no checkpoint. | `AuditCheckpointService.CreateCheckpointAsync`; `AuditSaveChangesInterceptor.DescribeCheckpointChange` |
| 5 | Export requires a fresh step-up, signs the committed head, writes `AuditBundleExported`, then streams rows up to that head. No bytes if the audit write fails. The button is "Export signed bundle". One export at a time. Schema stays version 2. | `AuditController.Export`; `AdministratorStepUp`; `AuditCheckpointService.ExportAsync`; `AuditBundleExport.WriteToAsync`; ADR-0030 |
| 6 | The offline verifier re-walks the bundle, checks the checkpoint against that bundle's head, and checks the signature. An optional pinned key checks that the embedded public key is the expected one. | `AuditBundleVerifier` |
| 7 | The verifier accepts formats `eclipsvault.audit-bundle/1` and `/2`. It returns a version error for an unknown format. | `AuditBundleSchema`; `AuditBundleVerifier` |
| 8 | The 1.0.0 verifier predates the version check and can report `INVALID — "the row was edited"` for an intact bundle in a later format. | `README.md` signed-checkpoint paragraph |

Verify can miss deleted checkpoints or changes to entries after the latest checkpoint if their
hashes are recomputed. An auditor's saved bundle survives edits to the live database. See the
consequences in ADR-0030, item 12 in `docs/THREAT_MODEL.md`, and the message
`AuditChainVerifier.VerifyAsync` builds when a checkpoint anchors the result.

The Attestation panel on `/Audit` says an outside auditor can prove the trail "even if rows were
deleted and the hashes rewritten". That holds for a bundle the auditor already exported. It does
not change the coverage of Verify on the live database.

`docs/THREAT_MODEL.md` item 12 also says an insider who can read `ECLIPSVAULT_AUDIT_SIGNING_KEY`
from the host can re-sign. Key pinning accepts a signature made with the expected key. The
rehearsal did not test re-signing.

Leave Verify and Export out of the demo. Describe Verify's checkpoint coverage on slide 6.
Export asks for a fresh code and downloads the trail.

## 7. Dynamic credential

The Angular page is at `/DynamicSecrets`. Issuance requires a target connection for the role's
backend and a licence that allows the feature. The rehearsal did not issue a credential.

| Step | What happens | Source |
|---|---|---|
| 1 | The role is refused when disabled, or when the licence feature is not allowed, before any password exists. | `DynamicSecretService.IssueAsync`; `RequireAsync(LicenseFeatures.DynamicSecrets)` |
| 2 | A role names its backend: SQL Server through `DynamicSecrets:TargetConnectionString`, or PostgreSQL through `DynamicSecrets:PostgresTargetConnectionString`. Neither is the vault's own database; on PostgreSQL the target must be a distinct cluster. | `DynamicSecretRole.Backend`; `DynamicSecretTargetOptions`; `SqlServerBackend`; `PostgresBackend`; `docs/INSTALL.md` |
| 3 | A name and a password are minted. The backend creates the login first. | `CredentialMint`; `DynamicSecretService.IssueAsync` |
| 4 | The lease row stores identity, expiry, and status. It has no password field. | `DynamicSecretLease` class comment and properties |
| 5 | The password is returned once in `IssuedCredentialDto`. | `DynamicSecretService.IssueAsync` return |
| 6 | If the lease insert fails, removal is attempted. When that removal also fails, the caller gets `MintUnrecordedException` and `DynamicSecretsController` logs the role and login at critical. A mint that fails after the login may exist, and whose cleanup also fails, throws `MintIncompleteException`; the controller answers 502 `mint_incomplete` and logs the same way. The password is not in that log. | `DynamicSecretService.IssueAsync`; `DynamicSecretsController` catch of `MintIncompleteException` and `MintUnrecordedException`; `LogLoginMayRemain` |
| 7 | `DynamicLeaseWorker` destroys due leases through `ReapDueLeasesAsync`. On PostgreSQL, revocation first sets the role `NOLOGIN`, waits for connections still starting up, and ends the role's sessions before `DROP ROLE`. On SQL Server it kills the login's sessions before `DROP LOGIN`. A failed destroy closes the lease as `RevocationFailed` with a fixed note, audited as critical. | `DynamicSecretService.DestroyAsync`; `PostgresBackend` `AwaitStartupsAsync` and `pg_terminate_backend`; `DbSeeder` SQL Server revocation; `AuditSaveChangesInterceptor` lease rule; ADR-0031 |

A revocation attempt can fail. The page warns that a failed cleanup can leave an active
temporary credential without a recorded lease.

On a fresh Development database, the seed adds four role recipes. It adds the SQL Server roles
`phoenix_db_reader` and `global_db_writer` to an empty roles table. It adds the PostgreSQL roles
`phoenix_pg_reader` and `global_pg_writer` if those names are missing. The demo uses a static
secret. See `DbSeeder.SeedDynamicSecretRolesAsync`.

## 8. Operations outside the demo

| Skipped path | Why | Source |
|---|---|---|
| Honey-tokens `Production_AWS_Root_Key` and `Global_SQL_SA_Password` | A by-id read trips intrusion response and bypasses the access policy. They are omitted from the secrets list. | `DbSeeder` comments; `SecretService.TripHoneyTokenIfNeededAsync`; `SecretService.ListAsync` |
| `Phoenix_Ephemeral_Deploy_Token` | Seeded to expire five minutes after seeding. | `DbSeeder.SeedDevelopmentDataAsync` |
| `Phoenix_Staging_Api_Key` | Confidential, so the network rule applies. Loopback is trusted by default, so it is a poor denial demo. | `DbSeeder`; `appsettings.json` `Abac:TrustedIpCidrs` |
| Service-account reveal | A service account uses the same policy with an API key. Describe the flow aloud so the raw key stays off screen. | `SecretsApiController` |
| Invitation and activation | Needs real SMTP and `Identity:PublicUrl`; the link carries a live token. | ADR-0033 |
| Break-glass recovery | The vault lifts the intrusion block and audits the recovery. The demo encounters no block. | `AuthController` `recover`; ADR-0032 |
| Verify, Create signed checkpoint, and Export signed bundle | Easy to overclaim. Slide 6 explains the boundary. Export also downloads the trail and demands step-up. | `AuditController`; ADR-0030 |
| A denial and an access request | The vault records the denial as `SecretAccessDenied`. The rehearsal did not attempt a network denial on loopback because loopback is trusted by default. | ADR-0031; `SecretDetailsController.OpenAsync` |
| Dynamic secret issuance | Needs a target connection and the licence gate. The minted password is shown once. | `DynamicSecretService.IssueAsync` |
