# Speaker notes / ჩანაწერები მომხსენებლისთვის

Use the notes in the audience's language with the matching slide in [storyboard.md](storyboard.md).
Use the Georgian equivalents for terms defined in [CONTEXT.md](../../../CONTEXT.md). Vault account is
საცავის ანგარიში, session is სესია, second factor is მეორე ფაქტორი, service account is სერვისის
ანგარიში, API key is API გასაღები, trusted network is სანდო ქსელი, share is გაზიარება.

Keep source identifiers such as `SecretAccessPolicy` in Latin script in both languages.

## Slide 1: Problem / პრობლემა

### English

Start with the Phoenix example. Save the framework for the architecture slide. "Phoenix keeps one database password that several
people need. If it lives in a file, possession of the file is the only control, and the file cannot
tell us who read it."

Describe the vault in one sentence. The operator hosts EclipsVault and holds the keys and the
database. The server can refuse and record each request to open a secret. Follow that one secret
through the remaining seven slides and skip the feature list.

Do not quote a price, a licence tier, or a compliance certificate. Do not say the product has been
deployed for this talk.

### ქართული

დაიწყეთ გუნდის მაგალითით. ფრეიმვორკი არქიტექტურის სლაიდზე ახსენეთ. „Phoenix-ის რამდენიმე წევრს მონაცემთა ბაზის
ერთი და იგივე პაროლი სჭირდება. თუ პაროლი ფაილშია, მის დაცვას მხოლოდ ფაილზე წვდომა განსაზღვრავს.
შემდეგ ვეღარ გავიგებთ, ვინ წაიკითხა პაროლი.“

საცავი ერთი წინადადებით აღწერეთ. ოპერატორი საკუთარ ინფრასტრუქტურაზე მართავს EclipsVault-ს,
გასაღებებსა და ბაზას. საიდუმლოს გახსნის თითოეული მოთხოვნის უარყოფა და აღრიცხვა სერვერს შეუძლია.
შემდეგ შვიდ სლაიდზე ამ საიდუმლოს მიჰყევით და ფუნქციების ჩამონათვალი გამოტოვეთ.

ფასი, ლიცენზიის კატეგორია და შესაბამისობის სერტიფიკატი გამოტოვეთ. ნუ იტყვით, რომ პროდუქტი ამ
მოხსენებისთვის განთავსდა.

## Slide 2: Server-enforced access / წვდომის კონტროლი სერვერზე

### English

Describe each account type once. A person signs in with a vault account on an Angular page. After the
password, `AuthController.Password` signs only an `MfaPending` cookie. `SessionIssuer.SignInAsync`
creates the session after the authenticator code, a recovery code, enrollment, a passkey, or SSO.
A passkey satisfies both factors. The user sets their own password and enrolls an authenticator
through an emailed invitation (ADR-0033). An application uses a service account and an API key on
`/api/v1/`. The UI API at `/api/ui/` does not accept that key. Angular route guards help with
navigation. See ADR-0005 and ADR-0012 for the access decisions.

Explain the four rules in the order `SecretAccessPolicy.Evaluate` checks them. The demo uses a
secret for which clearance and project suffice. `dev-user` has Standard clearance on `PHOENIX`,
and Standard and Internal have the same numeric level. The secret is in Development, so the
production window does not apply. The trusted-network rule starts above Internal, and step-up
starts at Secret, so the reveal requires no fresh code.

If someone asks "show me a denial", explain that `127.0.0.1/32` is trusted by default. A
Confidential request from this laptop over loopback therefore satisfies the network rule. A live
denial would require a failing account, project, sensitivity, or time-window check. That case was
outside the rehearsal.

Top Secret, Standard, Internal, Confidential, and Production are the enum names used here. A
vault account gets administrator rights from Top Secret clearance alone.

### ქართული

