This spec is **Area 7 part 7a, Overview and Secrets list** (ADR-0017 amendment). Parts 7b, Secret details (#SPEC7B), and 7c, Dynamic secrets (#SPEC7C), follow after this part's PR merges. The owner accepted all 23 grill decisions and the three delivery outlines, and confirmed them on 2026-11-11.

## Problem Statement

Opening Overview, Secrets or New secret from the Angular UI still leaves the app for a Legacy page (ADR-0001), and both command palettes search through a Legacy endpoint.

The grill's survey found these flaws, deductions from reading the code at `ab7997e`; each ticket proves its finding with a failing test first.

- **A taken name reads as an outage (F6).** Secret names are unique across the vault. Creating one with a name another project or a decoy already uses fails the save, and the page says "Vault unavailable (fail-closed)".
- **Overview figures (F10).** The service loads every account for every caller, though only administrators see the count; the "n secrets will expire" notice counts the 8-row table, not every expiring secret.

## Solution

The Angular UI serves the signed-in Overview at `/`, and Secrets and New secret at `/Secrets`, `/Secrets/Index` and `/Secrets/Create`, copying the Legacy pages exactly except for the named fixes. Both palettes search through the UI API with the Legacy contract unchanged. A taken name is a conflict (ADR-0031).

## User Stories

1. As anyone signed in, `/` opens the Overview in the Angular UI; signed out, it still shows the landing page.
2. As anyone, the Overview shows only secrets I could open now: totals by environment, the true number expiring within 7 days, and up to 8 expiring soon with a link to rotate each.
3. As a non-administrator, I see my own recent activity and the vault-wide critical count; as an administrator, vault-wide activity, the critical count, the staff account count and links to Audit log, Users, New user and Networks.
4. As anyone, the Secrets list shows exactly the secrets I could open now, never a decoy, and opening one goes to its details.
5. As anyone, either palette finds secrets I could open by name or project after two characters, at most eight, prefix matches first.
6. As anyone, I create a secret in my project (any project as an administrator) at or below my clearance, and land on its details with the Legacy success message.
7. As anyone, a taken name tells me so and keeps my form; a refused project or sensitivity shows the policy's reasons.

## Implementation Decisions

### Scope (Q2)

`HomeController.Index` serves the Angular document to a signed-in session and the landing view otherwise. `SecretsController.Index`, `Search` and `Create`, `Views/Home/Dashboard`, `Views/Secrets/Index` and `Create`, and their view models go. Landing and `Home/Error` stay until Area 9; `AuditDisplay` stays.

### UI API contract (Q13)

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

Session-only, `no-store`, anti-forgery on the POST, explicit types, regenerated TypeScript. Search: fewer than 2 characters → `[]`; decoys excluded; the list's access filter; name or project contains `q`; prefix matches first, then name; 8 rows; `url` is `/Secrets/Details/{id}`. Reads write no audit row. Administrator cards and links come from `/api/ui/me` areas (ADR-0012); the default project from `/api/ui/me`. Accounts are loaded only for administrators. Problems: `400 validation_failed` (name 3–128, value ≤ 8192, project ≤ 64); `403 creation_denied` with reasons; `409 name_taken`; `503 audit_unavailable`. Until 7b, a successful create sets the Legacy success flash for the Legacy Details page.

### Shared denial event (Q3)

This part adds `AuditAction.SecretAccessDenied` (its stored number, label and activity description) and withholds the secret's name from a denial row in self-service views: My activity, the Overview's own feed and the Your data export; administrators still see it. Nothing in 7a writes the event. 7b and 7c, which may run side by side, only write it, so neither branch adds an audit action number the other would collide with (the column stores the number).

### Presentation (Q17)

Exact copy of the three pages, word for word. Deliberate: the expiring notice uses `expiringWithin7Days`; `name_taken` reads "A secret with that name already exists."; Create's policy refusal shows the reasons as Legacy does.

## Testing Decisions

- **Unit:** search contract (2 characters, 8 rows, ordering, decoys); Overview figures, including the true expiring count and no account load for non-administrators; a denial row's name withheld in each self-service view.
- **Production HTTP, locally on PostgreSQL through `scripts/check.sh integration "<filter>"`, both databases in CI:** shapes; the access filter (other project, under-cleared, off-network, outside the window, decoy) on list, search and Overview; administrator-only figures; session-only, anti-forgery, `no-store`; `name_taken` for a name in another project and a decoy's name; `creation_denied`; one `SecretCreated` row.
- **AuditFailureFixture:** create answers 503 and stores nothing.
- **Vitest:** every page state, both Overview views, form errors.
- **Playwright** under browser-health: Overview as administrator and non-administrator, list, both palettes, Create including a taken name, keyboard, both themes. Each ticket verifies with `scripts/check.sh`, adding `integration "<filter>"` and `e2e <affected specs>`; CI runs SQL Server and the full browser suite.

## Delivery

Branch `area-7a-overview-and-secrets` from `main` after Area 6b merges, three sequential tickets, one PR. Claude Opus 5.5 (high) implements; the `security-reviewer` agent reviews every ticket and the whole part; Standards and Spec reviews go to GPT Sol 6.1 at xhigh; the PR gets GPT Sol and Thermos before the owner merges.

## Out of Scope

- Secret details, Denied, filing (7b); Dynamic secrets (7c).
- The landing page and `Home/Error` (Area 9).
- Per-project name uniqueness.
- Any redesign.

## Records

`docs/planning/area-7-secrets/`; `CONTEXT.md` (Share, Secrets section); ADRs 0017, 0028, 0031.
