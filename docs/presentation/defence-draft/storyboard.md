# Storyboard / სცენარი

The eight slides follow the same sequence in English and Georgian. The owner confirmed a 30-minute
limit for the presentation. Individual slide timings are pending, and the supplied C4 guide gives
no timing rule. Visuals for slides 3, 4, and 6 are in
[diagrams.md](diagrams.md) and [security-flows.md](security-flows.md).

პრეზენტაციისთვის მაქსიმუმ 30 წუთია გამოყოფილი. ცალკეულ სლაიდებზე დრო ჯერ არ განაწილებულა.

In the demo, a vault account for the fictional Phoenix team opens one Internal Development secret,
`Phoenix_Dev_Database_Password`, in project `PHOENIX`. An administrator then finds the `Revealed` row
in the audit log. Open only that secret during the talk.

## Slide 1: Problem / პრობლემა

### On screen (English)

A team keeps a shared credential in a file or a chat. Anyone who can read the file can read the
secret, and nobody can show afterwards who opened it, from where, or whether the record was changed.

EclipsVault is a self-hosted vault. It checks access on every request, seals each value
under its own data key, and writes an audit row for the reveal.

### On screen (ქართული)

გუნდი წვდომის საერთო მონაცემებს ფაილში ან ჩატში ინახავს. ფაილზე წვდომის მქონე ყველას შეუძლია
საიდუმლოს წაკითხვა. მოგვიანებით ვერავინ დაადგენს, ვინ გახსნა საიდუმლო, საიდან და შეიცვალა თუ არა ჩანაწერი.

EclipsVault მომხმარებლის ინფრასტრუქტურაზე განთავსებული საცავია. ის ყოველ მოთხოვნაზე ამოწმებს
წვდომას, თითოეულ მნიშვნელობას ცალკე მონაცემთა გასაღებით შიფრავს და გამჟღავნებას აუდიტის ჟურნალში
აღრიცხავს.

### Speaker shows

Keep the product off screen until after the problem. The next slide lists the four access checks.

პროდუქტის ინტერფეისს ჯერ ნუ აჩვენებთ. შემდეგ სლაიდზე ოთხი შემოწმებაა ჩამოთვლილი.

## Slide 2: Server-enforced access / წვდომის კონტროლი სერვერზე

### On screen (English)

The server decides who can open a secret. A person signing in with a vault account receives a
session after confirming both the password and the second factor. A service account calls the
Public API with its own API key and does not receive a session.

The server checks access to a secret against four rules in `SecretAccessPolicy`.

1. Clearance is at least the secret's sensitivity.
2. The project matches, unless the account holds Top Secret clearance or an explicit share.
3. A Production secret is inside the production window.
4. Confidential and higher must come from a trusted network.

The Phoenix secret used in this talk is Internal and Development, on project `PHOENIX`. A Standard
account on that project passes. The time and network rules apply to other secrets.

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
Internal, გარემო კი Development. ამ პროექტის ანგარიშს Standard დაშვებით წვდომა აქვს. დროისა და
ქსელის წესები სხვა საიდუმლოებზეც მოქმედებს მათი გარემოსა და მგრძნობელობის მიხედვით.

### Speaker shows

Show all four rules, then explain in one sentence why the demo uses an Internal Development secret.

აჩვენეთ ოთხივე წესი და ერთი წინადადებით ახსენით, რატომ შეარჩიეთ Internal საიდუმლო Development გარემოდან.

## Slide 3: Architecture / არქიტექტურა

### On screen (English)

All runtime containers are part of one system. The Angular UI runs in the browser, handles
sign-in, and calls the UI API with the session cookie. The Web application uses ASP.NET Core on
.NET 10. It serves the Angular files, both APIs, and three server pages for access blocked, error,
and not found. This revision has no Razor views. The `AngularUiRoutes` table explicitly lists the
page routes that serve Angular. At startup, `Database:Provider` must select one database. Redis
runs when enabled. Core and Infrastructure run inside Web as components. Node runs only at image
build time.

