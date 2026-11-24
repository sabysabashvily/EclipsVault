# Likely questions / სავარაუდო კითხვები

Use the sources named below for `origin/main` `1298958050e45f83aefeb4b6455d0a02169ab22a`
(24 November 2026, 15:18 Asia/Tbilisi). Say when a feature was outside the rehearsal.

დაეყრდენით ქვემოთ მითითებულ წყაროებს `origin/main`-ის რევიზიაზე
`1298958050e45f83aefeb4b6455d0a02169ab22a` (24 ნოემბერი 2026, 15:18 Asia/Tbilisi).
თუ რომელიმე ფუნქცია რეპეტიციაში არ გამოგიყენებიათ, ეს პირდაპირ თქვით.

## The browser decides who can see a secret, doesn't it?

### English

No. Angular route guards are convenience. `SecretAccessPolicy` runs on the server for the UI API
reveal and for the Public API. ADR-0012 says the UI does not decide from a hard-coded clearance
check. A hidden link is not the control.

### ქართული

არა. Angular-ის მარშრუტის მცველები მოხერხებულობაა. `SecretAccessPolicy` სერვერზე ეშვება UI API-ს
გახსნისთვისაც და საჯარო API-სთვისაც. ADR-0012 ამბობს, რომ ინტერფეისი დაშვებას ჩაშენებული
შემოწმებით არ წყვეტს. დამალული ბმული კონტროლი არ არის.

## Why does this secret not show the network and time checks?

### English

`Phoenix_Dev_Database_Password` is Internal and Development. `SecretAccessPolicy` requires a trusted
network for Confidential and above, and applies the access window only to Production. A Standard
member of `PHOENIX` can therefore run this demonstration at any time. `appsettings.json` trusts both
loopback addresses, `127.0.0.1/32` and `::1/128`.

### ქართული

`Phoenix_Dev_Database_Password`-ის მგრძნობელობაა Internal, გარემო კი Development.
`SecretAccessPolicy` სანდო ქსელს Confidential და უფრო მაღალი მგრძნობელობისთვის მოითხოვს,
დროის შეზღუდვას კი მხოლოდ Production გარემოში იყენებს. ამიტომ `PHOENIX`-ის წევრს Standard დაშვებით
დემონსტრაციის ჩატარება ნებისმიერ დროს შეუძლია. `appsettings.json` ორივე ლოკალურ loopback
მისამართს ენდობა: `127.0.0.1/32` და `::1/128`.

## Where is the service account in the demo?

### English

In the explanation, not on screen. A service account has no session and calls `/api/v1/secrets`
with an API key. `ApiKeyAuthenticationHandler` builds the same kind of subject, and
`SecretsApiController` uses the same authorisation policy. Issuing a key shows the raw secret once.
That does not belong in a recording. Source: `CONTEXT.md`; ADR-0005; `SecretsApiController`.

### ქართული

ახსნაში, არა ეკრანზე. სერვისის ანგარიშს სესია არ აქვს და `/api/v1/secrets`-ს API გასაღებით იძახებს.
`ApiKeyAuthenticationHandler` იმავე სახის სუბიექტს აწყობს და `SecretsApiController` იმავე
ავტორიზაციის წესს იყენებს. გასაღების გაცემა ნედლ საიდუმლოს ერთხელ აჩვენებს. ეს ჩანაწერში არ
ეკუთვნის. წყარო: `CONTEXT.md`; ADR-0005; `SecretsApiController`.

## Does the server ever see the plaintext?

### English

Yes. This is a server-side vault, not a design where the browser alone can decrypt. After the audit
row commits, `AesGcmCryptoEngine.UnsealAsync` opens the value in the Web process, and the Angular
details page renders it once. The data key is wrapped by the master key so a database copy of the
ciphertext is not the plaintext. It is not a claim that the operator's process cannot read a secret
it is serving.

### ქართული

დიახ. ეს სერვერზე მდგომი საცავია, არა ისეთი დიზაინი, სადაც მხოლოდ ბრაუზერს შეუძლია გაშიფვრა.
აუდიტის ჩანაწერის დაფიქსირების შემდეგ `AesGcmCryptoEngine.UnsealAsync` მნიშვნელობას Web პროცესში
ხსნის და Angular-ის დეტალების გვერდი მას ერთხელ აჩვენებს. მონაცემთა გასაღები მასტერ-გასაღებითაა
შეხვეული, ამიტომ ბაზაში მყოფი შიფრული ტექსტი ღია ტექსტი არ არის. ეს არ არის მტკიცება, რომ
ოპერატორის პროცესი ვერ კითხულობს საიდუმლოს, რომელსაც თავად აწვდის.

