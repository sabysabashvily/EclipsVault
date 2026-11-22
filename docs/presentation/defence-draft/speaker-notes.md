# Speaker notes / მომხსენებლის შენიშვნები

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

დაიწყეთ გუნდით, არა ფრეიმვორკით. „Phoenix-ს სჭირდება ერთი ბაზის პაროლი, რომელიც რამდენიმე ადამიანს
ჭირდება. თუ ის ფაილში დევს, ფაილის ფლობა ერთადერთი კონტროლია და ფაილი ვერ გვეტყვის, ვინ წაიკითხა.“

შემდეგ ერთი წინადადება, რა არის საცავი: თვითჰოსტირებულია, გასაღებები და ბაზა ოპერატორს აქვს, და
ყოველი გახსნა მოთხოვნაა, რომელიც სერვერმა შეიძლება უარყოს და ჩაიწეროს. ყველა ფუნქცია არ ჩამოთვალოთ.
დარჩენილი შვიდი სლაიდი ამ ერთ საიდუმლოს მისდევს.

ფასი, ლიცენზიის საფეხური ან შესაბამისობის სერტიფიკატი არ ციტიროთ. არ თქვათ, რომ პროდუქტი ამ
მოხსენებისთვის განთავსდა.

## Slide 2 — Server-enforced access / სერვერის მიერ დაცული წვდომა

### English

Two identities, said once. A person uses a vault account. After the password, `AccountController.Login`
signs only an `MfaPending` cookie. `SessionIssuer.SignInAsync` creates the session after the
authenticator code, a recovery code, enrollment, a passkey, or SSO. A passkey is both factors at
once. An application uses a service account and an API key on `/api/v1/`. The UI API at `/api/ui/`
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

ორი იდენტობა, თითოჯერ. ადამიანი საცავის ანგარიშს იყენებს. პაროლის შემდეგ `AccountController.Login`
მხოლოდ `MfaPending` ქუქის წერს. `SessionIssuer.SignInAsync` სესიას ქმნის ავთენტიფიკატორის კოდის,
აღდგენის კოდის, ჩარიცხვის, passkey-ს ან SSO-ს შემდეგ. Passkey ორივე ფაქტორია ერთად. აპლიკაცია
სერვისის ანგარიშს და API გასაღებს იყენებს `/api/v1/`-ზე. `/api/ui/` ამ გასაღებს არ იღებს. ADR-0005
და ADR-0012 გადაწყვეტილებებია: Angular-ის მცველები მოხერხებულობაა.

ოთხი წესი გაიარეთ იმ რიგით, რომლითაც `SecretAccessPolicy.Evaluate` ამოწმებს. შემდეგ თქვით, რატომ არის
ცოცხალი საიდუმლო განზრახ მარტივი. `dev-user` არის Standard პროექტზე `PHOENIX`. საიდუმლო Internal-ია,
იგივე რიცხვითი დონე, Development-ია, ამიტომ წარმოების ფანჯარა არ ეხება, და Internal-ია, ამიტომ სანდო
ქსლის წესი არ ეხება. გაძლიერებული დადასტურება Secret-იდან იწყება, ამიტომ ეს გახსნა ახალ კოდს არ
ითხოვს.

თუ გეკითხებიან „უარი მაჩვენე“, loopback-ზე ნუ მოიფიქრებთ. ნაგულისხმევ სანდო სიაში `127.0.0.1/32`
შედის, ამიტომ ამ ლეპტოპიდან გახსნილი Confidential საიდუმლო მაინც სანდო ქსელშია. უარს სჭირდება
ანგარიში, პროექტი, მგრძნობელობა ან ფანჯარა, რომელიც ნამდვილად ვერ გადის. ეს ვარიანტი არ
რეპეტირებულა.

სიტყვები Top Secret, Standard, Internal, Confidential და Production ენამების სახელებია.
ადმინისტრატორი არის საცავის ანგარიში Top Secret დაშვებით, არა ცალკე როლი.