SMTP, Vault Transit, an identity provider, and the dynamic-secret target databases, SQL Server or
PostgreSQL, require their respective configuration. The vault sends account activation invitations
through SMTP. The two deployments are Development and the production reference in
`deploy/production/`. In production, Caddy provides automatic HTTPS in front of the vault and
PostgreSQL. The demo runs in a disposable Development vault. See
[diagrams.md](diagrams.md).

### On screen (ქართული)

ყველა კონტეინერი ერთ სისტემას ეკუთვნის. Angular UI ბრაუზერში მუშაობს, შესვლას ამუშავებს და UI API-ს
სესიის ქუქით მიმართავს. Web აპლიკაცია ASP.NET Core-ზეა
აწყობილი და .NET 10-ზე მუშაობს. ის აწვდის Angular-ის ფაილებს, ამუშავებს ორივე API-ის მოთხოვნებს და
აბრუნებს სამ საკუთარ სერვერულ გვერდს: „წვდომა დაბლოკილია“, „შეცდომა“ და „ვერ მოიძებნა“. Razor-ის ხედები სრულად
ამოღებულია. `AngularUiRoutes` ჩამოთვლის მარშრუტებს, რომლებზეც სერვერი Angular-ის გვერდებს
აბრუნებს. გასაშვებად საცავს `Database:Provider`-ით ერთი მონაცემთა ბაზა უნდა მიეთითოს. Redis
მხოლოდ ჩართვისას გამოიყენება. Core და Infrastructure Web-ის შიდა კომპონენტებია. Node მხოლოდ
იმიჯის აგებისას ეშვება.

SMTP, Vault Transit, იდენტობის პროვაიდერი და დინამიკური საიდუმლოების სამიზნე ბაზები, SQL Server
ან PostgreSQL, მხოლოდ შესაბამისი კონფიგურაციის შემთხვევაში გამოიყენება. ახალი ანგარიშის
გასააქტიურებელი მოწვევა SMTP-ით იგზავნება. დიაგრამაზე ორი განთავსებაა, Development და Production.
`deploy/production/`-ში აღწერილ ნიმუშში Caddy ავტომატურ HTTPS-ს უზრუნველყოფს. მის უკან საცავი და
PostgreSQL მუშაობს. დემონსტრაცია მხოლოდ Development გარემოში ერთჯერად გაშვებას აჩვენებს.
დიაგრამა მოცემულია [diagrams.md](diagrams.md)-ში.

### Speaker shows

Show the context, container, and Web component diagrams in that order. Keep Core and
Infrastructure inside Web in the component diagram.

თანმიმდევრობით აჩვენეთ კონტექსტის, კონტეინერებისა და Web-ის კომპონენტების დიაგრამები. Core და
Infrastructure Web-ის ფარგლებში დატოვეთ, რადგან ცალ-ცალკე არ განთავსდება.

## Slide 4: Reveal ordering / მნიშვნელობის გამჟღავნების თანმიმდევრობა

### On screen (English)

`SecretDetailsController.Get` loads the envelope with `GetDetailsAsync` without writing an audit
row. It checks access and commits `Viewed` only if access is allowed. This Internal secret is below
the step-up requirement for `Reveal`. `SecretService.RevealAsync` writes `SecretRevealed` through
`AuditSink` and calls `DecryptToDtoAsync` only after the row commits. If the audit write fails, the
request ends before decryption.

The log shows `Revealed` for a value reveal and `Viewed` for an allowed details request. A refusal
produces `Access denied` without a `Viewed` or `Revealed` entry.

### On screen (ქართული)