## What if the audit write fails?

### English

`AuditSink.WriteAsync` throws `AuditWriteFailedException` and does not report success.
`RevealAsync` awaits that call before `DecryptToDtoAsync`. The value is not decrypted for that
request. Source: `AuditSink`; `SecretService.RevealAsync`. This draft did not pull the database
plug to show it.

### ქართული

`AuditSink.WriteAsync` ისვრის `AuditWriteFailedException`-ს და წარმატებას არ აჩვენებს. `RevealAsync`
ამ გამოძახებას `DecryptToDtoAsync`-მდე ელოდება. ამ მოთხოვნისთვის მნიშვნელობა არ იშიფრება. წყარო:
`AuditSink`; `SecretService.RevealAsync`. ამ მონახაზმა ბაზის გამორთვა ამის საჩვენებლად არ სცადა.

## Why are Viewed and Revealed separate audit rows?

### English

After the access check allows the details page, `SecretDetailsController.Get` calls
`RecordViewedAsync`, which writes `SecretMetadataViewed`. When someone requests the value,
`RevealAsync` commits `SecretRevealed` before decryption. The audit log labels them `Viewed` and
`Revealed`. If policy refuses access, `OpenAsync` records `SecretAccessDenied`, shown as
`Access denied`. See `SecretService`, `AuditDisplay`, and ADR-0031.

### ქართული

თუ წვდომის შემოწმება დეტალების ნახვის უფლებას იძლევა, `SecretDetailsController.Get` იძახებს
`RecordViewedAsync`-ს, რომელიც `SecretMetadataViewed`-ს წერს. მნიშვნელობის მოთხოვნისას
`RevealAsync` გაშიფვრამდე აფიქსირებს `SecretRevealed`-ს. აუდიტის ჟურნალში ისინი `Viewed` და
`Revealed` სახელებით ჩანს. თუ წვდომის წესი მოთხოვნას უარყოფს, `OpenAsync` აფიქსირებს
`SecretAccessDenied`-ს, რომელიც ჟურნალში `Access denied` სახელით ჩანს.
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

არა. არა ყოველი წაშლისთვის და არა ბოლო ხელმოწერილი საკონტროლო წერტილისგან დამოუკიდებლად.
`AuditChainVerifier.VerifyAsync` ჩანაწერებს ნაკადად გადის და ამოწმებს მიმდინარე გასაღებით
ხელმოწერილ საკონტროლო წერტილებს. აჩვენებს რიგითობის წყვეტას, გაწყვეტილ კავშირს, წერტილს რომელიც არ
ემთხვევა, ან კუდს რომელიც ბოლო ასეთ წერტილამდე აკლია. საკონტროლო წერტილების წაშლამ, ან მხოლოდ იმ
წერტილის შემდეგ ჩანაწერების შეცვლამ და მათი ჰეშების თავიდან დათვლამ, შეიძლება მაინც დატოვოს ჩუმი
Verify. `AuditBundleVerifier` ამოწმებს მისთვის მიცემულ შეკვრას. აუდიტორის მიერ უკვე შენახულ შეკვრას
ცოცხალი ბაზის გვიანი შესწორება არ ცვლის. წყარო: `AuditChainVerifier`;
`AuditCheckpointService.VerifyAsync`; `AuditBundleVerifier`; ADR-0030; `docs/THREAT_MODEL.md`
პუნქტი 12.

## What about an old verifier?

### English

The current verifier reads `eclipsvault.audit-bundle/1` and `/2`. An unknown format gets a version
error. The repository README records that verifier 1.0.0 predates that check and can report
`INVALID — "the row was edited"` for an intact bundle in a later format. Re-run with a verifier at
least as new as the vault that exported the bundle. The rehearsal used the README for the 1.0.0
behaviour. See `AuditBundleSchema` and `AuditBundleVerifier` for the current check.

### ქართული

