# Security flows / უსაფრთხოების ნაკადები

Each step names the source it was read from, at `origin/main` `0bb59de1e8f64d1b7d04468c303c1b108dd6f04b`
unless the step says Area 6a. Planned steps are not current behaviour. No flow was executed for
this draft.

The talk uses flow 3. The others are the answers around it.

## 1. Vault account sign-in

Current. A session does not exist after the password alone.

| Step | What happens | Source |
|---|---|---|
| 1 | The password is checked. Failure stays on the sign-in page. | `AccountController.Login` |
| 2 | Success signs an `MfaPending` cookie, not the session cookie. | `AccountController.Login`; `Program.cs` scheme `MfaPending` |
| 3 | First use enrolls an authenticator. A later use asks for a code. A recovery code or a passkey are the other current paths. | `AccountController` redirects to `EnrollTotp` or `Totp`; `SessionIssuer.SignInAsync` call sites for TOTP, recovery, enrollment, and passkey |
| 4 | `SessionIssuer.SignInAsync` issues the session and records the success audit. | `SessionIssuer` |
| 5 | SSO, when enabled, proves identity at the IdP. The vault still decides admission, then uses the same issuer. | `Program.cs` SSO block; `AccountController` SSO sign-in; ADR-0002; `CONTEXT.md` identity provider |

Do not photograph step 3. Enrollment material and recovery codes are out of the talk.

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

A share lets a recipient cross the project boundary only. Clearance, window, and network still
apply. Source: the comment on rule 2 in `SecretAccessPolicy`, and `CONTEXT.md` share.

## 3. Reveal, audit before decryption

Current, and the same on the Area 6a head. This is the live journey.

| Step | What happens | Source |
|---|---|---|
| 1 | A session requests the Legacy reveal action. | `SecretsController.Reveal`; `AngularUiRoutes` does not list this path |
| 2 | `GetDetailsAsync` loads the envelope, trips a honey-token if this row is one, then commits `SecretMetadataViewed`. | `SecretService.GetDetailsAsync` |
| 3 | `CheckAccessAsync` runs flow 2. A denial redirects to the Denied page and does not reveal. | `SecretsController.CheckAccessAsync` |
| 4 | Sensitivity below Secret skips step-up. Secret and above, with a stale strong sign-in, returns the page asking for a code and does not decrypt yet. | `appsettings.json` `StepUp`; `SecretsController.Reveal` and `StepUpReveal` |
| 5 | `RevealAsync` commits `SecretRevealed` through `AuditSink` and returns only after that commit. | `SecretService.RevealAsync`; `AuditSink` |
| 6 | `DecryptToDtoAsync` calls `UnsealAsync`, copies the plaintext into the response, and zeroes the buffer. | `SecretService.DecryptToDtoAsync`; `AesGcmCryptoEngine.UnsealAsync` for the default engine |

The audit action stored is `SecretRevealed`. The page shows `Revealed`. Source: `AuditDisplay`.

If step 5 fails, step 6 does not run. Source: the call order in `RevealAsync` and `AuditSink`'s
contract that it throws `AuditWriteFailedException` rather than returning an unaudited success.

## 4. Envelope

Current. Default engine `AesGcmLocal`.

| Step | What happens | Source |
|---|---|---|
| 1 | Seal generates a fresh 32-byte data key, seals the payload with it, and wraps the data key with the master key. | `AesGcmCryptoEngine.SealAsync` |
| 2 | The row stores ciphertext, wrapped data key, key id, and algorithm. | `SealedSecret` returned by `SealAsync`; `SecretService.CreateAsync` copies those fields |
| 3 | The payload's associated data is the row identity, so an envelope copied onto another row does not open. | `AesGcmCryptoEngine` class comment; `SecretBinding.ForCurrentValue` |
| 4 | Unseal unwraps the data key with the master key named by the row, then opens the payload. | `AesGcmCryptoEngine.UnsealAsync` |
| 5 | When `Crypto:Engine` is `VaultTransit`, the master key stays in Vault and the data key is still local to the payload. | `VaultTransitCryptoEngine` class comment; `appsettings.json` `Vault` section |

Master-key rotation exists as `IKekRotationService` on `main`. The resumable batch rules and the
Angular Encryption keys page are PR #147, head `ab7997e`, not merged. This talk does not rotate a
key. Source for the unmerged decision: `docs/adr/0029-master-key-rotation-runs-in-resumable-batches.md`
on that head only.

## 5. Checkpoint and export

Split into what the code does and what Area 6b specifies.

### Current

| Step | What happens | Source |
|---|---|---|
| 1 | Every audit insert is stamped into a hash chain at the interceptor, with a monotonic sequence. | `README.md` audit section, consistent with `AuditLogReader.VerifyIntegrityAsync` reading `PreviousHash`, `EntryHash`, and `Sequence` |
| 2 | Verify loads chained rows and reports a sequence gap, a broken previous-hash, or a content hash mismatch. Otherwise it reports the chain intact for the rows loaded. | `AuditLogReader.VerifyIntegrityAsync`; `AuditController.Verify` |
| 3 | Create checkpoint signs the current head, saves the checkpoint, then writes `AuditCheckpointCreated` as a later standalone audit. | `AuditCheckpointService.CreateCheckpointAsync` |
| 4 | Export loads every chained row into memory, signs that head, writes `AuditBundleExported`, and returns the whole bundle. No step-up. | `AuditCheckpointService.ExportAsync`; `AuditController.Export` |
| 5 | The offline verifier re-walks the bundle, checks the checkpoint against that bundle's head, and checks the signature. An optional pinned key checks that the embedded public key is the expected one. | `AuditBundleVerifier` |
| 6 | Formats `eclipsvault.audit-bundle/1` and `/2` verify. An unknown format is refused and is not called an edited row. | `AuditBundleSchema`; `AuditBundleVerifier` |
| 7 | The 1.0.0 verifier predates that refusal and can report `INVALID — "the row was edited"` for a later bundle. That is the old verifier, not tampering. | `README.md` signed-checkpoint paragraph |

