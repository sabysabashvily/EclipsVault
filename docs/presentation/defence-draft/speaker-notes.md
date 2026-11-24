# Speaker notes / ჩანაწერები მომხსენებლისთვის

Read these with the matching slide in [storyboard.md](storyboard.md). Say the Georgian block when
the room is Georgian, and the English block when it is not. Do not translate a term that
[CONTEXT.md](../../../CONTEXT.md) defines by inventing a second product name: vault account is
საცავის ანგარიში, session is სესია, second factor is მეორე ფაქტორი, service account is სერვისის
ანგარიში, API key is API გასაღები, trusted network is სანდო ქსელი, share is გაზიარება.

Names such as `SecretAccessPolicy` stay in Latin in both languages. They are the source identifiers.

## Slide 1 — Problem / პრობლემა

### English

Start with the team, not with the framework. "Phoenix keeps one database password that several
people need. If it lives in a file, possession of the file is the only control, and the file cannot
tell us who read it."

Then one sentence of what the vault is: self-hosted, the operator holds the keys and the database,
and each open is a request the server can refuse and record. Do not list every feature. The next
seven slides follow this one secret.

Do not quote a price, a licence tier, or a compliance certificate. Do not say the product has been
deployed for this talk.

### ქართული

დაიწყეთ გუნდის მაგალითით და ფრეიმვორკი ჯერ არ ახსენოთ. „Phoenix-ის რამდენიმე წევრს მონაცემთა ბაზის
ერთი და იგივე პაროლი სჭირდება. თუ პაროლი ფაილშია, მის დაცვას მხოლოდ ფაილზე წვდომა განსაზღვრავს.
შემდეგ ვეღარ გავიგებთ, ვინ წაიკითხა პაროლი.“

შემდეგ ერთი წინადადებით ახსენით, რა არის საცავი. ის ოპერატორის ინფრასტრუქტურაზეა განთავსებული,
გასაღებები და ბაზაც ოპერატორთანაა, საიდუმლოს გახსნისას კი სერვერს ეგზავნება მოთხოვნა, რომლის
უარყოფა და აღრიცხვაც სერვერს შეუძლია. ყველა ფუნქციის ჩამოთვლა საჭირო არ არის. შემდეგი შვიდი
სლაიდი ამ ერთ საიდუმლოს ეხება.

ფასი, ლიცენზიის კატეგორია და შესაბამისობის სერტიფიკატი გამოტოვეთ. ნუ იტყვით, რომ პროდუქტი ამ
მოხსენებისთვის განთავსდა.

## Slide 2 — Server-enforced access / წვდომის კონტროლი სერვერზე

### English

Two identities, said once. A person uses a vault account. The sign-in page is Angular. After the
password, `AuthController.Password` signs only an `MfaPending` cookie. `SessionIssuer.SignInAsync`
creates the session after the authenticator code, a recovery code, enrollment, a passkey, or SSO.
A passkey is both factors at once. An administrator never chooses a new account's password: an
emailed invitation lets the person set it and enroll the authenticator (ADR-0033). An application uses a service account and an API key on `/api/v1/`. The UI API at `/api/ui/`
does not take that key. ADR-0005 and ADR-0012 are the decisions: Angular guards are convenience.

Walk the four rules on `SecretAccessPolicy.Evaluate` in the order the code checks them. Then say why
the live secret is dull on purpose. `dev-user` is Standard on `PHOENIX`. The secret is Internal,
which is the same numeric level, Development, so the production window does not apply, and Internal,
so the trusted-network rule does not apply. Step-up starts at Secret, so this reveal does not ask
for a fresh code.

If someone asks "show me a denial", do not improvise one on loopback. The default trusted list
includes `127.0.0.1/32`, so a Confidential secret opened from this laptop is still on a trusted
network. A denial needs an account, a project, a sensitivity, or a window that actually fails. That
variant was not rehearsed.

The words Top Secret, Standard, Internal, Confidential, and Production are the enum names. An
administrator is a vault account with Top Secret clearance, not a separate role.

### ქართული

