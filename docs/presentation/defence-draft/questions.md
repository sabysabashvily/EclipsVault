# Likely questions / სავარაუდო კითხვები

Answer from the source named here, at `origin/main` `0bb59de1e8f64d1b7d04468c303c1b108dd6f04b`.
If the honest answer is "planned" or "not run", say that and stop.

პასუხი აქ დასახელებული წყაროდან გაეცით, `origin/main` `0bb59de`-ზე. თუ პატიოსანი პასუხია
„დაგეგმილი“ ან „არ გაშვებულა“, ასე თქვით და გაჩერდით.

## The browser decides who can see a secret, doesn't it?

### English

No. Angular route guards are convenience. `SecretAccessPolicy` runs on the server for the Legacy
reveal and for the Public API. ADR-0012 says the UI does not decide from a hard-coded clearance
check. A hidden link is not the control.

### ქართული

არა. Angular-ის მარშრუტის მცველები მოხერხებულობაა. `SecretAccessPolicy` სერვერზე ეშვება Legacy
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
row commits, `AesGcmCryptoEngine.UnsealAsync` opens the value in the Web process, and the Legacy
page renders it. The data key is wrapped by the master key so a database copy of the ciphertext is
not the plaintext. It is not a claim that the operator's process cannot read a secret it is serving.

### ქართული

დიახ. ეს სერვერზე მდგომი საცავია, არა ისეთი დიზაინი, სადაც მხოლოდ ბრაუზერს შეუძლია გაშიფვრა.
აუდიტის ჩანაწერის დაფიქსირების შემდეგ `AesGcmCryptoEngine.UnsealAsync` მნიშვნელობას Web პროცესში
ხსნის და Legacy გვერდი მას აჩვენებს. მონაცემთა გასაღები მასტერ-გასაღებითაა შეხვეული, ამიტომ ბაზაში
მყოფი შიფრული ტექსტი ღია ტექსტი არ არის. ეს არ არის მტკიცება, რომ ოპერატორის პროცესი ვერ კითხულობს
საიდუმლოს, რომელსაც თავად აწვდის.

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

`GetDetailsAsync` writes `SecretMetadataViewed` when the details load. `RevealAsync` writes
`SecretRevealed` when the value is requested, and that second write is the one ordered before
decryption. The page labels them `Viewed` and `Revealed`. Source: `SecretService`; `AuditDisplay`.

### ქართული

`GetDetailsAsync` წერს `SecretMetadataViewed`-ს, როცა დეტალები იტვირთება. `RevealAsync` წერს
`SecretRevealed`-ს, როცა მნიშვნელობაა მოთხოვნილი, და ეს მეორე ჩანაწერია, რომელიც გაშიფვრამდე
დგას. გვერდი მათ `Viewed` და `Revealed` ეძახის. წყარო: `SecretService`; `AuditDisplay`.

## Does Verify prove nobody deleted or rewrote the trail?

### English

No. Not for every deletion, and not regardless of the latest signed checkpoint.
`VerifyIntegrityAsync` re-walks the rows that are present. It reports a gap, a broken link, or a
row whose stored hash no longer matches. It does not load checkpoints. A deleted tail, or a rewrite
that recomputes every hash, can still look intact. A signed checkpoint proves the head it signed
only when a check compares them. Today's button does not. `AuditBundleVerifier` does that comparison
for a bundle it is given. A bundle an auditor already keeps is unchanged by a later edit of the live
database. Source: `AuditLogReader`; `AuditCheckpointService`; `AuditBundleVerifier`;
`docs/THREAT_MODEL.md` item 12.

The stronger check, atomic checkpoint row, and streamed step-up export are specified for Area 6b
and are not merged. Even that specification says a database writer can delete checkpoints, or change
rows after the latest checkpoint, without that Verify noticing. Source: ADR-0030 on Area 6a head
`ab7997e`.

### ქართული