## Slide 3 — Architecture / არქიტექტურა

### English

Point at the context picture first: one person, the vault, and the external systems. The
application that uses a service account is an external system, not a person. Then the container
picture. Inside the boundary, the Angular UI runs in the browser and calls the UI API with the
session cookie. The Web application is ASP.NET Core on .NET 10 and serves those files, both APIs,
and the Razor sign-in pages that remain. Secrets, the audit log, and Encryption keys are Angular
routes on this revision. One database is selected. Redis appears only when enabled. Node is a
build stage in the Dockerfile and is not a box in that picture. The Angular files are what the
browser runs.

Say "module" when you point at Core and Infrastructure. Web references both projects.
Infrastructure references Core. Core references no NuGet package. `EclipsVault.AuditVerifier`
references Core only, so the offline check does not need the database or the private key.

Name the hosted workers that `Program.cs` and `DependencyInjection` actually register:
`AuditGroupCommitter`, `SecretLifecycleWorker`, `DynamicLeaseWorker`, and `LicenseStartupCheck`.
They are threads in Web, not extra containers.

Close with two deployments, Development and production; the demonstration is one Development
run, recorded in [demo-setup-reset.md](demo-setup-reset.md). Development is `docker-compose.yml`
plus the Web process. Production is the image in the Dockerfile, behind a TLS proxy, with no demo
seed. Redis in the compose file does nothing until `Redis:Enabled` is true; the committed
`appsettings.json` sets it false. The copied Development file sets `Email:Sender` to `Smtp` and
points it at Mailpit. This rehearsal did not open Mailpit. The committed default remains `Log`.

### ქართული

ჯერ კონტექსტის სურათს მიუთითეთ: ერთი პიროვნება, საცავი და გარე სისტემები. აპლიკაცია, რომელიც
სერვისის ანგარიშს იყენებს, გარე სისტემაა, არა პიროვნება. შემდეგ კონტეინერის სურათს. საზღვრის
შიგნით Angular UI ბრაუზერში ეშვება და UI API-ს სესიის ქუქით იძახებს. Web აპლიკაცია ASP.NET Core-ა
.NET 10-ზე და ამ ფაილებს, ორივე API-ს და დარჩენილ Razor-ის შესვლის გვერდებს აწვდის. საიდუმლოები,
აუდიტის ჟურნალი და Encryption keys ამ რევიზიაზე Angular-ის მარშრუტებია. არჩეულია ერთი ბაზა.
Redis მხოლოდ მაშინ ჩანს, როცა ჩართულია. Node Dockerfile-ის აწყობის საფეხურია და ამ სურათში უჯრა არ
არის. Angular-ის ფაილები ისაა, რასაც ბრაუზერი უშვებს.

Core-სა და Infrastructure-ზე თითის დაჭერისას თქვით „მოდული“. Web ორივე პროექტს იყენებს.
Infrastructure იყენებს Core-ს. Core-ს NuGet პაკეტი არ აქვს. `EclipsVault.AuditVerifier` მხოლოდ Core-ს
იყენებს, ამიტომ ხაზგარეშე შემოწმებას ბაზა ან პირადი გასაღები არ სჭირდება.

დაასახელეთ ფონური მუშაკები, რომლებსაც `Program.cs` და `DependencyInjection` ნამდვილად არეგისტრირებს:
`AuditGroupCommitter`, `SecretLifecycleWorker`, `DynamicLeaseWorker` და `LicenseStartupCheck`. ისინი
Web-ის ნაკადებია, არა დამატებითი კონტეინერები.

