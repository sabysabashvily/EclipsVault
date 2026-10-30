# Lessons carried from Areas 1–3

Read from the Area 2 tickets (#75–#81), the Area 3 spec and tickets (#83–#91), their briefs and close
comments in `logs/area-3/`, and the final reports and review ledgers in `logs/ticket-8x/`.

1. **Serve each page in the ticket that builds it.** Area 2 did (#78–#80), and its last ticket only
   retired and verified. Area 3 built its Angular pages without serving them (#89, #90), so their
   green browser runs proved nothing about the new pages; #91 inherited two carry-forwards and needed
   nine review rounds and dozens of check runs. Area 4 serves Shared with me in ticket 4 and Access
   requests in ticket 5.
2. **Prove each finding with a failing test first.** Grill findings are code-read deductions; every
   ticket that fixes one names it and shows it failing before the fix.
3. **Briefs pin the starting point.** Exact branch and HEAD, the only allowed untracked entry
   (`docs/research/`), owner authorization (no push, no PR, no issue closing), a read-first list,
   rules, required checks, required reviews with the model policy, and where evidence goes
   (`logs/ticket-<n>/`, never `/tmp` or Downloads).
4. **Audit-failure tests are phase-correct.** Set up and sign in with auditing working, then reject
   audit writes, so the intended change is the step that fails.
5. **Browser specs must not wait out real throttles.** Area 3's browser suite grew to about 50 minutes
   locally and the CI job limit went from 30 to 75 minutes. Area 4 needs several accounts in other
   projects and clearances: create them through the administrator pages once per run and spread the
   sign-ins.
6. **Local Docker has 4 GB.** Run integration and browser suites separately; a serial runsettings file
   is allowed, but concurrency tests stay concurrent. Never prune Docker broadly.
7. **Isolate sign-in budgets in integration tests** with distinct source addresses.
8. **Reviews:** security-reviewer on every authorization or audit ticket; Standards and Spec by a
   different model from the author; a finding-by-finding ledger. Fix small findings directly.
9. **Close each ticket with a comment** naming its commits, the green CI run on that exact commit and
   the local counts. Push only on the owner's word, and never report CI green without seeing the run.
10. **Area 3's issues carried no triage labels.** Area 4 follows that unless the owner asks otherwise.
