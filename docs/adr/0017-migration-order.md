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