მიმდინარე შემმოწმებელი კითხულობს `eclipsvault.audit-bundle/1` და `/2` ფორმატებს. უცნობი ფორმატის
შემთხვევაში ის ვერსიის შეცდომას აბრუნებს. რეპოზიტორიის README-ში წერია, რომ 1.0.0 ვერსია ამ
შემოწმებამდე გამოვიდა და უფრო ახალ ფორმატში შენახულ უცვლელ შეკვრაზეც შეიძლება აჩვენოს
`INVALID — "the row was edited"`. ხელახლა გაუშვით შემმოწმებელი, რომლის ვერსიაც შეკვრის
გამომტანი საცავის ვერსიის ტოლი ან უფრო ახალია. რეპეტიციისთვის 1.0.0-ის ქცევის წყარო README იყო.
მიმდინარე შემოწმებისთვის იხილეთ `AuditBundleSchema` და `AuditBundleVerifier`.

## Someone with the host can re-sign, can't they?

### English

Yes, if they can read `ECLIPSVAULT_AUDIT_SIGNING_KEY`. `docs/THREAT_MODEL.md` item 12 says a rewrite
re-signed with the vault's own key still verifies, including against a pinned copy of that key.
Pinning catches a different key, not this one. The mitigation recorded there is to export on a
schedule and keep the bundles somewhere the host cannot rewrite. This draft did not do that export.

### ქართული

დიახ, თუ მათ `ECLIPSVAULT_AUDIT_SIGNING_KEY`-ის წაკითხვა შეუძლიათ. `docs/THREAT_MODEL.md` პუნქტი 12
ამბობს, რომ საცავის საკუთარი გასაღებით თავიდან ხელმოწერილი გადაწერა მაინც მოწმდება, იმ გასაღების
ჩამაგრებული ასლის წინააღმდეგაც. ჩამაგრება სხვა გასაღებს იჭერს, არა ამას. იქ ჩაწერილი შერბილებაა
განრიგით ექსპორტი და შეკვრების შენახვა იქ, სადაც ჰოსტი ვერ გადაწერს. ამ მონახაზმა ეს ექსპორტი არ
გააკეთა.

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

საცავი პაროლს ერთხელ აბრუნებს, გაცემისას, და მის ასლს არ ინახავს. `DynamicSecretLease` ინახავს
სამიზნე ბაზაში შექმნილი ანგარიშის სახელს, ვადასა და სტატუსს. ვადის გასვლისას `DynamicLeaseWorker`
`DynamicSecretService`-ს ანგარიშის გაუქმებას სთხოვს. თუ სამიზნე ბაზა გაუქმებას ვერ ასრულებს,
სერვისი `RevocationFailed` სტატუსს აფიქსირებს.

Development რეჟიმში `DbSeeder` როლების ცარიელ ცხრილში SQL Server-ისთვის ამატებს
`phoenix_db_reader`-სა და `global_db_writer`-ს. PostgreSQL-ისთვის ამატებს `phoenix_pg_reader`-სა და
`global_pg_writer`-ს, თუ შესაბამისი სახელები ცხრილში ჯერ არ არის. გასაცემად საჭიროა როლის
სამიზნე ბაზასთან კავშირის გამართვა და დინამიკური საიდუმლოების ლიცენზიის შემოწმების გავლა.
რეპეტიციაში ეს ნაბიჯი გამოტოვეს.

## Are Core and Infrastructure separate services? Is Node running in production?

### English

Core and Infrastructure are components inside the Web application. In C4, the Angular UI is a
container that runs in the browser, including sign-in. The Dockerfile's Node stage builds the
Angular files. The runtime image starts `EclipsVault.Web.dll`, which serves those files and writes
the access-blocked, error, and not-found pages on the server. See [diagrams.md](diagrams.md)
sections 2 and 3, the `Dockerfile`, ADR-0008, and ADR-0034.

### ქართული

Core და Infrastructure Web აპლიკაციის კომპონენტებია. C4-ში Angular UI კონტეინერია და ბრაუზერში
ეშვება, შესვლის გვერდის ჩათვლით. Dockerfile-ის Node ეტაპი Angular-ის ფაილებს აწყობს. გაშვების
იმიჯი უშვებს `EclipsVault.Web.dll`-ს, რომელიც ამ ფაილებს აწვდის ბრაუზერს და სერვერზე ქმნის
„წვდომა დაბლოკილია“, „შეცდომა“ და „ვერ მოიძებნა“ გვერდებს. იხილეთ [diagrams.md](diagrams.md)-ის
მე-2 და მე-3 ნაწილები, `Dockerfile`, ADR-0008 და ADR-0034.

