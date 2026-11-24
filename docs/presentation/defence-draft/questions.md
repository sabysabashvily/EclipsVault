# Likely questions / სავარაუდო კითხვები

Use the sources named below for `origin/main` `1298958050e45f83aefeb4b6455d0a02169ab22a`
(24 November 2026, 15:18 Asia/Tbilisi). Say when a feature was outside the rehearsal.

დაეყრდენით ქვემოთ მითითებულ წყაროებს `origin/main`-ის რევიზიიდან
`1298958050e45f83aefeb4b6455d0a02169ab22a` (2026 წლის 24 ნოემბერი, 15:18 Asia/Tbilisi).
თუ რომელიმე ფუნქცია რეპეტიციაზე არ შემოწმებულა, ეს პირდაპირ თქვით.

## The browser decides who can see a secret, doesn't it?

### English

No. Angular route guards are convenience. `SecretAccessPolicy` runs on the server for the UI API
reveal and for the Public API. ADR-0012 says the UI does not decide from a hard-coded clearance
check. A hidden link is not the control.

### ქართული

არა. Angular-ის მარშრუტების შემოწმებები მხოლოდ ნავიგაციას ამარტივებს. სერვერი წვდომას
`SecretAccessPolicy`-ით ამოწმებს როგორც UI API-ით მნიშვნელობის გამჟღავნებისას, ისე საჯარო API-ზე
მოთხოვნისას. ADR-0012-ის მიხედვით, ინტერფეისი წვდომაზე გადაწყვეტილებას კოდში ფიქსირებული დაშვების
შემოწმებით არ იღებს. ბმულის დამალვა წვდომას ვერ აკონტროლებს.

## Why does this secret not show the network and time checks?

### English

`Phoenix_Dev_Database_Password` is Internal and Development. `SecretAccessPolicy` requires a trusted
network for Confidential and above, and applies the access window only to Production. A Standard
member of `PHOENIX` can therefore run this demonstration at any time. `appsettings.json` trusts both
loopback addresses, `127.0.0.1/32` and `::1/128`.

### ქართული

`Phoenix_Dev_Database_Password`-ის მგრძნობელობის დონეა Internal, გარემო კი Development.
`SecretAccessPolicy` სანდო ქსელიდან მოთხოვნას Confidential და უფრო მაღალი დონის საიდუმლოებისთვის
მოითხოვს. წვდომის დროითი ფანჯარა მხოლოდ Production გარემოზე ვრცელდება. ამიტომ `PHOENIX`-ის
წევრს Standard დაშვებით დემონსტრაციის ჩატარება ნებისმიერ დროს შეუძლია. `appsettings.json`-ში
ორივე loopback მისამართი სანდოდაა მითითებული: `127.0.0.1/32` და `::1/128`.

## Where is the service account in the demo?

### English

In the explanation, not on screen. A service account has no session and calls `/api/v1/secrets`
with an API key. `ApiKeyAuthenticationHandler` builds the same kind of subject, and
`SecretsApiController` uses the same authorisation policy. Issuing a key shows the raw secret once.
That does not belong in a recording. Source: `CONTEXT.md`; ADR-0005; `SecretsApiController`.

### ქართული

სერვისის ანგარიშს მხოლოდ სიტყვიერად ვახსენებთ. მას სესია არ აქვს და `/api/v1/secrets`-ს API
გასაღებით მიმართავს. `ApiKeyAuthenticationHandler` იმავე ტიპის სუბიექტს ქმნის და
`SecretsApiController` ავტორიზაციის იმავე პოლიტიკას იყენებს. გაცემისას საცავი API გასაღების
სრულ მნიშვნელობას ერთხელ აჩვენებს. ეს მომენტი ვიდეოში არ უნდა მოხვდეს.
წყაროებია `CONTEXT.md`, ADR-0005 და `SecretsApiController`.

## Does the server ever see the plaintext?

### English

Yes. This is a server-side vault, not a design where the browser alone can decrypt. After the audit
row commits, `AesGcmCryptoEngine.UnsealAsync` opens the value in the Web process, and the Angular
details page renders it once. The data key is wrapped by the master key so a database copy of the
ciphertext is not the plaintext. It is not a claim that the operator's process cannot read a secret
it is serving.

