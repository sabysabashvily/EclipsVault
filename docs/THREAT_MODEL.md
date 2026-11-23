# Threat model

This document states what EclipsVault is designed to withstand, and — just as importantly — what it
is **not**. It is the security boundary the product draws. Everything below the line "Out of scope"
is the operator's responsibility, an inherent limit of self-hosted software, or both. Read it
alongside the [`LICENSE`](../LICENSE) (which disclaims warranty and places deployment, hosting, and
key management with the operator) and [`SECURITY.md`](../SECURITY.md) (how to report a vulnerability
and what is in scope for a report).

EclipsVault is **self-hosted**. It runs on infrastructure the operator controls, sealing secrets
with a master key (KEK) the operator holds. The vendor never receives, sees, or stores the operator's
secrets, KEK, database, or vault contents. That single fact shapes everything here.

## Assets

What is worth protecting, in rough order of value:

| Asset | Where it lives | How it is protected at rest |
|---|---|---|
| Secret values (and prior versions) | Database | Envelope encryption: a per-secret AES-256-GCM DEK, wrapped by the KEK. The database holds ciphertext only. |
| The master key (KEK) | Environment / KMS | Supplied out of band; never committed. With `Crypto:Engine=VaultTransit` it never enters the process — the app holds only single-use DEKs. |
| Audit trail | Database | Append-only, hash-chained, with signed checkpoints; independently verifiable offline (`EclipsVault.AuditVerifier`). |
| Authentication material | Database | Passwords as salted Argon2id hashes; TOTP seeds; WebAuthn/passkey public keys; single-use recovery codes as Argon2id hashes. |
| Data Protection key ring | Durable path | Sealed at rest with the KEK; inert without it. |
| Session state / kill switches | Memory or Redis | Server-side revocation and IP blacklist; Redis access itself is password-gated. |

## Actors and trust boundaries

- **Anonymous network attacker** — reaches the app before authenticating.
- **Authenticated user (low privilege)** — a valid account, constrained by ABAC.
- **Authenticated administrator** — a trusted operator of the vault.
- **Database / backup holder** — anyone who obtains the data at rest.
- **Host / process** — anyone with code execution or memory access on the running machine.
- **Operator** — who deploys, configures, hosts, and holds the KEK.
- **Vendor / supply chain** — the released artifacts and the pipeline that builds them.

## In scope — threats EclipsVault is designed to resist

Each control makes the attack *harder or evident*, not metaphysically impossible.

1. **Reading secrets from the database at rest.** Envelope encryption (AES-256-GCM, a DEK per secret,
   KEK-wrapped) means a stolen database, backup, or read replica yields ciphertext. The KMS engine
   keeps the KEK out of the app process entirely.
2. **Password guessing / credential stuffing.** Argon2id hashing, per-account lockout, a per-IP rate
   limiter, and screening every password against a bundled compromised-password corpus — at change
   time *and* at first-admin bootstrap.
3. **Single-factor compromise.** MFA is mandatory: no session cookie exists until a second factor
   passes. TOTP or passkeys (WebAuthn); high-sensitivity reveals on a stale session force step-up
   re-authentication before any decryption.
4. **Over-broad or lateral access.** Attribute-based access control evaluates clearance × environment
   × trusted-network × time-window on every secret, with project boundaries. Sharing crosses the
   project boundary only and never raises the clearance ceiling. Grant revocation is issuer-scoped
   and refuses indistinguishably from "not found" (closes the IDOR a naive revoke-by-id would open).
5. **Tampering and repudiation.** The audit trail is fail-closed (a reveal that cannot be audited
   aborts *before* decrypting), hash-chained for tamper evidence, and checkpoint-signed so an exported
   bundle can be verified offline against the public key.
6. **Session theft and stale sessions.** Server-side revocation, sign-out-everywhere, and a password
   change that revokes every other session — the device holding the old password is turned out on its
   next request.
7. **Brute force and intrusion.** An IP blacklist keyed by canonical range (O(1) lookup), automated
   intrusion response, trusted-network rules, and decoy/honey-token secrets that sit outside the
   normal ABAC rules to catch a snooping insider.
