# Storyboard / სცენარი

Eight slides, in this order. Each slide has the same beat in English and in Georgian. The owner confirmed a presentation cap of 30 minutes. Detailed slide timing is
unallocated; the supplied C4 guide contains no timing rule. Visuals for slides 3, 4, and 6 are in
[diagrams.md](diagrams.md) and [security-flows.md](security-flows.md).

The working story is one path. A synthetic Phoenix team has a vault account on project `PHOENIX`.
That account opens one Internal Development secret, `Phoenix_Dev_Database_Password`. An administrator
then finds the `Revealed` row in the audit log. Do not open another secret during the talk.

## Slide 1 — Problem / პრობლემა

Timing: allocation pending within the presentation's 30-minute cap.

დრო: სლაიდზე ჯერ არ განაწილებულა; პრეზენტაციის ზედა ზღვარია 30 წუთი.

### On screen (English)

A team keeps a shared credential in a file or a chat. Anyone who can read the file can read the
secret, and nobody can show afterwards who opened it, from where, or whether the record was changed.

EclipsVault is a self-hosted vault for that problem. It decides each request itself, seals each value
under its own data key, and writes an audit row for the reveal.

### On screen (ქართული)

გუნდი წვდომის საერთო მონაცემებს ფაილში ან ჩათში ინახავს. ვისაც ფაილის წაკითხვა შეუძლია, საიდუმლოსაც კითხულობს,
და შემდეგ ვერავინ აჩვენებს, ვინ გახსნა ის, საიდან, და შეიცვალა თუ არა ჩანაწერი.

EclipsVault ამ ამოცანის თვითჰოსტირებული საცავია. ის თვითონ წყვეტს ყოველ მოთხოვნას, ყოველ მნიშვნელობას
საკუთარი მონაცემთა გასაღებით ბეჭდავს და გახსნისთვის აუდიტის ჩანაწერს წერს.

### Speaker shows

No product screen yet. The next slide names the four checks.

პროდუქტის ეკრანი ჯერ არ არის. შემდეგი სლაიდი ოთხ შემოწმებას ასახელებს.

## Slide 2 — Server-enforced access / სერვერის მიერ დაცული წვდომა

Timing: allocation pending within the presentation's 30-minute cap.

დრო: სლაიდზე ჯერ არ განაწილებულა; პრეზენტაციის ზედა ზღვარია 30 წუთი.

### On screen (English)

The browser does not decide. A vault account signs in with a password and a second factor, and only
then receives a session. A service account never has a session; it calls the Public API with its own
API key.

For one secret, the server asks four things, in `SecretAccessPolicy`:

1. Clearance is at least the secret's sensitivity.
2. The project matches, unless the account holds Top Secret clearance or an explicit share.
3. A Production secret is inside the production window.
4. Confidential and higher must come from a trusted network.

The Phoenix secret used in this talk is Internal and Development, on project `PHOENIX`. A Standard
account on that project passes. The other rules are real; this one secret simply does not trip them.

### On screen (ქართული)

ბრაუზერი არ წყვეტს. საცავის ანგარიში პაროლით და მეორე ფაქტორით შედის და მხოლოდ ამის შემდეგ იღებს
სესიას. სერვისის ანგარიშს სესია არასოდეს აქვს; ის საჯარო API-ს თავისი API გასაღებით იძახებს.

ერთი საიდუმლოსთვის სერვერი ოთხ რამეს კითხულობს, `SecretAccessPolicy`-ში:

1. დაშვების დონე საიდუმლოს მგრძნობელობაზე ნაკლები არ არის.
2. პროექტი ემთხვევა, თუ ანგარიშს Top Secret დაშვება ან ცალკე გაზიარება არ აქვს.
3. Production საიდუმლო წარმოების ფანჯარაშია.
4. Confidential და უფრო მაღალი სანდო ქსელიდან უნდა მოვიდეს.

ამ მოხსენების Phoenix საიდუმლო Internal და Development არის, პროექტზე `PHOENIX`. ამ პროექტის Standard
ანგარიში გადის. დანარჩენი წესები მოქმედებს; ეს ერთი საიდუმლო უბრალოდ მათ არ ეხება.

