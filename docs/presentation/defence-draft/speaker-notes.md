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
and the Legacy pages that remain. One database is selected. Redis appears only when enabled. Node
is a build stage in the Dockerfile and is not a box in that picture. The Angular files are what the
browser runs.

Say "module" when you point at Core and Infrastructure. Web references both projects.
Infrastructure references Core. Core references no NuGet package. `EclipsVault.AuditVerifier`
references Core only, so the offline check does not need the database or the private key.

Name the hosted workers that `Program.cs` and `DependencyInjection` actually register:
`AuditGroupCommitter`, `SecretLifecycleWorker`, `DynamicLeaseWorker`, and `LicenseStartupCheck`.
They are threads in Web, not extra containers.

Close with two deployments, Development and production; the demonstration is one Development
run. Do not pretend either was started for this draft. Development is `docker-compose.yml` plus
`dotnet run`. Production is the image in the Dockerfile, behind a TLS proxy, with no demo
seed. Redis in the compose file does nothing until `Redis:Enabled` is true; the committed
`appsettings.json` sets it false.

### ქართული

ჯერ კონტექსტის სურათს მიუთითეთ: ერთი პიროვნება, საცავი და გარე სისტემები. აპლიკაცია, რომელიც
სერვისის ანგარიშს იყენებს, გარე სისტემაა, არა პიროვნება. შემდეგ კონტეინერის სურათს. საზღვრის
შიგნით Angular UI ბრაუზერში ეშვება და UI API-ს სესიის ქუქით იძახებს. Web აპლიკაცია ASP.NET Core-ა
.NET 10-ზე და ამ ფაილებს, ორივე API-ს და დარჩენილ Legacy გვერდებს აწვდის. არჩეულია ერთი ბაზა.
Redis მხოლოდ მაშინ ჩანს, როცა ჩართულია. Node Dockerfile-ის აწყობის საფეხურია და ამ სურათში უჯრა არ
არის. Angular-ის ფაილები ისაა, რასაც ბრაუზერი უშვებს.

Core-სა და Infrastructure-ზე თითის დაჭერისას თქვით „მოდული“. Web ორივე პროექტს იყენებს.
Infrastructure იყენებს Core-ს. Core-ს NuGet პაკეტი არ აქვს. `EclipsVault.AuditVerifier` მხოლოდ Core-ს
იყენებს, ამიტომ ხაზგარეშე შემოწმებას ბაზა ან პირადი გასაღები არ სჭირდება.

დაასახელეთ ფონური მუშაკები, რომლებსაც `Program.cs` და `DependencyInjection` ნამდვილად არეგისტრირებს:
`AuditGroupCommitter`, `SecretLifecycleWorker`, `DynamicLeaseWorker` და `LicenseStartupCheck`. ისინი
Web-ის ნაკადებია, არა დამატებითი კონტეინერები.

დაასრულეთ ორი განთავსებით, Development-ით და production-ით; დემონსტრაცია ერთი Development
გაშვებაა. ნუ მოიტყუებით, თითქოს რომელიმე ამ მონახაზისთვის გაეშვა. Development არის
`docker-compose.yml` პლუს `dotnet run`. Production არის Dockerfile-ის იმიჯი, TLS პროქსის უკან,
სადემონსტრაციო სამყაროს გარეშე. compose
ფაილში Redis არაფერს აკეთებს, სანამ `Redis:Enabled` ჭეშმარიტი არ არის; ჩადებული `appsettings.json`
მას მცდარზე აყენებს.

## Slide 4 — Reveal ordering / გახსნის რიგითობა

### English

Read the order off the diagram, slowly.

"The controller loads the envelope and writes `Viewed`. Then it asks `SecretAccessPolicy`. If the
answer is no, it redirects and does not call `RevealAsync`. If the answer is yes, `RevealAsync`
asks `AuditSink` to commit `SecretRevealed`. The sink waits until that row is committed. Only the
code after that wait unwraps the data key and opens the value."

That wait is the whole claim. `AuditSink.WriteAsync` throws `AuditWriteFailedException` when the
commit fails, and `RevealAsync` does not catch it before `DecryptToDtoAsync`.

Say the engine name you are actually running. The committed default is `AesGcmLocal`: a fresh data
key seals the value, and the master key wraps the data key. `VaultTransit` is a different engine,
used only when `Crypto:Engine` says so. Do not describe a key-management service you have not
configured.

The Area 6a head does not change this method. You may say "this order is the code on `main` and on
the open encryption PR." You may not say the encryption PR has merged.

