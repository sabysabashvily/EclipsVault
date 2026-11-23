# During the migration, Angular owns an explicit list of paths at the same URLs

Each area's PR added its page paths to a single server-side route table that serves the Angular UI's
`index.html`. Migrated pages kept their URLs so bookmarks kept working. During the migration, moving
between an Angular page and a Razor page was a full page load on the same session, and both UIs used
the same sidebar. Mounting Angular under `/app/` would have changed every bookmark twice.

ADR-0034 supersedes the original plan to replace this list after the migration. The explicit route
table still names each page and applies its authorization rule.
