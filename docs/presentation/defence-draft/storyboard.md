# Storyboard / სცენარი

Eight slides, in this order. Each slide has the same beat in English and in Georgian. The owner confirmed a presentation cap of 30 minutes. Detailed slide timing is
unallocated; the supplied C4 guide contains no timing rule. Visuals for slides 3, 4, and 6 are in
[diagrams.md](diagrams.md) and [security-flows.md](security-flows.md).

The working story is one path. A synthetic Phoenix team has a vault account on project `PHOENIX`.
That account opens one Internal Development secret, `Phoenix_Dev_Database_Password`. An administrator
then finds the `Revealed` row in the audit log. Do not open another secret during the talk.

## Slide 1 — Problem / პრობლემა

Timing: allocation pending within the presentation's 30-minute cap.

ამ სლაიდისთვის დრო ჯერ არ გამოყოფილა. პრეზენტაციისთვის სულ მაქსიმუმ 30 წუთია.

### On screen (English)

A team keeps a shared credential in a file or a chat. Anyone who can read the file can read the
secret, and nobody can show afterwards who opened it, from where, or whether the record was changed.

EclipsVault is a self-hosted vault for that problem. It decides each request itself, seals each value
under its own data key, and writes an audit row for the reveal.

### On screen (ქართული)

გუნდი წვდომის საერთო მონაცემებს ფაილში ან ჩატში ინახავს. ფაილზე წვდომის მქონე ყველას შეუძლია
საიდუმლოს წაკითხვა. მოგვიანებით ვერავინ დაადგენს, ვინ გახსნა საიდუმლო, საიდან და შეიცვალა თუ არა ჩანაწერი.

EclipsVault ამ პრობლემას მომხმარებლის ინფრასტრუქტურაზე განთავსებული საცავით აგვარებს. საცავი თავად
იღებს გადაწყვეტილებას თითოეულ მოთხოვნაზე, თითოეულ მნიშვნელობას ცალკე მონაცემთა გასაღებით შიფრავს
და მნიშვნელობის გამჟღავნებას აუდიტის ჟურნალში აღრიცხავს.

### Speaker shows

No product screen yet. The next slide names the four checks.

პროდუქტის ინტერფეისს ჯერ ნუ აჩვენებთ. შემდეგ სლაიდზე ოთხი შემოწმებაა ჩამოთვლილი.

## Slide 2 — Server-enforced access / წვდომის კონტროლი სერვერზე

Timing: allocation pending within the presentation's 30-minute cap.

ამ სლაიდისთვის დრო ჯერ არ გამოყოფილა. პრეზენტაციისთვის სულ მაქსიმუმ 30 წუთია.

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

წვდომაზე გადაწყვეტილებას სერვერი იღებს. საცავის ანგარიშით შესვლისას მომხმარებელი ჯერ პაროლს
იყენებს, შემდეგ მეორე ფაქტორს. სესია მხოლოდ ამის შემდეგ იქმნება. სერვისის ანგარიში საჯარო API-ს
საკუთარი API გასაღებით მიმართავს. სერვისის ანგარიშისთვის სესია არ იქმნება.

სერვერი საიდუმლოზე წვდომას `SecretAccessPolicy`-ის ოთხი წესით ამოწმებს.

1. ანგარიშის დაშვების დონე საიდუმლოს მგრძნობელობის დონეზე დაბალი არ უნდა იყოს.
2. ანგარიშისა და საიდუმლოს პროექტები უნდა ემთხვეოდეს, გარდა შემთხვევისა, როცა ანგარიშს Top Secret
   დაშვება აქვს ან საიდუმლო უშუალოდ ამ ანგარიშთანაა გაზიარებული.
3. Production საიდუმლოზე წვდომის მოთხოვნა დაშვებულ დროით ფანჯარაში უნდა მოხვდეს.
4. Confidential და უფრო მაღალი მგრძნობელობის საიდუმლოებზე მოთხოვნა სანდო ქსელიდან უნდა მოდიოდეს.

დემონსტრაციისთვის შერჩეული Phoenix-ის საიდუმლო პროექტ `PHOENIX`-ს ეკუთვნის. მისი მგრძნობელობის დონეა
Internal, გარემო კი Development. ამ პროექტის ანგარიშს Standard დაშვებით წვდომა აქვს. დანარჩენი
წესები ძალაშია და ამ საიდუმლოზე არ ვრცელდება.

