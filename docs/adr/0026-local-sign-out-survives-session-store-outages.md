# Local cookie clearing remains available during session-store outages

Store-backed authentication can fail before the normal sign-out handler runs. Provide an exact-route
terminal branch outside `/api/ui`, before blacklist, session and authentication middleware that reads
stores. Its GET issues a fresh anonymous antiforgery request token with `no-store`; its POST validates
that token with an anonymous principal and deletes the configured authentication cookie using its
cookie manager, path, domain and chunk handling.

The branch reads no account identity, performs no server-side revocation, returns no protected data
and never continues into protected routes. An authenticated antiforgery token cannot substitute for
the anonymous token. Deliberately allowlist this anonymous surface and test exact paths, methods,
antiforgery, cookie deletion and the absence of account authority during a real store outage.

Account-wide sign-out remains session-only and fully validated. If it fails, the fallback reports
local clearing while preserving `sign_out_incomplete`; it never claims that other devices were
signed out. This limited anonymous exception preserves local sign-out availability without allowing
an unvalidated principal to authorize an account-wide change.