ორივე ტიპის ანგარიში ერთხელ აღწერეთ. მომხმარებელი საცავის ანგარიშით შედის Angular-ის გვერდიდან.
პაროლის შემოწმების შემდეგ `AuthController.Password` ხელს აწერს მხოლოდ `MfaPending` ქუქის.
`SessionIssuer.SignInAsync` სესიას ქმნის ავთენტიფიკატორის ან აღდგენის კოდის დადასტურების,
ავთენტიფიკატორის რეგისტრაციის, passkey-ით ან SSO-ით შესვლის შემდეგ. passkey ორივე ფაქტორს ერთად
ადასტურებს. ელფოსტით მიღებული მოწვევით მომხმარებელი თავად ადგენს პაროლს და არეგისტრირებს
ავთენტიფიკატორს (ADR-0033). აპლიკაცია სერვისის ანგარიშითა და API გასაღებით მიმართავს `/api/v1/`-ს.
`/api/ui/` ამ გასაღებს არ იღებს. Angular-ის მარშრუტების შემოწმებები ნავიგაციას ამარტივებს. წვდომის
შესახებ არქიტექტურული გადაწყვეტილებები აღწერილია ADR-0005-სა და ADR-0012-ში.

ოთხი წესი `SecretAccessPolicy.Evaluate`-ში მოცემული თანმიმდევრობით ახსენით. შემდეგ აჩვენეთ, რატომ
კმარა ამ მაგალითში დაშვებისა და პროექტის შემოწმება. `dev-user`-ს პროექტ `PHOENIX`-ზე Standard
დაშვება აქვს. Standard და Internal ერთსა და იმავე რიცხვით დონეზეა. საიდუმლო Development გარემოს ეკუთვნის,
ამიტომ მასზე Production-ის წვდომის დროითი ფანჯარა არ ვრცელდება. Internal დონეზე სანდო ქსელის წესიც არ
მოქმედებს. გაძლიერებული ავთენტიფიკაცია Secret დონიდან მოითხოვება, ამიტომ ამ მნიშვნელობის
საჩვენებლად ახალი კოდი საჭირო არ არის.

თუ წვდომაზე უარის ჩვენებას გთხოვენ, აუხსენით, რომ loopback-ზე `127.0.0.1/32` ნაგულისხმევად სანდო
მისამართია. ამიტომ ამ ლეპტოპიდან Confidential საიდუმლოზე მოთხოვნაც სანდო
ქსელიდან მოდის. უარის საჩვენებლად საჭიროა შემთხვევა, როცა ანგარიშის დაშვება, პროექტი, საიდუმლოს
მგრძნობელობა ან წვდომის დროითი ფანჯარა წესს ვერ აკმაყოფილებს. ეს ვარიანტი რეპეტიციაზე არ შემოწმებულა.

Top Secret, Standard, Internal, Confidential და Production ჩამონათვალების (enum) მნიშვნელობების
სახელებია. საცავის ანგარიშს ადმინისტრატორის უფლებებისთვის მხოლოდ Top Secret დაშვება სჭირდება.

## Slide 3: Architecture / არქიტექტურა

### English

Start with the person, the vault, and the external systems on the context diagram. The application
that uses a service account is one of those external systems. Switch to the container diagram.
The Angular UI runs in the browser inside the system boundary and calls the UI API with the
session cookie. The Web application is ASP.NET Core on .NET 10 and serves those files, both APIs,
and three server pages for access blocked, error, and not found. Sign-in also runs in Angular.
The revision has no Razor views. `AngularUiRoutes` lists the page routes that serve Angular
(ADR-0034). The process requires `Database:Provider` to select one database at startup. Redis appears
when enabled. Node runs in the Dockerfile's build stage, outside the runtime diagram. The browser
executes the Angular files that Web serves.

Say "module" when you point at Core and Infrastructure. Web references both projects.
Infrastructure references Core. Core references no NuGet package. `EclipsVault.AuditVerifier`
references Core only, so the offline check does not need the database or the private key.

