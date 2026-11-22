# During the migration, Angular owns an explicit list of paths at the same URLs

Each migrated area's PR adds its paths to a single server-side route table that serves the Angular UI's `index.html`, and removes the Razor controller that used to answer them. URLs do not change, so bookmarks keep working. Moving between an Angular area and a Razor area is an ordinary full page load on the same session. When the last area moves, the list becomes a catch-all. Mounting Angular under `/app/` until the end was rejected: it breaks every bookmark twice. During the migration both UIs render the same sidebar, copied exactly.

Amended by ADR-0034: the route table stays the explicit list of pages; there is no catch-all.
