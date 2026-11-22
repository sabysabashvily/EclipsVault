# Defence draft / დაცვის მონახაზი

English and Georgian storyboard for one talk about EclipsVault. The talk starts with a synthetic
Phoenix team opening one synthetic secret, then looks at that reveal in the audit log. The owner confirmed a presentation cap of 30 minutes. Detailed slide timing is still
unallocated; the supplied C4 guide contains no timing rule.

ეს არის EclipsVault-ის ერთი მოხსენების ინგლისურ-ქართული მონახაზი. მოხსენება იწყება სინთეზური Phoenix
გუნდით, რომელიც ერთ სინთეზურ საიდუმლოს ხსნის, შემდეგ კი ამ გახსნას აუდიტის ჟურნალში ამოწმებს.
პრეზენტაციის დადასტურებული ზედა ზღვარია 30 წუთი. სლაიდებზე დროის ზუსტი განაწილება
ჯერ დასაზუსტებელია; მოწოდებული C4 გზამკვლევი ხანგრძლივობას არ განსაზღვრავს.

This folder is the rehearsal draft for the 24 November defence. It records one disposable
Development walk. It does not record a production deployment or a security guarantee.

## Read in this order

| File | What it is |
|---|---|
| [storyboard.md](storyboard.md) | Eight bilingual slides within a 30-minute cap; detailed allocation pending |
| [speaker-notes.md](speaker-notes.md) | What to say, and what not to claim |
| [diagrams.md](diagrams.md) | Context, container, Web component, reveal code, deployment |
| [diagrams.html](diagrams.html) | The same five pictures, rendered, with bilingual captions |
| [security-flows.md](security-flows.md) | Sign-in, access, reveal, keys, checkpoint, dynamic lease |
| [demo-setup-reset.md](demo-setup-reset.md) | Disposable Development setup, the walk, and reset |
| [rehearsal-checklist.md](rehearsal-checklist.md) | One-page run order for the presenter |
| [questions.md](questions.md) | Likely questions, with the answer the source supports |
| [screens/](screens/) | Light-theme captures of the live steps. No secret values |

## Revision this draft was read from

| Name | Commit | What it is |
|---|---|---|
| `origin/main` | `d576dd93a30bc461183ca7b71c89f038adbff446` | Current `main` when this draft was refreshed, 21 November 2026, 22:42 Asia/Tbilisi. Merge of Area 7b secret details. |

The earlier draft was read at `0bb59de` on 10 November 2026. That note is history. The pictures and
the flows in this folder describe `d576dd9`.

## Current, and what is still not this UI

**Current** means the code on `origin/main` at `d576dd9`. That includes:

- master-key rotation in resumable batches, and the Angular Encryption keys page (`KekRotationService`, `EncryptionController`, ADR-0029)
- checkpoint-boundary verification, atomic checkpoint recording, streamed step-up export, `AuditLogViewed`, `AuditChainVerified`, and one-at-a-time Verify and Export (`AuditChainVerifier`, `AuditCheckpointService`, `AuditController`, ADR-0030)
- Angular Overview, Secrets, New secret, Secret details, Denied, access requests, and Dynamic secrets, with denials recorded as denials and shares that only read (ADR-0031)

Verify still does not prove every deletion. Deleting checkpoints, or changing rows after the latest
checkpoint signed by the current key, can leave it quiet. See [security-flows.md](security-flows.md).

**Still the Razor sign-in pages, on this revision:** `AccountController` and `Views/Account/`
(password, authenticator, enrollment, recovery, passkey). They are not in `AngularUiRoutes`.

**No invitation flow** is in this revision's source. A search for an invitation type in the C#
projects finds none.

The signed-out landing page and the shared Razor layouts also remain. The feature pages named in
`AngularUiRoutes` are the Angular UI.

## Old-verifier note, kept

From `README.md` at `origin/main`, the signed-checkpoint paragraph:

> A bundle records the format it was written in, and a verifier reads its own format and every older
> one — so a bundle an auditor archived years ago keeps verifying. The reverse does not hold: a
> verifier older than the vault that exported the bundle refuses it and says so explicitly, rather
> than recomputing hashes under the wrong scheme and reporting an honest trail as edited. Auditors
> should keep a verifier at least as new as the vault. One exception: the 1.0.0 verifier shipped
> before this check existed, so given a bundle from a later vault it reports `INVALID — "the row was
> edited"`. That is a verifier that is too old, not a trail that was tampered with; re-run with the
> current verifier to get a true answer.

The current verifier's refusal of an unknown format, without calling it tampering, is
`AuditBundleVerifier` together with `AuditBundleSchema` (`V1` and `V2`). The 1.0.0 sentence is the
README's historical note. This draft did not run that old verifier.

## What stays unresolved

- Presentation cap: at most 30 minutes, confirmed by the owner. Detailed slide allocation and
  marking criteria remain unconfirmed. The supplied C4 guide requires context/container diagrams
  and PDF export; it gives no timing rule.
- The local Georgian diploma: not in this checkout. The story follows `CONTEXT.md`, the ADRs on
  `main`, and the source named in the other files.
- `scripts/check.sh` was not run. This change does not modify the program.
- The 1.0.0 verifier binary was not executed. The note in the section above is the repository README's.