Name the hosted workers that `Program.cs` and `DependencyInjection` actually register:
`AuditGroupCommitter`, `SecretLifecycleWorker`, `DynamicLeaseWorker`, and `LicenseStartupCheck`.
The diagram keeps these threads inside the Web container.

Show Development and the production reference. The one Development run used for the demo is
in [demo-setup-reset.md](demo-setup-reset.md). Development runs the services in `docker-compose.yml`
alongside the Web process. The reference in `deploy/production/` uses Caddy for automatic HTTPS,
the signed vault image, and PostgreSQL on an internal network. A volume holds the Data Protection
key ring. The vault trusts forwarded client addresses only from Caddy's fixed address. Production
does not seed demo data, and the rehearsal did not start the reference deployment. Web uses Redis
only when `Redis:Enabled` is true. The committed `appsettings.json` sets it to false. The copied
Development file sets `Email:Sender` to `Smtp` and points at Mailpit. The 24 November rehearsal used
the committed default, `Log`, and sent no mail.

For questions about dynamic secrets on PostgreSQL, explain that the role selects SQL Server or
PostgreSQL. Each backend connects to a target database separate from the vault's database.

### ქართული

ჯერ კონტექსტის დიაგრამაზე მიუთითეთ მომხმარებელზე, საცავსა და გარე სისტემებზე. სერვისის ანგარიშის
მქონე აპლიკაცია გარე სისტემად არის წარმოდგენილი. შემდეგ კონტეინერების დიაგრამაზე გადადით.
Angular UI სისტემის ნაწილია. ის ბრაუზერში მუშაობს და UI API-ს სესიის ქუქით მიმართავს. Web აპლიკაცია
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

ფონური პროცესების რეგისტრაცია `Program.cs`-სა და `DependencyInjection`-ში ხდება:
`AuditGroupCommitter`, `SecretLifecycleWorker`, `DynamicLeaseWorker` და `LicenseStartupCheck`.
დიაგრამაზე შესრულების ეს ნაკადები Web-ის კონტეინერში დატოვეთ.

აჩვენეთ Development და Production განთავსება. დემონსტრაცია Development გარემოში ერთხელ ჩატარდა.
გაშვების აღწერა მოცემულია [demo-setup-reset.md](demo-setup-reset.md)-ში. Development
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

## Slide 4: Reveal ordering / მნიშვნელობის გამჟღავნების თანმიმდევრობა

### English

Use the sequence diagram to explain the order of calls.

"The details request loads the envelope and writes nothing. Then it asks `SecretAccessPolicy`. If
the answer is no, it records `Access denied` and does not call `RevealAsync`. If the answer is yes,
it commits `Viewed`. When the person presses Reveal value, the check runs again, and `RevealAsync`
asks `AuditSink` to commit `SecretRevealed`. The sink waits until that row is committed. Only the
code after that wait unwraps the data key and opens the value."

If the commit fails, `AuditSink.WriteAsync` throws `AuditWriteFailedException`, and
`RevealAsync` does not catch it before `DecryptToDtoAsync`.

Name the engine configured for the demo. With the committed default, `AesGcmLocal`, a fresh data
key seals the value and the master key wraps the data key. The vault uses `VaultTransit` only when
`Crypto:Engine` selects it. Describe only the key-management service you configured.

Mention master-key rotation and the Encryption keys page without running a rotation. On this
revision, `KekRotationService` rewraps data keys in batches without decrypting secret values.
If a failure stops the pass after a batch commits, the response gives
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

თუ აუდიტის ჩანაწერი ვერ დაფიქსირდა,
`AuditSink.WriteAsync` იწვევს `AuditWriteFailedException` გამონაკლისს. `RevealAsync` ამ გამონაკლისს
`DecryptToDtoAsync`-ის გამოძახებამდე არ ამუშავებს.

