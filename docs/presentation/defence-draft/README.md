# Defence draft / დაცვის მონახაზი

The 24 November defence uses one example in English and Georgian. A fictional Phoenix team opens
a test secret, then an administrator finds the reveal in the audit log. The owner confirmed a
30-minute limit. Slide timings are pending, and the supplied C4 guide gives no timing rule.

EclipsVault-ის დაცვაზე ერთ მაგალითს ინგლისურად და ქართულად განვიხილავთ. გამოგონილი გუნდი Phoenix
ერთ სატესტო საიდუმლოს ხსნის. შემდეგ ადმინისტრატორი აუდიტის ჟურნალში პოულობს გამჟღავნების ჩანაწერს.
მფლობელმა დაადასტურა, რომ პრეზენტაცია მაქსიმუმ 30 წუთს უნდა გაგრძელდეს. სლაიდებს შორის დრო ჯერ
არ განაწილებულა. მოწოდებული C4 გზამკვლევი პრეზენტაციის ხანგრძლივობას არ განსაზღვრავს.

The rehearsal ran once in a disposable Development vault. Its results apply to that run and
provide no evidence about production deployment or security guarantees.

## Rehearsal reading order

| File | What it is |
|---|---|
| [storyboard.md](storyboard.md) | Eight bilingual slides within a 30-minute cap, with slide timings pending |
| [speaker-notes.md](speaker-notes.md) | Narration and limits on what the demo proves |
| [diagrams.md](diagrams.md) | Context, container, Web component, reveal code, deployment |
| [diagrams.html](diagrams.html) | Five rendered diagrams with bilingual captions |
| [security-flows.md](security-flows.md) | Sign-in, invitation, access, reveal, keys, checkpoint, dynamic lease |
| [demo-setup-reset.md](demo-setup-reset.md) | Disposable Development setup, demo steps, and reset |
| [rehearsal-checklist.md](rehearsal-checklist.md) | One-page run order for the presenter |
| [questions.md](questions.md) | Likely questions, with the answer the source supports |
| [screens/](screens/) | Light-theme screenshots of the demo with secret values excluded |

## Rehearsal revision

| Name | Commit | What it is |
|---|---|---|
| `origin/main` | `1298958050e45f83aefeb4b6455d0a02169ab22a` | Tip of `main` at the 24 November 2026 refresh, 15:18 Asia/Tbilisi. This commit merged the third UI polish pass (#217). |

Earlier drafts describe `0bb59de` on 10 November 2026 and `d576dd9` on 21 November 2026.
The diagrams, security flows, and screenshots here describe `1298958`.

## Features at the rehearsal revision

The code on `origin/main` at `1298958` includes:

- Sign-in runs in Angular. `AngularUiRoutes` serves the browser pages `/Account/Login`, `/Account/Totp`,
  `/Account/EnrollTotp`, `/Account/RecoveryCode`, and `/Account/Recover`. Those pages call
  `/api/ui/auth` (ADR-0032). `AccountController` keeps only the two single sign-on redirects.
- The vault activates new accounts by email. It uses `Identity:PublicUrl` to build the invitation link
  and sends it through real SMTP. The 256-bit token lasts 72 hours, and the vault stores only
  its SHA-256 hash. The user sets a password and enrolls an authenticator in one audited
  transaction (ADR-0033).
- This revision has no Razor views, `site.css`, or `site.js`. For failures outside the Angular UI,
  `ServerPage` uses `server-page.css` to return one of three pages for access blocked, error, or not
  found (ADR-0034).
- `Database:Provider` must be `Postgres` or `SqlServer`. A missing or unknown value stops startup.
- The reference production deployment in `deploy/production/` uses Caddy for automatic HTTPS, the
  vault image, and PostgreSQL. A volume holds the Data Protection key ring, and
  `ForwardedHeaders__KnownProxies` names Caddy's address. Setup is in `docs/INSTALL.md`.
- The Encryption keys page lists Vault Transit master keys (`ICryptoEngine.GetMasterKeysAsync`).
  A rotation that fails after committing batches reports how many items it rewrapped. The response is
  `audit_unavailable` for an audit failure or `rotation_interrupted` for another failure. The
  Transit token needs `read` on `transit/keys/<key>` (`README.md`, ADR-0029).
- Administrators can release guards left by a failed break-glass recovery from Networks
  (`NetworksController` `blocks/release-guard`).
- Dynamic secrets support PostgreSQL and SQL Server (`PostgresBackend`,
  `DynamicSecrets:PostgresTargetConnectionString`). Development seeds `phoenix_pg_reader` and
  `global_pg_writer` next to the SQL Server roles.
- After the `saas-design` redesign and three polish passes, the UI has a collapsible sidebar, a
  command palette ("Search or jump to…", ⌘K), and equal light and dark themes.

ბრაუზერის ინტერფეისი და შესვლის გვერდები Angular-ზე მუშაობს. ამ რევიზიაში Razor-ის ხედები აღარ არის.
ელფოსტით მიღებული მოწვევით მომხმარებელი ერთ ტრანზაქციაში ადგენს პაროლს და არეგისტრირებს
ავთენტიფიკატორს. საცავი აქტივაციას აუდიტში აღრიცხავს. Angular-ის გარეთ სერვერი სამიდან შესაბამის
შეცდომის გვერდს აბრუნებს. საცავი `Database:Provider`-ის გარეშე არ ეშვება.
რეპოზიტორიაში მოცემულია Production-ში განთავსების ნიმუში Caddy-ითა და PostgreSQL-ით.
დინამიკური საიდუმლოები PostgreSQL-ზეც მუშაობს.

Master-key rotation runs in resumable batches (ADR-0029). The audit trail records refusals
(ADR-0031). Audit bundle export streams its result after a step-up check (ADR-0030).

Verify can miss deleted checkpoints or changes to rows after the latest checkpoint signed by the
current key. See [security-flows.md](security-flows.md).

## Verifier compatibility

From `README.md` at `origin/main`, the signed-checkpoint paragraph:

> A bundle records the format it was written in, and a verifier reads its own format and every older
> one — so a bundle an auditor archived years ago keeps verifying. The reverse does not hold: a
> verifier older than the vault that exported the bundle refuses it and says so explicitly, rather
> than recomputing hashes under the wrong scheme and reporting an honest trail as edited. Auditors
> should keep a verifier at least as new as the vault. One exception: the 1.0.0 verifier shipped
> before this check existed, so given a bundle from a later vault it reports `INVALID — "the row was
> edited"`. That is a verifier that is too old, not a trail that was tampered with; re-run with the
> current verifier to get a true answer.

`AuditBundleVerifier` and `AuditBundleSchema` (`V1` and `V2`) handle unknown formats as version
errors. The 1.0.0 behavior comes from the repository README. The rehearsal did not run that binary.

## Verification and open items

- The owner confirmed the 30-minute limit. Slide timings and marking criteria remain unconfirmed.
  The supplied C4 guide requires context and container diagrams and PDF export. It gives no timing rule.
- This checkout lacks the local Georgian diploma. The defence follows `CONTEXT.md`, the ADRs on
  `main`, and the source named in the other files.
- `scripts/check.sh` passed locked restore, the Release build, and the unit suite during the draft
  refresh. The Release build that served the screenshots finished with no warnings and no errors.
- The reference production deployment was reviewed without starting it. The rehearsal sent no
  invitation email.
