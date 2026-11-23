# Likely questions / სავარაუდო კითხვები

Answer from the source named here, at `origin/main` `d576dd93a30bc461183ca7b71c89f038adbff446`
(21 November 2026, 22:42 Asia/Tbilisi). If the honest answer is "not on this revision" or "not
rehearsed", say that and stop.

პასუხი აქ დასახელებული წყაროდან გაეცით, `origin/main` `d576dd9`-ზე (21 ნოემბერი 2026, 22:42
Asia/Tbilisi). თუ პატიოსანი პასუხია „ამ რევიზიაზე არ არის“ ან „არ რეპეტირებულა“, ასე თქვით და
გაჩერდით.

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

Because it was chosen not to. `Phoenix_Dev_Database_Password` is Internal and Development.
`SecretAccessPolicy` consults the trusted network only at Confidential and above, and the production
window only for Production. The rules are in the same method. The live path is the one a Standard
member of `PHOENIX` can finish without depending on the clock or on moving off loopback.
`appsettings.json` trusts `127.0.0.1/32`, so a Confidential secret from this laptop would still be
on a trusted network.

### ქართული

იმიტომ, რომ ასეა შერჩეული. `Phoenix_Dev_Database_Password` Internal და Development არის.
`SecretAccessPolicy` სანდო ქსელს მხოლოდ Confidential-იდან ზემოთ კითხულობს, წარმოების ფანჯარას კი
მხოლოდ Production-ზე. წესები იმავე მეთოდშია. ცოცხალი გზა ისეთია, რომ `PHOENIX`-ის Standard წევრმა
ის საათზე ან loopback-იდან წასვლაზე დამოკიდებულების გარეშე დაასრულოს. `appsettings.json` ენდობა
`127.0.0.1/32`-ს, ამიტომ ამ ლეპტოპიდან Confidential საიდუმლო მაინც სანდო ქსელში იქნებოდა.

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

## Why are there two audit rows?

### English

After the access check allows the details page, `RecordViewedAsync` writes `SecretMetadataViewed`.
`GetDetailsAsync` itself writes nothing, so a refusal is `SecretAccessDenied` instead. `RevealAsync`
writes `SecretRevealed` when the value is requested, and that write is the one ordered before
decryption. The page labels them `Viewed`, `Access denied`, and `Revealed`. Source:
`SecretDetailsController.OpenAsync`; `SecretService`; `AuditDisplay`; ADR-0031.

### ქართული

მას შემდეგ, რაც წვდომის შემოწმება დეტალების გვერდს უშვებს, `RecordViewedAsync` წერს
`SecretMetadataViewed`-ს. `GetDetailsAsync` თავად არაფერს წერს, ამიტომ უარი `SecretAccessDenied`-ა.
`RevealAsync` წერს `SecretRevealed`-ს, როცა მნიშვნელობაა მოთხოვნილი, და ეს ჩანაწერია, რომელიც
გაშიფვრამდე დგას. გვერდი მათ `Viewed`, `Access denied` და `Revealed` ეძახის. წყარო:
`SecretDetailsController.OpenAsync`; `SecretService`; `AuditDisplay`; ADR-0031.

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

Keep the README note. A current verifier reads `eclipsvault.audit-bundle/1` and `/2`, and refuses a
newer format with the words that this is not evidence of tampering. The 1.0.0 verifier shipped
before that check. Given a bundle from a later vault it reports `INVALID — "the row was edited"`.
That sentence means the verifier is too old. Re-run with a verifier at least as new as the vault
that exported the bundle. Source: `README.md` signed-checkpoint paragraph; `AuditBundleSchema`;
`AuditBundleVerifier`. This draft did not execute the 1.0.0 binary.

### ქართული

დაიცავით README-ს შენიშვნა. მიმდინარე შემმოწმებელი კითხულობს `eclipsvault.audit-bundle/1`-ს და
`/2`-ს და უფრო ახალ ფორმატს უარყოფს სიტყვებით, რომ ეს ხელყოფის მტკიცებულება არ არის. 1.0.0
შემმოწმებელი ამ შემოწმებამდე გამოვიდა. გვიანი საცავის შეკვრაზე ის აჩვენებს
`INVALID — "the row was edited"`. ეს წინადადება ნიშნავს, რომ შემმოწმებელი ძალიან ძველია. თავიდან
გაუშვით შემმოწმებელი, რომელიც იმ საცავის ტოლი ან უფრო ახალია, რომელმაც შეკვრა გამოიტანა. წყარო:
`README.md`-ის ხელმოწერილი საკონტროლო წერტილის აბზაცი; `AuditBundleSchema`; `AuditBundleVerifier`.
ამ მონახაზმა 1.0.0 ბინარი არ გაუშვა.

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

