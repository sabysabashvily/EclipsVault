# Defence draft / დაცვის მონახაზი

English and Georgian storyboard for one talk about EclipsVault. The talk starts with a synthetic
Phoenix team opening one synthetic secret, then looks at that reveal in the audit log. The owner confirmed a presentation cap of 30 minutes. Detailed slide timing is still
unallocated; the supplied C4 guide contains no timing rule.

ეს ერთი მოხსენების ინგლისურ-ქართული მონახაზია EclipsVault-ის შესახებ. ჯერ გამოგონილი გუნდი Phoenix
ერთ სატესტო საიდუმლოს ხსნის, შემდეგ კი გახსნის ჩანაწერს აუდიტის ჟურნალში ვნახავთ.
მფლობელმა დაადასტურა, რომ პრეზენტაცია მაქსიმუმ 30 წუთს უნდა გაგრძელდეს. სლაიდებს შორის დრო ჯერ
არ განაწილებულა. მოწოდებული C4 გზამკვლევი პრეზენტაციის ხანგრძლივობას არ განსაზღვრავს.

This folder is the rehearsal draft for the 24 November defence. It records one disposable
Development walk. It does not record a production deployment or a security guarantee.

## Read in this order

| File | What it is |
|---|---|
| [storyboard.md](storyboard.md) | Eight bilingual slides within a 30-minute cap; detailed allocation pending |
| [speaker-notes.md](speaker-notes.md) | What to say, and what not to claim |
| [diagrams.md](diagrams.md) | Context, container, Web component, reveal code, deployment |
| [diagrams.html](diagrams.html) | The same five pictures, rendered, with bilingual captions |
| [security-flows.md](security-flows.md) | Sign-in, invitation, access, reveal, keys, checkpoint, dynamic lease |
| [demo-setup-reset.md](demo-setup-reset.md) | Disposable Development setup, the walk, and reset |
| [rehearsal-checklist.md](rehearsal-checklist.md) | One-page run order for the presenter |
| [questions.md](questions.md) | Likely questions, with the answer the source supports |
| [screens/](screens/) | Light-theme captures of the live steps. No secret values |

## Revision this draft was read from

| Name | Commit | What it is |
|---|---|---|
| `origin/main` | `1298958050e45f83aefeb4b6455d0a02169ab22a` | Current `main` when this draft was refreshed, 24 November 2026, 15:18 Asia/Tbilisi. Merge of the third UI polish pass (#217). |

Earlier drafts were read at `0bb59de` on 10 November 2026 and at `d576dd9` on 21 November 2026.
Those notes are history. The pictures, the flows, and the screenshots in this folder describe
`1298958`.

## Current on this revision

The code on `origin/main` at `1298958` includes:

- Angular sign-in. `AngularUiRoutes` serves the browser pages `/Account/Login`, `/Account/Totp`,
  `/Account/EnrollTotp`, `/Account/RecoveryCode`, and `/Account/Recover`. Those pages call
  `/api/ui/auth` (ADR-0032). `AccountController` keeps only the two single sign-on redirects
- new accounts are activated by an emailed invitation: a 256-bit token stored only as a SHA-256 hash,
  valid for 72 hours, sent through real SMTP to a link built from `Identity:PublicUrl`. The person
  sets the password and enrolls the authenticator in one audited transaction (ADR-0033)
- no Razor view, `site.css`, or `site.js` is left. Failures outside the Angular UI get one of three
  server pages, access blocked, error, or not found, written by `ServerPage` with
  `server-page.css` (ADR-0034)
- `Database:Provider` must be `Postgres` or `SqlServer`. Startup stops when it is missing or unknown
- a reference production deployment in `deploy/production/`: Caddy with automatic HTTPS, the vault
  image, and PostgreSQL, with the Data Protection key ring on a volume and
  `ForwardedHeaders__KnownProxies` set to Caddy's address. `docs/INSTALL.md` walks through it
- Vault Transit master keys on the Encryption keys page (`ICryptoEngine.GetMasterKeysAsync`), and a
  rotation that fails after committing batches reports how many items moved. The response is
  `audit_unavailable` for an audit failure or `rotation_interrupted` for another failure. The
  Transit token needs `read` on `transit/keys/<key>` (`README.md`, ADR-0029)
- administrators can release the guards a failed break-glass recovery left, from Networks
  (`NetworksController` `blocks/release-guard`)
- dynamic secrets on PostgreSQL as well as SQL Server (`PostgresBackend`,
  `DynamicSecrets:PostgresTargetConnectionString`). Development seeds `phoenix_pg_reader` and
  `global_pg_writer` next to the SQL Server roles
- the `saas-design` redesign and three polish passes: a collapsible sidebar, a command palette
  ("Search or jump to…", ⌘K), and equal light and dark themes

ამ რევიზიაში ბრაუზერის ინტერფეისი, შესვლის გვერდების ჩათვლით, Angular-ზე მუშაობს. Razor-ის ხედები
სრულად ამოღებულია. ელფოსტით მიღებული მოწვევით მომხმარებელი თავად ადგენს პაროლს და არეგისტრირებს
ავთენტიფიკატორს. ორივე მოქმედება ერთ ტრანზაქციაში სრულდება და აუდიტში აღირიცხება. Angular-ის
გარეთ სერვერი სამიდან შესაბამის გვერდს აბრუნებს. საცავი `Database:Provider`-ის გარეშე არ ეშვება.
რეპოზიტორიაში მოცემულია Production-ში განთავსების ნიმუში Caddy-ითა და PostgreSQL-ით.
დინამიკური საიდუმლოები PostgreSQL-ზეც მუშაობს.

Master-key rotation runs in resumable batches (ADR-0029). The audit trail records refusals
(ADR-0031). Audit bundle export streams its result after a step-up check (ADR-0030).

Verify still does not prove every deletion. Deleting checkpoints, or changing rows after the latest
checkpoint signed by the current key, can leave it quiet. See [security-flows.md](security-flows.md).

The talk still runs on the Development demo world. It shows nothing about a production deployment,
even though the repository now has a reference one.

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

## Verification and open items

- Presentation cap: at most 30 minutes, confirmed by the owner. Detailed slide allocation and
  marking criteria remain unconfirmed. The supplied C4 guide requires context/container diagrams
  and PDF export; it gives no timing rule.
- The local Georgian diploma: not in this checkout. The story follows `CONTEXT.md`, the ADRs on
  `main`, and the source named in the other files.
- `scripts/check.sh` passed locked restore, the Release build, and the unit suite during the draft
  refresh. The Release build that served the screenshots finished with no warnings and no errors.
- The 1.0.0 verifier binary was not executed. The note in the section above is the repository README's.
- The reference production deployment was read, not started. No invitation email was sent during
  the walk.