ორივე ტიპის ანგარიში ერთხელ ახსენეთ. მომხმარებელი საცავის ანგარიშით შედის Angular-ის გვერდიდან.
პაროლის შემოწმების შემდეგ `AuthController.Password` ხელს აწერს მხოლოდ `MfaPending` ქუქის.
`SessionIssuer.SignInAsync` სესიას ქმნის ავთენტიფიკატორის ან აღდგენის კოდის დადასტურების,
ავთენტიფიკატორის რეგისტრაციის, passkey-ით ან SSO-ით შესვლის შემდეგ. passkey ორივე ფაქტორს ერთად
ადასტურებს. ადმინისტრატორი ახალი ანგარიშის პაროლს არ ირჩევს. ელფოსტით მიღებული მოწვევით მომხმარებელი
თავად ადგენს პაროლს და არეგისტრირებს ავთენტიფიკატორს (ADR-0033). აპლიკაცია სერვისის ანგარიშითა
და API გასაღებით მიმართავს `/api/v1/`-ს. `/api/ui/` ამ გასაღებს არ იღებს. ADR-0005 და ADR-0012
აღწერს არქიტექტურულ გადაწყვეტილებებს. Angular-ის მარშრუტების შემოწმებები მხოლოდ ნავიგაციას ამარტივებს.

ოთხი წესი `SecretAccessPolicy.Evaluate`-ში მოცემული თანმიმდევრობით ახსენით. შემდეგ თქვით, რატომ
შეარჩიეთ მარტივი შემთხვევა. `dev-user`-ს პროექტ `PHOENIX`-ზე Standard დაშვება აქვს. საიდუმლოს
Internal მგრძნობელობას რიცხობრივად იგივე დონე შეესაბამება. საიდუმლო Development გარემოს ეკუთვნის,
ამიტომ მასზე Production-ის წვდომის დროითი ფანჯარა არ ვრცელდება. Internal დონეზე სანდო ქსელის წესიც არ
მოქმედებს. გაძლიერებული ავთენტიფიკაცია Secret დონიდან მოითხოვება, ამიტომ ამ მნიშვნელობის
საჩვენებლად ახალი კოდი საჭირო არ არის.

თუ წვდომაზე უარის ჩვენებას გთხოვენ, loopback-ზე იმპროვიზაციას ნუ ეცდებით. სანდო მისამართების
ნაგულისხმევ სიაში შედის `127.0.0.1/32`. ამ ლეპტოპიდან Confidential საიდუმლოზე მოთხოვნაც სანდო
ქსელიდან მოდის. უარის საჩვენებლად საჭიროა შემთხვევა, როცა ანგარიშის დაშვება, პროექტი, საიდუმლოს
მგრძნობელობა ან წვდომის დროითი ფანჯარა წესს ვერ აკმაყოფილებს. ეს ვარიანტი რეპეტიციაზე არ შემოწმებულა.

Top Secret, Standard, Internal, Confidential და Production ჩამონათვალების (enum) მნიშვნელობების
სახელებია. ადმინისტრატორის უფლებები საცავის ანგარიშის Top Secret დაშვებიდან მოდის. ცალკე
ადმინისტრატორის როლი არ არსებობს.

## Slide 3 — Architecture / არქიტექტურა

### English

Point at the context picture first: one person, the vault, and the external systems. The
application that uses a service account is an external system, not a person. Then the container
picture. Inside the boundary, the Angular UI runs in the browser and calls the UI API with the
session cookie. The Web application is ASP.NET Core on .NET 10 and serves those files, both APIs,
and three server pages of its own: access blocked, error, and not found. No Razor view is left on
this revision; sign-in is Angular too. `AngularUiRoutes` lists the page routes that serve
Angular (ADR-0034). One database is selected by `Database:Provider`, and the process
refuses to start without it. Redis appears only when enabled. Node is a build stage in the
Dockerfile and is not a box in that picture. The Angular files are what the browser runs.

Say "module" when you point at Core and Infrastructure. Web references both projects.
Infrastructure references Core. Core references no NuGet package. `EclipsVault.AuditVerifier`
references Core only, so the offline check does not need the database or the private key.

Name the hosted workers that `Program.cs` and `DependencyInjection` actually register:
`AuditGroupCommitter`, `SecretLifecycleWorker`, `DynamicLeaseWorker`, and `LicenseStartupCheck`.
They are threads in Web, not extra containers.

