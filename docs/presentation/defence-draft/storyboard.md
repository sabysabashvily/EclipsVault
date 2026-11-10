# Storyboard / სცენარი

Eight slides, in this order. Each slide has the same beat in English and in Georgian. Timing is
unallocated until the assessment PDF arrives. Visuals for slides 3, 4, and 6 are in
[diagrams.md](diagrams.md) and [security-flows.md](security-flows.md).

The working story is one path. A synthetic Phoenix team has a vault account on project `PHOENIX`.
That account opens one Internal Development secret, `Phoenix_Dev_Database_Password`. An administrator
then finds the `Revealed` row in the audit log. Do not open another secret during the talk.

## Slide 1 — Problem / პრობლემა

Timing: unallocated (assessment PDF not supplied).

დრო: გამოყოფილი არ არის (შეფასების PDF არ არის მოწოდებული).

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

## Slide 2 — Server-enforced access / სერვერის მიერ დაცული წვდომა

Timing: unallocated (assessment PDF not supplied).

დრო: გამოყოფილი არ არის (შეფასების PDF არ არის მოწოდებული).

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

## Slide 3 — Architecture / არქიტექტურა

Timing: unallocated (assessment PDF not supplied).

დრო: გამოყოფილი არ არის (შეფასების PDF არ არის მოწოდებული).

### On screen (English)

The container picture has the runtime containers inside one system. The Angular UI runs in the
browser and calls the UI API with the session cookie. The Web application, ASP.NET Core on .NET 10,
serves the Angular files, both APIs, and the Legacy pages that remain. One database is selected.
Redis is there only when enabled. Core and Infrastructure are components inside Web, not containers.
Node only builds the image.

SMTP, Vault Transit, and an identity provider are present only when configured. Development and
production are the two deployments. The talk's demonstration is a disposable Development run. The
picture is [diagrams.md](diagrams.md).

### On screen (ქართული)

კონტეინერის სურათში ერთი სისტემის გაშვების კონტეინერებია. Angular UI ბრაუზერში ეშვება და UI
API-ს სესიის ქუქით იძახებს. Web აპლიკაცია, ASP.NET Core .NET 10-ზე, Angular-ის ფაილებს, ორივე
API-ს და დარჩენილ Legacy გვერდებს აწვდის. არჩეულია ერთი ბაზა. Redis მხოლოდ მაშინ გამოიყენება, როცა
ჩართულია. Core და Infrastructure Web-ის კომპონენტებია, არა კონტეინერები. Node მხოლოდ იმიჯს აწყობს.

SMTP, Vault Transit და იდენტობის მომწოდებელი მხოლოდ მაშინ არსებობენ, როცა კონფიგურაციაშია.
Development და production ორი განთავსებაა. მოხსენების დემონსტრაცია Development-ის ერთჯერადი
გაშვებაა. სურათი [diagrams.md](diagrams.md)-შია.

### Speaker shows

Context diagram, then container diagram, then the Web component diagram. Do not draw Core or
Infrastructure as their own deployable boxes.

## Slide 4 — Reveal ordering / გახსნის რიგითობა

Timing: unallocated (assessment PDF not supplied).

დრო: გამოყოფილი არ არის (შეფასების PDF არ არის მოწოდებული).

### On screen (English)

On the current source, including the Area 6a head, a reveal commits its audit row and only then
decrypts.

`SecretsController.Reveal` loads details, runs the access check, and, for this Internal secret, does
not ask for step-up. `SecretService.RevealAsync` writes `SecretRevealed` through `AuditSink` and
waits until that row is committed. `DecryptToDtoAsync` runs after that wait. If the audit row cannot
be written, this request never decrypts.

The screen label for that row is `Revealed`. The details load just before it writes a separate
`Viewed` row. `Viewed` is the metadata read. `Revealed` is the value.

### On screen (ქართული)

მიმდინარე კოდში, Area 6a-ის თავის ჩათვლით, გახსნა ჯერ აუდიტის ჩანაწერს აფიქსირებს და მხოლოდ შემდეგ
გაშიფრავს.

`SecretsController.Reveal` დეტალებს ტვირთავს, წვდომას ამოწმებს და ამ Internal საიდუმლოსთვის
გაძლიერებულ დადასტურებას არ ითხოვს. `SecretService.RevealAsync` `SecretRevealed`-ს `AuditSink`-ით
წერს და ელოდება, სანამ ჩანაწერი დაფიქსირდება. `DecryptToDtoAsync` ამ ლოდინის შემდეგ ეშვება. თუ
აუდიტის ჩანაწერი ვერ იწერება, ეს მოთხოვნა არ გაშიფრავს.

ეკრანზე ამ ჩანაწერის წარწერა `Revealed` არის. მის წინ დეტალების ჩატვირთვა ცალკე `Viewed` ჩანაწერს
წერს. `Viewed` მეტამონაცემების წაკითხვაა. `Revealed` მნიშვნელობაა.

### Speaker shows

