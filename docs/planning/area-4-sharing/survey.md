# Area 4 Sharing: Legacy survey

Surveyed on `main` at `f31b0a8` by reading the code. Nothing was run, and no finding below has been
reproduced yet; each one is a code-read claim that a ticket must prove with a failing test first.

## What serves the two pages today

| Page | Route | Served by | Reads | Writes |
| --- | --- | --- | --- | --- |
| Shared with me | `/Secrets/SharedWithMe` | `SecretsController.SharedWithMe` (Area 7's controller), `Views/Secrets/SharedWithMe.cshtml` | `ISecretGrantService.ListSharedWithUserAsync(userId)` | none |
| Access requests | `/AccessRequests`, `/AccessRequests/Index` | `AccessRequestsController`, `Views/AccessRequests/Index.cshtml`, `AccessRequestsViewModel` | `ListToReviewAsync(isAdmin, project)`, `ListMineAsync(userId)` | `Approve`, `Reject`, `Cancel` (Withdraw) |
| (Denied page form) | `POST /AccessRequests/Create` | `AccessRequestsController.Create`, posted by `Views/Account/Denied.cshtml` | `ISecretService.GetDetailsAsync(secretId)` | `IAccessRequestService.CreateAsync` |

Core: `AccessRequestService` (Core/Application/AccessRequests), `SecretGrantService`, `NotificationService`.
Infrastructure: `AccessRequestRepository`, `SecretGrantRepository`, `AuditSaveChangesInterceptor`
(describes `SecretGrant` Added/Deleted only; no rule for `AccessRequest`).

## Behaviour

**Shared with me** lists every unexpired share whose secret is neither shredded nor expired: name,
project, environment, sensitivity, "Shared by" (`GrantedBy` username snapshot), "Access until" (UTC,
`yyyy-MM-dd HH:mm` or "no expiry"), and an Open button to `/Secrets/Details/{id}` (Legacy, Area 7).
Empty state links to `/Secrets`. No ABAC filter runs on this list.

**Access requests** has two panels.
- *To review*: pending requests for every project (administrator) or for the caller's project claim
  (anyone else), labelled "All projects" / "Your project". Columns: secret name + project badge,
  requester username, reason, "Denied because", requested time. Approve has a number input for share
  expiry in days (0 = none, HTML `max=3650`) and no confirmation. Reject has a `data-confirm`. The view
  never sends a decision note, although the controller and service accept one.
- *My requests*: every request the caller filed, newest first, with status badge, decision
  (`DecidedBy · date — note`) and a Withdraw button (with `data-confirm`) while pending. Empty state
  links to `/Secrets`.

**Filing** happens only from the Legacy Denied page, which a failed secret ABAC check redirects to
with `?secretId=`. The form posts `secretId`, `reason` and a hidden `deniedReasons` field holding the
denial text. Reason is trimmed, needs ≥3 characters, and is silently cut to 500. One pending request
per (secret, requester). Outcome is a TempData flash on `/AccessRequests`.

**Approval** (administrator, or any account whose project claim equals the request's project):
if the requester has no active share, `SecretGrantService.GrantAsync(secretId, requesterUsername,
ttlDays)` creates one with the approver as issuer; then the request becomes Approved, an
`AccessRequestApproved` row is written, and the requester is emailed.

**Notifications**: approve and reject email the requester (secret name, reviewer username, optional
note) and record the body in the notification outbox; best-effort, failures swallowed. Nothing is
sent to reviewers when a request is filed, or to anyone on withdrawal.

**Audit actions**: `AccessRequested`, `AccessRequestApproved`, `AccessRequestRejected`,
`AccessRequestCancelled` (standalone `IAuditSink` rows on the secret), plus `SecretShared` from the
interceptor when approval creates a share. My activity describes all of them (Sharing category).

**Links in**: Legacy sidebar (Sharing → Shared with me; Access → Access requests), Angular sidebar
and ⌘K palette (`shared-with-me`, `access-requests` area keys, both `VaultPolicies.Session`), Area 2's
Shared by me e2e ("Shared with me opens Legacy"), the Denied page form, the request-filed redirect.
**Links out**: Open → `/Secrets/Details/{id}`; both empty states → `/Secrets`.

**Tests**: none for access requests beyond palette navigation and the data-export count. Shared with
me is only asserted as a by-product of Area 2's share tests.

## Findings

F1. **A reviewer need not be able to read the secret.** Approve/Reject check only
"administrator or same project claim". The sharing panel (`SecretsController.Grant`) also requires
the issuer to pass the secret's ABAC read check. So a low-clearance project member, or an
administrator below the secret's sensitivity, can approve a share they could not issue directly.

F2. **The review queue discloses names the reviewer may not enumerate.** The queue shows secret
name, project and the requester's reason for every pending request in the project. The Secrets list
hides those same names from that reviewer by ABAC ("a name is not nothing").

F3. **Shared with me is an unfiltered enumeration route.** It shows name, project, environment and
sensitivity for every share, including a share to a recipient whose clearance is below the secret's
sensitivity (sharing never checks the recipient's clearance) and off the trusted network or outside
the production window, where the Secrets list would hide the row.

F4. **Every request transition saves first and audits second.** Create, Approve, Reject and Withdraw
commit the request row, then write a standalone audit row. If that write fails, the change stands
unaudited (fail-closed violation). Approval commits its share with `SecretShared` atomically, then
the status change and `AccessRequestApproved` separately, so a failure can leave a share and an
Approved request with no approval record.

F5. **Decisions race.** No concurrency check on the request; updates use `DbSet.Update` (whole row,
last writer wins). Approve vs Withdraw can end Withdrawn with a live share, or Approved after the
requester withdrew; Approve vs Reject can end Rejected with a live share and a "declined" email.
Two concurrent approvals can hit the unique share index, which surfaces as an audit failure.

F6. **An expired share blocks every future share to that person.** The unique index on
(secret, recipient) includes expired rows, and `GrantAsync` refuses when any row exists. Approval
of a request from someone whose share expired always fails with "already has a grant"; the sharing
panel fails the same way. Expired shares are invisible on Shared with me and Shared by me.

F7. **The reviewer reads requester-written "Denied because" text.** `deniedReasons` is a hidden form
field, stored as posted (no length check against the 2000-character column). A requester can write
anything there, e.g. "pre-approved by security".

F8. **Requests are accepted that no share can satisfy, and filing reveals the name.** A share lifts
only the project rule. A request is accepted when the requester's clearance is too low, or when they
can already open the secret, and the request then shows its name and project in My requests. The
lookup also writes a `SecretMetadataViewed` row for the denied requester.

F9. **Approval resolves the requester by username.** `GrantAsync` re-looks up
`request.RequesterUsername` through the username-or-email lookup instead of using
`RequesterUserId`. Deleting the requester cascades the request away today, so this is hardening.

F10. **Approval expiry is unbounded on the server.** A large `ttlDays` overflows `AddDays` → 500.

## Facts that are not findings

- Project keys are stored upper-case everywhere, so the project comparisons agree across databases.
- Changing an account's clearance or project ends its sessions, so a stale project claim cannot keep
  reviewer rights.
- An approved share's issuer is the approver, so it appears on the approver's Shared by me (Area 2).
- A request for a honey token trips it during filing (404 and session revocation).
- Secret and requester deletion cascade-delete requests and shares.
- The "denied access audited as Viewed" question on the Details page is still open with the owner;
  it is Area 7's, not this Area's.
