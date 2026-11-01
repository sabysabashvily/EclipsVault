Follows #83 (Area 3, merged in #94). ADR-0017 puts **Area 4: Sharing** next: Shared with me and Access requests. The owner accepted all 18 grill decisions and the six-ticket delivery outline.

## Problem Statement

Opening Shared with me or Access requests from the Angular UI still leaves the app for a Legacy page. The Angular UI has to replace every Razor view (ADR-0001), and these two pages are where a vault account receives access to another project's secret and where reviewers decide who gets it.

The grill's source survey found these flaws. They are deductions from reading the code at `f31b0a8`, not reproduced exploits; each ticket proves its finding with a failing test first.

- **A reviewer need not be able to read the secret.** Approving checks only "administrator or same project". The sharing panel also requires the issuer to pass the secret's access check, so a low-clearance project member can approve a share they could not issue directly.
- **The review queue shows names the reviewer may not list.** It shows secret names and projects that the Secrets list hides from the same reviewer.
- **Shared with me runs no access filter.** It shows name, project, environment and sensitivity for every share, including one to a recipient whose clearance is too low (sharing never checks it) and one seen off the trusted network.
- **Every request change saves first and audits second.** Filing, approving, rejecting and withdrawing leave an unaudited change when the audit write fails. Approval is split across two transactions.
- **Decisions race.** Approve against Withdraw or Reject is last-writer-wins, so a request can end Withdrawn or Rejected with a live share and a "declined" email.
- **An expired share blocks re-sharing for good.** The unique index includes expired rows, so approvals and the sharing panel both fail with "already has a grant".
- **The reviewer reads requester-written "Denied because" text** from a hidden form field.
- **Requests are accepted that no share could satisfy**, revealing the secret's name to someone whose clearance is too low, and writing a "metadata viewed" audit row.
- **Approval resolves the requester by username**, and its expiry has no server bound (a large value is a 500).
- **A lost race on the unique share index is reported as an audit failure.**
- **Access requests have no tests at all.**

## Solution

The Angular UI serves Shared with me at `/Secrets/SharedWithMe` and Access requests at `/AccessRequests` and `/AccessRequests/Index`, copying the Legacy pages exactly except for the named fixes. The rest of `/Secrets`, the secret's sharing panel, Details and the Denied page stay in the Legacy UI for Area 7. Filing a request stays a Legacy form post from the Denied page, now redirecting into the Angular page with its outcome.

Behind both pages, one rule issues a share (ADR-0027): a reviewer is anyone who could issue the share from the secret itself, and a share must be able to help its recipient. Every request change commits atomically with its audit rows and has exactly one winner. These rules live in Core, so the Legacy sharing panel obeys them too.

## User Stories

### Reaching the pages

1. As a vault account, the Sharing group's Shared with me entry, the Access group's Access requests entry and the ⌘K palette open both pages inside the Angular UI, from either shell.
2. As a vault account, my bookmarks `/Secrets/SharedWithMe`, `/AccessRequests` and `/AccessRequests/Index` keep working in any letter case, with the existing titles and the sign-in return URL.
3. As a vault account, Open on a shared secret, and both empty states' "secrets" links, take me to the Legacy Secrets pages on the same session.

### Shared with me

4. As a recipient, I see the secrets shared with me that I could list right now: name, project, environment, sensitivity, who shared it and until when.
5. As a recipient, a share I cannot use (clearance too low, off the trusted network, outside the production window) is not shown, and nothing counts the hidden ones.
6. As a recipient with nothing visible, I see the existing empty state.

### Filing a request

7. As a vault account denied a secret only because it belongs to another project, the Denied page offers Request access, and my request reaches the reviewers with the reasons the server recorded.
8. As a vault account whose clearance is too low, already able to open the secret, or with a request already pending, the Denied page tells me why instead of offering the form.
9. As a requester, after filing I land on Access requests and see whether it was filed or why it was refused.

### Reviewing

10. As a reviewer, I see only requests I could decide now (never my own), labelled "All projects" for an administrator and "Your project" otherwise.
11. As a reviewer, I approve with an optional expiry in days (0 = none, at most 3650); the share is created with me as its issuer and appears on my Shared by me.
12. As a reviewer, I reject after confirming; the requester is emailed either way.
13. As a reviewer, a request that was decided or withdrawn meanwhile, or that a share can no longer help, is refused with the reason, and nothing changes.
14. As a reviewer below the secret's sensitivity, off-network, or outside its project, I neither see nor can decide the request.

### My requests

15. As a requester, I see every request I filed, newest first, with status, decision and decider, and can withdraw a pending one after confirming.

### Failure and integrity

16. As anyone, if the audit trail cannot be written, filing, approving, rejecting or withdrawing changes nothing and says the audit trail is unavailable.
17. As an issuer, sharing with someone whose share expired works, and sharing with someone whose clearance is too low is refused with the reason.

## Implementation Decisions

### Scope (Q2)

- Shared with me moves: the route table owns `/Secrets/SharedWithMe`; `SecretsController.SharedWithMe` and its view go.
- Access requests moves: list, approve, reject and withdraw; every `AccessRequestsController` action and view except Create goes.
- `POST /AccessRequests/Create` stays a Legacy form endpoint for the Denied page, redirecting to `/AccessRequests?filed=1` or `/AccessRequests?refused=<code>`. The Angular page shows the outcome in the StatusLine and removes the parameter from the URL.

### UI API contract (Q11)

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