The focused code diagram in [diagrams.md](diagrams.md). Do not say the access check precedes every
audit row: the metadata `Viewed` row is written inside `GetDetailsAsync`, before `CheckAccessAsync`.

## Slide 5 — Live journey / ცოცხალი გზა

Timing: unallocated (assessment PDF not supplied).

დრო: გამოყოფილი არ არის (შეფასების PDF არ არის მოწოდებული).

### On screen (English)

1. Sign in as the synthetic Phoenix account. Password, then second factor. A session exists only
   after the second factor.
2. Open `Phoenix_Dev_Database_Password` and reveal it.
3. Leave that page. The value is not the point.
4. Sign in as the synthetic administrator and open the audit log.
5. Find `Revealed` for that secret name and that account.

Setup and the camera rules are in [demo-setup-reset.md](demo-setup-reset.md). This slide is the
script. The stack was not started while writing it.

### On screen (ქართული)

1. შედით სინთეზურ Phoenix ანგარიშით. პაროლი, შემდეგ მეორე ფაქტორი. სესია მხოლოდ მეორე ფაქტორის
   შემდეგ არსებობს.
2. გახსენით `Phoenix_Dev_Database_Password` და გამოაჩინეთ მნიშვნელობა.
3. დატოვეთ ეს გვერდი. მნიშვნელობა არ არის მთავარი.
4. შედით სინთეზურ ადმინისტრატორად და გახსენით აუდიტის ჟურნალი.
5. იპოვეთ `Revealed` ამ საიდუმლოს სახელზე და ამ ანგარიშზე.

მომზადება და კამერის წესები [demo-setup-reset.md](demo-setup-reset.md)-შია. ეს სლაიდი სცენარია.
მისი წერისას სტეკი არ გაშვებულა.

### Speaker shows

The audit row. Do not show enrollment, a recovery code, a private key, a cookie, or the revealed
value. Do not open `Production_AWS_Root_Key` or `Global_SQL_SA_Password`.

## Slide 6 — Checkpoint proof boundary / საკონტროლო წერტილის მტკიცებულების საზღვარი

Timing: unallocated (assessment PDF not supplied).

დრო: გამოყოფილი არ არის (შეფასების PDF არ არის მოწოდებული).

### On screen (English)

Today, Verify re-walks the chained rows that are still in the database. A gap, a broken link, or a
changed row that was not rehashed is reported. Deleting the newest rows, or rewriting rows and
recomputing every hash, can still leave that walk saying the chain is intact. Verify does not
compare the walk with the signed checkpoints.

A signed checkpoint and an exported bundle pin one head. The offline verifier checks the bundle it
was given. It does not watch the live database afterwards. Entries after the latest checkpoint are
outside that signature. Someone who can delete checkpoints, or edit only the later rows and rehash
them, is outside what today's Verify button proves.

Three Audit log changes are planned, not built: check the chain against the checkpoints, record a
checkpoint in the same transaction as its audit row, and stream the export only after a fresh
step-up and a committed export row. The bundle format stays version 2. An older verifier still has
the compatibility limit in the [README](README.md) of this folder.

### On screen (ქართული)

დღეს Verify იმ ჯაჭვურ ჩანაწერებს გადის, რომლებიც ბაზაში ჯერ კიდევ დევს. რიგითობის წყვეტას, გაწყვეტილ კავშირს
ან შეცვლილ და ხელახლა არ დაჰეშილ ჩანაწერს აჩვენებს. უახლესი ჩანაწერების წაშლამ, ან ჩანაწერების
გადაწერამ და ყველა ჰეშის თავიდან დათვლამ, შეიძლება მაინც დატოვოს შეტყობინება, რომ ჯაჭვი მთლიანია.
Verify ამ გავლას ხელმოწერილ საკონტროლო წერტილებს არ ადარებს.

ხელმოწერილი საკონტროლო წერტილი და ექსპორტირებული შეკვრა ჯაჭვის ბოლო ჩანაწერს ამაგრებს. ხაზგარეშე შემმოწმებელი
იმ შეკვრას ამოწმებს, რომელიც მიეცა. ამის შემდეგ ცოცხალ ბაზას აღარ უყურებს. ბოლო საკონტროლო
წერტილის შემდეგ ჩანაწერები ამ ხელმოწერის გარეთაა. ვისაც საკონტროლო წერტილების წაშლა შეუძლია, ან
მხოლოდ გვიანი ჩანაწერების შეცვლა და თავიდან დაჰეშვა, დღევანდელი Verify ღილაკის მტკიცებულების
გარეთაა.

აუდიტის ჟურნალის სამი ცვლილება დაგეგმილია, არა აშენებული: ჯაჭვის შედარება საკონტროლო წერტილებთან,
საკონტროლო წერტილის ჩაწერა იმავე ტრანზაქციაში, რომელშიც მისი აუდიტის ჩანაწერი იწერება, და ექსპორტის ნაკადად
გაგზავნა მხოლოდ ახალი გაძლიერებული დადასტურებისა და დაფიქსირებული ექსპორტის ჩანაწერის შემდეგ.
შეკვრის ფორმატი ვერსია 2 რჩება. ძველ შემმოწმებელს კვლავ აქვს თავსებადობის საზღვარი ამ საქაღალდის
[README](README.md)-ში.