### Speaker shows

The four rules, then the one sentence that this live secret is the easy case on purpose.

აჩვენეთ ოთხი წესი და ერთი წინადადებით ახსენით, რომ დემონსტრაციისთვის შეგნებულად შეარჩიეთ მარტივი შემთხვევა.

## Slide 3 — Architecture / არქიტექტურა

Timing: allocation pending within the presentation's 30-minute cap.

ამ სლაიდისთვის დრო ჯერ არ გამოყოფილა. პრეზენტაციისთვის სულ მაქსიმუმ 30 წუთია.

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

კონტეინერების დიაგრამაზე ჩანს, რა ეშვება ერთი სისტემის ფარგლებში. Angular UI ბრაუზერში მუშაობს,
შესვლის გვერდების ჩათვლით, და UI API-ს სესიის ქუქით მიმართავს. Web აპლიკაცია ASP.NET Core-ზეა
აწყობილი და .NET 10-ზე მუშაობს. ის აწვდის Angular-ის ფაილებს, ამუშავებს ორივე API-ის მოთხოვნებს და
აბრუნებს სამ საკუთარ სერვერულ გვერდს: „წვდომა დაბლოკილია“, „შეცდომა“ და „ვერ მოიძებნა“. Razor-ის ხედები სრულად
ამოღებულია. `AngularUiRoutes` ცხადად ჩამოთვლის მარშრუტებს, რომლებზეც სერვერი Angular-ის გვერდებს
აბრუნებს. `Database:Provider` ერთ მონაცემთა ბაზას განსაზღვრავს. ამ პარამეტრის გარეშე საცავი არ
ეშვება. Redis მხოლოდ ჩართვის შემთხვევაში გამოიყენება. Core და Infrastructure Web-ის შიდა
კომპონენტებია და ცალკე კონტეინერებად არ ეშვება. Node მხოლოდ იმიჯის ასაწყობად გამოიყენება.

SMTP, Vault Transit, იდენტობის პროვაიდერი და დინამიკური საიდუმლოების სამიზნე ბაზები, SQL Server
ან PostgreSQL, მხოლოდ შესაბამისი კონფიგურაციის შემთხვევაში გამოიყენება. ახალი ანგარიშის
გასააქტიურებელი მოწვევა SMTP-ით იგზავნება. დიაგრამაზე ორი განთავსებაა, Development და Production.
`deploy/production/`-ში აღწერილ ნიმუშში Caddy ავტომატურ HTTPS-ს უზრუნველყოფს. მის უკან საცავი და
PostgreSQL მუშაობს. დემონსტრაცია მხოლოდ Development გარემოში ერთჯერად გაშვებას აჩვენებს.
დიაგრამა მოცემულია [diagrams.md](diagrams.md)-ში.

### Speaker shows

Context diagram, then container diagram, then the Web component diagram. Do not draw Core or
Infrastructure as their own deployable boxes.

თანმიმდევრობით აჩვენეთ კონტექსტის, კონტეინერებისა და Web-ის კომპონენტების დიაგრამები. Core და
Infrastructure Web-ის ფარგლებში დატოვეთ, რადგან ცალ-ცალკე არ განთავსდება.

## Slide 4 — Reveal ordering / მნიშვნელობის გამჟღავნების თანმიმდევრობა

Timing: allocation pending within the presentation's 30-minute cap.

ამ სლაიდისთვის დრო ჯერ არ გამოყოფილა. პრეზენტაციისთვის სულ მაქსიმუმ 30 წუთია.

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

მიმდინარე კოდში საიდუმლოს მნიშვნელობის გაშიფვრა მხოლოდ აუდიტის ჩანაწერის დაფიქსირების შემდეგ ხდება.

`SecretDetailsController.Get` ჯერ `GetDetailsAsync`-ით ტვირთავს დაშიფვრის კონვერტს. ამ დროს ჩანაწერი
არ კეთდება. შემდეგ სერვერი წვდომას ამოწმებს და ნებართვის შემთხვევაში `Viewed`-ს აფიქსირებს.
ამ Internal საიდუმლოსთვის `Reveal` გაძლიერებულ ავთენტიფიკაციას არ ითხოვს. `SecretService.RevealAsync`
`SecretRevealed`-ს `AuditSink`-ის გავლით წერს და ჩანაწერის დაფიქსირებას ელოდება. მხოლოდ ამის შემდეგ
ეშვება `DecryptToDtoAsync`. თუ აუდიტის ჩანაწერი ვერ დაფიქსირდა, მნიშვნელობის გაშიფვრა არ ხდება.