Close with two deployments, Development and production; the demonstration is one Development
run, recorded in [demo-setup-reset.md](demo-setup-reset.md). Development is `docker-compose.yml`
plus the Web process. Production is drawn as the reference deployment in `deploy/production/`:
Caddy with automatic HTTPS, the signed vault image, and PostgreSQL on an internal network, with the
Data Protection key ring on a volume. The vault trusts forwarded client addresses only from Caddy's
fixed address. It has no demo seed. This talk did not start it. Redis in the compose file does
nothing until `Redis:Enabled` is true; the committed `appsettings.json` sets it false. The copied
Development file sets `Email:Sender` to `Smtp` and points it at Mailpit; the 24 November walk used
`Log`, the committed default, and sent no mail.

If asked about dynamic secrets on PostgreSQL: a role names its backend, SQL Server or PostgreSQL,
and each backend has its own target connection, never the vault's own database.

### ქართული

ჯერ კონტექსტის დიაგრამაზე მიუთითეთ მომხმარებელზე, საცავსა და გარე სისტემებზე. სერვისის ანგარიშის
მქონე აპლიკაცია გარე სისტემად არის წარმოდგენილი. შემდეგ კონტეინერების დიაგრამაზე გადადით.
სისტემის საზღვრებში Angular UI ბრაუზერში მუშაობს და UI API-ს სესიის ქუქით მიმართავს. Web აპლიკაცია
ASP.NET Core-ზეა აგებული და .NET 10-ზე მუშაობს. ის აწვდის Angular-ის ფაილებს, ამუშავებს ორივე API-ის
მოთხოვნებს და აბრუნებს სამ საკუთარ სერვერულ გვერდს: „წვდომა დაბლოკილია“, „შეცდომა“ და „ვერ მოიძებნა“.
ამ რევიზიაში Razor-ის ხედები სრულად ამოღებულია. შესვლის გვერდებიც Angular-ზე მუშაობს.
`AngularUiRoutes` ჩამოთვლის მარშრუტებს, რომლებზეც სერვერი Angular-ის გვერდებს აბრუნებს (ADR-0034).
`Database:Provider` ერთ მონაცემთა ბაზას განსაზღვრავს. ამ პარამეტრის გარეშე პროცესი არ ეშვება.
Redis მხოლოდ ჩართვის შემთხვევაში ჩანს. Node Dockerfile-ში მხოლოდ აგების ეტაპზე გამოიყენება და
დიაგრამაზე ცალკე არ არის გამოყოფილი. ბრაუზერი Angular-ის ფაილებს ასრულებს.

Core-სა და Infrastructure-ზე მიუთითეთ და ორივეს მოდული უწოდეთ. Web ორივე პროექტზეა დამოკიდებული,
Infrastructure კი Core-ზე. Core არცერთ NuGet პაკეტზე არ არის დამოკიდებული.
`EclipsVault.AuditVerifier` მხოლოდ Core-ს იყენებს, ამიტომ ოფლაინ შემოწმებას არც მონაცემთა ბაზა
სჭირდება და არც კერძო გასაღები.

დაასახელეთ ფონური პროცესები, რომლებიც `Program.cs`-სა და `DependencyInjection`-ში რეგისტრირდება:
`AuditGroupCommitter`, `SecretLifecycleWorker`, `DynamicLeaseWorker` და `LicenseStartupCheck`.
ისინი Web-ში შესრულების ნაკადებია და ცალკე კონტეინერებად არ ეშვება.

ბოლოს აღწერეთ ორი განთავსება, Development და Production. დემონსტრაცია Development გარემოში ერთი
გაშვებით შემოიფარგლება და აღწერილია [demo-setup-reset.md](demo-setup-reset.md)-ში. Development
გარემო შედგება `docker-compose.yml`-ით გაშვებული სერვისებისა და Web პროცესისგან. Production
ნაჩვენებია `deploy/production/`-ში აღწერილი ნიმუშით. მასში შედის Caddy ავტომატური HTTPS-ით,
საცავის ხელმოწერილი იმიჯი და შიდა ქსელში გაშვებული PostgreSQL. Data Protection-ის გასაღებების
ნაკრები მონაცემთა ტომზე ინახება. საცავი კლიენტის გადმოცემულ მისამართს მხოლოდ მაშინ ენდობა,
თუ მას Caddy-ის ფიქსირებული მისამართიდან იღებს. ამ გარემოში სადემონსტრაციო მონაცემები არ იქმნება.
Production განთავსება ამ მოხსენებისთვის არ გაშვებულა. Compose ფაილში აღწერილი Redis მხოლოდ
`Redis:Enabled`-ის ჩართვისას გამოიყენება. რეპოზიტორიაში შენახულ `appsettings.json`-ში პარამეტრი
გამორთულია. დაკოპირებულ Development ფაილში `Email:Sender`-ის მნიშვნელობაა `Smtp` და მითითებულია
Mailpit. 24 ნოემბრის რეპეტიციაზე გამოყენებული იყო რეპოზიტორიის ნაგულისხმევი მნიშვნელობა `Log`
და წერილი არ გაგზავნილა.