It is not stored. `DynamicSecretLease` says it deliberately holds no password. The mint is returned
once. `DynamicLeaseWorker` destroys the backend login when the lease is due. The talk does not issue
one: the seed roles target SQL Server, and issuance is behind the dynamic-secrets licence check.
Source: `DynamicSecretLease`; `DynamicSecretService`; `DynamicLeaseWorker`.

### ქართული

არ ინახება. `DynamicSecretLease` ამბობს, რომ განზრახ არ იკავებს პაროლს. გაცემული პაროლი ერთხელ
ბრუნდება. `DynamicLeaseWorker` ანადგურებს ბექენდის ლოგინს, როცა იჯარის ვადა მოდის. მოხსენება ამას
არ გასცემს: დათესილი როლები SQL Server-ს ეხება და გაცემა დინამიკური საიდუმლოების ლიცენზიის
შემოწმების უკანაა. წყარო: `DynamicSecretLease`; `DynamicSecretService`; `DynamicLeaseWorker`.

## Are Core and Infrastructure separate services? Is Node running in production?

### English

No, and no. Core and Infrastructure are components inside the Web application, not containers. The
Angular UI is a container, and it runs in the browser from files Web serves; the image does not
start a second server for it. The Dockerfile's Node stage builds those files and is not the runtime
image. The runtime image starts `EclipsVault.Web.dll`. Redis and the other compose services are
used only when configuration selects them. Source: [diagrams.md](diagrams.md) sections 2 and 3;
`Dockerfile`; ADR-0008.

### ქართული

არა და არა. Core და Infrastructure Web აპლიკაციის კომპონენტებია, არა კონტეინერები. Angular UI
კონტეინერია და ბრაუზერში ეშვება იმ ფაილებიდან, რომლებსაც Web აწვდის; იმიჯი მისთვის მეორე სერვერს
არ იწყებს. Dockerfile-ის Node საფეხური ამ ფაილებს აწყობს და გაშვების იმიჯი არ არის. გაშვების
იმიჯი იწყებს `EclipsVault.Web.dll`-ს. Redis და compose-ის სხვა სერვისები მხოლოდ მაშინ
გამოიყენება, როცა კონფიგურაცია ირჩევს მათ. წყარო: [diagrams.md](diagrams.md) ნაწილი 2 და 3;
`Dockerfile`; ADR-0008.

## Is this the production deployment?

### English

No. What you are being shown is Development's demo seed: two staff accounts and sample secrets
that production refuses to create. Production bootstraps a single administrator from a password
the operator supplies, applies schema from a deploy job, and serves the image behind TLS. Source:
`DbSeeder`; `docs/INSTALL.md`; `Dockerfile`. The rehearsal started only the disposable Development
vault. It did not start the production image.

### ქართული

არა. რასაც გიჩვენებენ, Development-ის სადემონსტრაციო სამყაროა: ორი თანამშრომლის ანგარიში და
სანიმუშო საიდუმლოები, რომელთა შექმნაზეც production უარს ამბობს. Production ერთ ადმინისტრატორს
აყენებს ოპერატორის მიერ მოწოდებული პაროლით, სქემას განთავსების სამუშაოდან იყენებს და იმიჯს TLS-ის
უკან აწვდის. წყარო: `DbSeeder`; `docs/INSTALL.md`; `Dockerfile`. რეპეტიციამ მხოლოდ ერთჯერადი
Development საცავი გაუშვა. Production იმიჯი არ გაუშვია.

## Did the encryption-keys work merge?

### English

Yes. On this revision `KekRotationService` implements `IKekRotationService`, and `EncryptionController`
serves `GET api/ui/encryption` behind the Angular page `/Encryption`. ADR-0029 is in `docs/adr/` on
`main`. The talk does not rotate a key. Rotation moves sealed envelopes in resumable batches and
does not decrypt a value. This answer does not recite the old PR's review list.

### ქართული

დიახ. ამ რევიზიაზე `KekRotationService` ახორციელებს `IKekRotationService`-ს, და
`EncryptionController` აწვდის `GET api/ui/encryption`-ს Angular-ის გვერდის `/Encryption` უკან.
ADR-0029 `main`-ის `docs/adr/`-შია. მოხსენება გასაღებს არ ატრიალებს. როტაცია დალუქულ კონვერტებს
განახლებად პარტიებად გადააქვს და მნიშვნელობას არ შლის. ეს პასუხი ძველი PR-ის განხილვების სიას არ
იმეორებს.

## How long is the talk, and what is the marking scheme?

### English

The owner confirmed a presentation cap of 30 minutes. Detailed slide allocation and marking
criteria are still unconfirmed. The supplied C4 guide sets diagram requirements, not talk timing.

### ქართული

პრეზენტაციის დადასტურებული ზედა ზღვარია 30 წუთი. სლაიდებზე დროის ზუსტი განაწილება და შეფასების
კრიტერიუმები ჯერ დასაზუსტებელია. მოწოდებული C4 გზამკვლევი ადგენს დიაგრამების მოთხოვნებს და არა
პრეზენტაციის ხანგრძლივობას.