### ქართული

რიგი დიაგრამიდან წაიკითხეთ, ნელა.

„კონტროლერი კონვერტს ტვირთავს და `Viewed`-ს წერს. შემდეგ `SecretAccessPolicy`-ს ეკითხება. თუ პასუხი
უარყოფითია, გადაამისამართებს და `RevealAsync`-ს არ იძახებს. თუ პასუხი დადებითია, `RevealAsync`
`AuditSink`-ს სთხოვს, დააფიქსიროს `SecretRevealed`. Sink ელოდება, სანამ ჩანაწერი დაფიქსირდება.
მხოლოდ ამ ლოდინის შემდეგ კოდი ხსნის მონაცემთა გასაღებს და მნიშვნელობას.“

ეს ლოდინი მთელი მტკიცებაა. `AuditSink.WriteAsync` ისვრის `AuditWriteFailedException`-ს, როცა
დაფიქსირება ვერ ხერხდება, და `RevealAsync` ამას `DecryptToDtoAsync`-მდე არ იჭერს.

დაასახელეთ ის ძრავი, რომელსაც ნამდვილად უშვებთ. ჩადებული ნაგულისხმევი `AesGcmLocal`-ია: ახალი
მონაცემთა გასაღები მნიშვნელობას ბეჭდავს, მასტერ-გასაღები კი მონაცემთა გასაღებს ახვევს. `VaultTransit`
სხვა ძრავია და მხოლოდ მაშინ გამოიყენება, როცა `Crypto:Engine` ასე ამბობს. ნუ აღწერთ გასაღებების
სერვისს, რომელიც არ მოგიწყვიათ.

Area 6a-ის თავი ამ მეთოდს არ ცვლის. შეგიძლიათ თქვათ: „ეს რიგი არის კოდი `main`-ზე და ღია დაშიფვრის
PR-ზე.“ ვერ იტყვით, რომ დაშიფვრის PR შეერწყა.

## Slide 5 — Live journey / ცოცხალი გზა

### English

Follow [demo-setup-reset.md](demo-setup-reset.md). If the disposable vault is not already up from a
separate checked run, skip the live clicks and talk through the five steps. Do not start a stack
for the first time in front of the room.

Narration while it works:

"This account is the Phoenix member. The secret list is still a Legacy page; secrets have not moved
to the Angular route table on this revision. I reveal one Internal secret. The value appears in the
page, so I leave it before anyone records the screen. I sign out and return as the administrator.
The audit log is also still Legacy, and only an administrator can open it. It lists the newest
row first. The row I want says `Revealed`, names `Phoenix_Dev_Database_Password`, and names this
account. The `Viewed` row below it is the details page, not the decryption."

If the authenticator enrollment screen appears, stop the projection. That screen is first-run
enrollment, and it does not belong in a recording. The same is true of recovery codes.

If a step-up form appears, you are not on the Internal secret. Stop. Do not type a code on camera.

### ქართული

გაჰყევით [demo-setup-reset.md](demo-setup-reset.md)-ს. თუ ერთჯერადი საცავი ცალკე შემოწმებული
გაშვებიდან უკვე არ დგას, ცოცხალი დაწკაპუნება გამოტოვეთ და ხუთი ნაბიჯი სიტყვიერად გაიარეთ. ოთახის
წინ პირველად სტეკი ნუ ჩართოთ.

ტექსტი, სანამ მუშაობს:

„ეს ანგარიში Phoenix-ის წევრია. საიდუმლოების სია ჯერ Legacy გვერდია; ამ რევიზიაზე საიდუმლოები
Angular-ის მარშრუტების ცხრილში არ გადასულა. ერთ Internal საიდუმლოს ვხსნი. მნიშვნელობა გვერდზე
ჩნდება, ამიტომ ვტოვებ მას, სანამ ვინმე ეკრანს ჩაიწერს. გამოვდივარ და ადმინისტრატორად ვბრუნდები.
აუდიტის ჟურნალიც ჯერ Legacy-ა და მხოლოდ ადმინისტრატორს შეუძლია მისი გახსნა. ის ჯერ უახლეს
ჩანაწერს აჩვენებს. ჩანაწერი, რომელიც მჭირდება, ამბობს `Revealed`, ასახელებს
`Phoenix_Dev_Database_Password`-ს და ამ ანგარიშს. ქვემოთ `Viewed` დეტალების გვერდია, არა გაშიფვრა.“