`SecretDetailsController.Get` დაშიფვრის კონვერტს `GetDetailsAsync`-ით ტვირთავს აუდიტის ჩანაწერის
გარეშე. შემდეგ წვდომას ამოწმებს და `Viewed`-ს მხოლოდ ნებართვის შემთხვევაში აფიქსირებს.
ამ Internal საიდუმლოსთვის `Reveal` გაძლიერებულ ავთენტიფიკაციას არ ითხოვს. `SecretService.RevealAsync`
`SecretRevealed`-ს `AuditSink`-ის გავლით წერს და ჩანაწერის დაფიქსირებას ელოდება. მხოლოდ ამის შემდეგ
ეშვება `DecryptToDtoAsync`. თუ აუდიტის ჩანაწერი ვერ დაფიქსირდა, მნიშვნელობის გაშიფვრა არ ხდება.

ჟურნალში მნიშვნელობის გამჟღავნება `Revealed`-ად ჩანს, ხოლო ნებადართული დეტალების ნახვა `Viewed`-ად.
წვდომაზე უარის შემთხვევაში ჟურნალი `Access denied`-ს აჩვენებს.

### Speaker shows

Show the sequence diagram in [diagrams.md](diagrams.md). Follow the access check and the `Viewed`
entry, then the "Reveal value" action, the `Revealed` entry, and decryption. Only an allowed
details request produces `Viewed`.

აჩვენეთ შესაბამისი კოდის დიაგრამა [diagrams.md](diagrams.md)-დან. აღწერეთ თანმიმდევრობა: წვდომის
შემოწმება, `Viewed`, "Reveal value"-ზე დაჭერის შემდეგ `Revealed` და ბოლოს გაშიფვრა. უარყოფილი
მოთხოვნა `Viewed`-ად არ აღირიცხება.

## Slide 5: Live journey / დემონსტრაცია

### On screen (English)

1. Sign in as the synthetic Phoenix account on the Angular page at `/Account/Login`. Enter the
   password, then confirm the second factor. The server issues the session and opens `/Secrets`
   after the second factor.
2. On Secrets, filter to `Phoenix_Dev_Database_Password` and click its name. The details URL is
   `/Secrets/Details/{id}`. Press "Reveal value".
3. Press "Back to list" before any capture so the value stays out of the recording.
4. Sign out from the account menu. Sign in as the synthetic administrator and open the audit log
   under Administration, at `/Audit`.
5. Filter by the same secret name. Find the `Revealed` badge for `dev-user` and the `Viewed` entry
   for opening the details page.

The 24 November run on this revision, setup instructions, and capture rules are in
[demo-setup-reset.md](demo-setup-reset.md).

### On screen (ქართული)

1. დემონსტრაციისთვის შექმნილი Phoenix-ის ანგარიშით შედით Angular-ის გვერდზე `/Account/Login`.
   ჯერ შეიყვანეთ პაროლი და შემდეგ დაადასტურეთ მეორე ფაქტორი. სესია მხოლოდ მეორე ფაქტორის
   დადასტურების შემდეგ იქმნება. შესვლისას იხსნება `/Secrets`.
2. Secrets-ის სია გაფილტრეთ სახელით `Phoenix_Dev_Database_Password` და დააჭირეთ მის სახელს.
   დეტალების მისამართია `/Secrets/Details/{id}`. დააჭირეთ "Reveal value"-ს.
3. ეკრანის გადაღებამდე დააჭირეთ "Back to list"-ს, რომ მნიშვნელობა ჩანაწერში არ მოხვდეს.
4. სესიიდან ანგარიშის მენიუს საშუალებით გამოდით. შედით სადემონსტრაციო ადმინისტრატორის ანგარიშით
   და Administration-ში გახსენით აუდიტის ჟურნალი მისამართზე `/Audit`.
5. ჟურნალი იმავე საიდუმლოს სახელით გაფილტრეთ. იპოვეთ `dev-user`-ის ჩანაწერი ნიშნით `Revealed`.
   `Viewed` დეტალების ნახვის ჩანაწერია.

ამ რევიზიაზე 24 ნოემბერს ჩატარებული დემონსტრაცია, მომზადების ინსტრუქცია და გადაღების წესები
მოცემულია [demo-setup-reset.md](demo-setup-reset.md)-ში.