ჟურნალში მნიშვნელობის გამჟღავნება `Revealed`-ად ჩანს, ხოლო ნებადართული დეტალების ნახვა `Viewed`-ად.
წვდომაზე უარის შემთხვევაში ჟურნალი `Access denied`-ს აჩვენებს.

### Speaker shows

The focused code diagram in [diagrams.md](diagrams.md). Say the order as drawn: access, then
`Viewed`, then on "Reveal value" the `Revealed` row, then decryption. Do not say a refused open is
recorded as `Viewed`.

აჩვენეთ შესაბამისი კოდის დიაგრამა [diagrams.md](diagrams.md)-დან. აღწერეთ თანმიმდევრობა: წვდომის
შემოწმება, `Viewed`, "Reveal value"-ზე დაჭერის შემდეგ `Revealed` და ბოლოს გაშიფვრა. უარყოფილი
მოთხოვნა `Viewed`-ად არ აღირიცხება.

## Slide 5 — Live journey / დემონსტრაცია

Timing: allocation pending within the presentation's 30-minute cap.

ამ სლაიდისთვის დრო ჯერ არ გამოყოფილა. პრეზენტაციისთვის სულ მაქსიმუმ 30 წუთია.

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

1. დემონსტრაციისთვის შექმნილი Phoenix-ის ანგარიშით შედით Angular-ის გვერდზე `/Account/Login`.
   ჯერ შეიყვანეთ პაროლი და შემდეგ დაადასტურეთ მეორე ფაქტორი. სესია მხოლოდ მეორე ფაქტორის
   დადასტურების შემდეგ იქმნება. შესვლისას იხსნება `/Secrets`.
2. Secrets-ის სია გაფილტრეთ სახელით `Phoenix_Dev_Database_Password` და დააჭირეთ მის სახელს.
   დეტალების მისამართია `/Secrets/Details/{id}`. დააჭირეთ "Reveal value"-ს.
3. ეკრანის გადაღებამდე დააჭირეთ "Back to list"-ს. დემონსტრაციისთვის თავად მნიშვნელობა საჭირო არ არის.
4. სესიიდან ანგარიშის მენიუს საშუალებით გამოდით. შედით სადემონსტრაციო ადმინისტრატორის ანგარიშით
   და Administration-ში გახსენით აუდიტის ჟურნალი მისამართზე `/Audit`.
5. ჟურნალი იმავე საიდუმლოს სახელით გაფილტრეთ. იპოვეთ `dev-user`-ის ჩანაწერი ნიშნით `Revealed`.
   `Viewed` დეტალების ნახვის ჩანაწერია.

მომზადებისა და გადაღების წესები მოცემულია [demo-setup-reset.md](demo-setup-reset.md)-ში. ნაბიჯების
თანმიმდევრობა ამ რევიზიაზე 24 ნოემბერს შემოწმდა. გაშვების აღწერაც იმავე ფაილშია.

### Speaker shows

`/Audit`, light theme, filtered to `Phoenix_Dev_Database_Password`, showing the `Revealed` badge
and the actor `dev-user`. Do not show enrollment, a recovery code, a private key, a cookie, or the
revealed value. Do not open `Production_AWS_Root_Key` or `Global_SQL_SA_Password`. Do not press
"Verify integrity" or "Export signed bundle".

აჩვენეთ `/Audit` ღია თემაში, `Phoenix_Dev_Database_Password`-ის ფილტრით. მიუთითეთ `Revealed`-ზე და
მოქმედების შემსრულებელ ანგარიშზე `dev-user`. ნუ აჩვენებთ ავთენტიფიკატორის რეგისტრაციას, აღდგენის
კოდს, კერძო გასაღებს, ქუქის ან გამჟღავნებულ მნიშვნელობას. ნუ გახსნით `Production_AWS_Root_Key`-ს ან
`Global_SQL_SA_Password`-ს. ნუ დააჭერთ "Verify integrity"-ს ან "Export signed bundle"-ს.

## Slide 6 — Checkpoint proof boundary / რას ადასტურებს საკონტროლო წერტილი

Timing: allocation pending within the presentation's 30-minute cap.

