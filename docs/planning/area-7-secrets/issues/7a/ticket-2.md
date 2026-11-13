## Parent

#SPEC

## What to build

The Angular UI serves Secrets and New secret, and both palettes search through the UI API (spec Q2, Q8, Q13, Q17):

- `GET /api/ui/secrets`, `GET /api/ui/secrets/search` and `POST /api/ui/secrets` as specified. The search keeps the Legacy contract exactly; the Angular palette and the Legacy `site.js` palette both call it.
- A taken name answers `409 name_taken` and stores nothing; a policy refusal `403 creation_denied` with the reasons. A successful create sets the Legacy success flash and the page goes to `/Secrets/Details/{id}`.
- `/Secrets`, `/Secrets/Index` and `/Secrets/Create` join the route table; `SecretsController.Index`, `Search` and `Create`, their views and view models go.
- Sign-in's default landing survives that removal (Area 8 finding F1, accepted by the owner): `AccountController.SignInTarget` defaults to `Url.Action("Index", "Secrets")`, which returns null once `SecretsController.Index` is gone. `LocalRedirect` then throws after `SessionIssuer` has recorded `LoginSucceeded` and set the cookie, so every sign-in without a return URL ends on the error page. Replace the computed default with the literal `/Secrets`. SSO's `Dashboard` default (Area 8 F2) stays with Area 8a.

## Acceptance criteria

- [ ] F6 is first shown failing (a taken name answers 503), then fixed.
- [ ] Area 8's F1 is first shown failing once `SecretsController.Index` is removed (a Production HTTP password sign-in with no return URL ends on the error page), then fixed: it lands on `/Secrets`.
- [ ] Unit: the search contract as a literal table (1 character, prefix before contains, 8-row cap, decoy excluded, project match).
- [ ] Production HTTP: shapes; the access filter on list and search; `name_taken` for another project's name and a decoy's name; `creation_denied` for another project and above clearance; one `SecretCreated` row; anti-forgery and `no-store`.
- [ ] AuditFailureFixture: create answers 503 and stores nothing.
- [ ] A retired-routes test pins that `/Secrets/Search` and `POST /Secrets/Create` no longer reach a handler.
- [ ] Vitest for list, create form and errors; Playwright under browser-health for the list, both palettes, create, a taken name, keyboard and both themes.
- [ ] `scripts/check.sh` passes (locked restore, Release build with 0 warnings, unit tests); `scripts/check.sh integration "<this ticket's filter>"` passes on PostgreSQL; `scripts/check.sh e2e <affected specs>` passes; `dotnet format --verify-no-changes` is clean. CI runs SQL Server and the full browser suite.

## Blocked by

- #T1