### Speaker shows

The four rules, then the one sentence that this live secret is the easy case on purpose.

ოთხი წესი, შემდეგ ერთი წინადადება, რომ ეს ცოცხალი საიდუმლო განზრახ მარტივი შემთხვევაა.

## Slide 3 — Architecture / არქიტექტურა

Timing: allocation pending within the presentation's 30-minute cap.

დრო: სლაიდზე ჯერ არ განაწილებულა; პრეზენტაციის ზედა ზღვარია 30 წუთი.

### On screen (English)

The container picture has the runtime containers inside one system. The Angular UI runs in the
browser, sign-in included, and calls the UI API with the session cookie. The Web application,
ASP.NET Core on .NET 10, serves the Angular files, both APIs, and three server pages of its own:
access blocked, error, and not found. No Razor view is left. An explicit route table,
`AngularUiRoutes`, lists the page routes that serve Angular. One database is selected by `Database:Provider`, which startup
requires. Redis is there only when enabled. Core and Infrastructure are components inside Web, not
containers. Node only builds the image.

SMTP, Vault Transit, an identity provider, and the dynamic-secret target databases, SQL Server or
PostgreSQL, are present only when configured. SMTP carries the invitation that activates a new
account. Development and production are the two deployments; production is drawn as the reference
deployment in `deploy/production/`, Caddy with automatic HTTPS in front of the vault and
PostgreSQL. The talk's demonstration is a disposable Development run. The picture is
[diagrams.md](diagrams.md).

### On screen (ქართული)

კონტეინერის სურათში ერთი სისტემის გაშვების კონტეინერებია. Angular UI ბრაუზერში ეშვება, შესვლის
ჩათვლით, და UI API-ს სესიის ქუქით იძახებს. Web აპლიკაცია, ASP.NET Core .NET 10-ზე, Angular-ის
ფაილებს, ორივე API-ს და საკუთარ სამ სერვერულ გვერდს აწვდის: დაბლოკილი წვდომა, შეცდომა და „ვერ
მოიძებნა“. Razor-ის ხედი აღარ დარჩა. ცხადი მარშრუტების ცხრილი, `AngularUiRoutes`, ჩამოთვლის
მარშრუტებს, რომლებზეც სერვერი Angular-ის გვერდებს აწვდის. ერთ ბაზას `Database:Provider` ირჩევს, რომლის გარეშეც საცავი არ ეშვება. Redis მხოლოდ
მაშინ გამოიყენება, როცა ჩართულია. Core და Infrastructure Web-ის კომპონენტებია, არა კონტეინერები.
Node მხოლოდ იმიჯს აწყობს.

SMTP, Vault Transit, იდენტობის მომწოდებელი და დინამიკური საიდუმლოების სამიზნე ბაზები, SQL Server
ან PostgreSQL, მხოლოდ მაშინ არსებობენ, როცა კონფიგურაციაშია. SMTP ახალი ანგარიშის გასააქტიურებელ
მოწვევას ატარებს. Development და production ორი განთავსებაა; production დახატულია როგორც
`deploy/production/`-ის საცნობარო განთავსება: Caddy ავტომატური HTTPS-ით საცავისა და PostgreSQL-ის
წინ. მოხსენების დემონსტრაცია Development-ის ერთჯერადი გაშვებაა. სურათი
[diagrams.md](diagrams.md)-შია.

### Speaker shows

Context diagram, then container diagram, then the Web component diagram. Do not draw Core or
Infrastructure as their own deployable boxes.

ჯერ კონტექსტის დიაგრამა, შემდეგ კონტეინერის, შემდეგ Web-ის კომპონენტის. Core და Infrastructure
ცალკე განთავსებად უჯრებად ნუ დახატავთ.

## Slide 4 — Reveal ordering / გახსნის რიგითობა

Timing: allocation pending within the presentation's 30-minute cap.

დრო: სლაიდზე ჯერ არ განაწილებულა; პრეზენტაციის ზედა ზღვარია 30 წუთი.

### On screen (English)

On the current source, a reveal commits its audit row and only then decrypts.