დაასრულეთ ორი განთავსებით, Development-ით და production-ით; დემონსტრაცია ერთი Development
გაშვებაა, ჩაწერილი [demo-setup-reset.md](demo-setup-reset.md)-ში. Development არის
`docker-compose.yml` პლუს Web პროცესი. Production არის Dockerfile-ის იმიჯი, TLS პროქსის უკან,
სადემონსტრაციო სამყაროს გარეშე. compose ფაილში Redis არაფერს აკეთებს, სანამ `Redis:Enabled`
ჭეშმარიტი არ არის; ჩადებული `appsettings.json` მას მცდარზე აყენებს. გადაწერილი Development ფაილი
`Email:Sender`-ს `Smtp`-ზე აყენებს და Mailpit-ზე მიუთითებს. ამ რეპეტიციამ Mailpit არ გახსნა.
ჩადებული ნაგულისხმევი კვლავ `Log`-ია.

## Slide 4 — Reveal ordering / გახსნის რიგითობა

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
moves envelopes in batches and does not open a value.

### ქართული

რიგი დიაგრამიდან წაიკითხეთ, ნელა.

„დეტალების მოთხოვნა კონვერტს ტვირთავს და არაფერს წერს. შემდეგ `SecretAccessPolicy`-ს ეკითხება. თუ
პასუხი უარყოფითია, წერს `Access denied`-ს და `RevealAsync`-ს არ იძახებს. თუ პასუხი დადებითია,
აფიქსირებს `Viewed`-ს. როცა ადამიანი Reveal value-ს აჭერს, შემოწმება თავიდან ეშვება და `RevealAsync`
`AuditSink`-ს სთხოვს, დააფიქსიროს `SecretRevealed`. Sink ელოდება, სანამ ჩანაწერი დაფიქსირდება.
მხოლოდ ამ ლოდინის შემდეგ კოდი ხსნის მონაცემთა გასაღებს და მნიშვნელობას.“

ეს ლოდინი მთელი მტკიცებაა. `AuditSink.WriteAsync` ისვრის `AuditWriteFailedException`-ს, როცა
დაფიქსირება ვერ ხერხდება, და `RevealAsync` ამას `DecryptToDtoAsync`-მდე არ იჭერს.

დაასახელეთ ის ძრავი, რომელსაც ნამდვილად უშვებთ. ჩადებული ნაგულისხმევი `AesGcmLocal`-ია: ახალი
მონაცემთა გასაღები მნიშვნელობას ბეჭდავს, მასტერ-გასაღები კი მონაცემთა გასაღებს ახვევს. `VaultTransit`
სხვა ძრავია და მხოლოდ მაშინ გამოიყენება, როცა `Crypto:Engine` ასე ამბობს. ნუ აღწერთ გასაღებების
სერვისს, რომელიც არ მოგიწყვიათ.

მასტერ-გასაღების როტაცია და Encryption keys-ის გვერდი ამ რევიზიაზეა. შეგიძლიათ დაასახელოთ. ოთახში
გასაღები ნუ დაატრიალებთ და ნუ იტყვით, რომ როტაცია საიდუმლოებს შლის. `KekRotationService` კონვერტებს
პარტიებად გადააქვს და მნიშვნელობას არ ხსნის.

## Slide 5 — Live journey / ცოცხალი გზა

### English

Follow [demo-setup-reset.md](demo-setup-reset.md) and [rehearsal-checklist.md](rehearsal-checklist.md).
If the disposable vault is not already up from a checked run, skip the live clicks and talk through
the five steps. Do not start a stack for the first time in front of the room.

Narration while it works:

"This account is the Phoenix member. Sign-in opens Secrets, which is an Angular page. The list
says two active secrets right after the reset: the one I will open, and a short-lived token I will
not. The token expires about five minutes after the reset, so the list may now say one. I filter to
`Phoenix_Dev_Database_Password` and press Open, then Reveal value. The value appears on the page,
so I press Back to list before anyone records the screen. I sign out from the account card and
return as the administrator. The audit log is an Angular page under Administration, and only an
administrator can open it. It shows an unlicensed notice; that notice says the vault still runs. I
filter to the secret name. The row I want says `Revealed` and names this account. The `Viewed` row
under it is the details page after the server allowed it, not the decryption. A `Created` row under
those is the Development seed. Latest checkpoint says none yet. I do not press Verify integrity or
Export signed bundle."