ამ სლაიდისთვის დრო ჯერ არ გამოყოფილა. პრეზენტაციისთვის სულ მაქსიმუმ 30 წუთია.

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

Verify ჰეშების ჯაჭვში გაერთიანებულ ჩანაწერებს ნაკადად კითხულობს და მოქმედი გასაღებით ხელმოწერილ
საკონტროლო წერტილებს ადარებს. ხელყოფაზე მიუთითებს მიმდევრობაში გამოტოვებული ჩანაწერი, ჯაჭვის
გაწყვეტილი ბმა, საკონტროლო წერტილისა და ჯაჭვის ბოლო ჰეშის შეუსაბამობა ან ბოლო ასეთი წერტილით
მოცულ მონაკვეთში ბოლო ჩანაწერების დანაკლისი. გვერდი განმარტავს, რომ ბოლო წერტილის ჩათვლით ჩანაწერები
ხელმოწერის გასაღებით მოწმდება, მომდევნო ჩანაწერებს კი შემდეგ საკონტროლო წერტილამდე მხოლოდ ჰეშების
ჯაჭვი აკავშირებს.

Verify-მ შეიძლება დარღვევა ვერ აღმოაჩინოს, თუ თავად საკონტროლო წერტილები წაიშალა ან მხოლოდ ბოლო
წერტილის შემდეგ ჩაწერილი მონაცემები შეიცვალა და მათი ჰეშები ხელახლა გამოითვალა. აუდიტორთან უკვე
შენახულ ხელმოწერილ ფაილში ჯაჭვის იმ მომენტის ბოლო ჰეშია დაფიქსირებული. ოფლაინ შემმოწმებელი ამოწმებს
ამ ფაილს და შემდეგ მოქმედ ბაზას აღარ აკვირდება. ვისაც საცავის ხელმოწერის გასაღების წაკითხვა შეუძლია,
ხელახლა ხელმოწერაც შეუძლია.

ჟურნალის გახსნისას იწერება `AuditLogViewed`, ხოლო Verify წერს `AuditChainVerified`-ს.
საკონტროლო წერტილი და `AuditCheckpointCreated` ერთ ტრანზაქციაში ფიქსირდება. Export ახლახან გავლილ
გაძლიერებულ ავთენტიფიკაციას მოითხოვს, წერს `AuditBundleExported`-ს და მხოლოდ ამ ჩანაწერის შემდეგ
აგზავნის მონაცემებს ნაკადად. ერთდროულად მხოლოდ ერთი შემოწმება და ერთი ექსპორტი შეიძლება შესრულდეს.
პაკეტის ფორმატი კვლავ მე-2 ვერსიაა. ძველი შემმოწმებლის თავსებადობის შეზღუდვა ამ საქაღალდის
[README](README.md)-შია აღწერილი.

### Speaker shows

The boundary, in one sentence: proof to an outsider is a bundle they already hold, checked offline
with a verifier new enough for that bundle. Do not say every deletion is detectable. Do not read
the anchored message as "the whole database is proven".

ერთი წინადადებით ახსენით, რომ გარე მხარისთვის მტკიცებულებაა მის ხელთ არსებული პაკეტი, რომელიც
ოფლაინ, ამ ფორმატთან თავსებადი ვერსიის შემმოწმებლით მოწმდება. ყველა წაშლის აღმოჩენა შეუძლებელია.
საკონტროლო წერტილის შესახებ შეტყობინება მთელი ბაზის სისწორეს არ ადასტურებს.

## Slide 7 — Final revision and test evidence / საბოლოო რევიზია და ტესტების შედეგები

Timing: allocation pending within the presentation's 30-minute cap.

ამ სლაიდისთვის დრო ჯერ არ გამოყოფილა. პრეზენტაციისთვის სულ მაქსიმუმ 30 წუთია.

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

ეს სლაიდი მოხსენების საბოლოო ვერსიის დამტკიცების შემდეგ შეავსეთ. მონახაზში მხოლოდ განხილული
მასალაა აღწერილი.