### ქართული

დიახ. აუდიტის ჩანაწერის დაფიქსირების შემდეგ `AesGcmCryptoEngine.UnsealAsync` მნიშვნელობის
გაშიფვრას სერვერზე გაშვებულ Web პროცესში ასრულებს.
Angular-ის დეტალების გვერდი გაშიფრულ მნიშვნელობას ერთხელ აჩვენებს. მონაცემთა გასაღები
მასტერ-გასაღებითაა შეფუთული, მონაცემთა ბაზაში კი დაშიფრული ტექსტი ინახება. ოპერატორის პროცესს
შეუძლია წაიკითხოს საიდუმლო, რომელსაც თავად აწვდის.

## What if the audit write fails?

### English

`AuditSink.WriteAsync` throws `AuditWriteFailedException` and does not report success.
`RevealAsync` awaits that call before `DecryptToDtoAsync`. The value is not decrypted for that
request. Source: `AuditSink`; `SecretService.RevealAsync`. This draft did not pull the database
plug to show it.

### ქართული

`AuditSink.WriteAsync` იწვევს `AuditWriteFailedException` გამონაკლისს და წარმატებას არ აბრუნებს.
`RevealAsync` `DecryptToDtoAsync`-ის გამოძახებამდე აუდიტის ჩაწერის დასრულებას ელოდება. ასეთ
მოთხოვნაზე მნიშვნელობის გაშიფვრა არ ხდება. წყაროებია `AuditSink` და `SecretService.RevealAsync`.
რეპეტიციაზე ამის საჩვენებლად მონაცემთა ბაზა არ გათიშულა.

## Why are Viewed and Revealed separate audit rows?

### English

After the access check allows the details page, `SecretDetailsController.Get` calls
`RecordViewedAsync`, which writes `SecretMetadataViewed`. When someone requests the value,
`RevealAsync` commits `SecretRevealed` before decryption. The audit log labels them `Viewed` and
`Revealed`. If policy refuses access, `OpenAsync` records `SecretAccessDenied`, shown as
`Access denied`. See `SecretService`, `AuditDisplay`, and ADR-0031.

### ქართული

დეტალების ნახვის ნებართვის მიღების შემდეგ `SecretDetailsController.Get` იძახებს `RecordViewedAsync`-ს,
რომელიც `SecretMetadataViewed`-ს წერს. მნიშვნელობის მოთხოვნისას `RevealAsync` გაშიფვრამდე
აფიქსირებს `SecretRevealed`-ს. აუდიტის ჟურნალში ეს ჩანაწერები, შესაბამისად, `Viewed` და `Revealed`
სახელებით ჩანს. თუ წვდომის პოლიტიკა მოთხოვნას უარყოფს, `OpenAsync` აფიქსირებს
`SecretAccessDenied`-ს. ჟურნალში მას `Access denied` ეწოდება.
იხილეთ `SecretService`, `AuditDisplay` და ADR-0031.

## Does Verify prove nobody deleted or rewrote the trail?

### English

No. Not for every deletion, and not regardless of the latest signed checkpoint.
`AuditChainVerifier.VerifyAsync` streams the rows and checks checkpoints signed by the current key.
It reports a gap, a broken link, a checkpoint that does not match, or a tail missing before the
latest such checkpoint. Deleting the checkpoints, or changing only the rows after that checkpoint
and recomputing their hashes, can still leave Verify quiet. `AuditBundleVerifier` checks a bundle
it is given. A bundle an auditor already keeps is unchanged by a later edit of the live database.
Source: `AuditChainVerifier`; `AuditCheckpointService.VerifyAsync`; `AuditBundleVerifier`;
ADR-0030; `docs/THREAT_MODEL.md` item 12.

### ქართული