### Speaker shows

Show `/Audit` in the light theme, filtered to `Phoenix_Dev_Database_Password`. Point to the
`Revealed` badge and the account `dev-user`. Keep enrollment, recovery codes, private keys, cookies,
and the revealed value off screen. Do not open `Production_AWS_Root_Key` or `Global_SQL_SA_Password`. Do not press
"Verify integrity" or "Export signed bundle".

აჩვენეთ `/Audit` ღია თემაში, `Phoenix_Dev_Database_Password`-ის ფილტრით. მიუთითეთ `Revealed`-ზე და
მოქმედების შემსრულებელ ანგარიშზე `dev-user`. ნუ აჩვენებთ ავთენტიფიკატორის რეგისტრაციას, აღდგენის
კოდს, კერძო გასაღებს, ქუქის ან გამჟღავნებულ მნიშვნელობას. ნუ გახსნით `Production_AWS_Root_Key`-ს ან
`Global_SQL_SA_Password`-ს. ნუ დააჭერთ "Verify integrity"-ს ან "Export signed bundle"-ს.

## Slide 6: Checkpoint proof boundary / რას ადასტურებს საკონტროლო წერტილი

### On screen (English)

Verify streams the audit rows and compares them with checkpoints signed by the current key. It
reports a gap, a broken link, a mismatched checkpoint head, or missing final entries within the
latest such checkpoint as tampering. The signature covers the entries through that checkpoint.
Until the next checkpoint, the entries after it have only the hash chain.

Verify can miss deleted checkpoints or changes limited to later rows if their hashes are
recomputed. A signed export records the head in a file the auditor can retain. The offline
verifier checks that file alone and never monitors the live database. Anyone who can read the
vault's signing key can re-sign the trail.

The vault writes `AuditLogViewed` when the log opens and `AuditChainVerified` when verification
finishes. It commits a checkpoint and `AuditCheckpointCreated` in one transaction. Export requires
a fresh step-up and streams only after `AuditBundleExported` commits. The vault allows one
verification and one export at a time. The bundle format remains version 2. See the
[README](README.md) for older verifier compatibility.

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

Explain in one sentence how an outside auditor checks a bundle they already hold offline with a
compatible verifier. Explain the limits separately. Some deletions can escape detection, and the
message refers to the checkpoint's coverage. Avoid saying "the whole database is proven".

ერთი წინადადებით ახსენით, რომ გარე მხარისთვის მტკიცებულებაა მის ხელთ არსებული პაკეტი, რომელიც
ოფლაინ, ამ ფორმატთან თავსებადი ვერსიის შემმოწმებლით მოწმდება. ყველა წაშლის აღმოჩენა შეუძლებელია.
საკონტროლო წერტილის შესახებ შეტყობინება მთელი ბაზის სისწორეს არ ადასტურებს.

## Slide 7: Final revision and test evidence / საბოლოო რევიზია და ტესტების შედეგები

### On screen (English)

Fill in the final values once the slide deck is approved. The following entries identify the
revision examined for the rehearsal.

| Item | Value |
|---|---|
| Talk revision | `1298958050e45f83aefeb4b6455d0a02169ab22a`. Slide deck approval is pending |
| `origin/main` at review | That commit, 24 November 2026, 15:18 Asia/Tbilisi |
| Disposable Development run | Setup, demo, and five screenshots from a vault on a different port. 24 November 2026, about 16:03 Asia/Tbilisi. See [demo-setup-reset.md](demo-setup-reset.md) |
| Release build for that walk | `dotnet build EclipsVault.slnx -c Release`: 0 warnings, 0 errors |
| Repository unit checks | `scripts/check.sh` passed locked restore, Release build, and unit tests |
| Backup recording | Pending. Follow the rehearsal checklist to record it |

### On screen (ქართული)