If the authenticator enrollment screen appears, stop the projection. That screen is first-run
enrollment, and it does not belong in a recording. The same is true of recovery codes.

If a step-up form appears, you are not on the Internal secret. Stop. Do not type a code on camera.

### ქართული

გაჰყევით [demo-setup-reset.md](demo-setup-reset.md)-ს და
[rehearsal-checklist.md](rehearsal-checklist.md)-ს. თუ ერთჯერადი საცავი შემოწმებული გაშვებიდან უკვე
არ დგას, ცოცხალი დაწკაპუნება გამოტოვეთ და ხუთი ნაბიჯი სიტყვიერად გაიარეთ. ოთახის წინ პირველად სტეკი
ნუ ჩართოთ.

ტექსტი, სანამ მუშაობს:

„ეს ანგარიში Phoenix-ის წევრია. შესვლა ხსნის Secrets-ს, რომელიც Angular-ის გვერდია. სია
გადატვირთვისთანავე ორ აქტიურ საიდუმლოს აჩვენებს: იმას, რომელსაც გავხსნი, და ხანმოკლე ტოკენს,
რომელსაც არ შევეხები. ტოკენს ვადა გადატვირთვიდან დაახლოებით ხუთ წუთში გასდის, ამიტომ სია ახლა
შეიძლება ერთს აჩვენებდეს.
ვფილტრავ `Phoenix_Dev_Database_Password`-ზე და ვაჭერ Open-ს, შემდეგ Reveal value-ს. მნიშვნელობა
გვერდზე ჩნდება, ამიტომ Back to list-ს ვაჭერ, სანამ ვინმე ეკრანს ჩაიწერს. ანგარიშის ბარათიდან
გამოვდივარ და ადმინისტრატორად ვბრუნდები. აუდიტის ჟურნალი Angular-ის გვერდია Administration-ში და
მხოლოდ ადმინისტრატორს შეუძლია მისი გახსნა. ის ულიცენზიო შეტყობინებას აჩვენებს; შეტყობინება ამბობს,
რომ საცავი მაინც მუშაობს. საიდუმლოს სახელით ვფილტრავ. ჩანაწერი, რომელიც მჭირდება, ამბობს
`Revealed` და ამ ანგარიშს ასახელებს. მის ქვეშ `Viewed` დეტალების გვერდია მას შემდეგ, რაც სერვერმა
დაუშვა, არა გაშიფვრა. მათ ქვეშ `Created` Development-ის თესლია. Latest checkpoint ამბობს, რომ ჯერ
არ არის. Verify integrity-სა და Export signed bundle-ს არ ვაჭერ.“

თუ ავთენტიფიკატორის ჩარიცხვის ეკრანი გამოჩნდება, პროექცია გააჩერეთ. ეს პირველი გაშვების ჩარიცხვაა და
ჩანაწერში არ ეკუთვნის. იგივე ეხება აღდგენის კოდებს.

თუ გაძლიერებული დადასტურების ფორმა გამოჩნდება, Internal საიდუმლოზე არ ხართ. გაჩერდით. კოდი კამერაზე
ნუ აკრიფოთ.

## Slide 6 — Checkpoint proof boundary / საკონტროლო წერტილის მტკიცებულების საზღვარი

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

ეს ის სლაიდია, სადაც დაუდევარი წინადადება ზედმეტად ძლიერია. Verify ახლა ადარებს კვალს მიმდინარე
გასაღებით ხელმოწერილ საკონტროლო წერტილებს. ეს არის `AuditChainVerifier.VerifyAsync`, რომელსაც
იძახებს `AuditCheckpointService.VerifyAsync`. ნუ დაუბრუნდებით ძველ წინადადებას, რომ Verify
საკონტროლო წერტილებს არ უყურებს. მეორე ძველ წინადადებაზეც ნუ გადახვალთ: რომ ყოველი წაშლა აღმოჩენადია.