`SecretDetailsController.Get` loads the envelope with `GetDetailsAsync`, which writes nothing, then
runs the access check. After allow it commits `Viewed`. For this Internal secret, `Reveal` does not
ask for step-up. `SecretService.RevealAsync` writes `SecretRevealed` through `AuditSink` and waits
until that row is committed. `DecryptToDtoAsync` runs after that wait. If the audit row cannot be
written, this request never decrypts.

The screen label for that row is `Revealed`. `Viewed` is the details page after allow. `Revealed`
is the value. A refusal writes `Access denied`, not `Viewed`.

### On screen (ქართული)

მიმდინარე კოდში გახსნა ჯერ აუდიტის ჩანაწერს აფიქსირებს და მხოლოდ შემდეგ გაშიფრავს.

`SecretDetailsController.Get` კონვერტს `GetDetailsAsync`-ით ტვირთავს, რომელიც არაფერს წერს, შემდეგ
კი წვდომას ამოწმებს. ნებართვის შემდეგ `Viewed`-ს აფიქსირებს. ამ Internal საიდუმლოსთვის `Reveal`
გაძლიერებულ დადასტურებას არ ითხოვს. `SecretService.RevealAsync` `SecretRevealed`-ს `AuditSink`-ით
წერს და ელოდება, სანამ ჩანაწერი დაფიქსირდება. `DecryptToDtoAsync` ამ ლოდინის შემდეგ ეშვება. თუ
აუდიტის ჩანაწერი ვერ იწერება, ეს მოთხოვნა არ გაშიფრავს.

ეკრანზე ამ ჩანაწერის წარწერა `Revealed` არის. `Viewed` ნებართვის შემდეგ დეტალების გვერდია.
`Revealed` მნიშვნელობაა. უარი წერს `Access denied`-ს, არა `Viewed`-ს.

### Speaker shows

The focused code diagram in [diagrams.md](diagrams.md). Say the order as drawn: access, then
`Viewed`, then on "Reveal value" the `Revealed` row, then decryption. Do not say a refused open is
recorded as `Viewed`.

ფოკუსირებული კოდის დიაგრამა [diagrams.md](diagrams.md)-ში. თქვით დახატული რიგი: წვდომა, შემდეგ
`Viewed`, შემდეგ "Reveal value"-ზე `Revealed`, შემდეგ გაშიფვრა. ნუ იტყვით, რომ უარყოფილი გახსნა
`Viewed`-ად იწერება.

## Slide 5 — Live journey / ცოცხალი გზა

Timing: allocation pending within the presentation's 30-minute cap.

დრო: სლაიდზე ჯერ არ განაწილებულა; პრეზენტაციის ზედა ზღვარია 30 წუთი.

### On screen (English)

1. Sign in as the synthetic Phoenix account at `/Account/Login`, an Angular page. Password, then
   second factor. A session exists only after the second factor. Sign-in then opens `/Secrets`.
2. On Secrets, filter to `Phoenix_Dev_Database_Password` and click its name. The details URL is
   `/Secrets/Details/{id}`. Press "Reveal value".
3. Press "Back to list" before any capture. The value is not the point.
4. Sign out from the account menu. Sign in as the synthetic administrator and open the audit log
   under Administration, at `/Audit`.
5. Filter to that secret name. Find the badge `Revealed` for `dev-user`. The `Viewed` row is the
   details page.

Setup and the camera rules are in [demo-setup-reset.md](demo-setup-reset.md). The click sequence
was walked on this revision on 24 November; the run record is in that file.

### On screen (ქართული)

1. შედით სინთეზურ Phoenix ანგარიშით მისამართზე `/Account/Login`, რომელიც Angular-ის გვერდია.
   პაროლი, შემდეგ მეორე ფაქტორი. სესია მხოლოდ მეორე ფაქტორის შემდეგ არსებობს. შესვლის შემდეგ
   იხსნება `/Secrets`.
2. Secrets-ზე გაფილტრეთ `Phoenix_Dev_Database_Password` და დააწკაპუნეთ მის სახელზე. დეტალების
   მისამართია `/Secrets/Details/{id}`. დააჭირეთ "Reveal value"-ს.
