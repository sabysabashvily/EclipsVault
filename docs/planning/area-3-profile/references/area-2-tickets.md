### #75 Area 2 · 1/7: Each share records its issuer by account id, closing the reused-username leak [OPEN]
## Parent

#74

## What to build

A share records its **issuer** by account id, and Shared by me lists and revokes by that id alone. A vault account given a username that once belonged to a deleted account sees none of that account's shares and cannot revoke them. This holds on the Legacy Shared by me page as soon as this ticket lands, because the scoping lives in Core.

This ticket also starts the Area 2 branch (cut from `main`) and commits the grill's records: `CONTEXT.md` (Share, Issuer, Recipient), ADR-0021, and ADR-0017's amendment adding Area 2 to the security-review list.

Backfill rule, in the migration for both providers: a share gets the id of the account whose username matches its issuer username (compared the way usernames are compared today) **and** which was created at or before the share. Otherwise the id stays empty, and the share appears on nobody's Shared by me. It stays revocable from the secret's own sharing list, as today.

## Acceptance criteria

- [ ] The share entity has a nullable issuer account id; new shares record the actor's account id; the issuer username stays for display and audit text only.
- [ ] Migrations exist for SQL Server and PostgreSQL, and CI's migration checks pass for both.
- [ ] Listing shares by issuer and issuer-scoped revocation compare the issuer id with the caller's account id; no query scopes by username any more; "gone" and "not yours" are still refused identically.
- [ ] Migration-data tests against a fresh Testcontainers SQL Server **and** a fresh Testcontainers PostgreSQL (images pinned by digest): migrate to the migration before this one, insert accounts and old-style shares, migrate up, and check the issuer id for each case: matching account created before the share; the same username reused by a later account; no matching account; a differently cased username.
- [ ] An HTTP integration test: an administrator deletes an account that issued a share, creates a new account with the same username, and the new account's Shared by me (the Legacy page for now) lists nothing, and its revocation of the old share is refused. The recipient keeps access.
- [ ] Unit tests for the share service's issuer recording and scoping, following the existing share-service tests.
- [ ] `CONTEXT.md`, ADR-0021 and the ADR-0017 amendment are committed.
- [ ] Release build with 0 warnings; unit, integration, and the new migration tests pass; `dotnet format --verify-no-changes` passes.

## Blocked by

None (can start immediately).


### #76 Area 2 · 2/7: Share changes are audited atomically, proven by a failing audit store [OPEN]
## Parent

#74

## What to build

Creating and revoking a share are written in one transaction with their audit rows (ADR-0021): both happen, or neither does. That covers sharing and revoking from the Legacy Secret details page too, whose pages are otherwise unchanged. Each share event has exactly one audit writer.

This ticket also builds the **second integration-test fixture whose audit writes fail**. It boots the same unmodified `Program`, and every audit write fails in it. Later tickets add their failure cases to it.

## Acceptance criteria

- [ ] `AuditSaveChangesInterceptor` has a rule for shares: creating one writes `SecretShared` ("Shared with '<recipient>'" plus " until <expiry>" when it expires), and revoking one writes `SecretShareRevoked` ("Revoked access for '<recipient>'"), each with the secret's id and name. The texts match today's.
- [ ] The share service no longer writes these events through `IAuditSink`.
- [ ] HTTP integration tests: sharing and revoking (through the existing Legacy Secret details actions) each produce **exactly one** audit row with the expected action, resource and text.
- [ ] A new collection fixture, in which every audit write fails, boots the unmodified `Program` in Production. Its first test: a share revocation answers the audit-failure response, and afterwards the share still exists and no audit row was added.
- [ ] The failing fixture is documented where the integration tests' setup is described, including why it is separate and that it is slower.
- [ ] Release build with 0 warnings; unit and both integration fixtures pass.

## Blocked by

- #75


### #77 Area 2 · 3/7: Session revocation reports whether it removed anything [CLOSED]
## Parent

#74

## What to build

Revoking a session reports whether it actually revoked anything, in both session registries. Later, Signed-in devices can then answer `404 session_not_found` and write no audit row for a session that isn't the caller's or no longer exists (ADR-0021), in a single-node or a Redis scaled-out deployment alike.

Existing callers (sign-out, account deletion, Legacy Signed-in devices) keep their behaviour.

## Acceptance criteria

- [ ] `ISessionRegistry.RevokeAsync` reports whether it revoked a live session of that account.
- [ ] The in-memory registry's tests cover: the account's own live session (revoked, reported true); another account's session (untouched, reported false); an unknown id (false); an already revoked session (false).
- [ ] The Redis registry gets its first tests, with the same cases, against a Testcontainers Redis whose image is pinned by digest.
- [ ] Every existing caller compiles and behaves as before; the sign-out and account-deletion tests still pass.
- [ ] Release build with 0 warnings; unit and integration tests pass.

