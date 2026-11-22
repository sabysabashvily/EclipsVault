# The route table stays the list of pages, and errors answer in the caller's format

ADR-0014 and ADR-0018 planned that the route table becomes a catch-all once the last Area moves, and
that `GlobalExceptionMiddleware` stops redirecting to an HTML error page. The Area 9 grill read the
code and found that a catch-all would serve the Angular UI with `200` for paths that are not pages (a
typo, an unknown Public API path, a missing bundle), and that one rule for every path would drop the
server's own checks: administrator-only pages, anonymous sign-in pages and pages that accept only the
pending sign-in cookie. This ADR replaces the catch-all and settles what each caller gets on failure.

## Decisions

- **No catch-all.** The route table remains the single list of the Angular UI's pages, each with its
  own authorization rule; `/` joins it as a session page. A path it does not name is not a page: a
  signed-in caller gets the not-found page with `404`, and a signed-out caller is sent to sign-in, as
  today, so the anonymous surface does not grow. `/api/ui/` keeps its Problem Details `404`,
  `/api/v1/` answers `404 { "error": "not_found" }`, and a path with a file extension gets a bare `404`,
  never the Angular document.
- **Errors answer in the caller's format, without a redirect.** The UI API keeps Problem Details
  (ADR-0006). The Public API gets `{ "error": "<code>" }` with the real status for every exception the
  middleware maps, and a blocked network gets `403 { "error": "blocked" }`. Every other request gets a
  server page with the real status and the middleware's fixed text for that failure, a request id and
  a link to `/`; never an exception's details. A honey-token trip outside the APIs still signs out and
  redirects to sign-in. `/Home/Error` is gone.
- **Server pages.** The vault writes three pages itself: access blocked, error and not found. They
  share one template, carry no script, and link one small static stylesheet that a blocked network can
  still load, because static files are served before the block.
- **Razor goes, MVC's view features stay.** `AddControllersWithViews` becomes `AddControllers()` plus
  `AddMvcCore().AddViews()`: no view engine, so nothing can render a view, while the framework's
  antiforgery filters and TempData stay registered. `AddControllers()` alone would unregister both.
- **No landing page.** `/` needs a session; a signed-out visitor is sent to sign-in. Only sign-in
  stays anonymous (ADR-0032). `/local-sign-out` stays reachable with no session: it is the outage
  branch mapped before authentication, not a page.
- **Headers.** Trusted Types is in the base Content Security Policy for every response (ADR-0013's
  final step), and every `/api/` response, the Public API's included, is `no-store`.
- **The surface is pinned on the endpoint table**, not only on controller actions. A test of the
  Production host lists the anonymous endpoints, the pending sign-in endpoints and the administrator
  pages exactly. Every other session endpoint, including the page not-found fallback and `/api/ui/`,
  requires a session. Public API endpoints stay API-key-only and accept no session (ADR-0005). An
  unknown `/api/v1/` path answers `404 { "error": "not_found" }` and does not challenge with the
  session cookie. `/local-sign-out` is a branch mapped before authentication, so the endpoint table
  cannot see it; a separate HTTP check pins that it still clears the cookie with no session.

## Consequences

- An Angular page needs its path in two places, the route table and the Angular router. A path in the
  router but not the table answers `404`; the reverse would serve an empty shell. The two lists are
  kept side by side by hand.
- A signed-out request for a missing static file is still sent to sign-in rather than answered `404`.
  It reveals nothing, and changing it would add an anonymous endpoint.
- The server pages' stylesheet is a small copy of the Angular UI's sign-in card rules; it can drift
  from them in looks, never in behaviour.

## Considered Options

- **A catch-all with an Angular `**` not-found route.** Rejected: every unknown path becomes a `200`,
  and the administrator and pending sign-in checks would move into Angular guards, which protect
  nothing (ADR-0012).
- **`AddControllers()` with our own antiforgery filter.** Rejected: new security code replacing the
  framework's.
- **Link the Angular stylesheet from the server pages** by reading its hashed name out of `index.html`.
  Rejected as fragile; unstyled pages were rejected as a visible regression on the blocked page.
- **An anonymous Angular landing page.** Rejected: more anonymous surface for marketing text.