3. ჩაწერამდე დააჭირეთ "Back to list"-ს. მნიშვნელობა არ არის მთავარი.
4. ანგარიშის მენიუდან გამოდით. შედით სინთეზურ ადმინისტრატორად და გახსენით აუდიტის ჟურნალი
   Administration-ში, მისამართზე `/Audit`.
5. გაფილტრეთ ამ საიდუმლოს სახელით. იპოვეთ ნიშანი `Revealed` ანგარიშზე `dev-user`. `Viewed`
   დეტალების გვერდია.

მომზადება და კამერის წესები [demo-setup-reset.md](demo-setup-reset.md)-შია. დაწკაპუნების რიგი ამ
რევიზიაზე 24 ნოემბერს გაიარა; გაშვების ჩანაწერი იმ ფაილშია.

### Speaker shows

`/Audit`, light theme, filtered to `Phoenix_Dev_Database_Password`, showing the `Revealed` badge
and the actor `dev-user`. Do not show enrollment, a recovery code, a private key, a cookie, or the
revealed value. Do not open `Production_AWS_Root_Key` or `Global_SQL_SA_Password`. Do not press
"Verify integrity" or "Export signed bundle".

`/Audit`, ღია თემა, გაფილტრული `Phoenix_Dev_Database_Password`-ზე, ნიშნით `Revealed` და აქტორით
`dev-user`. ნუ აჩვენებთ ჩარიცხვას, აღდგენის კოდს, პირად გასაღებს, ქუქის ან გამჟღავნებულ მნიშვნელობას.
ნუ გახსნით `Production_AWS_Root_Key`-ს ან `Global_SQL_SA_Password`-ს. ნუ დააჭერთ "Verify
integrity"-ს ან "Export signed bundle"-ს.

## Slide 6 — Checkpoint proof boundary / საკონტროლო წერტილის მტკიცებულების საზღვარი

Timing: allocation pending within the presentation's 30-minute cap.

დრო: სლაიდზე ჯერ არ განაწილებულა; პრეზენტაციის ზედა ზღვარია 30 წუთი.

### On screen (English)

Verify streams the chained rows and compares them with checkpoints signed by the current key. A
gap, a broken link, a checkpoint whose head does not match, or a missing tail before the latest
such checkpoint is reported as tampering. The page then says where proof ends: entries up to that
checkpoint are proven against the signing key; later entries are only the hash chain until the next
checkpoint.

Deleting the checkpoints themselves, or rewriting only the rows after the latest checkpoint and
recomputing those hashes, can still leave Verify quiet. A signed export pins one head in a file the
auditor already holds. The offline verifier checks that file. It does not watch the live database
afterwards. Someone who can read the vault's own signing key can re-sign.

Opening the log writes `AuditLogViewed`. Verify writes `AuditChainVerified`. A checkpoint commits
in the same transaction as `AuditCheckpointCreated`. Export writes `AuditBundleExported` and streams
only after that row, and only after a fresh step-up. Verify and Export run one at a time. The
bundle format stays version 2. An older verifier still has the compatibility limit in the
[README](README.md) of this folder.

### On screen (ქართული)

Verify ჯაჭვურ ჩანაწერებს ნაკადად გადის და ადარებს მათ მიმდინარე გასაღებით ხელმოწერილ საკონტროლო
წერტილებს. რიგითობის წყვეტას, გაწყვეტილ კავშირს, თავს რომელიც არ ემთხვევა, ან ბოლო ასეთ წერტილამდე
აკლულ კუდს ხელყოფად აჩვენებს. გვერდი შემდეგ ამბობს, სად მთავრდება მტკიცებულება: იმ წერტილამდე
ჩანაწერები ხელმოწერის გასაღებითაა დამტკიცებული; გვიანი ჩანაწერები მხოლოდ ჰეშის ჯაჭვია შემდეგ
წერტილამდე.