8. **Long-lived standing credentials.** Dynamic secrets are minted on demand and destroyed at lease
   end; a credential the vault fails to destroy is escalated to a critical alert rather than silently
   marked done.
9. **Dependency and release supply chain.** The Core project is BCL-only (zero third-party packages);
   versions are pinned centrally with committed lockfiles; CI restores in locked mode and fails on any
   vulnerable dependency; a CycloneDX SBOM is emitted per build. Release images are **keyless-signed**
   with Sigstore cosign and ship a signed SBOM attestation, so an operator can prove the image they run
   was built by this repository's pipeline and audit what is in it ([`VERIFYING.md`](VERIFYING.md)).
10. **Configuration and credential leakage.** The database connection string is absent from committed
    config; the KEK comes from the environment or a KMS; TLS to the database is enforced by
    configuration; forwarded-header and `AllowedHosts` validation reject a spoofed client IP or Host.

## Out of scope — the boundary

These are **not** defended by EclipsVault. They are the operator's responsibility, an inherent limit
of self-hosted software, or both. A deployment that ignores them is not protected regardless of the
controls above.

1. **A compromised host.** If an attacker has root on the running machine, or can read process
   memory, they can reach the KEK and decrypted values in use. EclipsVault protects data **at rest
   and in the database** — not against an adversary who already owns the machine the vault runs on.
   Host hardening, OS patching, and isolation are the operator's.
2. **A malicious or compromised operator/administrator.** An administrator can, by design,
   administer. EclipsVault makes privileged actions **audited and tamper-evident**, not impossible.
   It does not defend against a trusted insider who holds the operator role.
3. **Loss of the KEK is permanent data loss.** There is no vendor backdoor and no recovery path. If
   the current and all retired KEKs are lost, every secret is cryptographically unrecoverable — this
   is a property, not a bug. Custody, rotation, and backup of the KEK are the operator's
   responsibility; keep it in a secret manager, **not** beside the database backup.
4. **Deployment and network security.** TLS termination, reverse-proxy configuration, firewalling,
   network exposure, database hardening, and secret-store choice are outside the application. It
   ships fail-closed defaults (production disables dev seeding and fallbacks; a missing KEK or a
   passwordless Redis refuses to start) but cannot enforce the infrastructure around it. See
   [`docs/INSTALL.md`](INSTALL.md).
5. **The database or backup holder who *also* holds the KEK.** Ciphertext plus the key is plaintext.
   Separating the two — different systems, different access — is the operator's job, and the reason
   the KEK belongs in a KMS or secret manager rather than an environment file next to the dump.
6. **The user's endpoint.** A compromised browser or device belonging to a legitimate user, malware
   that scrapes the clipboard after a one-shot reveal, or screen capture — EclipsVault reveals a
   value to an authenticated, authorized user; what happens on their machine is beyond it.
7. **Physical access and memory acquisition** of the host.
8. **Availability / denial of service** beyond the built-in per-IP and per-account throttling.
   EclipsVault is not DDoS mitigation; absorbing volumetric attacks is the operator's edge.
9. **The systems you point it at.** The security of the KMS, identity provider (SSO/OIDC), database,
   and SMTP relay you configure — and vulnerabilities in third-party dependencies themselves (report
   those upstream) — are outside this boundary.
10. **Cryptographic assumptions.** The controls assume today's primitives (Argon2id, AES-256-GCM,
    ECDSA P-256, SHA-256) hold. A future break of an underlying algorithm is out of scope.
11. **Reclassifying a secret by writing to the database directly.** Envelopes are bound to the row
    they belong in, so someone with database write cannot *move* a secret's ciphertext somewhere they
    are cleared to read it (in scope, item 1 above). The binding covers immutable identity only —
    deliberately, so that reclassifying a secret is not a re-encryption — which means the access
    *attributes* are not bound. An attacker with database write can therefore lower a secret's
    `Sensitivity`, change its `ProjectKey`, or change its `Environment`, and the ABAC rules will then
    correctly permit an account that could not read it before. The envelope never moved; the policy
    inputs did.
    - **What still holds:** the read itself is audited like any other, so the access is attributable
      and tamper-evident.
    - **The tell:** a reclassification performed through the vault writes a `SecretUpdated` audit
      entry. One performed directly against the database does not. **A secret whose classification
      changed with no audit entry explaining it is the signal**, and it is worth alerting on.
    - **Reducing it:** this is the general "database write is a powerful position" case — keep the
      application's login least-privileged (see the invariants below), keep database credentials out
      of reach of the application host, and review the audit trail rather than the table.