სანაცვლოდ თქვით: „Verify პასუხობს კითხვას იმ ჩანაწერებზე, რომლებიც ამ გასაღების ბოლო საკონტროლო
წერტილამდეა. ამ წერტილის შემდეგ ჩანაწერები მხოლოდ ჰეშის ჯაჭვია. საკონტროლო წერტილების წაშლამ, ან
მხოლოდ იმ გვიანი ჩანაწერების შეცვლამ და მათი ჰეშების თავიდან დათვლამ, შეიძლება მაინც კარგად
გამოიყურებოდეს. ხაზგარეშე შემმოწმებელი იმ შეკვრას ამოწმებს, რომელიც ხელში აქვს. აუდიტორის მიერ უკვე
შენახული შეკვრა გვიან გადაწერას მაინც არ დაემთხვევა. ამიტომ შენახული ექსპორტი მნიშვნელოვანია, და
ამიტომაც არ ვიტყვი, რომ ყოველი წაშლა აღმოჩენადია, როგორიც უნდა იყოს ბოლო საკონტროლო წერტილი.“

ქცევები, რომლებიც ადრე დაგეგმილად იყო აღწერილი, ამ რევიზიაზეა: `AuditLogViewed`, როცა ჟურნალი
იხსნება, `AuditChainVerified`, როცა Verify სრულდება, `AuditCheckpointCreated` იმავე ტრანზაქციაში,
რომელშიც საკონტროლო წერტილი, ნაკადური ექსპორტი `AuditBundleExported`-ისა და ახალი გაძლიერებული
დადასტურების შემდეგ, და თითო შემოწმება და თითო ექსპორტი ერთდროულად. `AuditController.Export`
`ExportAsync`-მდე იძახებს `AdministratorStepUp`-ს. `CreateCheckpointAsync` საკონტროლო წერტილს
`SaveChangesAsync`-ით ინახავს, და `DescribeCheckpointChange` აუდიტის ჩანაწერს იმავე პარტიას ამატებს.

დაიცავით ძველი შემმოწმებლის შენიშვნა. მიმდინარე შემმოწმებელი იღებს შეკვრის ფორმატებს
`eclipsvault.audit-bundle/1` და `/2`, და უფრო ახალ ფორმატს უარყოფს ისე, რომ ჩანაწერს შეცვლილს არ
ეძახის. 1.0.0 შემმოწმებელი, რომელიც ამ უარზე ადრე გამოვიდა, უფრო ახალ შეკვრაზე აჩვენებს
`INVALID — "the row was edited"`. ეს ანგარიში ძველი ხელსაწყოა, არა ხელყოფის მტკიცებულება.

Verify-ზე დაწკაპუნების შემდეგ ნუ იტყვით „intact“ ისე, თითქოს ბოლო საკონტროლო წერტილის შემდეგ
ჩანაწერებსაც ფარავდეს, ან თითქოს წაშლილი საკონტროლო წერტილები მაინც გამოჩნდებოდეს. თუ მაინც
დააწკაპუნებთ, წაიკითხეთ ის შეტყობინება, რომელსაც ნამდვილად აბრუნებს. სანამ საკონტროლო წერტილი არ
არსებობს, შეტყობინება ამბობს, რომ ჯაჭვი მთლიანია და რომ ბაზის დამწერს მაინც შეუძლია უახლესი
ჩანაწერები წაშალოს ან ჰეშები თავიდან დათვალოს. საკონტროლო წერტილის შემდეგ ის ასახელებს მიმდევრობას,
რომელსაც ხელმოწერა ფარავს, და ოთახს ეუბნება, გარე მხარისთვის შეკვრა გაიტანოს.

## Slide 7 — Final revision and test evidence / საბოლოო რევიზია და ტესტის მტკიცებულება

### English

Read only filled cells. For an empty one, say "not recorded yet".