თავად საკონტროლო წერტილების წაშლამ, ან მხოლოდ ბოლო წერტილის შემდეგ ჩანაწერების გადაწერამ და იმ
ჰეშების თავიდან დათვლამ, შეიძლება მაინც დატოვოს ჩუმი Verify. ხელმოწერილი ექსპორტი ერთ თავს ამაგრებს
ფაილში, რომელიც აუდიტორს უკვე აქვს. ხაზგარეშე შემმოწმებელი იმ ფაილს ამოწმებს. ამის შემდეგ ცოცხალ
ბაზას აღარ უყურებს. ვისაც საცავის საკუთარი ხელმოწერის გასაღების წაკითხვა შეუძლია, თავიდან მოაწერს
ხელს.

ჟურნალის გახსნა წერს `AuditLogViewed`-ს. Verify წერს `AuditChainVerified`-ს. საკონტროლო წერტილი
იმავე ტრანზაქციაში ფიქსირდება, რომელშიც `AuditCheckpointCreated`. Export წერს
`AuditBundleExported`-ს და მხოლოდ ამ ჩანაწერის შემდეგ აგზავნის ნაკადს, და მხოლოდ ახალი გაძლიერებული
დადასტურების შემდეგ. Verify და Export თითო-თითოდ ეშვება. შეკვრის ფორმატი ვერსია 2 რჩება. ძველ
შემმოწმებელს კვლავ აქვს თავსებადობის საზღვარი ამ საქაღალდის [README](README.md)-ში.

### Speaker shows

The boundary, in one sentence: proof to an outsider is a bundle they already hold, checked offline
with a verifier new enough for that bundle. Do not say every deletion is detectable. Do not read
the anchored message as "the whole database is proven".

საზღვარი, ერთ წინადადებაში: მტკიცებულება გარე პირისთვის არის შეკვრა, რომელიც უკვე აქვს, შემოწმებული
ხაზგარეშე იმ შეკვრისთვის საკმარისად ახალი შემმოწმებლით. ნუ იტყვით, რომ ყოველი წაშლა აღმოჩენადია.
დამაგრებულ შეტყობინებას ნუ წაიკითხავთ როგორც „მთელი ბაზა დამტკიცებულია“.

## Slide 7 — Final revision and test evidence / საბოლოო რევიზია და ტესტის მტკიცებულება

Timing: allocation pending within the presentation's 30-minute cap.

დრო: სლაიდზე ჯერ არ განაწილებულა; პრეზენტაციის ზედა ზღვარია 30 წუთი.

### On screen (English)

Fill this slide when the talk is frozen. This draft only records what was read.

| Item | Value |
|---|---|
| Talk revision | `1298958050e45f83aefeb4b6455d0a02169ab22a`, not a frozen slide deck |
| `origin/main` read | that commit, 24 November 2026, 15:18 Asia/Tbilisi |
| Disposable Development walk | setup, the live walkthrough, and five screenshots, 24 November 2026, about 16:03 Asia/Tbilisi, on a port-shifted vault. See [demo-setup-reset.md](demo-setup-reset.md) |
| Release build for that walk | `dotnet build EclipsVault.slnx -c Release`: 0 warnings, 0 errors |
| Repository unit checks | `scripts/check.sh` passed locked restore, Release build, and unit tests |
| Backup recording | not made. The checklist says how |

### On screen (ქართული)

ეს სლაიდი მაშინ შეავსეთ, როცა მოხსენება გაყინულია. ეს მონახაზი მხოლოდ წაკითხულს აღწერს.

| პუნქტი | მნიშვნელობა |
|---|---|
| მოხსენების რევიზია | `1298958050e45f83aefeb4b6455d0a02169ab22a`, გაყინული სლაიდების გარეშე |
| წაკითხული `origin/main` | იგივე commit, 24 ნოემბერი 2026, 15:18 Asia/Tbilisi |
| ერთჯერადი Development გავლა | მომზადება, დემონსტრაციის გავლა და ხუთი ეკრანის სურათი, 24 ნოემბერი 2026, დაახლოებით 16:03 Asia/Tbilisi, სხვა პორტზე გაშვებულ საცავზე. იხილეთ [demo-setup-reset.md](demo-setup-reset.md) |
| ამ გავლის Release აწყობა | `dotnet build EclipsVault.slnx -c Release`: 0 გაფრთხილება, 0 შეცდომა |
| რეპოზიტორიის ერთეულოვანი ტესტები | `scripts/check.sh`-მა წარმატებით დაასრულა ფიქსირებული დამოკიდებულებების აღდგენა, Release აწყობა და ერთეულოვანი ტესტები |
| სარეზერვო ჩანაწერი | არ გაკეთებულა. სია ამბობს, როგორ |

