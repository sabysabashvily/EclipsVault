# Area 4 Sharing: grill checkpoint

Branch for the grill: none. Checkout on `main` at `f31b0a8`. Records stay uncommitted; the first
ticket commits them. Survey: [survey.md](survey.md) (findings F1–F10).

## Round 1: asked 2026-10-30, all ten recommendations ACCEPTED by the owner

| Q | Topic | Recommendation |
| --- | --- | --- |
| Q1 | Security review | Area 4 joins ADR-0017's `security-reviewer` list (2–8); one sentence, like Area 2's. |
| Q2 | Scope split | Shared with me moves (route table + delete `SecretsController.SharedWithMe` and its view). Sharing panel, Details and Denied stay with Area 7. Access requests moves whole to `/api/ui/access-requests`, except `POST /AccessRequests/Create`, which stays a Legacy form endpoint for the Denied page, rewired to fixed Core, redirecting to `/AccessRequests?filed=1` or `?refused=<code>`; Angular shows it in the StatusLine and strips it from the URL. |
| Q3 | Who decides (F1, F2) | The sharing-panel rule: administrator or project member and the secret's ABAC read passes at decision time. Queue lists only decidable requests (same filter as the Secrets list), never your own. Anything else is `404 request_not_found`. |
| Q4 | Who files (F7, F8) | Server computes eligibility and denial reasons; posted `deniedReasons` ignored then removed. File only when clearance passes, project fails and no active share; network/time ignored. Refusals: `share_cannot_help`, already able to open, already pending. Rechecked at approval: `409 request_ineligible`. Honey-token trap kept; one `AccessRequested` row, no metadata-viewed row. |
| Q5 | Atomic transitions (F4, F5) | One transaction per transition via a `DescribeAccessRequestChange` interceptor rule; approve = share + status + `SecretShared` + `AccessRequestApproved`. Pending-only with a concurrency check: one winner, others `409 request_not_pending`. Audit failure `503 audit_unavailable`, nothing changes. Email after commit, best-effort. New ADR with Q3, Q4, Q8. |
| Q6 | Expired shares (F6) | In Core: a new share replaces an expired one in the same transaction; one `SecretShared` row noting the replacement, no revoked row. Active duplicates still refused. |
| Q7 | Shared with me filter (F3) | Same ABAC enumeration filter as the Secrets list, grant included; hidden rows omitted, empty state unchanged, no hidden count. |
| Q8 | Under-cleared shares (F3) | `GrantAsync` refuses a recipient whose clearance is below the sensitivity, for both callers; existing ones stay, hidden by Q7. |
| Q9 | Share by account (F9) | Approval shares to `RequesterUserId`, issuer from the session; usernames are display snapshots only. |
| Q10 | Approval inputs (F10) | `expiresInDays` 0–3650 (0 = none), server-validated `400 validation_failed`; no decision note on either endpoint; a stored note still displays. |

Recorded: ADR-0017 lists Area 4 for the security reviewer; ADR-0027 records Q3–Q8; CONTEXT.md
gains Expired share, Access request, Requester and Reviewer. All uncommitted.

## Round 2: asked 2026-10-30, all eight recommendations ACCEPTED by the owner

Q11 UI API contract, Q12 existing requester-written denial text, Q13 existing ineligible requests,
Q14 lost share-index race, Q15 Denied page form, Q16 presentation, Q17 concurrency mechanism,
Q18 test plan.

ADR-0027 gained Q12, Q13 and Q14. No questions remain; see [decisions.md](decisions.md).
