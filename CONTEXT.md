# EclipsVault

A self-hosted secrets vault: it stores secrets sealed under a master key, decides per request who may open them, and keeps a tamper-evident record of every access.

## Language

### Identity and sign-in

**Vault account**:
A person's account in the vault itself, with a fixed username, a clearance and a project. Only a vault account can hold a session.
_Avoid_: User (when the IdP identity is meant), login

**Identity provider (IdP)**:
An external system (e.g. Keycloak) that can vouch for who someone is during sign-in. Its word proves identity only; the vault decides whether that person gets in.
_Avoid_: Auth server, SSO (SSO is the sign-in method, the IdP is the system)

**Session**:
One signed-in browser of one vault account, which the vault can revoke at any moment and which dies on its next request once revoked.
_Avoid_: Token, login, JWT

**Second factor**:
The proof that must pass, after the password, before a session exists: an authenticator code or a recovery code. A passkey is both factors at once.
_Avoid_: 2FA code, OTP

**Step-up**:
A fresh second factor demanded before revealing a high-sensitivity secret on a session whose last strong sign-in is too old.
_Avoid_: Re-login, re-auth prompt

**Break-glass recovery**:
The one way a vault administrator signs back in from a blocked network: all factors at once, audited, lifting the block on their own range.
_Avoid_: Emergency login, unblock

**Service account**:
A non-interactive identity for an application. It never has a session; it calls the vault with an API key.
_Avoid_: Bot, machine user

**API key**:
The credential a service account presents, optionally narrowed below its account by a scope.
_Avoid_: Token, bearer token

### Sharing

**Share**:
Access to one secret, given to one vault account by another, optionally until an expiry. Its issuer can revoke it, and it outlives the issuer's account.
_Avoid_: Grant, permission

**Issuer**:
The vault account that gave a share, identified by its account, never by its username.
_Avoid_: Grantor, sharer, owner

**Recipient**:
The vault account a share was given to.
_Avoid_: Grantee

### The two APIs

**Public API**:
The versioned API service accounts call with an API key. A published contract that operators' scripts depend on.
_Avoid_: REST API, external API

**UI API**:
The endpoints only the Angular UI calls, through a session. Never reachable with an API key.
_Avoid_: Internal API, BFF API, backend

### The migration

**Legacy UI**:
The server-rendered pages being replaced. Shrinks to nothing by the end of the migration.
_Avoid_: Old UI, MVC frontend

**Area**:
A group of related pages behind one sidebar entry (e.g. Profile, Secrets, Users), named by a stable key such as `users` in `/api/ui/me`. An area migrates as one unit; a migration step may move several at once, as the Administration step moves all the administration areas. At any moment an area belongs wholly to either the Legacy UI or the Angular UI, never both.
_Avoid_: Module, feature, screen

**Angular UI**:
The browser application replacing the Legacy UI. It acts only through a session, never with a token of its own.
_Avoid_: SPA, client, frontend app
