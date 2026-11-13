## Parent

#SPEC

## What to build

Encryption keys is served by the Angular UI at `/Encryption` and `/Encryption/Index` (route table entries under `AdminOnly`):

```text
GET  /api/ui/encryption        { currentKekId, loadedKekIds,
                                 keys: [{ kekId, status: "current"|"retired"|"not_loaded", secrets, versions }],
                                 awaitingRotation, onMissingKeys }
POST /api/ui/encryption/rotate → 200 { currentKekId, secretsRewrapped, versionsRewrapped, onMissingKeys }
```

`awaitingRotation` counts rows a pass can move; `onMissingKeys` counts rows under master keys that are not loaded. Rotation needs no step-up.

## Acceptance criteria

- [ ] Endpoints are `AdminOnly`, session-only, `no-store`, anti-forgery-protected on the POST, with explicit types; TypeScript regenerated. Problems: `409 rotation_running`; `503 audit_unavailable` with `secretsRewrapped` and `versionsRewrapped`.
- [ ] The page matches Legacy word for word: the three notices, Keys in use, the Rotate panel and operator steps. Rotate uses ConfirmationDialog with the Legacy text; the Legacy flash text is in the StatusLine.
- [ ] Deliberate changes: the button is disabled when `awaitingRotation` is 0; success adds " n item(s) remain on keys that are not loaded." when above 0; the 409 and 503 texts from #SPEC; the status reloads after any outcome.
- [ ] `EncryptionController` and `Views/Encryption/Index` are removed; the authorization-surface and area tests are updated deliberately.
- [ ] HTTP integration tests on `KekRotationFixture`: shapes, every code, the authorization matrix (anonymous 401, non-administrator 403 on the API and `/Account/Denied` on the page), anti-forgery and `no-store`.
- [ ] AuditFailureFixture: rotate answers 503 with the committed counts.
- [ ] Vitest: loading, error with retry, each notice, the disabled button, the dialog, every StatusLine outcome.
- [ ] Playwright under browser-health: the page with nothing pending, the dialog opened and cancelled, both shells, both palettes, keyboard and both themes. Run locally only for this ticket's specs; the full suite runs in CI.
- [ ] Release build with 0 warnings; unit, integration, Vitest, lint, format check, build checks, `check:api-types`, build and audit pass.

## Blocked by

- #T1