დაასახელეთ დაშიფვრის ძრავა, რომელსაც დემონსტრაციისას იყენებთ. რეპოზიტორიაში ნაგულისხმევია
`AesGcmLocal`. მნიშვნელობა ახალი მონაცემთა გასაღებით იშიფრება, მონაცემთა გასაღები კი მასტერ-გასაღებით
იფუთება. `VaultTransit` სხვა ძრავაა და მხოლოდ მაშინ გამოიყენება, როცა ამას `Crypto:Engine` განსაზღვრავს.
ისაუბრეთ მხოლოდ კონფიგურირებულ გასაღებების მართვის სერვისზე.

ახსენეთ ამ რევიზიაში ხელმისაწვდომი მასტერ-გასაღების როტაცია და Encryption keys-ის გვერდი.
დემონსტრაციისას როტაცია არ გაუშვათ. `KekRotationService` მონაცემთა გასაღებებს ჯგუფებად ხელახლა
ფუთავს საიდუმლოების მნიშვნელობების გაშიფვრის გარეშე.
თუ როტაცია უკვე დაფიქსირებული ჯგუფის შემდეგ შეფერხდა, პასუხში ჩანს, რამდენი მონაცემთა გასაღები
შეიფუთა ხელახლა. აუდიტის შეცდომის კოდია `audit_unavailable`. ჯგუფის წარმატებით დაფიქსირების შემდეგ
სხვა შეცდომის კოდია `rotation_interrupted`. Vault Transit-ის გამოყენებისას გვერდზე მისი გასაღების
ვერსიები ჩანს.

## Slide 5: Live journey / დემონსტრაცია

### English

Follow [demo-setup-reset.md](demo-setup-reset.md) and [rehearsal-checklist.md](rehearsal-checklist.md).
Use a disposable vault that is already running and has passed rehearsal. Otherwise, describe the
five steps aloud. Start and check the stack before the audience arrives.

Demo narration

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

Use slide 6 to explain what Verify covers on the live database. The Attestation panel's sentence
about deleted rows applies to a retained export.

If the first-run authenticator enrollment screen appears, turn off the projector. Keep that
screen and recovery codes out of the recording. If a step-up form appears, stop and check which
secret you opened. This Internal secret does not ask for a code. Keep code entry off camera.

### ქართული

მიჰყევით [demo-setup-reset.md](demo-setup-reset.md)-სა და
[rehearsal-checklist.md](rehearsal-checklist.md)-ში მოცემულ ინსტრუქციებს. სტეკი აუდიტორიის მოსვლამდე
გაუშვით და შეამოწმეთ. თუ შემოწმებული ერთჯერადი საცავი მზად არ არის, ხუთივე ნაბიჯი ზეპირად აღწერეთ.

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

მოქმედ მონაცემთა ბაზაზე Verify-ის შემოწმების ფარგლები მე-6 სლაიდზეა. Attestation პანელში
წაშლილი ჩანაწერების შესახებ წინადადება უკვე შენახულ ექსპორტს ეხება.

თუ პირველი შესვლისას ავთენტიფიკატორის რეგისტრაციის გვერდი გამოჩნდა, პროექტორი გამორთეთ.
ვიდეოში არ უნდა მოხვდეს არც ეს გვერდი და არც აღდგენის კოდები. თუ გაძლიერებული ავთენტიფიკაციის
ფორმა გამოჩნდა, შეჩერდით და გადაამოწმეთ, რომელი საიდუმლო გახსენით. ამ Internal საიდუმლოსთვის
კოდი საჭირო არ არის. კოდი მხოლოდ კამერის მიღმა შეიყვანეთ.

## Slide 6: Checkpoint proof boundary / რას ადასტურებს საკონტროლო წერტილი

### English

Verify compares the trail with checkpoints signed by the current key.
`AuditCheckpointService.VerifyAsync` calls `AuditChainVerifier.VerifyAsync` for this check.
Some deletions can escape detection even with the latest checkpoint.