თუ PostgreSQL-ზე დინამიკურ საიდუმლოებზე გკითხავენ, ახსენით, რომ როლი განსაზღვრავს ბექენდს, SQL Server-ს
ან PostgreSQL-ს. თითოეულ ბექენდს სამიზნე ბაზასთან საკუთარი კავშირი აქვს. საცავის მონაცემთა ბაზა
სამიზნედ არ გამოიყენება.

## Slide 4 — Reveal ordering / მნიშვნელობის გამჟღავნების თანმიმდევრობა

### English

Read the order off the diagram, slowly.

"The details request loads the envelope and writes nothing. Then it asks `SecretAccessPolicy`. If
the answer is no, it records `Access denied` and does not call `RevealAsync`. If the answer is yes,
it commits `Viewed`. When the person presses Reveal value, the check runs again, and `RevealAsync`
asks `AuditSink` to commit `SecretRevealed`. The sink waits until that row is committed. Only the
code after that wait unwraps the data key and opens the value."

That wait is the whole claim. `AuditSink.WriteAsync` throws `AuditWriteFailedException` when the
commit fails, and `RevealAsync` does not catch it before `DecryptToDtoAsync`.

Say the engine name you are actually running. The committed default is `AesGcmLocal`: a fresh data
key seals the value, and the master key wraps the data key. `VaultTransit` is a different engine,
used only when `Crypto:Engine` says so. Do not describe a key-management service you have not
configured.

Master-key rotation and the Encryption keys page are on this revision. You may name them. You may
not rotate a key in the room, and you may not say a rotation decrypts the secrets. `KekRotationService`
rewraps data keys in batches. If a failure stops the pass after a batch commits, the response gives
the counts already rewrapped. An audit failure gives `audit_unavailable`. Other failures after a
committed batch give `rotation_interrupted`. With Vault Transit, the page lists its key versions.

### ქართული

დიაგრამაზე ნაჩვენები თანმიმდევრობა ნელა წაიკითხეთ.

„დეტალების მოთხოვნისას სერვერი ტვირთავს დაშიფვრის კონვერტს და ამ ეტაპზე არაფერს წერს. შემდეგ
წვდომას `SecretAccessPolicy`-ით ამოწმებს. უარის შემთხვევაში წერს `Access denied`-ს და `RevealAsync`-ს
არ იძახებს. ნებართვის შემთხვევაში აფიქსირებს `Viewed`-ს. როცა მომხმარებელი Reveal value-ს აჭერს,
სერვერი წვდომას ხელახლა ამოწმებს. `RevealAsync` `AuditSink`-ს `SecretRevealed`-ის ჩაწერას სთხოვს.
`AuditSink` ჩანაწერის დაფიქსირებას ელოდება. მხოლოდ ამის შემდეგ ხსნის კოდი შეფუთულ მონაცემთა
გასაღებს და მნიშვნელობის გასაშიფრად იყენებს.“

აღწერილი თანმიმდევრობა ჩანაწერის დაფიქსირების მოლოდინს ეფუძნება. თუ ჩანაწერი ვერ დაფიქსირდა,
`AuditSink.WriteAsync` იწვევს `AuditWriteFailedException` გამონაკლისს. `RevealAsync` ამ გამონაკლისს
`DecryptToDtoAsync`-ის გამოძახებამდე არ ამუშავებს.

დაასახელეთ დაშიფვრის ძრავა, რომელსაც დემონსტრაციისას იყენებთ. რეპოზიტორიაში ნაგულისხმევია
`AesGcmLocal`. მნიშვნელობა ახალი მონაცემთა გასაღებით იშიფრება, მონაცემთა გასაღები კი მასტერ-გასაღებით
იფუთება. `VaultTransit` სხვა ძრავაა და მხოლოდ მაშინ გამოიყენება, როცა ამას `Crypto:Engine` განსაზღვრავს.
ისაუბრეთ მხოლოდ კონფიგურირებულ გასაღებების მართვის სერვისზე.