თუ ავთენტიფიკატორის ჩარიცხვის ეკრანი გამოჩნდება, პროექცია გააჩერეთ. ეს პირველი გაშვების ჩარიცხვაა და
ჩანაწერში არ ეკუთვნის. იგივე ეხება აღდგენის კოდებს.

თუ გაძლიერებული დადასტურების ფორმა გამოჩნდება, Internal საიდუმლოზე არ ხართ. გაჩერდით. კოდი კამერაზე
ნუ აკრიფოთ.

## Slide 6 — Checkpoint proof boundary / საკონტროლო წერტილის მტკიცებულების საზღვარი

### English

This is the slide where the README is too strong. `README.md` says any edit, deletion, insertion, or
reorder is caught by Verify integrity. `AuditLogReader.VerifyIntegrityAsync` only re-walks the rows
it loaded. A missing tail has no gap to see, and a fully rehashed rewrite still links. Do not read
that README sentence aloud.

Say instead: "Verify answers a question about the rows that are here. A signed checkpoint answers a
question about one past head, and only if something compares them. Today's button does not. The
offline verifier does, for the bundle in its hand. A bundle an auditor already stored still
disagrees with a later rewrite. That is why a retained export matters, and it is also why I will
not say every deletion is detectable whatever the latest checkpoint is."

Then the three planned items, with the word planned. They are the Area 6b specification on the
unmerged branch: anchor Verify to checkpoints signed by the current key, commit
`AuditCheckpointCreated` with the checkpoint, and stream the export after the export audit row and
a fresh step-up. Today's `AuditController.Export` has no step-up, and `CreateCheckpointAsync` saves
the checkpoint and writes the audit row afterwards.

Keep the old-verifier note. A current verifier accepts bundle formats `eclipsvault.audit-bundle/1`
and `/2`, and refuses a newer format without saying the row was edited. The 1.0.0 verifier, which
predates that refusal, reports `INVALID — "the row was edited"` for a later bundle. That report is
the old tool, not proof of tampering.

Do not click Verify and then say "intact" as if it included the checkpoint. If you do click it, read
the message as "these present rows rehash", and point at the latest checkpoint line as a separate
fact.

### ქართული

ეს ის სლაიდია, სადაც README ზედმეტად ძლიერია. `README.md` ამბობს, რომ ნებისმიერ შეცვლას, წაშლას,
ჩასმას ან გადალაგებას Verify integrity იჭერს. `AuditLogReader.VerifyIntegrityAsync` მხოლოდ იმ
ჩანაწერებს გადის, რომლებიც ჩატვირთა. აკლული კუდი რიგითობის წყვეტას არ ტოვებს, და სრულად თავიდან დაჰეშილი
გადაწერა მაინც იკავშირება. README-ს ეს წინადადება ხმამაღლა ნუ წაიკითხოთ.

სანაცვლოდ თქვით: „Verify პასუხობს კითხვას იმ ჩანაწერებზე, რომლებიც აქ არის. ხელმოწერილი საკონტროლო
წერტილი პასუხობს კითხვას ერთ წარსულ თავზე, და მხოლოდ მაშინ, თუ რამე მათ ადარებს. დღევანდელი ღილაკი
არ ადარებს. ხაზგარეშე შემმოწმებელი ადარებს, იმ შეკვრისთვის, რომელიც ხელში აქვს. აუდიტორის მიერ უკვე
შენახული შეკვრა გვიან გადაწერას მაინც არ დაემთხვევა. ამიტომ შენახული ექსპორტი მნიშვნელოვანია, და
ამიტომაც არ ვიტყვი, რომ ყოველი წაშლა აღმოჩენადია, როგორიც უნდა იყოს ბოლო საკონტროლო წერტილი.“

შემდეგ სამი დაგეგმილი პუნქტი, სიტყვით „დაგეგმილი“. ეს არის Area 6b-ის სპეციფიკაცია შეურწყმელ
ტოტზე: Verify მიემაგროს მიმდინარე გასაღებით ხელმოწერილ საკონტროლო წერტილებს, `AuditCheckpointCreated`
საკონტროლო წერტილთან ერთად დაფიქსირდეს, და ექსპორტი ნაკადად წავიდეს ექსპორტის აუდიტის ჩანაწერისა და
ახალი გაძლიერებული დადასტურების შემდეგ. დღევანდელ `AuditController.Export`-ს გაძლიერებული
დადასტურება არ აქვს, და `CreateCheckpointAsync` ჯერ საკონტროლო წერტილს ინახავს და აუდიტის ჩანაწერს
შემდეგ წერს.