"Verify answers a question about the rows up to the latest checkpoint this key signed.
Entries after that checkpoint are only the hash chain. Deleting the checkpoints, or editing only
those later rows and recomputing their hashes, can still look fine. The offline verifier checks the
bundle in its hand. A bundle an auditor already stored still disagrees with a later rewrite. That
is why a retained export matters, and it is also why I will not say every deletion is detectable
whatever the latest checkpoint is."

The vault writes `AuditLogViewed` when the log opens and `AuditChainVerified` when Verify finishes.
It saves a checkpoint and `AuditCheckpointCreated` in one transaction. Export requires a fresh
step-up and streams after `AuditBundleExported` commits. The vault allows one verification and
one export at a time. `AuditController.Export` calls `AdministratorStepUp`
before `ExportAsync`. `CreateCheckpointAsync` saves the checkpoint through `SaveChangesAsync`, and
`DescribeCheckpointChange` adds the audit row to that same batch.

A current verifier accepts bundle formats `eclipsvault.audit-bundle/1`
and `/2`, and refuses a newer format without saying the row was edited. The 1.0.0 verifier, which
predates that refusal, reports `INVALID — "the row was edited"` for a later bundle. That report is
a compatibility error in the old tool.

If you click Verify, read the returned message. With no checkpoint, it says the chain is intact
and warns that a database writer could still delete the newest entries or recompute hashes.
With a checkpoint, it names the sequence covered by the signature and recommends exporting a
bundle for an outside party. Keep "intact" within the reported scope. Rows after the latest
checkpoint lack signature coverage, and Verify can miss deleted checkpoints.

### ქართული

Verify აუდიტის ჯაჭვს მოქმედი გასაღებით ხელმოწერილ საკონტროლო წერტილებს ადარებს.
`AuditCheckpointService.VerifyAsync` ამ შემოწმებისთვის `AuditChainVerifier.VerifyAsync`-ს იძახებს.
ბოლო საკონტროლო წერტილის არსებობის შემთხვევაშიც შეიძლება ზოგიერთი წაშლა შეუმჩნეველი დარჩეს.

„Verify ჩანაწერებს ამ გასაღებით ხელმოწერილი ბოლო საკონტროლო წერტილის ჩათვლით ადასტურებს.
შემდეგ ჩანაწერებს მხოლოდ ჰეშების ჯაჭვი აკავშირებს. საკონტროლო წერტილების წაშლა შეიძლება შეუმჩნეველი
დარჩეს. იგივე ეხება ბოლო წერტილის შემდეგ ჩანაწერების შეცვლას, თუ მათი ჰეშებიც ხელახლა გამოითვალა.
ოფლაინ შემმოწმებელი მხოლოდ მისთვის გადაცემულ პაკეტს ამოწმებს. აუდიტორთან უკვე შენახული პაკეტი
მოგვიანებით გადაწერილ ჩანაწერებს აღარ დაემთხვევა. ამიტომ შენახულ ექსპორტს მნიშვნელობა აქვს.
ბოლო საკონტროლო წერტილის არსებობაც კი არ იძლევა ყოველი წაშლის აღმოჩენის გარანტიას.“

ჟურნალის გახსნისას იწერება `AuditLogViewed`,
Verify-ის დასრულებისას კი `AuditChainVerified`. `AuditCheckpointCreated` საკონტროლო წერტილთან
ერთად იმავე ტრანზაქციაში ფიქსირდება. ნაკადური ექსპორტისთვის საჭიროა ახლახან გავლილი გაძლიერებული
ავთენტიფიკაცია და `AuditBundleExported`-ის დაფიქსირება. ერთდროულად მხოლოდ ერთი შემოწმება და ერთი
ექსპორტი შეიძლება შესრულდეს. `AuditController.Export` `ExportAsync`-მდე `AdministratorStepUp`-ს
იძახებს. `CreateCheckpointAsync` საკონტროლო წერტილს `SaveChangesAsync`-ით ინახავს.
`DescribeCheckpointChange` აუდიტის ჩანაწერს იმავე ჯგუფს ამატებს.

