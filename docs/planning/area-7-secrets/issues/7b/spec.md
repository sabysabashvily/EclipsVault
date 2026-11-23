This spec is **Area 7 part 7b, Secret details** (ADR-0017 amendment). It follows part 7a (#SPEC7A) and starts from `main` after 7a's PR merges; part 7c (#SPEC7C) may run beside it. The owner accepted all 23 grill decisions and the three delivery outlines, and confirmed them on 2026-11-11.

## Problem Statement

Opening a secret, or being refused one, from the Angular UI still leaves the app for a Legacy page (ADR-0001), and the Denied page files access requests through a Legacy form endpoint.

The grill's survey found these flaws, deductions from reading the code at `ab7997e`; each ticket proves its finding with a failing test first.

- **A share lets its recipient change and delete the secret (F2).** Rotate, managed rotation, restore and delete check only read access, which a share grants.
- **A denied read is recorded as Viewed (F1).** `GetDetailsAsync` writes `SecretMetadataViewed` before the access check, for every by-id action and the Public API; the denied caller's My activity and Overview feed then show the secret's name.
- **Save-then-audit on restore and managed rotation (F3).** Both commit with `SecretUpdated`, then write their real event; if that fails the change stands, and managed rotation says "The credential is unchanged".
- **Concurrent changes to one secret (F4).** Two rotations collide on the version index (reported as an audit outage) or lose a value from history.
- **Sharing needs no fresh proof (F5).** A share of a high-sensitivity secret hands another account the reveal that itself needs step-up.
- **Restoring a managed secret changes only the stored copy (GPT challenge A7-10).** The backend keeps the newer password, so the vault then serves one the backend refuses.
- **Denied discloses attributes (F9, accepted):** project and sensitivity to an id holder.

## Solution

The Angular UI serves `/Secrets/Details/{id}` and `/Account/Denied`, copying the Legacy pages exactly except for the named fixes, and files access requests through the UI API. A denial is recorded as a denial, each action writes one event in its transaction, shares only read, a changed secret is never overwritten, and sharing needs the reveal's fresh proof (ADR-0031, ADR-0028).

## User Stories

1. As anyone who may open a secret, its page shows its metadata, versions, managed principal and licence note, and the sharing panel when I may share it.
2. As that person, I reveal the value or an archived version; at Secret and above with a sign-in older than 10 minutes, I confirm an authenticator code first.
3. As an administrator or project member who passes the access check, I rotate, rotate upstream, restore and delete; as a share recipient I see no such controls and the server refuses them.
4. As an administrator or project member, I share with an account by username or email, with an optional expiry, confirming a code first for a high-sensitivity secret, and revoke any share.
5. As anyone refused, I see the policy's reasons on the Denied page and, when a share would help, file a request and land on Access requests.
6. As an auditor, a refused attempt is an access denial with its reasons, never a view, and every action is one event committed with its change.
7. As a person refused, my own activity never shows the name of the secret I was refused.
8. As two people rotating at once, one succeeds and the other is told the secret changed; no value is lost.
9. As anyone rotating a managed secret, a failure tells me truthfully whether the real credential changed.

## Implementation Decisions

### Scope (Q2)

`SecretsController` (remaining actions), the Legacy `AccessRequestsController`, `Views/Secrets/Details`, `Views/Account/Denied` and their view models go. `AccountController.Denied` goes; the cookie's `AccessDeniedPath` stays `/Account/Denied`, now served by Angular.

### Recording (Q3, Q4, ADR-0031)

Lookup writes no row; decoys still trip before the access check. A served details GET writes `SecretMetadataViewed`; a refused by-id action, in the UI API and the Public API, writes `SecretAccessDenied` with the reasons, not critical. Both fail closed. The event and its name withheld in self-service views come from 7a. The row is written for `access_denied` and `not_permitted`, never for `step_up_required` (a challenge with its own step-up events), `not_found` or a list; filing writes only Area 4's events. Each action writes only its own event in its transaction: restore `SecretVersionRestored`, managed rotation `SecretUpstreamRotated`; no Viewed row first. The upstream put-back runs only when the write did not commit, never after a commit (even one whose cache eviction then fails), and ignores the request's cancellation.

### Rules (Q5, Q6, Q7)

The change right (administrator, or project member passing the access check) gates rotate, managed rotation, restore and delete (`403 not_permitted`), as it already gates sharing. Rotate, managed rotation and restore write only if the wrapped data key is still what they read; the version number is assigned inside that write; the loser gets `409 secret_changed`. Every write to a managed secret's value (managed rotation, rotate, shred) first takes a cluster-wide lock on its backend and principal (the existing `ISessionLocker` behind a narrow Core port), without waiting, held from reading the current value through commit or put-back; another writer gets `409 change_in_progress` before the backend is touched. The lifecycle worker shreds only if the wrapped data key and the past expiry are still what it read, and skips a locked secret until its next sweep. Restoring a version of a managed secret answers `409 restore_managed` and changes nothing. Reveal and version reveal keep step-up; sharing a secret at or above `StepUp:MinimumSensitivity`, from the panel or by approving an access request, needs the same fresh proof (`403 step_up_required`). Rotate, restore, delete and revoking a share never ask.

### UI API contract (Q14, Q15)

```text
GET    /api/ui/secrets/{id}  { secretId, name, project, environment, sensitivity, sensitivityLabel, algorithm,
                               createdAtUtc, updatedAtUtc, expiresAtUtc,
                               managed: { principal, licensed, evaluationDaysRemaining } | null,
                               versions: [{ versionId, versionNumber, archivedAtUtc, archivedBy, changeNote }],
                               canChange, canShare,
                               shares: [{ shareId, recipient, issuer, sinceUtc, expiresAtUtc }] | null,
                               stepUpMaxAgeMinutes }
POST   /api/ui/secrets/{id}/reveal                       → { label: "current value", value }
POST   /api/ui/secrets/{id}/versions/{versionId}/reveal  → { label: "version n", value }
POST   /api/ui/secrets/{id}/rotate          { newValue, changeNote?, renewTtlDays? } → 204
POST   /api/ui/secrets/{id}/rotate-managed  { renewTtlDays? }                        → 204
POST   /api/ui/secrets/{id}/versions/{versionId}/restore                             → 204
POST   /api/ui/secrets/{id}/shares          { recipient, ttlDays }                   → 204
DELETE /api/ui/secrets/{id}/shares/{shareId}                                         → 204
DELETE /api/ui/secrets/{id}                                                          → 204
GET    /api/ui/access-requests/eligibility?secretId=  → { eligible, refusal, message }
POST   /api/ui/access-requests  { secretId, reason }  → 204 | 409 <Area 4 refusal code>
```

Session-only, `no-store`, anti-forgery on writes, explicit types, regenerated TypeScript. Problems: `404 not_found` (unknown, expired, shredded or decoy: identical, trap fires); `403 access_denied` with reasons and `403 not_permitted`, both writing the denial row; `403 step_up_required` (no row); `409 change_in_progress`; `400 validation_failed`; `409 secret_changed`, `share_exists`, `share_refused` (Core's message); `404 share_not_found`; `503 audit_unavailable`. Restore of a managed secret answers `409 restore_managed` ("This secret is the live password of '{principal}'. Rotate it upstream instead; earlier versions stay readable."). Managed rotation adds `409 not_managed`, `409 rotation_unavailable`, `402 premium_not_licensed` (before the backend is touched), `502 upstream_refused` ("The backend refused the rotation. The credential is unchanged."), the upstream password put back on an audit failure or lost race before commit ("Nothing changed."), never after a commit, and `503 upstream_drifted` (Legacy drift sentence; critical `SecretUpstreamRotationDrifted` attempted; critical log if that fails). Evaluation-days reads fail open (Q9). Eligibility writes no row; filing a decoy answers 404 and trips it.

### Presentation (Q17)

Exact copies of Details and Denied. Legacy flash and confirm texts in StatusLine and ConfirmationDialog. Deliberate: StepUpDialog for reveal, version reveal, share and Area 4's approve (replacing the inline code form); change controls hidden unless `canChange`, and Restore also for a managed secret, sharing panel unless `canShare`; "That share no longer exists."; `secret_changed`, `not_permitted` and the managed-rotation texts. Details' `access_denied` opens `/Account/Denied?secretId=` with the reasons in navigation memory; a reload shows the generic paragraph. A revealed value lives only in page memory. After Delete the list shows the Legacy success text.

## Testing Decisions

- **Unit:** change right; one event per action including the interceptor rules; conditional write; step-up matrix; evaluation reads failing open; managed-rotation put-back and drift with fake backends, and no put-back after a commit.
- **Production HTTP, locally on PostgreSQL through `scripts/check.sh integration "<filter>"`, both databases in CI:** every shape and code; the authorization matrix (owner, project member, administrator, share recipient, other project, under-cleared, off-network, outside the window, decoy) on every endpoint; exact audit rows (Viewed only on GET, a denial row including on the Public API, names withheld in My activity, Overview and the data export); session-only, anti-forgery, `no-store`; managed rotation against a real SQL Server login on Testcontainers.
- **AuditFailureFixture:** every write, the viewed and denial rows, managed rotation's put-back.
- **Deterministic races on both databases:** two rotations; rotation against restore; a secret rotation during a master key rotation; two managed rotations of one principal, and a managed rotation against a rotate, ending with the backend at the stored value; rotation and restore against shredding, in both orders.
- **Vitest:** every state and dialog flow of both pages, and Area 4's approve with StepUpDialog.
- **Playwright** under browser-health: reveal, step-up reveal with a real TOTP code, version reveal, rotate, restore, share and revoke, delete, a recipient seeing no change controls, Denied → filing → Access requests, keyboard, both themes.

## Delivery

Branch `area-7b-secret-details` from `main` after 7a merges, five sequential tickets, one PR. Claude Opus 5.5 (high) implements; the `security-reviewer` agent reviews every ticket and the whole part; Standards and Spec reviews go to GPT Sol 6.1 at xhigh; the PR gets GPT Sol and Thermos before the owner merges. Whichever of 7b, 7c and Area 8a merges later merges `main`, regenerates the API types, reconciles `AngularUiRoutes`, `AccountController` and the e2e sign-in helpers, builds and tests (lesson 16), and its reviewers re-check the reconciled files.

## Out of Scope

- Dynamic secrets (7c).
- Recording denials for non-secret pages.
- Per-share edit rights.
- Any redesign.

## Records

`docs/planning/area-7-secrets/`; `CONTEXT.md` (Share, Secrets section); ADRs 0017, 0021, 0027, 0028, 0029, 0031.
