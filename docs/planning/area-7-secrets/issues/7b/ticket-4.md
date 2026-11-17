## Parent

#SPEC

## What to build

The Angular UI serves the Denied page and files access requests through the UI API (spec Q2, Q10, Q14, Q17):

- `GET /api/ui/access-requests/eligibility?secretId=` (no audit row) and `POST /api/ui/access-requests` on the existing Area 4 controller, with Area 4's refusal codes; filing a decoy answers 404 and trips it.
- `/Account/Denied` served by Angular under the session policy, copied word for word: the reasons when they arrived in navigation memory, the generic paragraph otherwise; the filing form or the refusal sentence from the eligibility endpoint; "Back to secrets" and, from `/api/ui/me` areas, "Manage trusted networks".
- After filing, the page goes to `/AccessRequests?filed=1` or `?refused=<code>`, `secret_not_found` included.
- `AccountController.Denied`, `Views/Account/Denied`, the Legacy `AccessRequestsController` and the rest of `SecretsController` go.

## Acceptance criteria

- [ ] Production HTTP: eligibility and filing shapes and every refusal code; one `AccessRequested` row and no viewed or denial row on filing; anti-forgery and `no-store`; an administrator-only page still redirects a non-administrator to `/Account/Denied`.
- [ ] AuditFailureFixture: filing answers 503 and stores nothing.
- [ ] A retired-routes test pins that `POST /AccessRequests/Create` and every Legacy `/Secrets/*` action no longer reach a handler.
- [ ] Vitest: reasons, generic text, form, refusal sentence.
- [ ] Playwright under browser-health: a refused open → Denied with reasons → filing → Access requests showing it; an under-cleared requester sees the refusal sentence; keyboard and both themes.
- [ ] `scripts/check.sh` passes (locked restore, Release build with 0 warnings, unit tests); `scripts/check.sh integration "<this ticket's filter>"` passes on PostgreSQL; `scripts/check.sh e2e <affected specs>` passes; `dotnet format --verify-no-changes` is clean. CI runs SQL Server and the full browser suite.

## Blocked by

- #T3
