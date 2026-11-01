## Parent

#SPEC

## What to build

Notifications (`/Notifications`, `/Notifications/Index`) and License (`/License`, `/License/Index`) are served by the Angular UI under `AdminOnly`:

```text
GET    /api/ui/notifications   { delivery: "off"|"smtp"|"log", smtpTarget,
                                 outbox: [{ sentAtUtc, to, subject, body, eventType, transport,
                                            status: "sent"|"failed"|"suppressed", error }] }
GET    /api/ui/license         { status, message, licence: { issuedTo, tier, contact, licenseId, issuedAtUtc,
                                 expiresAtUtc, updatesUntilUtc, maxNodes } | null, updatesLapsed, beyondTier }
```

Both pages are read-only exact copies. The License installation text names `ECLIPSVAULT_LICENSE` or the file at `License:FilePath`; every other sentence, including the commercial ones, is copied word for word. The License endpoint reads the startup snapshot, computes `updatesLapsed` at request time, never starts an evaluation and never fails on licence state.

## Acceptance criteria

- [ ] Endpoints are `AdminOnly`, session-only, `no-store`, with explicit types; TypeScript regenerated.
- [ ] Notifications matches Legacy: the three delivery notices, the filter (shared TableFilter), columns, status badges, the failure marker with its error, the body as the subject's `title`, and the empty state.
- [ ] License matches Legacy: status badge tones, message, the licence details with "never — perpetual" and "unlimited", the update-window notice, the beyond-tier notice and the installation panel (corrected text only).
- [ ] `NotificationsController`, `LicenseController`, their views and view models are removed; the Legacy licence banner stays; the authorization-surface and area tests are updated deliberately.
- [ ] HTTP integration tests: shapes, the authorization matrix and `no-store`; viewing License creates no `FeatureEvaluation` row; it answers 200 with a missing and an invalid licence.
- [ ] Unit tests of the License response for each status, including a signed perpetual licence past every date, which stays valid (Production tests cannot sign a licence).
- [ ] Vitest: both pages' loading, error, empty and populated states, each delivery notice and each licence status.
- [ ] Playwright under browser-health, locally and in CI: the outbox shows a provisioning email captured in this run; License shows the unlicensed state; both licence notices and both sidebars and the palette open the pages; keyboard and both themes.
- [ ] Release build with 0 warnings; every check listed in #T2 passes.

## Blocked by

- #T2
