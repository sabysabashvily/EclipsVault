# npm is held to the same supply-chain rules as NuGet, with a runtime allowlist

The threat model promises pinned, locked, audited dependencies and a signed image with an SBOM. Angular's build tooling runs inside the build that produces that image, so a compromised dev dependency can alter the code that later displays secret values. The npm side therefore gets the same rules as NuGet:

- The Node version is pinned in the repo and in CI.
- `package-lock.json` is committed and installs use `npm ci`.
- A high or critical `npm audit` finding fails CI, in dev dependencies as well as runtime ones. Fix it by upgrading or overriding the package, never by suppressing the finding, the same rule as `NU1902`/`NU1903`.
- The npm runtime packages appear in the SBOM.

Runtime packages are limited to `@angular/*`, `rxjs` and `tslib`. There is no component library. The Angular UI uses self-hosted Geist, solid neutral surfaces in light and dark themes, and one orange accent. Its tokens live in `src/styles.css`; Legacy pages keep their separate `site.css`. A component library would bring its own runtime style injection into a strict-CSP app and a second visual language. Adding any other runtime package needs a new ADR.