ამ რევიზიაში ხელმისაწვდომია მასტერ-გასაღების როტაცია და Encryption keys-ის გვერდი. შეგიძლიათ
ორივე ახსენოთ. დემონსტრაციისას როტაცია არ გაუშვათ. როტაცია მხოლოდ მონაცემთა გასაღებებს ეხება.
`KekRotationService` მათ ჯგუფებად ხელახლა ფუთავს საიდუმლოების მნიშვნელობების გაშიფვრის გარეშე.
თუ როტაცია უკვე დაფიქსირებული ჯგუფის შემდეგ შეფერხდა, პასუხში ჩანს, რამდენი მონაცემთა გასაღები
შეიფუთა ხელახლა. აუდიტის შეცდომის კოდია `audit_unavailable`. ჯგუფის წარმატებით დაფიქსირების შემდეგ
სხვა შეცდომის კოდია `rotation_interrupted`. Vault Transit-ის გამოყენებისას გვერდზე მისი გასაღების
ვერსიები ჩანს.

## Slide 5 — Live journey / დემონსტრაცია

### English

Follow [demo-setup-reset.md](demo-setup-reset.md) and [rehearsal-checklist.md](rehearsal-checklist.md).
If the disposable vault is not already up from a checked run, skip the live clicks and talk through
the five steps. Do not start a stack for the first time in front of the room.

Narration while it works:

"This account is the Phoenix member. Sign-in opens Secrets, which is an Angular page. The list
says two active secrets right after the reset: the one I will open, and a short-lived token I will
not. The token expires about five minutes after the reset, so the list may now say one. I filter to
`Phoenix_Dev_Database_Password` and click its name, then Reveal value. The value appears on the page,
so I press Back to list before anyone records the screen. I sign out from the account card and
return as the administrator. The audit log is an Angular page under Administration, and only an
administrator can open it. It shows an unlicensed notice; that notice says the vault still runs.
The badge beside the heading says Not checked, because nobody has pressed Verify. I filter to the
secret name. The row I want says `Revealed` and names this account. The `Viewed` row
under it is the details page after the server allowed it, not the decryption. A `Created` row under
those is the Development seed. Latest checkpoint says none yet. I do not press Verify integrity or
Export signed bundle."

Do not read the Attestation panel's sentence about deleted rows aloud as a promise about the live
database. Slide 6 says what Verify proves.

If the authenticator enrollment screen appears, stop the projection. That screen is first-run
enrollment, and it does not belong in a recording. The same is true of recovery codes.

If a step-up form appears, you are not on the Internal secret. Stop. Do not type a code on camera.

### ქართული

მიჰყევით [demo-setup-reset.md](demo-setup-reset.md)-სა და
[rehearsal-checklist.md](rehearsal-checklist.md)-ში მოცემულ ინსტრუქციებს. თუ ერთჯერადი საცავი
წინასწარ არ გაგიშვიათ და არ შეგიმოწმებიათ, ეკრანზე დემონსტრაცია გამოტოვეთ და ხუთივე ნაბიჯი ზეპირად
აღწერეთ. სტეკი პირველად აუდიტორიის წინაშე არ გაუშვათ.

დემონსტრაციის ტექსტი

„ეს ანგარიში Phoenix-ის წევრს ეკუთვნის. შესვლის შემდეგ იხსნება Angular-ის გვერდი Secrets.
საწყისი მდგომარეობის აღდგენისთანავე სიაში ორი აქტიური საიდუმლოა: ის, რომელსაც გავხსნი, და
მოკლევადიანი ტოკენი, რომელსაც არ გავხსნი. ტოკენს ვადა აღდგენიდან დაახლოებით ხუთ წუთში გასდის,
ამიტომ სიაში შეიძლება უკვე მხოლოდ ერთი აქტიური საიდუმლო ჩანდეს.
სიას `Phoenix_Dev_Database_Password`-ის სახელით ვფილტრავ. ვაჭერ სახელს, შემდეგ კი Reveal value-ს.
მნიშვნელობა გვერდზე ჩნდება. ეკრანის გადაღებამდე Back to list-ს ვაჭერ. ანგარიშის ბარათიდან გამოვდივარ
და ადმინისტრატორის ანგარიშით შევდივარ. აუდიტის ჟურნალი Angular-ის გვერდია Administration-ში.
მისი გახსნა მხოლოდ ადმინისტრატორს შეუძლია. შეტყობინებაში წერია, რომ საცავს ლიცენზია არ აქვს და
მუშაობას მაინც აგრძელებს. სათაურთან ჩანს Not checked, რადგან Verify ჯერ არ გაშვებულა.
ჟურნალს საიდუმლოს სახელით ვფილტრავ. საჭირო ჩანაწერს აწერია `Revealed` და მითითებულია მოქმედების
შემსრულებელი ანგარიში. ქვემოთ `Viewed` აღნიშნავს დეტალების ნახვას სერვერის ნებართვის შემდეგ. ამ დროს მნიშვნელობის
გაშიფვრა არ ხდება. მის ქვემოთ `Created` აღნიშნავს Development გარემოში სადემონსტრაციო ჩანაწერის
წინასწარ შექმნას. Latest checkpoint ველში წერია, რომ საკონტროლო წერტილი ჯერ არ არსებობს.
Verify integrity-სა და Export signed bundle-ს არ ვაჭერ.“

