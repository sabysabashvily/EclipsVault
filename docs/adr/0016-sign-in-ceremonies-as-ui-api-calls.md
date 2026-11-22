# Sign-in moves to the UI API with the same server-side state, and SSO stays redirects

The sign-in ceremonies move to `/api/ui/auth/...` as JSON calls. This covers password, authenticator code, recovery code, first-time authenticator enrollment, passkey, break-glass recovery and sign-out. The server keeps every piece of state exactly as today: the short-lived MFA-pending cookie, then a session only after the second factor passes. The Angular UI holds none of it. The per-IP throttle, lockout, breached-password screening and auditing apply unchanged. The anonymous-endpoint allowlist test is updated deliberately for each new anonymous endpoint. Until Area 9 retires the Legacy error page, its sign-out form still posts to `/Account/Logout`, which runs the same sign-out as the UI API.

Three things deliberately stay outside the Angular UI's control:

- **SSO stays top-level browser redirects.** The trip to the identity provider and back is a navigation, never a JavaScript call. The callback ends by sending the browser into the Angular UI.
- **The IP-blacklist exemption moves with break-glass recovery.** Today only `/Account/Recover` passes; afterwards only the Angular recovery route and its one API endpoint do.
- **The "access blocked" page stays server-generated HTML.** It is not a Razor view, and a blocked caller must not need the Angular UI to see it.
