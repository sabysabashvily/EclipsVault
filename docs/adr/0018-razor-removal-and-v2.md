# Razor goes in one final cleanup; main stays releasable throughout; the result is v2.0.0

Each area's views go in that area's PR (ADR-0009). A final cleanup PR removes the rest:

- MVC view support (`AddControllersWithViews` becomes `AddControllers`), the conventional route, `Views/`, `site.js` and the Razor layout.
- The route table from ADR-0014 becomes a catch-all.
- `GlobalExceptionMiddleware` stops redirecting to an HTML error page and answers in the caller's format.
- The authorization-surface test is updated.

Because every merged area matches what it replaced and is proven end to end, `main` can be released at any point, so security fixes never wait for the migration. The release after the cleanup is v2.0.0: the UI is replaced entirely, and anything scripting the old HTML forms breaks. Cutting releases stays with the owner.

Amended by ADR-0034: no catch-all; `AddControllers()` keeps MVC's view features without Razor; errors answer in the caller's format without a redirect.