## How does a new vault account get its first credentials?

### English

An administrator enters the person's email address. The vault emails a single-use invitation
using `Identity:PublicUrl` and SMTP. The person chooses a password and enrols an authenticator in
one audited activation. The seeded Development accounts enrol at first sign-in. The rehearsal
used those accounts. See ADR-0033, `UserAdminService`, and `AccountActivationService`.

### ქართული

ადმინისტრატორს შეჰყავს მომხმარებლის ელფოსტის მისამართი. საცავი `Identity:PublicUrl`-იდან
აწყობს ერთჯერადი მოწვევის ბმულს და SMTP-ით აგზავნის. მომხმარებელი ერთ აუდიტირებულ
აქტივაციაში ირჩევს პაროლს და არეგისტრირებს ავთენტიფიკატორს. Development რეჟიმში შექმნილ
სადემონსტრაციო ანგარიშებზე ავთენტიფიკატორის რეგისტრაცია პირველი შესვლისას ხდება.
რეპეტიციაში ეს ანგარიშები გამოიყენეს. იხილეთ ADR-0033, `UserAdminService` და
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

რეპეტიციაში გამოიყენეს ერთჯერადი Development საცავი ორი წინასწარ შექმნილი თანამშრომლის
ანგარიშითა და სანიმუშო საიდუმლოებით. Production რეჟიმში, როცა საცავში ანგარიშები ჯერ არ არის,
`DbSeeder` ოპერატორის მიერ მითითებული საწყისი პაროლით ქმნის `vault-admin` ანგარიშს.
გაშვებისთვის `Database:Provider` უნდა იყოს `Postgres` ან `SqlServer`.

[საცნობარო განთავსება](../../../deploy/production/) ერთ Docker ჰოსტზე უშვებს საცავის იმიჯს,
Caddy-ს HTTPS-ისთვის და PostgreSQL-ს. ამ განთავსებაში საცავი მიგრაციებს გაშვებისას ასრულებს.
[Production-ის დაყენების ინსტრუქცია](../../INSTALL.md) ასევე აღწერს მიგრაციების ცალკე
განთავსების ამოცანით შესრულებას. რეპეტიციის Development გარემო აღწერილია
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

დიახ. ადმინისტრატორისთვის განკუთვნილი Angular გვერდი `/Encryption` გასაღებების მდგომარეობას
`GET /api/ui/encryption`-იდან კითხულობს. `KekRotationService` საიდუმლოებისა და დაარქივებული
ვერსიების მონაცემთა გასაღებებს მოქმედი მასტერ-გასაღებით ხელახლა ახვევს. სამუშაო ჯგუფებად
სრულდება და შეწყვეტის შემდეგ შეიძლება გაგრძელდეს. საიდუმლოს დაშიფრული მნიშვნელობა უცვლელი რჩება.
Vault Transit-ის გამოყენებისას გვერდი აჩვენებს ძრავის მიერ მოწოდებულ Transit-ის გასაღების ვერსიებს.
ძრავა მონაცემთა გასაღებების ხელახლა შეფუთვას Transit-ს სთხოვს. რეპეტიციაში გასაღების როტაცია
არ ჩაუტარებიათ. იხილეთ `EncryptionController` და
[ADR-0029](../../adr/0029-master-key-rotation-runs-in-resumable-batches.md).

## How long is the talk, and what is the marking scheme?

### English

The owner confirmed a presentation cap of 30 minutes. Detailed slide allocation and marking
criteria are still unconfirmed. The supplied C4 guide sets diagram requirements, not talk timing.

### ქართული

პრეზენტაციის დადასტურებული ზედა ზღვარია 30 წუთი. სლაიდებზე დროის ზუსტი განაწილება და შეფასების
კრიტერიუმები ჯერ დასაზუსტებელია. მოწოდებული C4 გზამკვლევი ადგენს დიაგრამების მოთხოვნებს და არა
პრეზენტაციის ხანგრძლივობას.