## Blocked by

None (can start immediately).


### #78 Area 2 · 4/7: Signed-in devices served by the Angular UI [CLOSED]
## Parent

#74

## What to build

Signed-in devices is served by the Angular UI at `/Sessions` and `/Sessions/Index` and looks and behaves as the Legacy page does (#74, stories 10–22). A vault account can sign out another session after confirming, or every other session at once. A signed-out device loses access on its next request.

This ticket builds two shared pieces every later write Area reuses: an **accessible confirmation dialog** and an **inline status line** announced to screen readers.

Contract (from #74):

```text
GET    /api/ui/sessions                    the account's live sessions, which one is current
DELETE /api/ui/sessions/{sessionId}        204 | 404 session_not_found | 409 current_session | 503 revoked_unrecorded
POST   /api/ui/sessions/sign-out-others    200 { signedOut } | 503 revoked_unrecorded (carrying signedOut)
```

## Acceptance criteria

- [ ] The three endpoints exist with the session-only attributes used by Area 1, `no-store`, the anti-forgery header on writes, and Problem Details codes `session_not_found`, `current_session` (Legacy text) and `revoked_unrecorded` (text in #74 story 19).
- [ ] Signing out one session: the current session gives `409` with no audit row; nothing revoked gives `404` with no audit row; otherwise the session is revoked, then `SessionRevokedByUser` is audited. If that audit write fails, the revocation stands and the answer is `503 revoked_unrecorded`.
- [ ] Sign out all other sessions revokes every other session before auditing any of them, and audits only those revoked. If any audit write fails, the answer is `503 revoked_unrecorded` with the number signed out.
- [ ] Response types are explicit, and the TypeScript is regenerated and committed.
- [ ] The route table, Angular routes (case-insensitive, lazily loaded, title "Signed-in devices — EclipsVault"), the sidebar entry, the palette, and the links from Sign-in history and Security checkup all open the page inside the Angular UI.
- [ ] The page matches Legacy: device icons by device label, "This device" and "Current", relative times with the exact UTC on hover, the empty state, the header text and its link to sign out everywhere, the confirmation texts, and the status texts ("That session was signed out — it loses access on its next request.", "Signed out N other session(s).", "There were no other sessions to sign out.").
- [ ] The confirmation dialog: focus moves into it, Cancel is the default, Escape cancels, focus returns to the button that opened it, and its text is announced. Native `confirm()` is not used.
- [ ] HTTP integration tests: two accounts each list and revoke only their own sessions; parameters a caller adds have no effect; `401` for anonymous and API-key callers; anti-forgery refusal; `no-store`; the pinned JSON shape; every status code; audit rows written exactly when stated above.
- [ ] Failing-audit fixture: a single sign-out revokes (the session's next request is refused) and answers `503 revoked_unrecorded`; sign-out-others revokes every other session and reports the count.
- [ ] Unit tests for the audit-failure paths with a failing audit sink.
- [ ] Vitest: the dialog's behaviour; the page's loading, error with retry, empty, list and status states.
- [ ] Playwright, under the browser-health fixture: the page matches Legacy; signing out a second browser context's session through the dialog ends that context's access; sign-out-others. No CSP, Trusted Types or console errors.
- [ ] Release build with 0 warnings; unit, integration, Vitest, lint, format check and e2e pass.

## Blocked by

- #76
- #77


### #79 Area 2 · 5/7: Your data served by the Angular UI [OPEN]
## Parent

#74

## What to build

Your data is served by the Angular UI at `/Data` and `/Data/Index`. It shows the same account fields, "What's included" cards and notice as Legacy (#74, stories 23–29). Both download buttons download the same JSON file as today. The export is recorded before the file is handed over, and refused when it can't be recorded.

Contract (from #74):

```text
GET  /api/ui/data           the page's account fields and counts
POST /api/ui/data/export    the JSON file, audited first | 503 audit_unavailable
```

## Acceptance criteria

- [ ] Both endpoints exist with the session-only attributes, `no-store`, and the anti-forgery header on the export.
- [ ] The export audits `PersonalDataExported` first, then sends the file with today's body (export schema 1.0, indented), filename `eclipsvault-data-<username>-<yyyymmdd>.json`, `Content-Disposition: attachment`, and `no-store, no-cache, must-revalidate` plus `Pragma: no-cache`.
- [ ] The Angular UI downloads through `HttpClient` with the anti-forgery header, saves through a temporary object URL with the filename from `Content-Disposition`, and revokes the object URL straight afterwards. No second anti-forgery path is added.
- [ ] Response types are explicit, and the TypeScript is regenerated and committed.
- [ ] The route table, Angular routes (title "Your data — EclipsVault"), the sidebar entry and the palette open the page inside the Angular UI.
- [ ] The page matches Legacy: the account fields, the six cards, the notice and both buttons.
- [ ] HTTP integration tests: each account's summary and export contain only its own data; viewing writes no audit row; the export writes exactly one; `401` for anonymous and API-key callers; anti-forgery refusal; the headers above; the pinned JSON shapes.
- [ ] Failing-audit fixture: the export answers `503 audit_unavailable` and no file body is sent.
- [ ] Vitest: the page's loading, error with retry, content, and download-failure states.
- [ ] Playwright, under the browser-health fixture: the page matches Legacy, and the download produces a file with the expected name and an account matching the signed-in vault account. No CSP, Trusted Types or console errors.
- [ ] Release build with 0 warnings; unit, integration, Vitest, lint, format check and e2e pass.

## Blocked by

- #76


### #80 Area 2 · 6/7: Shared by me served by the Angular UI [OPEN]
## Parent

#74

## What to build

Shared by me is served by the Angular UI at `/Secrets/SharedByMe`. The rest of `/Secrets` stays in the Legacy UI. An issuer sees every live share they gave, newest first, and can revoke one after confirming. The recipient loses access on their next request (#74, stories 30–39).

Contract (from #74):

```text
GET    /api/ui/shared-by-me             the issuer's live shares, newest first
DELETE /api/ui/shared-by-me/{shareId}   204 | 404 share_not_found | 503 audit_unavailable
```

## Acceptance criteria

- [ ] Both endpoints exist with the session-only attributes, `no-store` and the anti-forgery header on the revocation. They scope by the issuer's account id (from #75) and never by username.
- [ ] `404 share_not_found` uses the Legacy text "That grant no longer exists, or it wasn't yours to revoke." for both "gone" and "not yours", with no audit row.
- [ ] Revocation is audited atomically (from #76); if the audit write fails, the answer is `503 audit_unavailable` and the share is unchanged.
- [ ] Response types are explicit, and the TypeScript is regenerated and committed. The response carries no issuer username.
- [ ] The route table, Angular routes (title "Shared by me — EclipsVault"), the Sharing group's Shared by me entry and the palette open the page inside the Angular UI; Shared with me still opens the Legacy page.
- [ ] The page matches Legacy: columns, "no expiry", the empty state with "Go to your secrets", the confirmation text naming the recipient and secret, and "Access revoked." Built with the shared dialog and status line from #78.
- [ ] HTTP integration tests: two issuers each list and revoke only their own shares; parameters a caller adds have no effect; the reused-username case against the new endpoints; an issuer who can no longer open the secret can still list and revoke; expired shares and shares on shredded secrets are left out; `401`, anti-forgery, `no-store` and the pinned JSON shape.
- [ ] Failing-audit fixture: a revocation through the new endpoint leaves the share and the audit trail unchanged.
- [ ] Vitest: the page's loading, error with retry, empty, list and status states.
- [ ] Playwright, under the browser-health fixture: the page matches Legacy, and after revoking, the recipient's browser context can no longer open the secret. No CSP, Trusted Types or console errors.
- [ ] Release build with 0 warnings; unit, integration, Vitest, lint, format check and e2e pass.

## Blocked by

- #75
- #76
- #78


### #81 Area 2 · 7/7: Retire the Legacy Area 2 pages and verify the Area [OPEN]
## Parent

#74

## What to build

Signed-in devices, Your data and Shared by me belong entirely to the Angular UI. The Legacy views, view models, display helpers and actions for these pages are deleted, including the Legacy POST actions, so no old write path remains. The whole Area is verified and reviewed, and the PR is ready for the owner.

## Acceptance criteria

- [ ] The Legacy Sessions and Data controllers and views, the Shared by me view and its two Secrets actions, and their view models and display helpers that nothing else uses are deleted. The Legacy layout's sidebar links still work, because they point at URLs the Angular UI now serves.
- [ ] Requests to the old POST actions no longer reach any handler, which a test pins.
- [ ] The Area-policy pin test, the anonymous-endpoint allowlist test and the layering tests pass unchanged or are updated deliberately.
- [ ] Full verification on the final commit: locked restore, Release build with 0 warnings, `dotnet format --verify-no-changes`, unit, both integration fixtures, the migration-data tests, `check:api-types`, lint, format check, Vitest, build checks, production build, `npm audit`, e2e and `e2e:verify-guards`.
- [ ] The `security-reviewer` agent reviews the whole Area diff against ADR-0021 and #74's owner stories; its findings are fixed or answered.
- [ ] A Standards and a Spec review of the whole Area diff, by a different model from the author.
- [ ] The PR body is written to a file for the owner to open the PR. Nothing is pushed by the agent.

## Blocked by

- #78
- #79
- #80