არა. არა ყოველი წაშლისთვის და არა ბოლო ხელმოწერილი საკონტროლო წერტილისგან დამოუკიდებლად.
`VerifyIntegrityAsync` იმ ჩანაწერებს გადის, რომლებიც არსებობს. აჩვენებს რიგითობის წყვეტას, გაწყვეტილ კავშირს
ან ჩანაწერს, რომლის შენახული ჰეში აღარ ემთხვევა. საკონტროლო წერტილებს არ ტვირთავს. წაშლილმა კუდმა
ან ყველა ჰეშის თავიდან დათვლით გადაწერამ შეიძლება მაინც მთლიანად გამოიყურებოდეს. ხელმოწერილი
საკონტროლო წერტილი იმ თავს ამტკიცებს, რომელსაც მოაწერა ხელი, მხოლოდ მაშინ, როცა შემოწმება მათ
ადარებს. დღევანდელი ღილაკი არ ადარებს. `AuditBundleVerifier` ამ შედარებას აკეთებს მისთვის მიცემული
შეკვრისთვის. აუდიტორის მიერ უკვე შენახულ შეკვრას ცოცხალი ბაზის გვიანი შესწორება არ ცვლის. წყარო:
`AuditLogReader`; `AuditCheckpointService`; `AuditBundleVerifier`; `docs/THREAT_MODEL.md` პუნქტი 12.

უფრო ძლიერი შემოწმება, საკონტროლო წერტილის ატომური ჩანაწერი და ნაკადური გაძლიერებული ექსპორტი
Area 6b-შია სპეციფიცირებული და შერწყმული არ არის. იმ სპეციფიკაციაც ამბობს, რომ ბაზის დამწერს შეუძლია
წაშალოს საკონტროლო წერტილები ან შეცვალოს ჩანაწერები ბოლო საკონტროლო წერტილის შემდეგ ისე, რომ ამ
Verify-მ ვერ შეამჩნიოს. წყარო: ADR-0030 Area 6a-ის თავზე `ab7997e`.

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

No. What you are being shown, if the stack is up at all, is Development's demo seed: two staff
accounts and sample secrets that production refuses to create. Production bootstraps a single
administrator from a password the operator supplies, applies schema from a deploy job, and serves
the image behind TLS. Source: `DbSeeder`; `docs/INSTALL.md`; `Dockerfile`. This draft did not start
either one.

### ქართული

არა. რასაც გიჩვენებენ, თუ სტეკი საერთოდ დგას, Development-ის სადემონსტრაციო სამყაროა: ორი თანამშრომლის ანგარიში
და სანიმუშო საიდუმლოები, რომელთა შექმნაზეც production უარს ამბობს. Production ერთ ადმინისტრატორს
აყენებს ოპერატორის მიერ მოწოდებული პაროლით, სქემას განთავსების სამუშაოდან იყენებს და იმიჯს TLS-ის
უკან აწვდის. წყარო: `DbSeeder`; `docs/INSTALL.md`; `Dockerfile`. ამ მონახაზმა არც ერთი არ გაუშვა.

## Did the encryption-keys pull request merge? Did its tests pass?

### English

It had not merged when this draft was written. PR #147 was open at `ab7997e`. The check list read
then was all `SUCCESS`, including `e2e`, which finished at 21:41 Asia/Tbilisi on 10 November 2026.
The review list on that PR was empty in the same read, so this answer does not include a Thermos or
GPT approval. The reveal order does not depend on that PR: `SecretService.RevealAsync` is unchanged
between `main` and that head. The PR's own subject is master-key rotation and the Angular Encryption
keys page.

### ქართული

ამ მონახაზის წერისას ის შერწყმული არ იყო. PR #147 ღია იყო `ab7997e`-ზე. მაშინ წაკითხული შემოწმებების
სია მთლიანად `SUCCESS` იყო, `e2e`-ს ჩათვლით, რომელიც დასრულდა 10 ნოემბერს 2026, 21:41 Asia/Tbilisi.
იმავე წაკითხვაში ამ PR-ის განხილვების სია ცარიელი იყო, ამიტომ ეს პასუხი Thermos-ის ან GPT-ის
დამტკიცებას არ შეიცავს. გახსნის რიგი ამ PR-ზე არ დგას: `SecretService.RevealAsync` `main`-სა და იმ
თავს შორის უცვლელია. PR-ის საკუთარი თემა მასტერ-გასაღების როტაცია და Angular-ის Encryption keys
გვერდია.

## How long is the talk, and what is the marking scheme?

### English

Not in this draft. The assessment PDF was not supplied and was not fetched. Every slide is
unallocated. Do not invent a duration.

### ქართული

ამ მონახაზში არ არის. შეფასების PDF არ მოწოდებულა და არ მოძებნილა. ყოველი სლაიდი გამოყოფილი დროის
გარეშეა. ხანგრძლივობა ნუ მოიფიქრებთ.