მიმდინარე შემმოწმებელი იღებს პაკეტის ფორმატებს
`eclipsvault.audit-bundle/1` და `/2`. უფრო ახალ ფორმატს უარყოფს ისე, რომ ჩანაწერს შეცვლილად არ
აცხადებს. 1.0.0 ვერსია ამ შემოწმების დამატებამდე გამოვიდა და უფრო ახალ პაკეტზე აჩვენებს
`INVALID — "the row was edited"`. ეს ძველი ვერსიის თავსებადობის შეცდომაა.

თუ Verify-ს გაუშვებთ, წაიკითხეთ ეკრანზე ნაჩვენები შეტყობინება. საკონტროლო წერტილის გარეშე შეტყობინება
ადასტურებს ჯაჭვის მთლიანობას და გაფრთხილებთ, რომ ბაზაში ჩაწერის უფლების მქონე პირს მაინც შეუძლია
უახლესი ჩანაწერების წაშლა ან ჰეშების ხელახლა გამოთვლა. საკონტროლო წერტილის შექმნის შემდეგ
შეტყობინება ასახელებს, მიმდევრობის რომელ ნომრამდე ვრცელდება ხელმოწერა, და გარე მხარისთვის პაკეტის
ექსპორტს გთავაზობთ. „intact“ ამ შეტყობინებაში აღწერილ შემოწმებას ეხება. ბოლო საკონტროლო წერტილის
შემდეგ ჩანაწერებზე ხელმოწერა არ ვრცელდება, ხოლო წაშლილი საკონტროლო წერტილები Verify-მ შეიძლება ვერ შენიშნოს.

## Slide 7: Final revision and test evidence / საბოლოო რევიზია და ტესტების შედეგები

### English

Read the filled cells in the revision table and say "not recorded yet" for an empty cell. The rehearsal used `origin/main` at
`1298958050e45f83aefeb4b6455d0a02169ab22a`. Angular sign-in, invitations, the server pages,
Encryption keys, the audit-log proof boundary, and the Angular secret and audit pages are on that
revision. The Release build for the demo had no warnings or errors. During the draft
refresh, `scripts/check.sh` passed locked restore, the Release build, and the unit suite. The
separate Development run is recorded in [demo-setup-reset.md](demo-setup-reset.md).

If the owner settles on a different revision, replace the hash on the slide and read the hash of the
commit the vault is running.

### ქართული

რევიზიის ცხრილში წაიკითხეთ შევსებული უჯრები. ცარიელ უჯრაზე თქვით „ჯერ არ არის დაფიქსირებული“.
რეპეტიცია ჩატარდა `origin/main`-ის რევიზიაზე
`1298958050e45f83aefeb4b6455d0a02169ab22a`. ამ რევიზიაში ხელმისაწვდომია Angular-ის შესვლის,
საიდუმლოებისა და აუდიტის გვერდები, მოწვევები, სერვერული გვერდები და Encryption keys. ამავე
რევიზიას ეხება აუდიტის ჟურნალის შემოწმების აღწერილი ფარგლები. დემონსტრაციისთვის Release რეჟიმში
აგება გაფრთხილებისა და შეცდომის გარეშე დასრულდა. მონახაზის განახლებისას `scripts/check.sh`-ით
წარმატებით დასრულდა დამოკიდებულებების ფიქსირებული ვერსიებით აღდგენა, Release რეჟიმში აგება და
ერთეულოვანი ტესტები. Development გარემოში ცალკე ჩატარებული დემონსტრაცია
[demo-setup-reset.md](demo-setup-reset.md)-შია აღწერილი.

თუ მფლობელი საბოლოოდ სხვა რევიზიას აირჩევს, განაახლეთ ჰეში და მოხსენებაზე ახალი მნიშვნელობა
დაასახელეთ. სლაიდზე გაშვებული საცავის კომიტის ჰეში უნდა ჩანდეს.

## Slide 8: Lessons and limitations / დასკვნები და შეზღუდვები

### English

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