### Speaker shows

The boundary, in one sentence: proof to an outsider is a bundle they already hold, checked offline
with a verifier new enough for that bundle. Do not say every deletion is detectable.

## Slide 7 — Final revision and test evidence / საბოლოო რევიზია და ტესტის მტკიცებულება

Timing: unallocated (assessment PDF not supplied).

დრო: გამოყოფილი არ არის (შეფასების PDF არ არის მოწოდებული).

### On screen (English)

Fill this slide when the talk is frozen. This draft only records what was read.

| Item | Value at drafting |
|---|---|
| Talk revision | not frozen |
| `origin/main` read | `0bb59de1e8f64d1b7d04468c303c1b108dd6f04b` |
| Area 6a head read | `ab7997e14ba98fd37055e6ca77fab4c6c8aba490` (PR #147, open) |
| PR #147 CI | all listed checks `SUCCESS` when read; latest `e2e` at 21:41 Asia/Tbilisi, 10 November 2026 |
| Thermos and GPT review | not visible in the PR reviews read here |
| This documentation change | `git diff --check` and the draft's own link check; see the PR |
| Stack, rehearsal, recording | not run |

### On screen (ქართული)

ეს სლაიდი მაშინ შეავსეთ, როცა მოხსენება გაყინულია. ეს მონახაზი მხოლოდ წაკითხულს აღწერს.

| პუნქტი | მნიშვნელობა მონახაზის დროს |
|---|---|
| მოხსენების რევიზია | გაყინული არ არის |
| წაკითხული `origin/main` | `0bb59de1e8f64d1b7d04468c303c1b108dd6f04b` |
| წაკითხული Area 6a | `ab7997e14ba98fd37055e6ca77fab4c6c8aba490` (PR #147, ღია) |
| PR #147 CI | წაკითხვისას ყველა ჩამოთვლილი შემოწმება `SUCCESS`; ბოლო `e2e` 10 ნოემბერი 2026, 21:41 Asia/Tbilisi |
| Thermos და GPT განხილვა | აქ წაკითხულ PR განხილვებში არ ჩანს |
| ეს დოკუმენტაცია | `git diff --check` და მონახაზის საკუთარი ბმულების შემოწმება; იხილეთ PR |
| სტეკი, რეპეტიცია, ჩანაწერი | არ გაშვებულა |

### Speaker shows

The revision table. If a cell still says "not run" or "not frozen", say that. Do not invent a test
count.

## Slide 8 — Lessons and limitations / დასკვნები და საზღვრები

Timing: unallocated (assessment PDF not supplied).

დრო: გამოყოფილი არ არის (შეფასების PDF არ არის მოწოდებული).

### On screen (English)

- The server, not the Angular screen, allows or refuses the secret.
- The reveal row is committed before decryption. That is current code.
- A data key seals the value. The master key wraps the data key. The dynamic lease stores no minted
  password.
- The audit walk proves the rows it can see. A checkpoint proves the head it signed. Those are
  different sentences.
- Checkpoint-boundary verification, atomic checkpoint recording, and streamed step-up export are
  planned.
- The 1.0.0 verifier can call an honest later bundle edited. Use a verifier at least as new as the
  vault, and remember that one exception.
- Development seeds a demo world. Production does not. This talk uses the demo world and proves
  nothing about a production deployment.

### On screen (ქართული)

- საიდუმლოს უფლებას ან უარს Angular-ის ეკრანი კი არა, სერვერი იძლევა.
- გახსნის ჩანაწერი გაშიფვრამდე ფიქსირდება. ეს მიმდინარე კოდია.
- მონაცემთა გასაღები მნიშვნელობას ბეჭდავს. მასტერ-გასაღები მონაცემთა გასაღებს ახვევს. დინამიკური
  იჯარა გაცემულ პაროლს არ ინახავს.
- აუდიტის გავლა იმ ჩანაწერებს ამტკიცებს, რომლებსაც ხედავს. საკონტროლო წერტილი იმ თავს ამტკიცებს,
  რომელსაც მოაწერა ხელი. ეს ორი განსხვავებული წინადადებაა.
- საკონტროლო წერტილის საზღვრის შემოწმება, საკონტროლო წერტილის ატომური ჩაწერა და ნაკადური
  გაძლიერებული ექსპორტი დაგეგმილია.
- 1.0.0 შემმოწმებელმა უცვლელ, უფრო ახალ შეკვრას შეიძლება „შეცვლილი“ უწოდოს. გამოიყენეთ საცავის
  თანაბარი ან უფრო ახალი შემმოწმებელი და დაიმახსოვრეთ ეს ერთი გამონაკლისი.
- Development სადემონსტრაციო სამყაროს ქმნის. Production — არა. ეს მოხსენება სადემონსტრაციო სამყაროს იყენებს და
  production განთავსებაზე არაფერს ამტკიცებს.

### Speaker shows

The limitations, slowly. Stop there.
