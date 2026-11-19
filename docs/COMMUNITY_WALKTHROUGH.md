# Community evaluation: tester walkthrough

This evaluation checks whether someone new to EclipsVault can complete its basic secrets workflow
and understand access restrictions. **Use invented values only. Do not enter real passwords, API
keys, recovery codes from other services, or company data.** This is an evaluation candidate;
production readiness has not been established.

The steps below describe expected behaviour, not a completed test report. The maintainer must
first complete [evaluation preparation](COMMUNITY_EVALUATION_SETUP.md). No paid EclipsVault licence
is needed for these steps.

## Before you start

The maintainer supplies the candidate URL, its commit or image digest, a fresh test account and its
initial password, and an agreed way to return feedback. Use a browser and an authenticator app.
Copy the [feedback template](COMMUNITY_FEEDBACK.md) into your notes. Record **Pass**, **Fail**,
**Blocked**, or **Not run** for each step; a blocked step is not a pass.
Use **Blocked** when a prerequisite prevents the check (for example, the candidate does not build
or T1 cannot complete). Use **Fail** when you performed the check and observed the wrong behaviour.

Use a browser profile separate from your normal accounts. For the session test you will need a
second browser or another independent profile; two tabs share a session. Actions that change or
remove data open a **Please confirm** box: **Continue** goes ahead, **Cancel** stops. The maintainer uses a
separate administrator session for the marked checks. Never include passwords, MFA QR codes,
enrollment keys, recovery codes, cookies, or configuration files in feedback or screenshots.

## T1 — Sign in and enroll MFA

1. Before signing in, visit `/Secrets` on the supplied instance. Expect a sign-in challenge and no
   secret list or values.
2. Sign in with your supplied test account. On first use, add the displayed QR code or enrollment
   key to your authenticator, then select **Confirm enrollment** with a current code.
3. Open **Secrets**. Sign out through the account menu and sign in again using your password and
   a fresh authenticator code.

**Expected:** access requires the second factor; subsequent sign-in uses the existing enrollment.
Record anything confusing about enrollment or returning to the vault. If you cannot finish, mark
dependent steps Blocked rather than asking for MFA to be disabled.

## T2 — Store and reveal an invented value

Open **New secret** and enter these fields. The maintainer gives each tester a different prefix
(for example, `EVAL_A`); replace that prefix consistently if yours differs.

| Field | Value |
|---|---|
| Name | `EVAL_A_WORKFLOW` |
| Secret value | `dummy-only-value-v1` |
| Project key | `PHOENIX` |
| Environment | `Development` |
| Sensitivity | `Internal` |
| TTL in days | `0` |

Select **Encrypt and store**. Find and open the secret, then select **Reveal value**. Optionally
use **Copy** and paste into a temporary local note to check the result; clear that note and clipboard
afterwards. Navigate back to the list and reopen the secret.

**Expected:** the list and initial details page do not show the value. Reveal shows exactly
`dummy-only-value-v1`; opening the details page again hides it. The secret appears in your list and
the global search opened from the sidebar. Record whether the distinction between viewing metadata
and revealing a value was clear.

## T3 — Rotate and recover a previous value

1. Expand **Rotate value**. Set **New value** to `dummy-only-value-v2` and **Change note** to
   `Community evaluation`. Leave renewal blank, select **Rotate secret**, and confirm.
2. Reveal the current value: expect `dummy-only-value-v2`.
3. Under **Version history**, reveal the archived value: expect `dummy-only-value-v1`.
4. Select **Restore** on that archived version and confirm. Reveal the current value again.

**Expected:** restoring makes `dummy-only-value-v1` current and preserves `dummy-only-value-v2`
in history. No value is lost. This changes only a stored test string; it does not change a password
in an external system. Record whether the history and confirmation messages explain what happened.

## T4 — Check project and clearance restrictions

Your account has **Standard** clearance and belongs to **PHOENIX**. The maintainer has prepared two
dummy secrets: `EVAL_A_OTHER_PROJECT` in ORION and `EVAL_A_HIGH_CLEARANCE` in PHOENIX with Secret
sensitivity. Neither is shared with your account.

1. Refresh **Secrets**, open the sidebar search and search each exact name, then open **Overview**.
   Neither restricted name should appear. Both expire within two days, yet **Expiring within 7 days**
   must not count them: with only your T2 secret, which never expires, it shows 0.
2. With no one else changing data and no test secrets expiring during the comparison, compare the
   Overview **Active secrets** total with the number of rows in your Secrets list. They should agree;
   hidden secrets must not inflate the total. Refresh both pages and repeat if the data changed.
3. The maintainer gives you the details URLs of these two dummy records. Open each directly in
   your signed-in profile. Expect access denied and no secret value. The denial names the rule that
   failed, for example the record's project or the sensitivity it requires; that is expected. The
   record's name and value must not appear.
4. Try creating `EVAL_A_DENIED_PROJECT` with project ORION, Development, Internal, TTL 0 and value
   `dummy-denied`. Then try `EVAL_A_DENIED_CLEARANCE` with project PHOENIX, Development, Secret,
   TTL 0 and the same dummy value.

**Expected:** both creates are refused with an explanation. The maintainer confirms neither record
was stored. A restricted record appearing on a list, search, overview, or direct page is a failure
even if its value stays hidden. Record the failed step; do not broaden your permissions to make it pass.

## T5 — Sign out another session

1. Sign in to the same test account in a second browser or independent profile, completing MFA.
2. In your first session, open **Signed-in devices** (sidebar, under **Security**) and select
   **Sign out all other sessions**.
3. In the second session, make a new request by opening `/Secrets` or refreshing its page.

**Expected:** the second session must sign in again; the first remains usable. A page already
displayed in the second browser is not erased remotely, so judge the new request, not the old pixels.

## T6 — Check the audit trail with the maintainer

The maintainer opens **Audit log** in the administrator session, filters the displayed entries by
your test prefix or username, and checks the creation (**Created**), reveals (**Revealed**,
**Version revealed**), rotation (**Updated**) and version restoration (**Version restored**). Check
session revocation under your username too (**Device signed out**), then select **Verify integrity**.

**Expected:** the successful actions have matching actors and resources, secret values are absent
from audit details, and integrity reports **Hash chain intact**. The page shows the latest 200
entries; on a busy instance, filter the secret and session events separately and ask the maintainer
to check the export if an event has fallen outside that page. This check does not simulate an audit
storage failure or prove every security property.

## T7 — Delete your test secret and finish

Delete `EVAL_A_WORKFLOW`, confirm, then try its saved details URL and search for its name again.
Ask the maintainer to check that the deletion was audited. Sign out of both browser profiles.

**Expected:** the secret and its version history are no longer accessible, the search does not
return it, and the audit event remains. Return the completed feedback template. Remove the test
authenticator entry only after the maintainer confirms that the test account is no longer needed.

## What to tell us

Record where you needed help, what you expected before clicking, and what the application did.
Missing or unclear instructions are useful findings too. A short description with the step ID and
sanitised screenshot is more useful than “it works” or “it is broken.” If a step exposes data to the
wrong account, stop that check and report it through the agreed private channel.

This walkthrough covers the basic Community workflow. Installation, backup restoration, upgrades,
external integrations, and a full security assessment need their own evidence before a production
readiness decision.