Attestation პანელში წაშლილი ჩანაწერების შესახებ წინადადება მოქმედ მონაცემთა ბაზაზე გარანტიად ნუ
გაიმეორებთ. მე-6 სლაიდი განმარტავს, რას ადასტურებს Verify.

თუ ავთენტიფიკატორის რეგისტრაციის გვერდი გამოჩნდა, პროექტორზე ჩვენება შეწყვიტეთ. რეგისტრაცია
პირველივე შესვლისას ხდება და ვიდეოში არ უნდა მოხვდეს. იგივე ეხება აღდგენის კოდებს.

გაძლიერებული ავთენტიფიკაციის ფორმის გამოჩენა ნიშნავს, რომ Internal საიდუმლო არ გაგიხსნიათ.
შეჩერდით და კოდი კამერის წინ არ შეიყვანოთ.

## Slide 6 — Checkpoint proof boundary / რას ადასტურებს საკონტროლო წერტილი

### English

This is the slide where a careless sentence is too strong. Verify now does compare the trail with
checkpoints signed by the current key. That is `AuditChainVerifier.VerifyAsync`, called from
`AuditCheckpointService.VerifyAsync`. Do not go back to the old sentence that Verify ignores
checkpoints. Do not jump to the other old sentence either: that every deletion is detectable.

Say instead: "Verify answers a question about the rows up to the latest checkpoint this key signed.
Entries after that checkpoint are only the hash chain. Deleting the checkpoints, or editing only
those later rows and recomputing their hashes, can still look fine. The offline verifier checks the
bundle in its hand. A bundle an auditor already stored still disagrees with a later rewrite. That
is why a retained export matters, and it is also why I will not say every deletion is detectable
whatever the latest checkpoint is."

The behaviours that used to be described as planned are on this revision: `AuditLogViewed` when the
log opens, `AuditChainVerified` when Verify finishes, `AuditCheckpointCreated` in the same
transaction as the checkpoint, streamed export after `AuditBundleExported` and a fresh step-up, and
one verification and one export at a time. `AuditController.Export` calls `AdministratorStepUp`
before `ExportAsync`. `CreateCheckpointAsync` saves the checkpoint through `SaveChangesAsync`, and
`DescribeCheckpointChange` adds the audit row to that same batch.

Keep the old-verifier note. A current verifier accepts bundle formats `eclipsvault.audit-bundle/1`
and `/2`, and refuses a newer format without saying the row was edited. The 1.0.0 verifier, which
predates that refusal, reports `INVALID — "the row was edited"` for a later bundle. That report is
the old tool, not proof of tampering.

Do not click Verify and then say "intact" as if it covered rows after the latest checkpoint, or as
if deleted checkpoints would still show. If you do click it, read the message it actually returns.
With no checkpoint yet, that message says the chain is intact and that a database writer could
still delete the newest entries or recompute hashes. After a checkpoint, it names the sequence the
signature covers and tells the room to export a bundle for an outside party.

### ქართული

შემოწმების ფარგლები ზუსტად აღწერეთ. ამ რევიზიაში Verify აუდიტის ჯაჭვს მოქმედი გასაღებით
ხელმოწერილ საკონტროლო წერტილებს ადარებს. `AuditChainVerifier.VerifyAsync`-ს
`AuditCheckpointService.VerifyAsync` იძახებს. წინა ფორმულირება, თითქოს Verify საკონტროლო წერტილებს
არ ამოწმებდეს, მოძველებულია. ასევე მცდარია მტკიცება, რომ ყოველი წაშლის აღმოჩენა შესაძლებელია.

