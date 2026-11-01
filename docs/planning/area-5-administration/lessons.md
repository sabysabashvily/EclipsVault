# Lessons carried into Area 5

Lessons 1–10 come from Areas 1–3, through `docs/planning/area-4-sharing/lessons.md`. Lessons 11–14 are
what Area 4 taught, read from its checkpoint, the #102 reports and `logs/area-4/cloud-102-review.md`.

1. **Serve each page in the ticket that builds it**, so its browser tests prove the served page.
2. **Prove each finding with a failing test first.** Grill findings are code-read deductions.
3. **Briefs pin the starting point:** exact branch and HEAD, allowed untracked entries (`docs/research/`,
   `docs/diploma/`), owner authorization, a read-first list, required checks and reviews, and evidence
   under `logs/ticket-<n>/`.
4. **Audit-failure tests are phase-correct:** set up and sign in with auditing working, then reject
   audit writes, so the intended change is the step that fails.
5. **Browser specs must not wait out real throttles.** Create accounts once per run and spread sign-ins.
6. **Local Docker has 4 GB.** Run integration and browser suites separately. Never prune Docker broadly.
7. **Isolate sign-in budgets in integration tests** with distinct source addresses.
8. **Reviews:** security-reviewer on every authorization or audit ticket; Standards and Spec by a
   different model from the author; a finding-by-finding ledger. Fix small findings directly.
9. **Close each ticket with a comment** naming its commits, the green CI run on that exact commit and
   the local counts. Push only on the owner's word; never report CI green without seeing the run.
10. **No triage labels on Area issues** unless the owner asks.
11. **Only the whole-Area review saw the decoy defect.** Requests filed before their secret became a
    decoy stayed in the review queue, and approving one shared the decoy without tripping it
    (`27e3c11`). Every per-ticket review missed it, because no single ticket held both the queue and
    the honey-token rule. So: keep the whole-Area security review in the last ticket, and put decoy
    state (a secret flagged as a decoy after related rows exist) into the grill's authorization
    matrix from the start.
12. **Area 4 had no cross-model review.** GPT Sol 6.1 was out of usage, so r1 and r2 ran on Claude Opus 5.5
    (effort high) in fresh sessions, and the checkpoint says so. That was an outage workaround, not
    the rule: from 2026-11-04, Standards and Spec reviews go to GPT Sol 6.1 at xhigh again. If it is
    still unavailable, the report says so.
13. **One ticket, one worker, from a pushed HEAD.** A cloud session and the local #102 thread both did
    #102 from different starting points. They duplicated the retired-routes test, and the cloud attempt
    never saw the local decoy fix. A parallel worker starts only from a pushed, named commit, and the
    coordinator checks it against the other line before accepting anything.
14. **Settle presentation gaps in the grill.** Area 4 ended with owner follow-ups that a grill question
    could have closed: no info style in the StatusLine, copied "grant" wording, and a dead note
    parameter.