საბოლოო მონაცემები სლაიდების დამტკიცების შემდეგ შეავსეთ. ცხრილში რეპეტიციისთვის განხილული
რევიზიაა მითითებული.

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

## Slide 8: Lessons and limitations / დასკვნები და შეზღუდვები

### On screen (English)

- The server decides access to the secret. Angular displays the result.
- The server commits the reveal entry before decryption. It logs a refusal as `Access denied` and
  reserves `Viewed` for allowed details requests.
- A data key seals the value. The master key wraps the data key. The dynamic lease stores no minted
  password.
- Verify checks the trail through the latest checkpoint signed by the current key. Later rows
  have only the hash chain. An outside auditor can also check a bundle they already hold.
- Verify can miss deleted checkpoints or edits limited to rows after the latest checkpoint.
  ADR-0030 describes this limit.
- The 1.0.0 verifier can call an honest later bundle edited. Use a verifier at least as new as the
  vault, and remember that one exception.
- Sign-in runs in Angular. Users activate new accounts through an emailed invitation. Keep the
  invitation off screen because the link carries a live token.
- Development alone seeds demo data. The talk uses that data in a disposable Development vault.
  The reference production deployment is outside the demo's scope. The demo did not start it.

### On screen (ქართული)

- საიდუმლოზე წვდომას სერვერი აკონტროლებს, Angular-ის ინტერფეისი კი შედეგს აჩვენებს.
- მნიშვნელობის გამჟღავნების ჩანაწერი გაშიფვრამდე ფიქსირდება. წვდომაზე უარი `Access denied`-ად
  აღირიცხება. `Viewed` მხოლოდ ნებადართულ ნახვას აღნიშნავს.
- მნიშვნელობა მონაცემთა გასაღებითაა დაშიფრული. მონაცემთა გასაღები მასტერ-გასაღებითაა შეფუთული.
  დინამიკური საიდუმლოს იჯარის ჩანაწერში გაცემული პაროლი არ ინახება.
- Verify აუდიტის ჩანაწერებს მოქმედი გასაღებით ხელმოწერილი ბოლო საკონტროლო წერტილის ჩათვლით
  ადასტურებს. შემდეგ ჩანაწერებს მხოლოდ ჰეშების ჯაჭვი აკავშირებს. გარე აუდიტორს შეუძლია შეამოწმოს
  პაკეტი, რომელსაც უკვე ინახავს.
- Verify-მ შეიძლება დარღვევა ვერ დააფიქსიროს, თუ საკონტროლო წერტილები წაიშალა ან მხოლოდ ბოლო
  წერტილის შემდეგ ჩაწერილი მონაცემები შეიცვალა. ეს შეზღუდვა კვლავ მოქმედებს და ADR-0030-შია აღწერილი.
- 1.0.0 ვერსიის შემმოწმებელმა უცვლელი პაკეტიც შეიძლება შეცვლილად გამოაცხადოს, თუ პაკეტის ფორმატი
  უფრო ახალია. გაითვალისწინეთ ეს გამონაკლისი და გამოიყენეთ საცავის ვერსიის ტოლი ან უფრო ახალი
  შემმოწმებელი.
- ბრაუზერში შესვლის გვერდები Angular-ზე მუშაობს. ახალი ანგარიში ელფოსტით გაგზავნილი მოწვევით
  აქტიურდება. დემონსტრაციაზე მოწვევა არ ჩანს, რადგან ბმული მოქმედ ტოკენს შეიცავს.
- სადემონსტრაციო მონაცემები მხოლოდ Development გარემოში იქმნება. დემონსტრაცია ამ მონაცემებს
  ერთჯერად საცავში იყენებს. რეპოზიტორიაში აღწერილი Production განთავსება ამ მოხსენებისთვის არ გაშვებულა
  და დემონსტრაციის ფარგლებს გარეთაა.

### Speaker shows

Read the limitations slowly and end the talk there.

შეზღუდვები ნელა ჩამოთვალეთ და ამით დაასრულეთ.