What current Verify does not do: it never reads `AuditCheckpoints`. Deleting the newest rows, or
rewriting rows and recomputing hashes, can still produce "intact" for the rows that remain. A
checkpoint row that nobody consults does not close that gap. An exported bundle already held
outside the database still contains the head it signed; a later live rewrite does not change that
file. Source for the retained-bundle point: `docs/THREAT_MODEL.md` item 12. Source for the in-app
gap: the body of `VerifyIntegrityAsync`, and the Area 6b problem statement at Area 6a head
`ab7997e` (`docs/planning/area-6-audit-and-keys/issues/6b/spec.md`).

`docs/THREAT_MODEL.md` item 12 also says an insider who can read `ECLIPSVAULT_AUDIT_SIGNING_KEY`
from the host can re-sign. Key pinning does not catch the vault's own key. This draft repeats that
limit and does not test it.

### Planned, Area 6b, not merged

Read from `docs/adr/0030-the-audit-log-claims-only-what-it-proves.md` and the 6b specification on
`ab7997e`. Not present in `AuditLogReader`, `AuditCheckpointService`, or `AuditController` on either
revision compared here.

| Planned behaviour | Spec statement | Still true after it, per the ADR |
|---|---|---|
| Checkpoint-boundary verification | Stream the trail and check checkpoints signed by the current key: signature, head hash at that sequence, and no missing entries up to the latest such checkpoint. | Deleting checkpoints, or changing entries after the latest checkpoint, can still leave Verify quiet. |
| Atomic checkpoint recording | The checkpoint commits with its `AuditCheckpointCreated` row. | Not how `CreateCheckpointAsync` works today. |
| Streamed step-up export | Sign the committed head, write `AuditBundleExported`, then stream rows up to that head. No bytes if the audit write fails. Fresh step-up required. Schema stays version 2. | Today's export builds the whole bundle in memory and does not demand step-up. |

Do not tell the room that these three are done. Do not tell the room that, once they are done,
every deletion or rewrite is detectable regardless of the latest signed checkpoint. The ADR's own
consequence forbids that sentence.

## 6. Dynamic credential

Current, and not a live click. Issuing one needs the separate SQL Server target connection and the
licence feature gate. This draft did not issue one.

| Step | What happens | Source |
|---|---|---|
| 1 | The role is refused when disabled, or when the licence feature is not allowed, before any password exists. | `DynamicSecretService` issue method, `RequireAsync(LicenseFeatures.DynamicSecrets)` |
| 2 | A name and a password are minted. The backend creates the login first. | `CredentialMint`; `DynamicSecretService` |
| 3 | The lease row stores identity, expiry, and status. It has no password field. | `DynamicSecretLease` class comment and properties |
| 4 | The password is returned once in `IssuedCredentialDto`. | `DynamicSecretService` return |
| 5 | If the lease insert fails, revocation is attempted on a best-effort basis. Cleanup failures are silently suppressed in this revision. | `DynamicSecretService` catch around `AddLeaseAsync`; `TryRevokeQuietlyAsync` |
| 6 | `DynamicLeaseWorker` destroys due leases. A failed destroy is recorded on the lease. | `DynamicLeaseWorker` class comment |

An attempted revocation is not proof that the temporary credential was removed. A failed
cleanup can leave a live credential without a recorded lease. Area 7c plans to log cleanup failures;
that change does not guarantee revocation.

The Development seed creates two role recipes, `phoenix_db_reader` and `global_db_writer`. They are
not part of the five-step journey. Source: `DbSeeder.SeedDynamicSecretRolesAsync`.

## 7. What the live journey deliberately skips

| Skipped path | Why | Source |
|---|---|---|
| Honey-tokens `Production_AWS_Root_Key` and `Global_SQL_SA_Password` | A by-id read trips intrusion response and bypasses the access policy. | `DbSeeder` comments; `SecretService.TripHoneyTokenIfNeededAsync` |
| `Phoenix_Ephemeral_Deploy_Token` | Seeded to expire about five minutes after seeding. | `DbSeeder.SeedDevelopmentDataAsync` |
| `Phoenix_Staging_Api_Key` | Confidential, so the network rule applies. Loopback is trusted by default, so it is a poor denial demo. | `DbSeeder`; `appsettings.json` `Abac:TrustedIpCidrs` |
| Service-account reveal | Same policy, different credential. Saying it is enough; the raw key would be on screen. | `SecretsApiController` |
| Checkpoint click and export click | Easy to overclaim. Slide 6 explains them. Export also downloads the trail. | `AuditController` |