არა. ყველა წაშლის გამოვლენა შეუძლებელია. შემოწმების ფარგლებს ბოლო ხელმოწერილი საკონტროლო წერტილი
განსაზღვრავს. `AuditChainVerifier.VerifyAsync` ჩანაწერებს ნაკადად კითხულობს და მოქმედი გასაღებით
ხელმოწერილ საკონტროლო წერტილებს ამოწმებს. ის მიუთითებს მიმდევრობაში გამოტოვებულ ჩანაწერზე,
ჯაჭვის გაწყვეტილ ბმაზე, საკონტროლო წერტილის შეუსაბამობაზე ან ბოლო ასეთი წერტილით მოცულ მონაკვეთში
ბოლო ჩანაწერების დანაკლისზე. Verify-მ დარღვევა შეიძლება ვერ აღმოაჩინოს, თუ საკონტროლო წერტილები
წაიშალა ან მხოლოდ ბოლო წერტილის შემდეგ ჩანაწერები შეიცვალა და მათი ჰეშები ხელახლა გამოითვალა.
`AuditBundleVerifier` მისთვის გადაცემულ პაკეტს ამოწმებს. მოქმედ მონაცემთა ბაზაში მოგვიანებით შეტანილი
ცვლილება აუდიტორთან უკვე შენახულ პაკეტს ვერ შეცვლის. წყაროებია `AuditChainVerifier`,
`AuditCheckpointService.VerifyAsync`, `AuditBundleVerifier`, ADR-0030 და `docs/THREAT_MODEL.md`-ის
მე-12 პუნქტი.

## What about an old verifier?

### English

The current verifier reads `eclipsvault.audit-bundle/1` and `/2`. An unknown format gets a version
error. The repository README records that verifier 1.0.0 predates that check and can report
`INVALID — "the row was edited"` for an intact bundle in a later format. Re-run with a verifier at
least as new as the vault that exported the bundle. The rehearsal used the README for the 1.0.0
behaviour. See `AuditBundleSchema` and `AuditBundleVerifier` for the current check.

### ქართული

მიმდინარე შემმოწმებელი `eclipsvault.audit-bundle/1` და `/2` ფორმატებს კითხულობს. უცნობ ფორმატზე
ის ვერსიის შესახებ შეცდომას აბრუნებს. რეპოზიტორიის README-ის მიხედვით, 1.0.0 ვერსია ამ
შემოწმების დამატებამდე გამოვიდა. ამიტომ უფრო ახალი ფორმატის უცვლელ პაკეტზეც შეიძლება აჩვენოს
`INVALID — "the row was edited"`. შემოწმება გაიმეორეთ იმავე ან უფრო ახალი ვერსიის შემმოწმებლით,
ვიდრე საცავია, რომელმაც პაკეტი გაიტანა. რეპეტიციაზე 1.0.0-ის ქცევის აღწერა README-ს ეყრდნობოდა.
მიმდინარე შემოწმებისთვის იხილეთ `AuditBundleSchema` და `AuditBundleVerifier`.

## Someone with the host can re-sign, can't they?

### English

Yes, if they can read `ECLIPSVAULT_AUDIT_SIGNING_KEY`. `docs/THREAT_MODEL.md` item 12 says a rewrite
re-signed with the vault's own key still verifies, including against a pinned copy of that key.
Pinning catches a different key, not this one. The mitigation recorded there is to export on a
schedule and keep the bundles somewhere the host cannot rewrite. This draft did not do that export.

### ქართული

დიახ, თუ `ECLIPSVAULT_AUDIT_SIGNING_KEY`-ის წაკითხვა შეუძლიათ. `docs/THREAT_MODEL.md`-ის მე-12
პუნქტის მიხედვით, გადაწერილი ჩანაწერები შემოწმებას მაინც გაივლის, თუ ისინი საცავისავე გასაღებითაა
ხელახლა ხელმოწერილი. ეს იმ შემთხვევაშიც ასეა, როცა შემმოწმებელი ამ გასაღების წინასწარ შენახულ
ასლს იყენებს. ასეთი შედარება მხოლოდ სხვა გასაღებით ხელმოწერას გამოავლენს. რისკის შესამცირებლად
იქვე აღწერილია პერიოდული ექსპორტი და პაკეტების შენახვა იქ, სადაც ჰოსტს მათი გადაწერა არ შეუძლია.
რეპეტიციაზე ეს ექსპორტი არ ჩატარებულა.

## Where is the dynamic password stored?

### English

The vault returns the password once, at issue, and keeps no copy. `DynamicSecretLease` records the
backend login's identity, expiry, and status. `DynamicLeaseWorker` asks `DynamicSecretService` to
revoke due credentials. If the backend refuses, the service records `RevocationFailed`.

