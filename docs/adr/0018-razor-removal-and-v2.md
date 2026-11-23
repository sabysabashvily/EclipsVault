# Razor goes in one final cleanup; main stays releasable throughout; the result is v2.0.0

Each area's views go in that area's PR (ADR-0009). A final cleanup PR removes the rest:

- Area 9a removes the Razor views, the conventional route, `site.js` and `site.css`. It registers
  controllers with `AddControllers()` and retains the MVC antiforgery filters and TempData with
  `AddMvcCore().AddViews()`.
- The explicit route table applies each page's authorization rule. Unknown page paths return the
  not-found server page to signed-in visitors. See ADR-0014 and ADR-0034.
- `GlobalExceptionMiddleware` answers failures in the caller's format at the original URL. A
  honey-token trip outside the APIs still redirects to sign-in.
- The Production endpoint-table test pins the anonymous, pending-sign-in and administrator routes.

Because every merged area matches what it replaced and is proven end to end, `main` can be released at any point, so security fixes never wait for the migration. The release after the cleanup is v2.0.0: the UI is replaced entirely, and anything scripting the old HTML forms breaks. Cutting releases stays with the owner.
