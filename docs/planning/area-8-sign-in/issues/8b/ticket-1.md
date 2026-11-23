## Parent

#SPEC

## What to build

Invited accounts and invitations on the server (spec Q13, Q14, Q16–Q18, ADR-0033).

- Model: awaiting-activation state and invitations (token hash, expiry, pending seed); migrations in both sets.
- `POST /api/ui/users` keeps today's password body. The email contract, the retirement of generated addresses and the New user form move together in ticket 2, with the helpers.
- `POST /api/ui/users/{id}/invitation` (resend), replacing the token. Resend sends to the stored address and takes no new one.
- `POST /api/ui/users/{id}/invitation-address` replaces the stored address of an awaiting-activation account. Fresh proof. `InvitationAddressReplaced` names the new address and commits with it. It spends the current token in the same transaction, so the link already sent dies, and it sends no mail; the administrator resends to the new address. `409 already_active` after activation. `409 email_taken` is generic.
- Delivery through the unrecorded mail path, the link built only from `Identity:PublicUrl`; `invitationsAvailable` and `409 invitations_unavailable` without mail or the setting.
- Invited accounts refused by every sign-in route, including pending-sign-in validation and final session issuance, SSO `NotActivated`; excluded from the last-administrator count; reset `409 not_activated`.
- The Q18 data migration in both sets: the selection rule, the credential-generation advance, the pending-secret clear, and no audit write. It never invites the last enabled administrator. The upgrade note says to enroll the bootstrap administrator first. When every enabled administrator would otherwise be invited, `vault-admin` stays if it is in the set, otherwise the earliest created one stays, on the password it already has.
- Audit actions `InvitationSent`, `InvitationAddressReplaced` and `AccountActivated` (with Activity and audit labels); `UserCreated` names the address.

## Acceptance criteria

- [ ] F9 is first shown failing (a request-built link), then impossible. F3 and F7 stay visible until ticket 2 switches create off the administrator-chosen password.
- [ ] Production HTTP: resend and address-replacement shapes, codes and fresh proof; resend cannot change the address; replacement sends no mail; stored token spending commits in the same transaction as `InvitationAddressReplaced` (a rejected row leaves the address and the token unchanged); create still accepts the password body the current form sends; invited accounts refused by password (same timing), pending validation, final issuance, SSO and break-glass, including a stamp that still matches; last-administrator rule; the token absent from the outbox, logs and audit rows.
- [ ] The real database proves the atomic address/token/audit outcome here. Ticket 2 adds the activation endpoints and proves that the replaced link answers `404 invitation_invalid`; no acceptance assertion is dropped.
- [ ] The data migration's selection rule, generation advance and secret clear are proved on both databases, including a pre-migration pending cookie, a stored pending secret and an in-flight SSO stamp. The last enabled administrator is not invited and still signs in with the existing password. A replaced address is the one resend delivers to. `dotnet ef` checks pass for both sets.
- [ ] AuditFailureFixture: create, resend and address replacement change nothing when the row fails.
- [ ] `scripts/check.sh` and `scripts/check.sh integration "<this ticket's filter>"` pass; `npm run check:api-types` passes. CI runs SQL Server and the full browser suite.

## Blocked by

None (the branch starts from `main` after 8a merges).
