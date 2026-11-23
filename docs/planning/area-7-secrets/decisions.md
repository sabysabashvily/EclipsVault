# Area 7 Overview, Secrets and Dynamic secrets: decision record

Status: all 23 recommendations accepted (rounds 1–3) and confirmed by the owner on 2026-11-11.
GPT Sol 6.1 challenge (2026-11-12, HOLD): all ten findings resolved. A7-1 to A7-5 and A7-8 folded in; the
owner answered A7-6, A7-7, A7-9 and A7-10 on 2026-11-12 (Q24, Q25; see [checkpoint.md](checkpoint.md)). GPT's re-check (2026-11-13, HOLD) closed A7-1 to A7-3 and
A7-5 to A7-7; A7-4, A7-8, R2-1 and R2-2 are folded below (Q16, Q25, Q26), and the r3 re-check's corrections to
R2-1 and R2-2 with them.
Issue drafts are in [issues/](issues/), not yet published.

Survey and findings F1–F11: [survey.md](survey.md). Rounds: [checkpoint.md](checkpoint.md). Glossary:
`CONTEXT.md` (Sharing: Share; Secrets: Secret, Reveal, Secret version, Secret rotation, Managed secret,
Decoy, Access denial, Dynamic role, Dynamic credential, Lease). ADRs: 0017 and 0028 amended, 0031 new.

## Decisions