Development's `DbSeeder` adds `phoenix_db_reader` and `global_db_writer` to an empty roles table for
SQL Server, and adds `phoenix_pg_reader` and `global_pg_writer` for PostgreSQL when those names are
missing. Issuance needs a configured target for the role's backend and the dynamic-secrets licence
check. The rehearsal skipped issuance.

### ქართული

საცავი პაროლს მხოლოდ გაცემისას აბრუნებს და ასლს არ ინახავს. `DynamicSecretLease`-ში აღირიცხება
სამიზნე ბაზის ანგარიშის იდენტიფიკატორი, ვადა და სტატუსი. ვადის გასვლისას `DynamicLeaseWorker`
`DynamicSecretService`-ს ანგარიშის გაუქმებას სთხოვს. თუ სამიზნე ბაზა გაუქმებაზე უარს აბრუნებს,
სერვისი `RevocationFailed` სტატუსს აფიქსირებს.

Development გარემოში `DbSeeder` SQL Server-ის როლებს, `phoenix_db_reader`-სა და
`global_db_writer`-ს, როლების ცარიელ ცხრილში ამატებს. PostgreSQL-ისთვის `phoenix_pg_reader`-სა და
`global_pg_writer`-ს ამატებს, თუ შესაბამისი სახელები ცხრილში ჯერ არ არის. დინამიკური საიდუმლოს
გასაცემად უნდა იყოს გამართული როლის სამიზნე ბაზასთან კავშირი და წარმატებით დასრულდეს ლიცენზიის
შემოწმება. რეპეტიციაზე დინამიკური საიდუმლო არ გაცემულა.

## Are Core and Infrastructure separate services? Is Node running in production?

### English

Core and Infrastructure are components inside the Web application. In C4, the Angular UI is a
container that runs in the browser, including sign-in. The Dockerfile's Node stage builds the
Angular files. The runtime image starts `EclipsVault.Web.dll`, which serves those files and writes
the access-blocked, error, and not-found pages on the server. See [diagrams.md](diagrams.md)
sections 2 and 3, the `Dockerfile`, ADR-0008, and ADR-0034.

### ქართული

Core და Infrastructure Web აპლიკაციის შიდა კომპონენტებია. C4-ში Angular UI კონტეინერია, რომელიც
ბრაუზერში მუშაობს, შესვლის გვერდების ჩათვლით. Dockerfile-ში Node-ის ეტაპზე აიგება Angular-ის
ფაილები. გასაშვები იმიჯი უშვებს `EclipsVault.Web.dll`-ს, რომელიც ამ ფაილებს ბრაუზერს აწვდის და
სერვერზე ქმნის სამ გვერდს: „წვდომა დაბლოკილია“, „შეცდომა“ და „ვერ მოიძებნა“.
იხილეთ [diagrams.md](diagrams.md)-ის მე-2 და მე-3 ნაწილები, `Dockerfile`, ADR-0008 და ADR-0034.

## How does a new vault account get its first credentials?

### English

An administrator enters the person's email address. The vault emails a single-use invitation
using `Identity:PublicUrl` and SMTP. The person chooses a password and enrols an authenticator in
one audited activation. The seeded Development accounts enrol at first sign-in. The rehearsal
used those accounts. See ADR-0033, `UserAdminService`, and `AccountActivationService`.

### ქართული

ადმინისტრატორს შეჰყავს მომხმარებლის ელფოსტის მისამართი. საცავი `Identity:PublicUrl`-ის მიხედვით
ადგენს ერთჯერადი მოწვევის ბმულს და SMTP-ით აგზავნის. მომხმარებელი ანგარიშის გააქტიურებისას
ირჩევს პაროლს და არეგისტრირებს ავთენტიფიკატორს. ეს ერთი აქტივაციაა და აუდიტში აღირიცხება.
Development გარემოში წინასწარ შექმნილი ანგარიშებისთვის ავთენტიფიკატორის რეგისტრაცია პირველი
შესვლისას ხდება. რეპეტიციაზე სწორედ ეს ანგარიშებია გამოყენებული. იხილეთ ADR-0033, `UserAdminService` და
`AccountActivationService`.

