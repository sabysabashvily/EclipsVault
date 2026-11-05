Follows #SPEC5A (Area 5 part 5a, Accounts). This spec is **Area 5 part 5b, Vault settings**: Networks, Notifications and License (ADR-0017 amendment). The owner accepted all 26 grill decisions and both delivery outlines, and confirmed them on 2026-11-01.

## Problem Statement

Opening Networks, Notifications or License from the Angular UI still leaves the app for a Legacy page (ADR-0001).

The grill's survey found these flaws, deductions from reading the code at `a621855`; each ticket proves its finding with a failing test first.

- **Lifting an intrusion-defence block happens before it is audited.** The block lives in Redis or memory, outside the database; if the audit write fails, the range stays unblocked with no record.
- **An off-network administrator session can trust its own address in one click** and then passes the network rule for Confidential and higher secrets. Trusting a network and lifting a block need no fresh proof.
- **The License page's installation text is wrong:** it names a `license.key` file in the content root, which the vault never reads.

## Solution

The Angular UI serves Networks at `/Admin/Networks`, Notifications at `/Notifications` and `/Notifications/Index`, and License at `/License` and `/License/Index`, all under `AdminOnly`, copying the Legacy pages exactly except for the named fixes. Lifting a block is undone when it cannot be recorded (ADR-0028), and trusting a network or lifting a block needs a recent strong sign-in, through the step-up dialog 5a built.

## User Stories

1. As an administrator, the Administration group's Networks, Notifications and License entries, the palette, the Dashboard's Networks action, the Denied page's "Manage trusted networks" and both licence notices open these pages in the Angular UI.
2. As an administrator, I see my address as the vault sees it, trust it or another range (at most /8) with a label, remove a trusted range after confirming, see the configured ranges, and lift a block after confirming.
3. As an administrator whose last strong sign-in is too old, trusting or lifting asks for an authenticator code once, or to sign in again.
4. As anyone, if a lift cannot be recorded, the block stays and the page says the audit trail is unavailable.
5. As an administrator, I see whether email delivery is off, via SMTP or the log transport, and the newest 200 outbox rows with their outcome, and can filter them.
6. As an administrator, I see how the vault is licensed, and correct installation instructions; viewing the page never starts an evaluation and works whatever the licence state.
7. As a non-administrator, these addresses send me to the Denied page and the UI API refuses me.

## Implementation Decisions

### Scope (Q2, Q12)

`AdminController` (Networks and its actions), `NotificationsController` and `LicenseController`, their views and view models go. The Legacy licence banner stays until the final cleanup.

### UI API contract (Q22)

```text
GET    /api/ui/networks        { currentAddress, currentAddressTrusted, configuredRanges,
                                 trustedNetworks: [{ networkId, range, label, addedBy, addedAtUtc }],
                                 blocks: [{ range, reason, blockedAtUtc }] }
POST   /api/ui/networks/trust-current-address → 201 { range }
POST   /api/ui/networks        { range, label } → 201 { range }
DELETE /api/ui/networks/{networkId} → 204
POST   /api/ui/networks/blocks/lift { range } → 204
GET    /api/ui/notifications   { delivery: "off"|"smtp"|"log", smtpTarget,
                                 outbox: [{ sentAtUtc, to, subject, body, eventType, transport,
                                            status: "sent"|"failed"|"suppressed", error }] }
GET    /api/ui/license         { status, message, licence: { issuedTo, tier, contact, licenseId, issuedAtUtc,
                                 expiresAtUtc, updatesUntilUtc, maxNodes } | null, updatesLapsed, beyondTier }
```

`AdminOnly`, session-only, `no-store`, anti-forgery on writes, explicit types, regenerated TypeScript. Problems: `400 validation_failed`; `403 step_up_required`; `404 network_not_found`, `block_not_found` ("That range is not currently blocked."); `409 network_exists`; `503 audit_unavailable`, `unblock_unrecorded`.

### Lifting a block (Q10, ADR-0028)

Lift, then audit. If the audit write fails, restore the exact block record (reason and time) and answer `503 audit_unavailable`. If restoring fails too, answer `503 unblock_unrecorded` and log critical.

### Step-up (Q7)

Trusting the current address, adding a network and lifting a block need a strong sign-in within `StepUp:MaxAuthAgeMinutes`; removing a network never does.

### Monitoring views (Q11)

Notifications and the block reasons show secret and decoy names to every administrator, unfiltered (ADR-0028 consequence).

### License (Q13, Q18)

Exact copy, including the commercial sentences word for word ("never gated", "still receives security patches", "not covered by your current tier", the README pointer). The installation text names `ECLIPSVAULT_LICENSE` or the file at `License:FilePath`. The endpoint reads the startup snapshot, computes `updatesLapsed` at request time, never starts an evaluation and never fails on licence state.

### Presentation (Q23)

Exact copy. Remove and Unblock use ConfirmationDialog with the Legacy text; Trust is unconfirmed. Legacy flash text in the StatusLine. Notifications uses the shared TableFilter, and the body stays a `title` tooltip.

## Testing Decisions

- **Unit:** the lift-then-restore sequence against both blacklist implementations; the License response for each status.
- **SQL Server Production HTTP:** shapes and codes; the authorization matrix; step-up; anti-forgery and `no-store`; viewing License creates no `FeatureEvaluation` row and answers normally with a missing, invalid or expired licence; the configured ranges and current address.
- **AuditFailureFixture:** add, trust-current and remove leave networks unchanged; lift leaves the block in place with its original reason and time.
- **Redis integration:** lift and restore against real Redis.
- **Vitest:** all three pages' states, dialogs, step-up and StatusLine.
- **Playwright** under browser-health, locally and in CI: trust and remove a range (step-up); lift a block written into the run's own Redis for a documentation range (tripping a decoy would block the browser itself); the Notifications list shows a provisioning email; License shows the unlicensed state; links from both shells, palette, Dashboard, Denied page and licence notice; keyboard and both themes.

## Delivery

Branch `area-5b-vault-settings` from `main` after #SPEC5A's PR merges, four sequential tickets, one PR. The `security-reviewer` agent reviews every ticket and the whole part.

## Out of Scope

- Rewording any commercial sentence (the owner's to supply).
- Filtering monitoring views by secret access.
- Capability evaluation status on the License page.
- Any redesign.

## Records

`docs/planning/area-5-administration/`; `CONTEXT.md` (Trusted network, Intrusion-defence block, Notification outbox); ADRs 0017, 0021, 0028.
