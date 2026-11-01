## Parent

#SPEC

## What to build

Access requests is served by the Angular UI at `/AccessRequests` and `/AccessRequests/Index`. Filing stays a Legacy form post from the Denied page, which now redirects into the Angular page with its outcome.

```text
GET  /api/ui/access-requests
     { reviewScope: "all" | "project",
       toReview: [{ requestId, secretName, project, requester, reason, deniedBecause, requestedAtUtc }],
       mine:     [{ requestId, secretName, project, status, requestedAtUtc, decidedBy, decidedAtUtc, decisionNote }] }
POST /api/ui/access-requests/{requestId}/approve   { expiresInDays }  → 204
POST /api/ui/access-requests/{requestId}/reject                       → 204
POST /api/ui/access-requests/{requestId}/withdraw                     → 204
```

## Acceptance criteria

- [ ] Endpoints are session-only, `no-store`, anti-forgery-protected on writes, take the account only from the session, and return explicit types; the TypeScript is regenerated and committed.
- [ ] Problems: `400 validation_failed`; `404 request_not_found` for every request the caller cannot decide or withdraw; `409 request_not_pending`, `request_ineligible` (with its reason), `share_exists`; `503 audit_unavailable`. A 401 only for an unusable session.
- [ ] `POST /AccessRequests/Create` redirects to `/AccessRequests?filed=1` or `/AccessRequests?refused=<code>`; the page shows the Legacy text in the StatusLine and removes the parameter. Unknown codes show nothing.
- [ ] The route table, Angular routes (title "Access requests — EclipsVault"), both sidebars' Access entries and the palette open the page inside the Angular UI. Every other `AccessRequestsController` action, its view and view model are removed; the authorization-surface and area tests are updated deliberately.
- [ ] The page matches Legacy: subtitle, both panels, "All projects"/"Your project", columns, "—" for missing reasons, status badges, decision text, the inline expiry box (0–3650, default 0, same title and label), both empty states. Reject and Withdraw use ConfirmationDialog with the Legacy wording; Approve is unconfirmed. Outcomes use the Legacy flash text in the StatusLine; both lists reload after every write.
- [ ] HTTP integration tests through the new endpoints: the authorization matrix from #T3; the pinned JSON shapes; every problem code; anti-forgery and `no-store`; the Create redirect codes.
- [ ] AuditFailureFixture: approve, reject and withdraw through the new endpoints answer `503 audit_unavailable` with nothing changed.
- [ ] Vitest: loading, error with retry, empty and list states for both panels, the dialogs, every StatusLine outcome and the filing-outcome parameter.
- [ ] Playwright under the browser-health fixture, locally and in CI, with contexts from `browserHealth.newContext()`: a requester in another project is denied, files on the Legacy Denied page and sees "filed" on the Angular page; a reviewer approves with an expiry and the requester sees it in Shared with me; reject and withdraw through the dialog; an under-cleared requester sees no form; keyboard and both themes. Accounts come from the normal administrator pages, and specs don't wait out real sign-in throttles.
- [ ] Release build with 0 warnings; unit, integration, Vitest, lint, format check, build checks, `check:api-types`, build, audit, `e2e` and `e2e:verify-guards` pass.

## Blocked by

- #T3
- #T4