12. **An insider who can read the audit signing key off the application host.** The checkpoint
    signing key is loaded from `ECLIPSVAULT_AUDIT_SIGNING_KEY` on the host running the vault. Anyone
    who can read that host's environment — a root user, an operator with deploy access, a process or
    crash dump — holds the key the vault signs with.
    - **What that buys them:** the hash chain is public and deterministic, so an attacker with
      database write can already rewrite history and recompute every row hash. What normally stops
      them is the signed checkpoint, which they cannot forge. With the real key they can: rewrite the
      chain, re-checkpoint it, and export a bundle that verifies — **including against a pinned key,
      because the key is genuinely the vault's.** Key-pinning closes the case where an attacker signs
      with a key of *their own*; it cannot close this one.
    - **What still holds:** this needs host-level compromise, which is a strictly higher bar than
      database access. Against an attacker with the database alone — a stolen dump, a backup, a read
      replica, a DBA — the chain and the signature both hold completely. That is the common case and
      it is covered.
    - **The tell, and the practical mitigation:** a rewrite cannot reach a bundle that has already
      left the building. **Export a signed bundle on a schedule and have the auditor retain it
      externally.** A later rewrite contradicts every retained bundle — the same sequence numbers
      resolve to different hashes — and the contradiction is evidence in itself. This costs nothing
      and converts host-key compromise from undetectable to detectable by comparison, which is the
      whole reason the export exists.
    - **Reducing it further:** hold the signing key somewhere the application host cannot read —
      an external signer or HSM. EclipsVault does not ship one today; `IAuditCheckpointSigner` is the
      seam where one would go. Until then, treat host access as equivalent to signing authority and
      restrict it accordingly.

## Invariants that bound the blast radius

These hold by construction and are worth stating because they limit how bad a failure can get:

- **Licensing never blocks decryption.** Enforcement is soft — a licensing failure, an expired token,
  or a malformed license degrades to a banner and an audit line. It can *never* lock an operator out
  of their own secrets.
- **Auditing is fail-closed.** A secret read that cannot be written to the audit trail aborts before
  it decrypts. There is no "read now, log later."
- **KEK / DEK separation, and the KMS option removes the KEK from the process** — so the highest-value
  key need not sit in application memory at all.
- **Least privilege at rest.** The application's own database login has no schema (DDL) rights;
  migrations run from the deploy job under a separate login. A compromise of the running app cannot
  rewrite the schema or the audit tables.
  - **Dynamic secrets and managed rotation do not weaken this.** They run `CREATE LOGIN`,
    `DROP LOGIN`, and `ALTER LOGIN`, which need server-level rights (`ALTER ANY LOGIN` on SQL
    Server, `CREATEROLE` on PostgreSQL) — rights that can re-password principals on that instance.
    Those statements therefore run against a **separately configured target database** over its own
    connection (`DynamicSecrets:TargetConnectionString`, or `DynamicSecrets:PostgresTargetConnectionString`
    for PostgreSQL roles), never over the vault's. The privileged credential is
    the target's, not the vault's, and the target can be — and normally should be — a different
    server entirely. If no target is configured, minting and rotation **refuse**; there is no
    fallback to the vault's connection, because a fallback would silently require the vault's own
    login to hold a privilege that would put the audit trail's own server within reach of an
    application compromise.

## Reporting a weakness

Found something in the in-scope list that does not hold? That is a vulnerability — report it per
[`SECURITY.md`](../SECURITY.md). Something in the out-of-scope list is a deployment or design boundary,
not a defect; if you think the boundary is drawn in the wrong place, that is a design discussion, and
also welcome by the same contact.