თქვით: „Verify ჩანაწერებს ამ გასაღებით ხელმოწერილი ბოლო საკონტროლო წერტილის ჩათვლით ადასტურებს.
შემდეგ ჩანაწერებს მხოლოდ ჰეშების ჯაჭვი აკავშირებს. საკონტროლო წერტილების წაშლა შეიძლება შეუმჩნეველი
დარჩეს. იგივე ეხება ბოლო წერტილის შემდეგ ჩანაწერების შეცვლას, თუ მათი ჰეშებიც ხელახლა გამოითვალა.
ოფლაინ შემმოწმებელი მხოლოდ მისთვის გადაცემულ პაკეტს ამოწმებს. აუდიტორთან უკვე შენახული პაკეტი
მოგვიანებით გადაწერილ ჩანაწერებს აღარ დაემთხვევა. ამიტომ შენახულ ექსპორტს მნიშვნელობა აქვს.
ბოლო საკონტროლო წერტილის არსებობაც კი არ იძლევა ყოველი წაშლის აღმოჩენის გარანტიას.“

ადრე დაგეგმილი მოქმედებები ამ რევიზიაში უკვე მუშაობს. ჟურნალის გახსნისას იწერება `AuditLogViewed`,
Verify-ის დასრულებისას კი `AuditChainVerified`. `AuditCheckpointCreated` საკონტროლო წერტილთან
ერთად იმავე ტრანზაქციაში ფიქსირდება. ნაკადური ექსპორტისთვის საჭიროა ახლახან გავლილი გაძლიერებული
ავთენტიფიკაცია და `AuditBundleExported`-ის დაფიქსირება. ერთდროულად მხოლოდ ერთი შემოწმება და ერთი
ექსპორტი შეიძლება შესრულდეს. `AuditController.Export` `ExportAsync`-მდე `AdministratorStepUp`-ს
იძახებს. `CreateCheckpointAsync` საკონტროლო წერტილს `SaveChangesAsync`-ით ინახავს.
`DescribeCheckpointChange` აუდიტის ჩანაწერს იმავე ჯგუფს ამატებს.

შეინარჩუნეთ შენიშვნა ძველი შემმოწმებლის შესახებ. მიმდინარე შემმოწმებელი იღებს პაკეტის ფორმატებს
`eclipsvault.audit-bundle/1` და `/2`. უფრო ახალ ფორმატს უარყოფს ისე, რომ ჩანაწერს შეცვლილად არ
აცხადებს. 1.0.0 ვერსია ამ შემოწმების დამატებამდე გამოვიდა და უფრო ახალ პაკეტზე აჩვენებს
`INVALID — "the row was edited"`. ეს პასუხი ძველი შემმოწმებლის ქცევას ასახავს და ხელყოფას არ ადასტურებს.

Verify-ზე დაჭერის შემდეგ „intact“ ისე ნუ წაიკითხავთ, თითქოს შემოწმება ბოლო საკონტროლო წერტილის
შემდეგ ჩანაწერებსაც ადასტურებდეს ან წაშლილ საკონტროლო წერტილებს გამოავლენდეს. თუ Verify-ს მაინც
გაუშვებთ, წაიკითხეთ ეკრანზე ნაჩვენები შეტყობინება. საკონტროლო წერტილის გარეშე შეტყობინება
ადასტურებს ჯაჭვის მთლიანობას და გაფრთხილებთ, რომ ბაზაში ჩაწერის უფლების მქონე პირს მაინც შეუძლია
უახლესი ჩანაწერების წაშლა ან ჰეშების ხელახლა გამოთვლა. საკონტროლო წერტილის შექმნის შემდეგ
შეტყობინება ასახელებს, მიმდევრობის რომელ ნომრამდე ვრცელდება ხელმოწერა, და გარე მხარისთვის პაკეტის
ექსპორტს გთავაზობთ.

## Slide 7 — Final revision and test evidence / საბოლოო რევიზია და ტესტების შედეგები

### English

Read only filled cells. For an empty one, say "not recorded yet".

Read the revision table. The code this talk describes is `origin/main` at
`1298958050e45f83aefeb4b6455d0a02169ab22a`. Angular sign-in, invitations, the server pages,
Encryption keys, the audit-log proof boundary, and the Angular secret and audit pages are on that
revision. The Release build that served the walk had no warnings and no errors. During the draft
refresh, `scripts/check.sh` passed locked restore, the Release build, and the unit suite. The
separate Development walk is recorded in [demo-setup-reset.md](demo-setup-reset.md).

