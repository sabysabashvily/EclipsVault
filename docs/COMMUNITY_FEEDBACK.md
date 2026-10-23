# Community evaluation feedback

Copy this template for one tester/session. Use invented data only. Remove credentials, MFA material,
cookies, configuration values, and personal or company information from any attachments. Send the
report through the channel agreed with the maintainer; do not publish a security finding before
discussing it privately.

## Session

- Date/time and timezone:
- Tester alias and assigned fixture prefix:
- Candidate commit or image digest (from maintainer):
- Browser/version and operating system:
- Database provider (from maintainer):
- Local supervised session or remote evaluation:
- Help received during the session:

## Results

Replace **Not run** with **Pass**, **Fail**, or **Blocked** as appropriate. For a partial step, explain
what succeeded and what did not; do not mark the whole step Pass.

| Step | Task | Result | Evidence or issue reference |
|---|---|---|---|
| T1 | Sign in, enroll MFA and sign in again | Not run | |
| T2 | Create, find and reveal a dummy secret | Not run | |
| T3 | Rotate, reveal history and restore a version | Not run | |
| T4 | Project/clearance restrictions across list, search, overview, direct access and creation | Not run | |
| T5 | Revoke another session | Not run | |
| T6 | Audit events and integrity (with maintainer) | Not run | |
| T7 | Delete test secret, verify audit and sign out | Not run | |

Optional application-only restart check (leave the database running): **Not run**.
Record whether the dummy value and existing MFA enrollment still work after the restart.
Stopping the RAM database starts a new session with new MFA enrollment; it is not a persistence check.

## One issue per copy of this block

- Short title:
- Step ID and account role (member or administrator):
- Reproduction steps:
- Expected result:
- Actual result, including the visible error wording:
- Happens every time or intermittently:
- Sanitised screenshot or evidence reference:
- Did it prevent completion, expose another account's data, or lose a dummy value?
- Help or workaround needed (if any):

## Usability

1. Where did you first need help or hesitate?
2. Which label, instruction or confirmation was unclear? What did you think it meant?
3. Could you distinguish viewing metadata, revealing a value, rotating it and restoring a version?
4. Was it clear why the two restricted secrets were unavailable?
5. What single change would make this workflow easier for you?

## Maintainer follow-up

- Findings to fix before another evaluation:
- Tracked issue(s) or change(s):
- Retest candidate commit/digest and failed step results:
- Test accounts/fixtures retired and instance stopped, or reason they remain:
- Installation, restore and upgrade evidence: **Not assessed by this walkthrough**.
- Production readiness: **Not established by this report**.
