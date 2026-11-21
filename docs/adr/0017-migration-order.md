# Migration order: read-only self-service first, Secrets next to last, sign-in last

Areas migrate in this order:

0. **Groundwork:** workspace, app shell, `/api/ui/me`, error handling, CSRF, no-store, test infrastructure, plus My access as the tracer area that proves the whole path end to end.
1. **Read-only self-service:** My activity, Sign-in history, Security checkup.
2. **Simple self-service writes:** Signed-in devices, Your data, Shared by me.
3. **Profile.**
4. **Sharing:** Shared with me, Access requests.
5. **Administration:** Users, Networks, Service accounts, Notifications, Licence.
6. **Audit log and Encryption keys.**
7. **Overview dashboard, Secrets and Dynamic secrets.** The dashboard goes with Secrets because it aggregates secret metadata through the same ABAC filter.
8. **Sign-in.**
9. **Final cleanup.**

Risk grows down the list, so the patterns settle on pages where a mistake cannot leak a secret. Sign-in goes last because the Razor sign-in hands over to Angular areas unchanged, the same session cookie serving both. Every area touching encryption, authorization, the audit chain or MFA (2, 3, 4, 5, 6, 7, 8) is reviewed by the `security-reviewer` agent before merge.

Area 2 joined that list when its grill found it revokes sessions and shares and changes how they are audited (ADR-0021).

Area 4 joined that list when its grill found that approving an access request issues a share, and that requests and shares were decided and audited outside the sharing rule (ADR-0027).

Area 5 runs in two parts, each with its own spec, branch and PR, after its grill found that it changes how administrator actions are committed, audited and authorized (ADR-0028). Part 5a, Accounts, moves Users and Service accounts. Part 5b, Vault settings, moves Networks, Notifications and License after 5a merges. The risk sits in 5a, and two smaller diffs keep each security review focused.

Area 6 runs in two parts, each with its own spec, branch and PR, after its grill found that master
key rotation could destroy secrets and that Verify could report a tampered audit trail as intact
(ADR-0029). Part 6a, Encryption keys, goes first because its defect loses data today. Part 6b, Audit
log, merges after 6a. By the owner's decision it started from 6a's branch before 6a merged, and took
`main` once 6a had. The two security reviews cover unrelated code: key
material, then the audit chain.

Area 7 runs in three parts, each with its own spec, branch and PR, after its grill found that a share
let its recipient change and delete the secret, that a refused read was recorded as a view, and that
step-up could be bypassed by sharing or minting (ADR-0031). Part 7a, Overview and Secrets list, goes
first and settles the secrets UI API. By the owner's decision it started from 6b's branch before 6b
merged, and took `main` once 6b had. Part 7b, Secret details (with Denied and filing an access
request), likewise started from 7a's branch before 7a merged, by the owner's decision. Part 7c, Dynamic
secrets, branches from `main` after 7a merges; it touches code unrelated to 7b, so it may run beside
7b. Each part has its own security review. Dynamic secrets was the Legacy sidebar's last page, so 7c moves the browser
checks that needed a Legacy page to Access denied, which 7b then serves from Angular; whichever of the
two merges second drops or re-homes those checks. 7b, the second, re-homes them to the error page, the
last signed-in page on the Legacy layout until Area 9; the browser specs name it once in
`e2e/legacy-shell.ts`, the integration tests in `VaultFixture.LegacyShell`.

Area 8 runs in two parts, each with its own spec, branch and PR, after its grill found that the
first sign-in of a new account can be completed by whoever knows the password an administrator chose
(#117, ADR-0033), and that the sign-in pages revealed which accounts exist (ADR-0032). Part 8a,
Sign-in, branches from `main` after 7a merges and may run beside 7b and 7c. Part 8b, Provisioning,
branches from `main` after 8a merges, because it reuses 8a's anonymous pages. Area 9 depends only on
8a. Each part has its own security review.