| პუნქტი | მონაცემები |
|---|---|
| მოხსენების რევიზია | `1298958050e45f83aefeb4b6455d0a02169ab22a`. სლაიდების საბოლოო ვერსია ჯერ არ დამტკიცებულა |
| განხილული `origin/main` | იგივე კომიტი, 2026 წლის 24 ნოემბერი, 15:18 Asia/Tbilisi |
| ერთჯერადი რეპეტიცია Development გარემოში | მომზადება, დემონსტრაცია და ეკრანის ხუთი ანაბეჭდი სხვა პორტზე გაშვებული საცავიდან. 2026 წლის 24 ნოემბერი, დაახლოებით 16:03 Asia/Tbilisi. იხილეთ [demo-setup-reset.md](demo-setup-reset.md) |
| რეპეტიციისთვის აგება Release რეჟიმში | `dotnet build EclipsVault.slnx -c Release`: 0 გაფრთხილება, 0 შეცდომა |
| რეპოზიტორიის შემოწმება | `scripts/check.sh`-ით წარმატებით დასრულდა დამოკიდებულებების ფიქსირებული ვერსიებით აღდგენა, Release რეჟიმში აგება და ერთეულოვანი ტესტები |
| სარეზერვო ჩანაწერი | არ გაკეთებულა. ჩაწერის ინსტრუქცია რეპეტიციის საკონტროლო სიაშია |

### Speaker shows

Read the revision table. Give the unit-check result and describe what the Development walk showed.

წაიკითხეთ რევიზიის ცხრილი. დაასახელეთ ერთეულოვანი ტესტების შედეგი და აღწერეთ, რა გამოჩნდა
Development გარემოში ჩატარებულ დემონსტრაციაზე.

## Slide 8 — Lessons and limitations / დასკვნები და შეზღუდვები

Timing: allocation pending within the presentation's 30-minute cap.

ამ სლაიდისთვის დრო ჯერ არ გამოყოფილა. პრეზენტაციისთვის სულ მაქსიმუმ 30 წუთია.

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

- საიდუმლოზე წვდომას სერვერი აკონტროლებს. Angular-ის ინტერფეისი ამ გადაწყვეტილებას არ იღებს.
- მნიშვნელობის გამჟღავნების ჩანაწერი გაშიფვრამდე ფიქსირდება. წვდომაზე უარი `Access denied`-ად
  აღირიცხება. `Viewed` მხოლოდ ნებადართულ ნახვას აღნიშნავს.
- მნიშვნელობა მონაცემთა გასაღებითაა დაშიფრული. მონაცემთა გასაღები მასტერ-გასაღებითაა შეფუთული.
  დინამიკური საიდუმლოს იჯარის ჩანაწერში გაცემული პაროლი არ ინახება.
- Verify აუდიტის ჩანაწერებს მოქმედი გასაღებით ხელმოწერილი ბოლო საკონტროლო წერტილის ჩათვლით
  ადასტურებს. შემდეგ ჩანაწერებს მხოლოდ ჰეშების ჯაჭვი აკავშირებს. სხვა პირის მიერ შენახული პაკეტი
  შემოწმების მესამე საშუალებაა.
- Verify-მ შეიძლება დარღვევა ვერ დააფიქსიროს, თუ საკონტროლო წერტილები წაიშალა ან მხოლოდ ბოლო
  წერტილის შემდეგ ჩაწერილი მონაცემები შეიცვალა. ეს შეზღუდვა კვლავ მოქმედებს და ADR-0030-შია აღწერილი.
- 1.0.0 ვერსიის შემმოწმებელმა უცვლელი პაკეტიც შეიძლება შეცვლილად გამოაცხადოს, თუ პაკეტის ფორმატი
  უფრო ახალია. გაითვალისწინეთ ეს გამონაკლისი და გამოიყენეთ საცავის ვერსიის ტოლი ან უფრო ახალი
  შემმოწმებელი.
- ბრაუზერში შესვლის გვერდები Angular-ზე მუშაობს. ახალი ანგარიში ელფოსტით გაგზავნილი მოწვევით
  აქტიურდება. დემონსტრაციაზე მოწვევა არ ჩანს, რადგან ბმული მოქმედ ტოკენს შეიცავს.
- სადემონსტრაციო მონაცემები მხოლოდ Development გარემოში იქმნება. რეპოზიტორიაში მოცემულია
  Production-ში განთავსების ნიმუში, რომელიც ამ მოხსენებისთვის არ გაშვებულა. დემონსტრაცია
  სადემონსტრაციო მონაცემებით ტარდება და Production-ში განთავსების შესახებ დასკვნის საფუძველს არ იძლევა.

### Speaker shows

The limitations, slowly. Stop there.

შეზღუდვები ნელა ჩამოთვალეთ და ამით დაასრულეთ.
