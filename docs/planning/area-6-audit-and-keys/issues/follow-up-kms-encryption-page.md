## Problem

With the KMS engine (`Crypto:Engine=VaultTransit`), the Encryption keys page and master key rotation compare Vault Transit key ids (`vault:<key>:v<n>`) against the environment master key's id. Every secret then shows under a key that is "not loaded", the critical notice never clears, and every pass re-wraps, and reports, every secret, calling Vault once per row.

Found by the Area 6 grill (finding K7), from reading the code; not reproduced. Part 6a copies the page as it is.

## What to build

- The active crypto engine reports its own current key id and the key ids it can unwrap, so status and rotation compare like with like under either engine.
- A test against a real Vault Transit server, or a faithful fake, proves the page shows the current key version and a pass re-wraps only rows on older versions.

## Acceptance criteria

- [ ] Under the KMS engine, a vault whose secrets are all on the latest Transit key version shows "All secrets are wrapped under the current KEK" and rotates nothing.
- [ ] After a Transit key rotation, the page counts rows on older versions and a pass moves them.
- [ ] The local-key engine behaves as before.