## Is this the production deployment?

### English

The rehearsal used a disposable Development vault with two seeded staff accounts and sample
secrets. In Production, `DbSeeder` creates `vault-admin` on an empty vault using the operator's
bootstrap password. Startup requires `Database:Provider` set to `Postgres` or `SqlServer`.

The [reference deployment](../../../deploy/production/) runs the vault image behind Caddy for
HTTPS on one Docker host, with PostgreSQL and migrations enabled at startup. The
[production runbook](../../INSTALL.md) also describes applying migrations from a separate deploy
job. The rehearsal used the Development setup in [demo-setup-reset.md](demo-setup-reset.md).

### ქართული

რეპეტიცია ჩატარდა ერთჯერად Development საცავში, რომელშიც წინასწარ იყო შექმნილი თანამშრომელთა
ორი ანგარიში და სატესტო საიდუმლოები. Production გარემოში, თუ საცავი ცარიელია, `DbSeeder`
ოპერატორის მიერ მითითებული საწყისი პაროლით ქმნის `vault-admin` ანგარიშს. გასაშვებად
`Database:Provider`-ის მნიშვნელობა უნდა იყოს `Postgres` ან `SqlServer`.

[განთავსების ნიმუშში](../../../deploy/production/) საცავის იმიჯი, HTTPS-ისთვის განკუთვნილი Caddy
და PostgreSQL ერთ Docker ჰოსტზე მუშაობს. Caddy მოთხოვნებს საცავს გადასცემს. მიგრაციები გაშვებისას
სრულდება. [Production-ში დაყენების ინსტრუქცია](../../INSTALL.md) ასევე აღწერს, როგორ უნდა
შესრულდეს მიგრაციები განთავსების ცალკე დავალებით. რეპეტიციის Development გარემო აღწერილია
[demo-setup-reset.md](demo-setup-reset.md)-ში.

## Did the encryption-keys work merge?

### English

Yes. The administrator's Angular page `/Encryption` reads key status from
`GET /api/ui/encryption`. `KekRotationService` rewraps data keys for secrets and archived versions
under the current master key in resumable batches. Secret ciphertext stays intact. With Vault
Transit, the page shows the Transit key versions reported by the engine, which asks Transit to
rewrap the data keys. The rehearsal did not rotate a key. See `EncryptionController` and
[ADR-0029](../../adr/0029-master-key-rotation-runs-in-resumable-batches.md).

### ქართული

დიახ. ადმინისტრატორის Angular გვერდი `/Encryption` გასაღებების სტატუსს `GET /api/ui/encryption`
მოთხოვნით იღებს. `KekRotationService` საიდუმლოებისა და მათი დაარქივებული ვერსიების მონაცემთა
გასაღებებს მოქმედი მასტერ-გასაღებით ხელახლა ფუთავს. როტაცია ჯგუფებად სრულდება და შეწყვეტის
შემდეგ შეიძლება გაგრძელდეს. საიდუმლოს დაშიფრული ტექსტი უცვლელი რჩება. Vault Transit-ის გამოყენებისას
გვერდი აჩვენებს Transit-ის გასაღების იმ ვერსიებს, რომლებსაც ძრავა აბრუნებს. მონაცემთა გასაღებების
ხელახლა შეფუთვას ძრავა Transit-ს სთხოვს. რეპეტიციაზე გასაღების როტაცია არ ჩატარებულა.
იხილეთ `EncryptionController` და
[ADR-0029](../../adr/0029-master-key-rotation-runs-in-resumable-batches.md).

## How long is the talk, and what is the marking scheme?

### English

The owner confirmed a presentation cap of 30 minutes. Detailed slide allocation and marking
criteria are still unconfirmed. The supplied C4 guide sets diagram requirements, not talk timing.

### ქართული

მფლობელმა დაადასტურა, რომ პრეზენტაცია მაქსიმუმ 30 წუთს უნდა გაგრძელდეს. სლაიდებს შორის დროის
განაწილება და შეფასების კრიტერიუმები ჯერ დაუდასტურებელია. მოწოდებული C4 გზამკვლევი დიაგრამების
მოთხოვნებს განსაზღვრავს. პრეზენტაციის ხანგრძლივობას ის არ ადგენს.
