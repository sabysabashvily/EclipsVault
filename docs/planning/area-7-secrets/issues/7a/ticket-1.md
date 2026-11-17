## Parent

#SPEC

## What to build

The Angular UI serves the signed-in Overview (spec Q2, Q11, Q13, Q17):

- `GET /api/ui/overview` as specified; staff account count only for administrators, and the account list loaded only for them; `activityScope` "vault" for administrators, "own" otherwise.
- `HomeController.Index` returns the Angular document to a signed-in session and the landing view to a signed-out visitor.
- The page copies `Views/Home/Dashboard` word for word; the expiring notice uses the true count. Administrator cards and links follow `/api/ui/me` areas.
- `Views/Home/Dashboard` and `DashboardViewModel` go.
- `AuditAction.SecretAccessDenied` with its label and activity description, written by 7b and 7c; a denial row shows without the secret's name in My activity, the Overview's own feed and the Your data export, and with it for administrators (spec "Shared denial event").

This ticket also commits the grill's records: `docs/planning/area-7-secrets/`, ADR-0031, the ADR-0017 and ADR-0028 changes, and `CONTEXT.md`.

## Acceptance criteria

- [ ] F10 is first shown failing (accounts loaded for a non-administrator; the notice's count), then fixed.
- [ ] Production HTTP: the response shape; non-administrators get `staffAccounts: null` and their own activity; the totals and expiring list hide another project's, an under-cleared, an off-network, an out-of-window and a decoy secret; session-only and `no-store`.
- [ ] Signed in, `/` serves the Angular document; signed out, the landing page.
- [ ] Production HTTP: a seeded `SecretAccessDenied` row appears without the secret's name in the caller's My activity, Overview feed and data export, and with it in the administrator's audit views.
- [ ] Vitest for both views; Playwright under browser-health for administrator and non-administrator, links, keyboard and both themes.
- [ ] `scripts/check.sh` passes (locked restore, Release build with 0 warnings, unit tests); `scripts/check.sh integration "<this ticket's filter>"` passes on PostgreSQL; `scripts/check.sh e2e <affected specs>` passes; `dotnet format --verify-no-changes` is clean. CI runs SQL Server and the full browser suite.

## Blocked by

None (the branch starts from `main` after Area 6b merges).