დაიცავით ძველი შემმოწმებლის შენიშვნა. მიმდინარე შემმოწმებელი იღებს შეკვრის ფორმატებს
`eclipsvault.audit-bundle/1` და `/2`, და უფრო ახალ ფორმატს უარყოფს ისე, რომ ჩანაწერს შეცვლილს არ
ეძახის. 1.0.0 შემმოწმებელი, რომელიც ამ უარზე ადრე გამოვიდა, უფრო ახალ შეკვრაზე აჩვენებს
`INVALID — "the row was edited"`. ეს ანგარიში ძველი ხელსაწყოა, არა ხელყოფის მტკიცებულება.

Verify-ზე დაწკაპუნების შემდეგ ნუ იტყვით „intact“ ისე, თითქოს საკონტროლო წერტილიც შედიოდეს. თუ მაინც
დააწკაპუნებთ, შეტყობინება წაიკითხეთ როგორც „ეს არსებული ჩანაწერები თავიდან დაიჰეშა“ და ბოლო
საკონტროლო წერტილის ხაზს ცალკე ფაქტად მიუთითეთ.

## Slide 7 — Final revision and test evidence / საბოლოო რევიზია და ტესტის მტკიცებულება

### English

Read only filled cells. For an empty one, say "not recorded yet".

You can say that PR #147's GitHub checks were green when this draft was written, and that the pull
request was still open. You cannot say a reviewer approved it, because the review list read for this
draft was empty. You cannot say this talk's own stack passed a test.

If the owner later freezes a revision, replace the "not frozen" cell and say the new hash. Do not
leave the drafting hash up if the running vault is a different commit.

### ქართული

მხოლოდ შევსებული უჯრები წაიკითხეთ. ცარიელზე თქვით „ჯერ ჩაწერილი არ არის“.

შეგიძლიათ თქვათ, რომ PR #147-ის GitHub შემოწმებები მწვანე იყო, როცა ეს მონახაზი დაიწერა, და რომ
pull request ჯერ ღია იყო. ვერ იტყვით, რომ განმხილველმა დაუმტკიცა, რადგან ამ მონახაზისთვის წაკითხული
განხილვების სია ცარიელი იყო. ვერ იტყვით, რომ ამ მოხსენების საკუთარმა სტეკმა ტესტი გაიარა.

თუ მფლობელი მოგვიანებით რევიზიას გაყინავს, „გაყინული არ არის“ უჯრა შეცვალეთ და ახალი ჰეში თქვით.
მონახაზის ჰეში ნუ დატოვოთ, თუ გაშვებული საცავი სხვა commit-ია.

## Slide 8 — Lessons and limitations / დასკვნები და საზღვრები

### English

Three lessons, then stop.

"The interesting part is not that a person can open a password. It is that the server checked
clearance, project, network, and time, and that the reveal row exists before the plaintext does."

"The audit trail is as strong as the check you actually run. A hash walk, a signed checkpoint, and
a bundle held by someone else are three different checks."

"What you saw was the Development demo world: synthetic accounts, one synthetic secret, no claim
about production."

Invite questions. The prepared answers are in [questions.md](questions.md). If a question crosses
into the planned Audit log work, say it is specified and not merged.

### ქართული

სამი დასკვნა, შემდეგ გაჩერდით.

„საინტერესო ის არ არის, რომ ადამიანმა პაროლი გახსნა. საინტერესოა, რომ სერვერმა შეამოწმა დაშვება,
პროექტი, ქსელი და დრო, და რომ გახსნის ჩანაწერი არსებობს მანამ, სანამ ღია ტექსტი იარსებებს.“

„აუდიტის კვალი იმდენად ძლიერია, რამდენადაც ის შემოწმება, რომელსაც ნამდვილად უშვებთ. ჰეშის გავლა,
ხელმოწერილი საკონტროლო წერტილი და სხვის მიერ შენახული შეკვრა სამი განსხვავებული შემოწმებაა.“

„რაც ნახეთ, Development-ის სადემონსტრაციო სამყარო იყო: სინთეზური ანგარიშები, ერთი სინთეზური საიდუმლო, და
არანაირი მტკიცება production-ზე.“

კითხვები მიიღეთ. მომზადებული პასუხები [questions.md](questions.md)-შია. თუ კითხვა დაგეგმილ აუდიტის
ჟურნალში გადადის, თქვით, რომ ის სპეციფიცირებულია და შერწყმული არ არის.