| Ref | Decision | Agreed behaviour |
| --- | --- | --- |
| Q1 | Split | Three parts, each with its own spec, branch and PR: **7a Overview and Secrets list** (Overview, list, Create, palette search; F6, F10), **7b Secret details** (every Details action, sharing panel, Delete, Denied, filing; F1–F5, F9), **7c Dynamic secrets** (F5 issue half, F7, F8, F11). 7a first; 7b and 7c from `main` after 7a merges; 7c may run beside 7b. ADR-0017 amended. |
| Q2 | Scope | 7a: signed-in `/` served by Angular from `HomeController.Index` (landing for signed-out); `/Secrets`, `/Secrets/Index`, `/Secrets/Create`; `GET /api/ui/secrets/search` with the exact contract, both palettes switched, `/Secrets/Search` deleted; Dashboard, Index, Create views and models deleted. 7b: `/Secrets/Details/{id}`, `/Account/Denied`; filing through the UI API; `SecretsController`, Legacy `AccessRequestsController`, Details and Denied views deleted. 7c: `/DynamicSecrets`; controller and view deleted. Landing and Error stay until Area 9; `AuditDisplay` stays. |
| Q3 | Denied reads (F1) | Lookup writes no row; decoys still trip first. A served details page writes `SecretMetadataViewed`; a refused by-id action (UI API and Public API) writes `SecretAccessDenied` with the reasons, not critical: for `403 access_denied` (the policy's reasons) and `403 not_permitted` ("a share grants read access only"); never for `403 step_up_required` (a challenge, with its own step-up events), `404 not_found`, the list, search, Overview or eligibility; filing writes only Area 4's events (A7-7). The event, its label and the self-service redaction land in 7a, so 7b and 7c only write it (A7-5). Both fail-closed (`503 audit_unavailable`, nothing disclosed). Self-service views (My activity, Overview own feed, Your data export) show a denial row without the secret's name. ADR-0031. |
| Q4 | One event per action (F3) | Each action writes only its own event, in the change's transaction; no Viewed row first. Restore commits with `SecretVersionRestored`, managed rotation with `SecretUpstreamRotated`, instead of `SecretUpdated`. Audit failure: `503 audit_unavailable`, nothing changed, upstream password put back, but only when the write did not commit: nothing after a successful commit undoes it, and the put-back ignores the request's cancellation (A7-2). |
| Q5 | Shares only read (F2) | Rotate, managed rotation, restore and delete need the sharing right (administrator, or project member who passes the access check). Recipients view and reveal only; otherwise `403 not_permitted`. Controls hidden from server flags. ADR-0031, `CONTEXT.md` Share. |
| Q6 | Concurrent changes (F4) | Rotate, managed rotation and restore write only if the wrapped data key is still what they read; version number assigned inside that write. Loser `409 secret_changed`; never an audit failure. Every write to a managed secret's value (managed rotation, rotate, shred; restore is refused, Q24) first takes a cluster-wide lock on its backend and principal (the existing `ISessionLocker`, behind a narrow Core port), without waiting, held from reading the current value through commit or put-back; another writer gets `409 change_in_progress` before the backend is touched, so a put-back can never undo a winner (A7-1). The lifecycle worker shreds only if the wrapped data key and the past expiry are still what it read, and skips a locked secret until its next sweep; a stale rotation or restore never writes onto a shredded row (A7-3). Deterministic races on both databases. |
| Q7 | Step-up (F5) | Reveal and version reveal unchanged (Secret+, 10 minutes; `403 step_up_required`, StepUpDialog, `/api/ui/step-up`). Sharing such a secret (panel and access-request approval) and issuing a dynamic credential for such a role need the same fresh proof. Rotate, restore, delete, revoking a share or lease never ask. ADR-0031, ADR-0028 amended. |
| Q8 | Taken name (F6) | Names stay vault-unique; `409 name_taken` "A secret with that name already exists."; form keeps its values; the oracle is an accepted consequence (ADR-0031). |
| Q9 | Licence reads (F8) | Evaluation-days reads on page views fail open: note omitted, warning logged; a reveal never depends on them; gates unchanged; viewing never starts a window. |
| Q10 | Denied reasons (F9) | Kept word for word; project and sensitivity disclosure to an id holder accepted (ADR-0031). |
| Q11 | Overview figures (F10) | Critical count stays for everyone; staff count and administrator links only for administrators, accounts loaded only for them; the notice uses the true expiring count. |
| Q12 | Glossary | Secrets section in `CONTEXT.md` (see above). |
| Q13 | 7a UI API | See below. |
| Q14 | 7b UI API | See below. |
| Q15 | Managed rotation outcomes | See "Problems" under 7b. |
| Q16 | 7c UI API and outcomes (F7, F11) | See below. |
| Q17 | Presentation | Exact copies; Legacy flash and confirm texts in StatusLine and ConfirmationDialog. Deliberate: StepUpDialog for reveal, version reveal, share, issue and Area 4 approval (replaces the inline code form); change controls hidden unless `canChange`, sharing panel unless `canShare`; true expiring count; "That share no longer exists."; new texts below; lists reload after every write. |
| Q18 | Test plan | See "Testing". |
| Q19 | 7a delivery | See "Delivery outline". |
| Q20 | 7b delivery | See "Delivery outline". |
| Q21 | 7c delivery | See "Delivery outline". |
| Q22 | Reviews | Claude Opus 5.5 (high) implements all eleven tickets (all protected; no Cursor code). Per ticket: `security-reviewer` plus GPT Sol 6.1 (xhigh) Standards and Spec in GPT windows, implementation continuing meanwhile; fixes re-checked by the raising reviewer. Per part PR: GPT Sol and Thermos, CI on the exact head, owner merge. Cursor may take browser-suite reruns and docs in fresh agents. Each ticket closes with commits, CI run and local counts. |
| Q23 | Cross-model challenge | After confirmation, one GPT Sol 6.1 (xhigh) challenge of this table and the drafts, run by the coordinator after 20:25 on 2026-11-11 as a fresh delegate; accepted changes folded in before publication. |
| Q24 | Managed restore (A7-10) | Restoring a version of a managed secret answers `409 restore_managed` "This secret is the live password of '{principal}'. Rotate it upstream instead; earlier versions stay readable." and changes nothing; Restore is hidden for managed secrets. A named 7b fix: Legacy restored only the stored copy, so the vault served a password the backend refused. Owner, 2026-11-12; ADR-0031. |
| Q25 | Unrecorded revoke (A7-9) | After `503 revoked_unrecorded`, the lease shows Active until its original expiry, when the sweep runs the revocation again. It closes as Expired only if that retry succeeds and its close is recorded. A failed retry closes it as RevocationFailed with its fixed note, as any sweep does; a non-idempotent revocation recipe can fail this way because the login is already gone. While auditing stays unavailable neither close is recorded: the lease stays Active and each later sweep tries again (R2-1). The original expiry is at most 1440 minutes away only for a lease issued after 7c (Q26); an older lease keeps its own expiry. Accepted by the owner on 2026-11-12: the credential is already destroyed, so only the display lags. ADR-0031 Consequences; a test pins it. |
| Q26 | Lease ceiling (R2-2) | No lease issued after 7c exceeds 1440 minutes: the effective TTL is the requested value, or the role's default when omitted, clamped to the role's maximum and to 1440. An explicit request over 1440 stays `400 validation_failed`. An omitted one issues the role's default capped at 1440: a default of 2880 issues 1440, which the response's `expiresAtUtc` shows; a default of 30 with a maximum of 2880 issues 30. Leases issued before 7c keep their expiry, so an operator-written role with a longer maximum can have one Active past 1440 minutes until it expires; the seeded roles' maxima are 60 and 30. Capping, not refusing, so such a role keeps issuing. Owner's decision, 2026-11-13: keep the cap. |

## UI API contracts

All endpoints: session-only, `no-store`, anti-forgery on writes, the caller only from the session,
explicit response types, regenerated TypeScript, Problem Details with a stable `code`.

### 7a Overview and Secrets list (Q13)

```text
GET  /api/ui/overview  { displayName, totalActiveSecrets, developmentCount, stagingCount, productionCount,
                         expiringWithin7Days, criticalEventsLast24h, staffAccounts (administrators; else null),
                         activityScope: "vault"|"own",
                         recentEvents: [{ timestampUtc, action, actionLabel, tone, resource, actor, isCritical }],
                         expiringSoon: [{ secretId, name, environment, expiresAtUtc }] }   ≤ 8 rows
GET  /api/ui/secrets   { secrets: [{ secretId, name, project, environment, sensitivity, sensitivityLabel,
                                     createdAtUtc, expiresAtUtc }] }
GET  /api/ui/secrets/search?q=   [{ id, name, project, environment, sensitivity, sensitivityName, url }]
POST /api/ui/secrets   { name, value, project, environment, sensitivity, ttlDays } → 201 { secretId }
```

Search keeps the Legacy contract: fewer than 2 characters → `[]`; decoys excluded; the list's access
filter; name or project contains `q`; prefix matches first, then name; 8 rows; `url` is
`/Secrets/Details/{id}`. Reads write no audit row. Administrator cards and links come from the areas in
`/api/ui/me`; the default project from `/api/ui/me`. Problems: `400 validation_failed` (field errors;
name 3–128, value ≤ 8192, project ≤ 64); `403 creation_denied` with reasons; `409 name_taken`;
`503 audit_unavailable`. Until 7b, a successful create also sets the Legacy success flash ("Secret 'x'
was envelope-encrypted and stored.") for the Legacy Details page.

### 7b Secret details (Q14, Q15)

```text
GET    /api/ui/secrets/{id}  { secretId, name, project, environment, sensitivity, sensitivityLabel, algorithm,
                               createdAtUtc, updatedAtUtc, expiresAtUtc,
                               managed: { principal, licensed, evaluationDaysRemaining } | null,
                               versions: [{ versionId, versionNumber, archivedAtUtc, archivedBy, changeNote }],
                               canChange, canShare,
                               shares: [{ shareId, recipient, issuer, sinceUtc, expiresAtUtc }] | null,
                               stepUpMaxAgeMinutes }                                   writes SecretMetadataViewed
POST   /api/ui/secrets/{id}/reveal                       → { label: "current value", value }
POST   /api/ui/secrets/{id}/versions/{versionId}/reveal  → { label: "version n", value }
POST   /api/ui/secrets/{id}/rotate          { newValue, changeNote?, renewTtlDays? } → 204
POST   /api/ui/secrets/{id}/rotate-managed  { renewTtlDays? }                        → 204
POST   /api/ui/secrets/{id}/versions/{versionId}/restore                             → 204
POST   /api/ui/secrets/{id}/shares          { recipient, ttlDays }                   → 204
DELETE /api/ui/secrets/{id}/shares/{shareId}                                         → 204
DELETE /api/ui/secrets/{id}                                                          → 204
GET    /api/ui/access-requests/eligibility?secretId=  → { eligible, refusal, message }  (no audit row)
POST   /api/ui/access-requests  { secretId, reason }  → 204 | 409 <Area 4 refusal code>
```

Problems on every secret endpoint: `404 not_found` (unknown, expired, shredded, or decoy: identical
answer, trap fires); `403 access_denied` with reasons (writes `SecretAccessDenied`); `403 not_permitted`
(writes `SecretAccessDenied`); `403 step_up_required` (no row); `409 change_in_progress`; `400 validation_failed`; `409 secret_changed`; `409 share_exists`;
`409 share_refused` (Core's safe message); `404 share_not_found`; `503 audit_unavailable`.

Managed rotation adds: `409 not_managed` and `409 rotation_unavailable` (Legacy messages);
`402 premium_not_licensed` (Legacy text, before the backend is touched); `502 upstream_refused`
("The backend refused the rotation. The credential is unchanged."; logged, no audit-trail claim); on an
audit failure or a lost race before commit the upstream password is put back (never after a commit), then `503 audit_unavailable` or
`409 secret_changed` with "Nothing changed."; if it cannot be put back, `503 upstream_drifted` with the
Legacy drift sentence, a critical `SecretUpstreamRotationDrifted` row attempted, and a critical log line
if that row fails too.

Restoring a version of a managed secret answers `409 restore_managed` and changes nothing; the Restore
control is hidden for managed secrets (Q24).

Details' `access_denied` opens `/Account/Denied?secretId=` with the reasons in navigation memory; a
reload shows the generic paragraph. Denied asks the eligibility endpoint whether to show the form.
Filing a decoy answers 404 and trips it; outcomes go to `/AccessRequests?filed=1` or `?refused=<code>`.
An unknown secret answers eligibility and filing with the decoy's 404, byte for byte (owner, 20 Nov), so
there is no `secret_not_found` code. A revealed value lives only in page memory and is cleared on departure.

### 7c Dynamic secrets (Q16)

```text
GET  /api/ui/dynamic-secrets   { roles: [{ roleId, name, description, project, environment, sensitivity, sensitivityLabel,
                                           backend, defaultTtlMinutes, maxTtlMinutes }],
                                 leaseScope: "everyone"|"own",
                                 leases: [{ leaseId, roleName, credentialIdentity, holder, issuedAtUtc, expiresAtUtc,
                                            closedAtUtc, status, revocationNote }],
                                 licence: { licensed, evaluationDaysRemaining } }
POST /api/ui/dynamic-secrets/issue  { roleId, ttlMinutes? } → 200 { leaseId, roleName, username, password, expiresAtUtc }
POST /api/ui/dynamic-secrets/leases/{leaseId}/revoke  → 200 { status }
```

Issue problems: `400 validation_failed` (1–1440; the effective TTL is also clamped to the role's maximum and to 1440, Q26); `404 role_not_found`; `403 access_denied` (Legacy
sentence; writes `SecretAccessDenied` on the role); `409 role_disabled`; `402 premium_not_licensed`
(Legacy text); `403 step_up_required`; any failed or uncertain mint (an error, a lost acknowledgement or a cancelled request, during any of the recipe's statements) runs the role's revocation statements for the known login, ignoring the request's cancellation and bounded by the target's command timeout (A7-4). If that cleanup succeeds, `502 mint_refused` ("…Nothing was issued."). If it fails or times out, `502 mint_incomplete` ("The backend failed part-way through, and the vault could not confirm that the login it started to create was removed. No password was shown or kept. An administrator must check the target."), a critical log line naming role and login, and the operator check in `docs/INCIDENT_RESPONSE.md`; a login the vault never recorded may then remain on the target until someone drops it. `503 audit_unavailable` after undoing a successful mint whose lease cannot be recorded; if that undo fails, the same critical log line. No password in any row, log, cache or error message: a backend exception is logged by type and error number, never by a message that could quote the statement. Revoke: one `404 lease_not_found` for unknown, closed or another's lease;
`200 { status }`, and on a backend refusal the page says "The backend refused to destroy the
credential; it may still work."; `503 revoked_unrecorded` when the credential was destroyed but not
recorded (the lease shows Active until its original expiry, when the sweep runs the revocation again. It closes as Expired only if that retry succeeds and its close is recorded; a failed retry (a non-idempotent recipe can fail on repeat) closes it as RevocationFailed, and while auditing stays unavailable neither close is recorded, so it stays Active and each later sweep tries again; Q25). `revocationNote` is a fixed sentence per cause;
raw backend errors go only to the log, and new rows store none. The password appears only in the
issue response and page memory.

## New texts (Q17)

- `secret_changed`: "This secret changed while you were working on it. Reload and try again."
- `name_taken`: "A secret with that name already exists."
- `not_permitted`: "Only an administrator or a member of this secret's project can change it."
- `mint_incomplete`: "The backend failed part-way through, and the vault could not confirm that the login it started to create was removed. No password was shown or kept. An administrator must check the target."
- `change_in_progress`: "Another change to this secret is in progress. Try again in a moment."
- `restore_managed`: "This secret is the live password of '{principal}'. Rotate it upstream instead; earlier versions stay readable."
- "That share no longer exists." (was "That grant no longer exists.")
- The Q15 and Q16 texts above.

## Carried over unchanged

Legacy wording of every page; the access filter on list, search and Overview; decoys dropped from every
list; reveal recorded before decryption; step-up threshold and window (`StepUp:MinimumSensitivity`,
`StepUp:MaxAuthAgeMinutes`); the Area 4 filing rule and refusal codes; the licence gates' fail-open
decision; area keys and `Session` visibility; the Public API's shapes.

## Testing (Q18)

- **Unit:** the change right; one event per action, including the interceptor rules for restore and
  upstream rotation; the conditional write; the step-up matrix; the search contract (2 characters,
  8 rows, ordering, decoys); the Overview figures; evaluation reads failing open; managed-rotation
  put-back and drift with fake backends, and no put-back after a successful commit whose cache eviction
  fails (A7-2); the dynamic undo-failure log, and a recipe failing after `CREATE LOGIN` cleaned up; failed or
  uncertain `CREATE LOGIN` and `CREATE USER`, request cancellation and cleanup failure (A7-4); the
  effective TTL for omitted and explicit values (Q26).
- **Production HTTP, locally on PostgreSQL through `scripts/check.sh integration "<filter>"`, both databases in CI:** every shape and code; the authorization
  matrix (owner, project member, administrator, share recipient, other project, under-cleared,
  off-network, outside the window, decoy); session-only, anti-forgery and `no-store`; exact audit rows
  (Viewed only on GET, a denial row including on the Public API, names withheld in self-service views).
- **AuditFailureFixture:** every write, the viewed and denial rows, managed rotation's put-back, the
  issue undo, `revoked_unrecorded` (the lease Active until its expiry, then closed by the sweep as Q25 describes).
- **Deterministic races on both databases:** two rotations; rotation against restore; a secret rotation
  during a master key rotation; two managed rotations of one principal, and a managed rotation against a
  rotate, ending with the backend at the stored value (A7-1); rotation and restore against
  shredding, in both orders (A7-3).
- **Real backend:** managed rotation and dynamic issue/revoke against a real SQL Server login on a
  Testcontainers SQL Server.
- **Vitest:** every page and dialog flow.
- **Playwright** under browser-health: Overview as administrator and non-administrator; list, search,
  palette; Create including a taken name; Details reveal, step-up reveal with real TOTP, version reveal,
  rotate, restore, share and revoke, delete, and a recipient seeing no change controls; Denied → filing
  → Access requests; dynamic issue and revoke against the run's own SQL Server (target and two roles,
  Internal and Secret, seeded through `E2E_COMPOSE_PROJECT`; the Secret role issued from a stale session
  through StepUpDialog; viewing and refused issues start no evaluation, the first permitted issue does;
  7c ticket 1 proves the target and login creation and removal through the Legacy page with the
  Internal role; ticket 2 runs this whole flow, A7-8).
- Each ticket verifies with `scripts/check.sh`, adding `integration "<filter>"` and `e2e <affected specs>`; CI runs SQL Server and the full browser suite (lesson 17).

## Delivery outline

Three specs, published together after the cross-model challenge and after Area 6b merges; 7b's and
7c's specs are blocked by 7a's. Tests land with their behaviour; each page is served in the ticket that
builds it. The schema is expected unchanged; any change needs migrations in both sets.

**7a Overview and Secrets list**, branch `area-7a-overview-and-secrets` from `main` after 6b merges:

| Ticket | Behaviour | Decisions |
| --- | --- | --- |
| 1 | Overview served by Angular; signed-in `/` served by Angular; administrator-only figures; true expiring count; the `SecretAccessDenied` event, label and self-service redaction shared by 7b and 7c (A7-5). Commits every grill record. | Q2, Q11, Q13, Q17 |
| 2 | Secrets list, palette search and New secret served by Angular; both palettes on the UI API search; `name_taken`; Legacy flash for Details; old actions and views deleted. | Q2, Q8, Q13, Q17 |
| 3 | Verify the whole part and prepare its PR. | whole-part reviews and gates |

**7b Secret details**, branch `area-7b-secret-details` from `main` after 7a merges:

| Ticket | Behaviour | Decisions |
| --- | --- | --- |
| 1 | Record denials as denials (7a's event) and one event per action; Public API; managed-rotation outcomes and put-back before commit only. | Q3, Q4, Q15 |
| 2 | Shares only read; changed secrets never overwritten, managed secrets locked per principal, managed restore refused, shredding conditional; sharing needs fresh proof, including Area 4's approval with StepUpDialog. | Q5, Q6, Q7 |
| 3 | Secret details served by Angular. | Q2, Q9, Q14, Q17 |
| 4 | Denied and filing served by Angular; `SecretsController`, Legacy `AccessRequestsController` and views deleted. | Q2, Q10, Q14, Q17 |
| 5 | Verify the whole part, including the decoy matrix (lesson 11), and prepare its PR. | whole-part reviews and gates |

**7c Dynamic secrets**, branch `area-7c-dynamic-secrets` from `main` after 7a merges, may run beside 7b:

| Ticket | Behaviour | Decisions |
| --- | --- | --- |
| 1 | Issuing and revoking follow ADR-0031: step-up, outcomes, undo log, honest revoke, fixed notes, evaluation reads failing open; lease ceiling; proves the e2e target and login creation and removal through the Legacy page. | Q7, Q9, Q16 |
| 2 | Dynamic secrets served by Angular, with the full Internal and Secret browser flow. | Q2, Q16, Q17 |
| 3 | Verify the whole part and prepare its PR (merge `main`, regenerate types, build and test first if 7b merged). | whole-part reviews and gates |

Shared files (A7-5): whichever of 7b, 7c and Area 8a merges later merges `main`, regenerates the API
types, reconciles `AngularUiRoutes`, `AccountController` and the e2e sign-in helpers, builds and tests
(lesson 16), and its reviewers re-check the reconciled files.

Area 9a's ticket 1 may start after 8a; its ticket 2 and its merge also wait for 7b and 7c (owner,
2026-11-12, recorded in Area 9's records; A7-6).

Schedule risk: eleven tickets after 6b's five, with Areas 8 and 9 to follow; the delivery plan's
14–17 November slot does not hold them. Time comes from 7c beside 7b and from reviews overlapping
implementation, never from dropping a gate.