### Speaker shows

Read the revision table. Give the unit-check result and describe what the Development walk showed.

წაიკითხეთ რევიზიის ცხრილი. თქვით ერთეულოვანი ტესტების შედეგი და აღწერეთ, რა აჩვენა Development
დემონსტრაციამ.

## Slide 8 — Lessons and limitations / დასკვნები და საზღვრები

Timing: allocation pending within the presentation's 30-minute cap.

დრო: სლაიდზე ჯერ არ განაწილებულა; პრეზენტაციის ზედა ზღვარია 30 წუთი.

### On screen (English)

- The server, not the Angular screen, allows or refuses the secret.
- The reveal row is committed before decryption. A refusal is `Access denied`, not `Viewed`.
- A data key seals the value. The master key wraps the data key. The dynamic lease stores no minted
  password.
- Verify proves the trail up to the latest checkpoint signed by the current key. Later rows are the
  hash chain only. A bundle held by someone else is a third check.
- Deleting checkpoints, or editing only the rows after the latest checkpoint, can still leave Verify
  quiet. That limit is current, from ADR-0030.
- The 1.0.0 verifier can call an honest later bundle edited. Use a verifier at least as new as the
  vault, and remember that one exception.
- The browser UI includes Angular sign-in. A new account is activated by an emailed
  invitation; the talk does not show one, because the link carries a live token.
- Development seeds a demo world. Production does not. The repository has a reference production
  deployment, which this talk did not start. This talk uses the demo world and proves nothing about
  a production deployment.

### On screen (ქართული)

- საიდუმლოს უფლებას ან უარს Angular-ის ეკრანი კი არა, სერვერი იძლევა.
- გახსნის ჩანაწერი გაშიფვრამდე ფიქსირდება. უარი `Access denied`-ა, არა `Viewed`.
- მონაცემთა გასაღები მნიშვნელობას ბეჭდავს. მასტერ-გასაღები მონაცემთა გასაღებს ახვევს. დინამიკური
  იჯარა გაცემულ პაროლს არ ინახავს.
- Verify ამტკიცებს კვალს მიმდინარე გასაღების ბოლო საკონტროლო წერტილამდე. გვიანი ჩანაწერები მხოლოდ
  ჰეშის ჯაჭვია. სხვის მიერ შენახული შეკვრა მესამე შემოწმებაა.
- საკონტროლო წერტილების წაშლამ, ან მხოლოდ ბოლო წერტილის შემდეგ ჩანაწერების შეცვლამ, შეიძლება მაინც
  დატოვოს ჩუმი Verify. ეს საზღვარი მიმდინარეა, ADR-0030-იდან.
- 1.0.0 შემმოწმებელმა უცვლელ, უფრო ახალ შეკვრას შეიძლება „შეცვლილი“ უწოდოს. გამოიყენეთ საცავის
  თანაბარი ან უფრო ახალი შემმოწმებელი და დაიმახსოვრეთ ეს ერთი გამონაკლისი.
- ბრაუზერის ინტერფეისში შესვლის გვერდი Angular-ისაა. ახალი ანგარიში ელფოსტით გაგზავნილი
  მოწვევით აქტიურდება; მოხსენება მას არ აჩვენებს, რადგან ბმული ცოცხალ ტოკენს ატარებს.
- Development სადემონსტრაციო სამყაროს ქმნის. Production — არა. რეპოზიტორიას აქვს production-ის
  საცნობარო განთავსება, რომელიც ამ მოხსენებას არ გაუშვია. ეს მოხსენება სადემონსტრაციო სამყაროს
  იყენებს და production განთავსებაზე არაფერს ამტკიცებს.

### Speaker shows

The limitations, slowly. Stop there.

საზღვრები, ნელა. იქ გაჩერდით.