Session-only, `no-store`, anti-forgery on writes, account only from the session, explicit response types, regenerated TypeScript. Problems: `400 validation_failed`; 401 for an unusable session; `404 request_not_found` for every request the caller cannot decide or withdraw; `409 request_not_pending`, `request_ineligible` (with its reason), `share_exists`; `503 audit_unavailable`.

### Sharing rule (ADR-0027: Q3, Q4, Q6, Q7, Q8, Q9, Q14)

- A reviewer is an administrator or member of the secret's project who passes the secret's access check at decision time. The queue uses the same filter; own requests are excluded.
- Filing is allowed only when the requester's clearance suffices, they are outside the secret's project, and they hold no active share; network and time are ignored. Refusals: `share_cannot_help`, already able to open, already pending. Approval re-checks: `409 request_ineligible`.
- The server writes the denial reasons at filing; the posted `deniedReasons` field is ignored and removed. Filing keeps the honey-token trap and writes one `AccessRequested` row, no metadata-viewed row.
- `GrantAsync` refuses a recipient whose clearance is below the secret's sensitivity, replaces an expired share in the same transaction (one `SecretShared` row noting it), and still refuses an active duplicate.
- A save that fails because a share for that recipient now exists answers `409 share_exists`; every other failed save still fails closed.
- Approval shares to `RequesterUserId` with the reviewer as issuer. Usernames are display snapshots.
- Shared with me passes each row through the Secrets list's access filter, share included.

### Atomic transitions (Q5, Q10, Q17)

- `AuditSaveChangesInterceptor` gains `DescribeAccessRequestChange`; the service stops writing these events through `IAuditSink`. Approval commits share, status, `SecretShared` and `AccessRequestApproved` in one transaction.
- `AccessRequest.Status` is an EF concurrency token; every update is field-specific. Exactly one of approve, reject or withdraw wins; losers get `409 request_not_pending`.
- Approval takes `expiresInDays` 0–3650, validated in Core. No decision note is accepted; a stored note still displays.
- Decision email goes after commit, best-effort, text unchanged.

### Existing data (Q12, Q13)

- A migration in both sets clears `DeniedReasons` on pending requests; the queue shows "—" for them.
- Pending requests the rule now refuses stay pending; Approve answers `409 request_ineligible`; Reject works.

### Legacy Denied page (Q15)

- The Request access form appears only when the request would be eligible; otherwise one sentence says why. The eligibility check writes no audit row.
- Help text: "Ask a reviewer, an administrator or a member of this secret's project who can open it, to share it with you." The hidden `deniedReasons` field goes.

### Presentation (Q16)

Exact copy: titles, subtitles, panels, columns, badges, empty states, UTC formats, button sizes. Approve keeps its inline expiry box, unconfirmed. Reject and Withdraw use ConfirmationDialog with the Legacy wording. Outcomes use the StatusLine with the Legacy flash text word for word. Both lists reload after every write.

## Testing Decisions

- **Unit:** filing eligibility as a literal table; reviewer rule; share refusal, expired replacement and active duplicate; Shared with me filter; approval bounds.
- **SQL Server Production HTTP:** every endpoint's shape and code; an authorization matrix (administrator below sensitivity, low-clearance project member, other-project member, the requester, an off-network reviewer); `404` for undecidable ids; session-only, anti-forgery and `no-store`; the Create redirect codes; the Denied form shown or hidden; Legacy sharing panel refusals.
- **AuditFailureFixture**, set up first and then rejecting: filing, approving, rejecting, withdrawing and replacing an expired share each leave everything unchanged and answer the audit failure.
- **Races on SQL Server and real PostgreSQL:** approve vs withdraw, approve vs reject, two approvals, approve vs the sharing panel. One winner; no share outlives a losing decision.
- **Migration:** the `DeniedReasons` clean-up on both providers.
- **Vitest:** both pages' loading, error with retry, empty, list, dialog and StatusLine states, including the filing outcome parameter.
- **Playwright** under the browser-health fixture, run locally and in CI, with extra contexts from `browserHealth.newContext()`: denied → file on Legacy Denied → outcome on the Angular page; approve with an expiry → requester sees it in Shared with me → Open into Legacy Details; reject; withdraw; under-cleared requester sees no form; links from both sidebars and the palette; keyboard and both themes. Accounts in other projects and clearances come from the normal administrator pages; specs avoid waiting out real sign-in throttles.

## Delivery

One branch `area-4-sharing` from `main`, six sequential tickets, one PR. Each page is served by the Angular UI in the same ticket that builds it, so its browser tests prove the served page. The first ticket commits `CONTEXT.md`, ADR-0017's amendment, ADR-0027 and `docs/planning/area-4-sharing/`. The `security-reviewer` agent reviews every ticket that changes sharing, authorization or auditing, and the whole Area before the PR.

## Out of Scope

- The secret's sharing panel, Details, the Denied page as a whole, and Grant/RevokeGrant (Area 7). Only the narrow Denied form and Core rule changes above touch them.
- Notifications to reviewers on filing, or to anyone on withdrawal; decision notes in the UI.
- Whether denied access to Details is audited as "Viewed" (open owner question, Area 7).
- Any redesign of either page.

## Records

Survey, decisions and contract: `docs/planning/area-4-sharing/`. Glossary: `CONTEXT.md` (Share, Issuer, Recipient, Expired share, Access request, Requester, Reviewer). ADRs: 0017 (amended), 0021, 0027.
