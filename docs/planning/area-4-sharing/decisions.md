# Area 4 Sharing: decision record

Status: all 18 recommendations accepted by the owner (rounds 1 and 2, 2026-10-30). Awaiting the
owner's "confirmed" on this table and the delivery outline. No code, branch, commit or issue exists.

Survey and findings F1–F10: [survey.md](survey.md). Glossary: `CONTEXT.md` (Expired share, Access
request, Requester, Reviewer). ADRs: 0017 (Area 4 joins security review), 0027 (new).

## Decisions

| Ref | Decision | Agreed behaviour |
| --- | --- | --- |
| Q1 | Security review | Area 4 joins ADR-0017's `security-reviewer` list. |
| Q2 | Scope split | Shared with me and Access requests move to Angular; `SecretsController.SharedWithMe`, its view and every `AccessRequestsController` action except Create are deleted. Sharing panel, Details and Denied stay Area 7's. `POST /AccessRequests/Create` stays a Legacy form endpoint, redirecting to `/AccessRequests?filed=1` or `?refused=<code>`, which Angular shows in the StatusLine and strips from the URL. |
| Q3 | Who decides (F1, F2) | A reviewer is anyone who could issue the share from the secret now: administrator or project member who passes the secret's access check. Queue lists only decidable requests, never your own; anything else `404 request_not_found`. |
| Q4 | Who files (F7, F8) | Server decides eligibility and writes the denial reasons; posted `deniedReasons` ignored and removed. Eligible = clearance suffices, outside the project, no active share (network/time ignored). Refusals `share_cannot_help`, already able to open, already pending. Rechecked at approval: `409 request_ineligible`. Honey-token trap kept; one `AccessRequested` row, no metadata-viewed row. |
| Q5 | Atomic transitions (F4, F5) | One transaction per transition through a `DescribeAccessRequestChange` interceptor rule. Approve commits share, status, `SecretShared` and `AccessRequestApproved` together. One winner; others `409 request_not_pending`. Audit failure `503 audit_unavailable` with nothing changed. Email after commit, best-effort. |
| Q6 | Expired shares (F6) | In Core: a new share replaces an expired one in the same transaction, one `SecretShared` row noting it; active duplicates still refused. |
| Q7 | Shared with me filter (F3) | Each row passes the Secrets list's access filter, share included; hidden rows omitted, no count, empty state unchanged. |
| Q8 | Under-cleared shares (F3) | `GrantAsync` refuses a recipient whose clearance is below the sensitivity, for approval and the sharing panel; existing ones stay, hidden by Q7. |
| Q9 | Share by account (F9) | Approval shares to `RequesterUserId`; issuer is the reviewer from the session; usernames are display snapshots only. |
| Q10 | Approval inputs (F10) | `expiresInDays` 0–3650 (0 = none), server-validated `400 validation_failed`; no decision note; a stored note still displays. |
| Q11 | UI API contract | See below. Session-only, `no-store`, anti-forgery on writes, account from the session, explicit response types, regenerated TypeScript. |
| Q12 | Requester-written denial text | Migration in both database sets clears `DeniedReasons` on pending requests; queue shows "—" for them. |
| Q13 | Now-ineligible pending requests | Stay pending and visible to qualified reviewers; Approve answers `409 request_ineligible` with the reason; Reject works; nothing closes automatically. |
| Q14 | Lost share-index race | A duplicate-share save failure answers `409 share_exists`, not `503`; every other save failure still fails closed. Core, so the sharing panel benefits. |
| Q15 | Denied page form | The form appears only when the request would be eligible, otherwise one sentence says why (no audit row for the check). Help text names reviewers as administrators or project members who can open the secret. Hidden `deniedReasons` field removed. |
| Q16 | Presentation | Exact copy of both pages. Approve keeps its inline expiry box, unconfirmed. Reject and Withdraw use ConfirmationDialog with the Legacy wording. Outcomes use the StatusLine with the Legacy flash text word for word. Both lists reload after every write. |
| Q17 | One-winner mechanism | `AccessRequest.Status` is an EF concurrency token; decisions are field-specific updates, never `DbSet.Update`. No schema change. |
| Q18 | Test plan | Unit (eligibility, reviewer rule, share refusal/replacement, filter, bounds); SQL Server Production HTTP (shapes, every code, authorization matrix, 404s, session-only/anti-forgery/no-store, Create redirects, Denied form); AuditFailureFixture for file/approve/reject/withdraw/replacement; races on SQL Server and real PostgreSQL; migration on both; Vitest; Playwright under browser-health with two contexts, run locally, covering file → approve → open, reject, withdraw, under-cleared requester, keyboard, both themes, sidebars and palette. |

## UI API contract (Q11)

```text
GET  /api/ui/shared-with-me
     { shares: [{ secretId, name, project, environment, sensitivity, sensitivityLabel, sharedBy, accessUntilUtc }] }
GET  /api/ui/access-requests
     { reviewScope: "all" | "project",
       toReview: [{ requestId, secretName, project, requester, reason, deniedBecause, requestedAtUtc }],
       mine:     [{ requestId, secretName, project, status, requestedAtUtc, decidedBy, decidedAtUtc, decisionNote }] }
POST /api/ui/access-requests/{requestId}/approve   { expiresInDays }  → 204
POST /api/ui/access-requests/{requestId}/reject                       → 204
POST /api/ui/access-requests/{requestId}/withdraw                     → 204
```

Problems: `400 validation_failed`; 401 stale session; `404 request_not_found`; `409
request_not_pending`, `request_ineligible` (with reason), `share_exists`; `503 audit_unavailable`.

## Carried over unchanged

Decision emails (text, best-effort, outbox); no email on filing or withdrawal; one pending request per
secret and requester; reason ≥3 characters, cut to 500; honey-token trap on filing; area keys and
`VaultPolicies.Session` visibility; Your data export fields; an approved share appears on the
reviewer's Shared by me.

## Delivery outline

Six sequential tickets on one branch from `main`, one PR at the end. Tests land with their behaviour.
Each page is served in the ticket that builds it (Area 2's pattern), so no page is staged unserved.
Issue drafts: [issues/](issues/). Lessons applied: [lessons.md](lessons.md).

| Ticket | Behaviour | Decisions |
| --- | --- | --- |
| 1 | Shares refuse recipients they cannot help and replace expired ones; commits the records | Q6, Q8, Q14 |
| 2 | Commit each access-request change with its audit rows, one winner | Q5, Q9, Q10, Q17 |
| 3 | File and decide access requests under the sharing rule | Q3, Q4, Q12, Q13, Q15 |
| 4 | Shared with me served by the Angular UI | Q2 (part), Q7, Q11 (part), Q16 |
| 5 | Access requests served by the Angular UI | Q2, Q11, Q16, Q18 browser flows |
| 6 | Verify the whole Area and prepare its single PR | whole-Area reviews and gates |
