This spec is **Area 7 part 7c, Dynamic secrets** (ADR-0017 amendment). It follows part 7a (#SPEC7A), starts from `main` after 7a's PR merges, and may run beside part 7b (#SPEC7B) in its own worktree. The owner accepted all 23 grill decisions and the three delivery outlines, and confirmed them on 2026-11-11.

## Problem Statement

Opening Dynamic secrets from the Angular UI still leaves the app for a Legacy page (ADR-0001).

The grill's survey found these flaws, deductions from reading the code at `ab7997e`; each ticket proves its finding with a failing test first.

- **Issuing needs no fresh proof (F5).** A dynamic credential for a Secret or Top Secret role is minted without step-up, though revealing a stored secret of the same sensitivity needs it.
- **Failure paths (F7).** A recipe that fails part-way (after `CREATE LOGIN`) throws before any undo runs, leaving a live login. If the lease cannot be recorded and undoing the mint fails, a live credential exists with no record and no log line. A revoke whose close cannot be recorded leaves the lease Active behind a generic 503. `RevocationError` stores the backend's raw exception text and shows it to the holder and every administrator.
- **A failed revoke says it worked (F11).** The page says "Credential handed back — it no longer works." after the backend refused.
- **Licence reads on the page do not fail open (F8).**

## Solution

The Angular UI serves `/DynamicSecrets`, copying the Legacy page exactly except for the named fixes. Issuing needs the reveal's fresh proof, failures are reported truthfully and recorded or logged, and the page's licence read fails open (ADR-0031, ADR-0028).

## User Stories

1. As anyone, I see the enabled dynamic roles I could use now, my leases (administrators: everyone's 50 newest), and the licence note.
2. As anyone, I issue a credential for a role, for up to its maximum lease and at most 1440 minutes, confirming an authenticator code first for a Secret or Top Secret role, and see its username and password exactly once.
3. As anyone, if the backend refuses or the vault cannot record the lease, I am told nothing was issued; if the vault cannot confirm that a partly created login was removed, I am told exactly that, and an administrator is alerted to check it.
4. As a lease's holder or an administrator, I revoke it and am told truthfully whether the backend destroyed it.
5. As anyone, a licence-store failure never breaks the page; an unlicensed vault past its evaluation refuses only new credentials.

## Implementation Decisions

### Scope (Q2)

`DynamicSecretsController`, `Views/DynamicSecrets/Index` and their view models go.

### UI API contract (Q16)

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

Session-only, `no-store`, anti-forgery on writes, explicit types, regenerated TypeScript. Roles pass the access check on the role and are re-checked on issue. Issue problems: `400 validation_failed` (1–1440; the effective TTL, the request or the role's default when omitted, is clamped to the role's maximum and to 1440, Q26; leases issued before 7c keep their expiry); `404 role_not_found`; `403 access_denied` (Legacy sentence; writes 7a's `SecretAccessDenied` on the role); `409 role_disabled`; `402 premium_not_licensed` (Legacy text); `403 step_up_required` (at or above `StepUp:MinimumSensitivity`; no row). Access and step-up checks run before the licence gate, so a refused issue never starts the evaluation window; any failed or uncertain mint (an error, a lost acknowledgement or a cancelled request, during any of the recipe's statements) runs the role's revocation statements for the known login, ignoring the request's cancellation and bounded by the target's command timeout (A7-4). If that cleanup succeeds, `502 mint_refused` ("…Nothing was issued."). If it fails or times out, `502 mint_incomplete` ("The backend failed part-way through, and the vault could not confirm that the login it started to create was removed. No password was shown or kept. An administrator must check the target."), a critical log line naming role and login, and the operator check in `docs/INCIDENT_RESPONSE.md`; a login the vault never recorded may then remain on the target until someone drops it. `503 audit_unavailable` after undoing a successful mint whose lease cannot be recorded; if that undo fails, the same critical log line. No password in any row, log, cache or error message: a backend exception is logged by type and error number, never by a message that could quote the statement. Revoke: one `404 lease_not_found` for unknown, closed or another's lease; `200 { status }`; `503 revoked_unrecorded` when destroyed but not recorded (the lease shows Active until its original expiry, at most 1440 minutes for a lease issued after 7c, when the sweep runs the revocation again. It closes as Expired only if that retry succeeds and its close is recorded; a failed retry (a non-idempotent recipe can fail on repeat) closes it as RevocationFailed, and while auditing stays unavailable neither close is recorded, so it stays Active and each later sweep tries again; an accepted delay, ADR-0031). `revocationNote` is a fixed sentence per cause; raw backend errors only in the log; new rows store none. The evaluation-days read fails open; viewing never starts a window.

### Presentation (Q17)

Exact copy, word for word; the Legacy flash and confirm texts in StatusLine and ConfirmationDialog. Deliberate: StepUpDialog on issue; a refused revoke says "The backend refused to destroy the credential; it may still work."; the issued credential lives only in page memory and is cleared on departure; the list reloads after every write.

## Testing Decisions

- **Unit:** step-up matrix for roles; undo-failure log with fake backends (no password in any log); a recipe failing after `CREATE LOGIN` cleaned up; failed or uncertain `CREATE LOGIN` and `CREATE USER`, request cancellation and cleanup failure answering `mint_incomplete`; the effective TTL for omitted and explicit values; fixed revocation notes; evaluation read failing open.
- **Production HTTP, locally on PostgreSQL through `scripts/check.sh integration "<filter>"`, both databases in CI:** shapes and codes; role filter and re-check (other project, under-cleared, off-network, outside the window); step-up; holder versus administrator revoke; issue and revoke against a real SQL Server login on Testcontainers; the password absent from every audit row and log.
- **AuditFailureFixture:** issue undoes the mint and answers 503; `revoked_unrecorded`, the lease staying Active until its expiry, staying Active through a sweep while audit writes are still rejected, and a sweep after they are restored closing it as Expired, or as RevocationFailed when the retry fails.
- **Vitest:** every state and dialog flow.
- **Playwright** under browser-health (ticket 1 proves the target with the Internal role through the Legacy page; ticket 2 runs the rest): issue and revoke against the run's own SQL Server as the dynamic target, with two roles (Internal and Secret) seeded through `E2E_COMPOSE_PROJECT`; the Secret role issued from a stale session through StepUpDialog with a real TOTP code; viewing and a refused issue start no evaluation window, the first permitted issue starts it; keyboard; both themes.

## Delivery

Branch `area-7c-dynamic-secrets` from `main` after 7a merges, three sequential tickets, one PR; it may run beside 7b. Whichever of 7b, 7c and Area 8a merges later merges `main`, regenerates the API types, reconciles `AngularUiRoutes`, `AccountController` and the e2e sign-in helpers, builds and tests (lesson 16), and its reviewers re-check the reconciled files. Claude Opus 5.5 (high) implements; the `security-reviewer` agent reviews every ticket and the whole part; Standards and Spec reviews go to GPT Sol 6.1 at xhigh; the PR gets GPT Sol and Thermos before the owner merges.

## Out of Scope

- Creating or editing dynamic roles.
- New backends.
- Idempotent revocation statements for operator-written roles.
- Any redesign.

## Records

`docs/planning/area-7-secrets/`; `CONTEXT.md` (Dynamic role, Dynamic credential, Lease); ADRs 0017, 0021, 0028, 0031.