Read the revision table. The code this talk describes is `origin/main` at
`d576dd93a30bc461183ca7b71c89f038adbff446`. Encryption keys, the audit-log proof boundary, and the
Angular secret and audit pages are on that revision. You cannot say this talk's own stack passed
the repository's test suite. You can say the disposable Development walk in
[demo-setup-reset.md](demo-setup-reset.md) was done, and only what that file records.

If the owner later freezes a different revision, replace the hash and say the new one. Do not leave
this hash up if the running vault is a different commit.

### ქართული

მხოლოდ შევსებული უჯრები წაიკითხეთ. ცარიელზე თქვით „ჯერ ჩაწერილი არ არის“.

წაიკითხეთ რევიზიის ცხრილი. კოდი, რომელსაც ეს მოხსენება აღწერს, არის `origin/main`
`d576dd93a30bc461183ca7b71c89f038adbff446`-ზე. Encryption keys, აუდიტის ჟურნალის მტკიცებულების
საზღვარი და Angular-ის საიდუმლოსა და აუდიტის გვერდები იმ რევიზიაზეა. ვერ იტყვით, რომ ამ მოხსენების
საკუთარმა სტეკმა რეპოზიტორიის ტესტები გაიარა. შეგიძლიათ თქვათ, რომ ერთჯერადი Development გავლა
[demo-setup-reset.md](demo-setup-reset.md)-ში გაკეთდა, და მხოლოდ ის, რასაც ის ფაილი წერს.

თუ მფლობელი მოგვიანებით სხვა რევიზიას გაყინავს, ჰეში შეცვალეთ და ახალი თქვით. ეს ჰეში ნუ დატოვოთ,
თუ გაშვებული საცავი სხვა commit-ია.

## Slide 8 — Lessons and limitations / დასკვნები და საზღვრები

### English

Three lessons, then stop.

"The interesting part is not that a person can open a password. It is that the server checked
clearance, project, network, and time, and that the reveal row exists before the plaintext does."

"The audit trail is as strong as the check you actually run. A hash walk, a signed checkpoint, and
a bundle held by someone else are three different checks."

"What you saw was the Development demo world: synthetic accounts, one synthetic secret, no claim
about production."

Invite questions. The prepared answers are in [questions.md](questions.md). If a question asks
whether every deletion is detectable, say no, and point at the latest checkpoint. If a question
asks for invitations or an Angular sign-in page, say those pages are still the Razor sign-in flow
and this revision has no invitation flow.

### ქართული

სამი დასკვნა, შემდეგ გაჩერდით.

„საინტერესო ის არ არის, რომ ადამიანმა პაროლი გახსნა. საინტერესოა, რომ სერვერმა შეამოწმა დაშვება,
პროექტი, ქსელი და დრო, და რომ გახსნის ჩანაწერი არსებობს მანამ, სანამ ღია ტექსტი იარსებებს.“

„აუდიტის კვალი იმდენად ძლიერია, რამდენადაც ის შემოწმება, რომელსაც ნამდვილად უშვებთ. ჰეშის გავლა,
ხელმოწერილი საკონტროლო წერტილი და სხვის მიერ შენახული შეკვრა სამი განსხვავებული შემოწმებაა.“

„რაც ნახეთ, Development-ის სადემონსტრაციო სამყარო იყო: სინთეზური ანგარიშები, ერთი სინთეზური საიდუმლო, და
არანაირი მტკიცება production-ზე.“

კითხვები მიიღეთ. მომზადებული პასუხები [questions.md](questions.md)-შია. თუ გეკითხებიან, ყოველი
წაშლა აღმოჩენადია თუ არა, თქვით არა და ბოლო საკონტროლო წერტილს მიუთითეთ. თუ გეკითხებიან მოწვევებზე
ან Angular-ის შესვლის გვერდზე, თქვით, რომ ეს გვერდები ჯერ Razor-ის შესვლის ნაკადია და ამ რევიზიას
მოწვევის ნაკადი არ აქვს.
