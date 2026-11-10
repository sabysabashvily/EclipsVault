# Defence draft / თავდაცვის მონახაზი

English and Georgian storyboard for one talk about EclipsVault. The talk starts with a synthetic
Phoenix team opening one synthetic secret, then looks at that reveal in the audit log. Slide timing
is unallocated: the assessment PDF was not in this checkout and was not fetched.

ეს არის EclipsVault-ის ერთი მოხსენების ინგლისურ-ქართული მონახაზი. მოხსენება იწყება სინთეზური Phoenix
გუნდით, რომელიც ერთ სინთეზურ საიდუმლოს ხსნის, შემდეგ კი ამ გახსნას აუდიტის ჟურნალში ამოწმებს.
სლაიდების დრო გამოყოფილი არ არის: შეფასების PDF ამ სამუშაო ასლში არ იყო და არ მოძებნილა.

This folder is a draft for a later rehearsal. It does not record a deployment, a recording, or a
security guarantee. Nothing in it was produced by launching a vault.

## Read in this order

| File | What it is |
|---|---|
| [storyboard.md](storyboard.md) | Eight slides, English then Georgian, no timings |
| [speaker-notes.md](speaker-notes.md) | What to say, and what not to claim |
| [diagrams.md](diagrams.md) | Context, container, Web component, reveal code, deployment |
| [security-flows.md](security-flows.md) | Sign-in, access, reveal, keys, checkpoint, dynamic lease |
| [demo-setup-reset.md](demo-setup-reset.md) | Disposable Development setup and reset. Not run |
| [questions.md](questions.md) | Likely questions, with the answer the source supports |

## Revisions this draft was read from

| Name | Commit | What it is |
|---|---|---|
| Owner's stated baseline | `4feec8c9dfa94dd439fca19338692fef30ddc6f6` | `main` as named in the ticket. It is the merge-base of today's `main` and Area 6a. |
| `origin/main` | `0bb59de1e8f64d1b7d04468c303c1b108dd6f04b` | Current `main` when this draft was written, 10 November 2026, 20:35 Asia/Tbilisi. Two commits after the baseline, both test-only (`6382ade`, then the merge `0bb59de`). |
| Area 6a, PR #147 | `ab7997e14ba98fd37055e6ca77fab4c6c8aba490` | Branch `area-6a-encryption-keys`, 10 November 2026, 20:28 Asia/Tbilisi. Open, not merged. |

`SecretService.cs`, `AuditCheckpointService.cs`, `Program.cs`, `Dockerfile`, and `docker-compose.yml`
have no diff between `origin/main` and the Area 6a head. The reveal order below is that shared code.
Area 6a does change master-key rotation and adds an Angular Encryption keys page; that behaviour is
not on `main` and is not presented here as current.

Graphify was not on `PATH`. It was not rebuilt and was not used.

## Current and planned

**Current** means the code on `origin/main` at `0bb59de`. Where a fact is also true of the Area 6a
head, the diagram table says so.

**Planned** means Area 6 part 6b, Audit log. The specification is on the unmerged Area 6a branch, in
`docs/planning/area-6-audit-and-keys/issues/6b/spec.md` and `docs/adr/0030-the-audit-log-claims-only-what-it-proves.md`
at `ab7997e`. There is no `area-6b-audit-log` implementation in either revision read here. Until that
work is verified and merged, these three stay planned:

- checkpoint-boundary verification
- atomic checkpoint recording
- streamed step-up export

The in-app Verify action re-walks the rows that are present. It does not, by itself, prove that every
deletion or rewrite is detectable no matter where the latest signed checkpoint sits. See
[security-flows.md](security-flows.md).

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

## Checks observed for PR #147

`gh pr view 147` on this drafting session showed the PR open, mergeable, and at head `ab7997e`.
Every check in `statusCheckRollup` was `SUCCESS`. The latest completion in that payload was the `e2e`
job at 21:41 Asia/Tbilisi on 10 November 2026 (`2026-11-10T17:41:10Z`). The same call returned no
pull-request reviews and no issue comments, so this draft does not confirm a Thermos review or a GPT
re-check. Those remain the owner's merge gates. This documentation change did not re-run that CI.

## What stays unresolved

- Assessment PDF: not supplied, so no duration, weighting, or required slide count beyond the eight
  named in the ticket.
- The local Georgian diploma: not in this checkout. The story follows the summary in the ticket,
  `CONTEXT.md`, the ADRs on `main`, and the source named below.
- No vault was started. Setup, reset, rehearsal, and recording are unchecked.
- `scripts/check.sh` was not run. This change does not modify the program, and the repository has no
  documentation renderer or markdown linter on `PATH` (`markdownlint` and `vale` were absent).
