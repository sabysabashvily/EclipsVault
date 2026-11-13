# Tests run in tiers, follow the system's own clock, and never retry

A change is proven in four tiers, each with its own job.
- **The inner loop.** `scripts/check.sh` builds and runs the unit tests in about two minutes.
- **Each change.** It adds only the affected tests:
  - `scripts/check.sh integration "<filter>"` on PostgreSQL;
  - `scripts/check.sh e2e <spec>...` for the browser specs a change touches.
- **The merge gate.** CI runs every integration test on SQL Server and on PostgreSQL, the full
  browser suite and the guard probes. A pull request merges only when that run is green.
- **The scheduled tier.** It runs what is too slow for every change: mutation testing of the
  security core, repeated runs that hunt for flaky tests, and browser runs under deliberate CPU
  load.

The full browser suite runs in CI, or in a cloud agent on a pushed branch, not on the owner's
machine for each ticket. A ticket worker runs its own specs locally, then hands the full run to CI.
Local full runs repeatedly failed on host load and restarts, in specs the ticket never touched, and
each failure cost about an hour.

A test never waits on the host's wall clock:
- Server code reads time through `TimeProvider`.
- A browser test that needs a time boundary reads the time the server recorded, or sets run-scoped
  state through `E2E_COMPOSE_PROJECT`.
- A fixed sleep is not a timing method.

PR #130 based Profile's mailbox waits on the server's recorded time and removed sign-in-budget
sleeps. Three normal-host runs passed. Validation of the final spec under load remained incomplete.

A test is never retried until it passes:
- Playwright runs with zero retries, and no test raises its timeout to hide slowness.
- A flaky test is a defect, fixed by finding the shared state or the clock it depends on. Two
  examples: #134 stopped the shared vault's background sweeps writing audit rows into other tests'
  windows, and gave each Users test a fresh sign-in budget.
- A test is not retried, skipped or quarantined to get a run green.

Tests on a shared host must not depend on what other tests or background workers do. A host shared
by many tests turns off timers that write audit rows. An assertion about "no rows" or "no change"
either runs on an isolated host or scopes itself to what the test caused, without filtering out
whole actions or actors.

This keeps every test that runs today. It moves the slowest feedback to CI, and the slowest checks
to a schedule. The cost is that a ticket's final proof waits for CI, roughly 45 minutes, instead of
a local run. The browser suite also stays sequential until each spec gets its own administrator
session and can be split across runners. The scheduled tier and the split runs are follow-up work;
until they exist, the first three tiers are the rule.