If the owner later freezes a different revision, replace the hash and say the new one. Do not leave
this hash up if the running vault is a different commit.

### ქართული

მხოლოდ შევსებული უჯრები წაიკითხეთ. ცარიელ უჯრაზე თქვით „ჯერ არ არის დაფიქსირებული“.

წაიკითხეთ რევიზიის ცხრილი. მოხსენება აღწერს კოდს `origin/main`-ის რევიზიაზე
`1298958050e45f83aefeb4b6455d0a02169ab22a`. ამ რევიზიაში ხელმისაწვდომია Angular-ის შესვლის,
საიდუმლოებისა და აუდიტის გვერდები, მოწვევები, სერვერული გვერდები და Encryption keys. ამავე
რევიზიას ეხება აუდიტის ჟურნალის შემოწმების აღწერილი ფარგლები. დემონსტრაციისთვის Release რეჟიმში
აგება გაფრთხილებისა და შეცდომის გარეშე დასრულდა. მონახაზის განახლებისას `scripts/check.sh`-ით
წარმატებით დასრულდა დამოკიდებულებების ფიქსირებული ვერსიებით აღდგენა, Release რეჟიმში აგება და
ერთეულოვანი ტესტები. Development გარემოში ცალკე ჩატარებული დემონსტრაცია
[demo-setup-reset.md](demo-setup-reset.md)-შია აღწერილი.

თუ მფლობელი საბოლოოდ სხვა რევიზიას აირჩევს, განაახლეთ ჰეში და მოხსენებაზე ახალი მნიშვნელობა
დაასახელეთ. სლაიდზე გაშვებული საცავის კომიტის ჰეში უნდა ჩანდეს.

## Slide 8 — Lessons and limitations / დასკვნები და შეზღუდვები

### English

Three lessons, then stop.

"The server checked clearance and project for this secret and wrote the reveal row before it
decrypted the value. It also enforces the network and time rules where they apply."

"The audit trail is as strong as the check you actually run. A hash walk, a signed checkpoint, and
a bundle held by someone else are three different checks."

"What you saw was the Development demo world: synthetic accounts, one synthetic secret, no claim
about production."

Invite questions. The prepared answers are in [questions.md](questions.md). If a question asks
whether every deletion is detectable, say no, and point at the latest checkpoint. If a question
asks about invitations, say a new account is activated by an emailed invitation (ADR-0033), and
that the talk does not show one because the link carries a live token.

### ქართული

სამი დასკვნა თქვით და ამით დაასრულეთ.

„ამ საიდუმლოზე სერვერმა დაშვების დონე და პროექტი შეამოწმა. მნიშვნელობის გაშიფვრამდე სერვერმა
გამჟღავნების ჩანაწერი დააფიქსირა. სერვერი ქსელისა და დროის წესებსაც ამოწმებს, როცა ისინი ვრცელდება.“

„რას ადასტურებს აუდიტის ჟურნალი, ჩატარებულ შემოწმებაზეა დამოკიდებული. ჰეშების ჯაჭვის შემოწმება,
ხელმოწერილი საკონტროლო წერტილის შემოწმება და სხვა პირის მიერ შენახული პაკეტის შემოწმება სამი
განსხვავებული პროცედურაა.“

„ნახეთ Development გარემოში ჩატარებული დემონსტრაცია, სატესტო ანგარიშებითა და ერთი სატესტო
საიდუმლოთი. ეს დემონსტრაცია Production გარემოზე დასკვნის საფუძველს არ იძლევა.“

მსმენელებს კითხვების დასმა შესთავაზეთ. მომზადებული პასუხები [questions.md](questions.md)-შია.
თუ გკითხავენ, შესაძლებელია თუ არა ყოველი წაშლის აღმოჩენა, უპასუხეთ უარყოფითად და ბოლო
საკონტროლო წერტილზე მიუთითეთ. მოწვევებზე კითხვის შემთხვევაში თქვით, რომ ახალი ანგარიში ელფოსტით
გაგზავნილი მოწვევით აქტიურდება (ADR-0033). დემონსტრაციისას ამ ბმულს არ აჩვენებთ, რადგან ის მოქმედ
ტოკენს შეიცავს.
