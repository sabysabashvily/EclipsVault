# Multi-stage production image for EclipsVault.Web. TLS is expected to terminate at a reverse
# proxy / ingress (see docs/INSTALL.md); the app listens on plain HTTP inside the network.
#
# Supply-chain note: both base images are pinned to a digest (the human-readable tag is kept
# alongside for clarity). A floating tag means the image that builds tomorrow is not the one you
# tested, and a supply-chain compromise would arrive with nothing in the diff — the same reasoning
# docker-compose.yml applies to its own images. To move to a newer .NET patch, inspect the exact
# patch tags you are moving to, then replace each FROM line's tag and digest together:
#   docker buildx imagetools inspect mcr.microsoft.com/dotnet/sdk:<sdk-version>     # copy the Digest: line
#   docker buildx imagetools inspect mcr.microsoft.com/dotnet/aspnet:<runtime-version>
# The security-scan job runs .github/dotnet-image-pin.py and fails until these tags are the latest patch.
# .NET pinned 2026-11-13 to runtime 10.0.15, the first patch with the System.Formats.Cbor fix:
# ASP.NET apps load Cbor from the shared framework in this image, not from the NuGet package.

# The Angular UI (ADR-0008). Pinned the same way; the version matches EclipsVault.AngularUi/.nvmrc:
#   docker buildx imagetools inspect node:24.22.0-bookworm-slim
# Digest pinned 2026-10-16. The build writes the bundles to EclipsVault.Web/wwwroot/ui/ and the
# document to EclipsVault.Web/AngularUi/index.html, the paths the runtime stage copies.
FROM node:24.22.0-bookworm-slim@sha256:a5f4366e834695d5f363b920c104953a535d3a9864d1bdf74566a0ebe01daee5 AS ui
WORKDIR /src/EclipsVault.AngularUi
COPY EclipsVault.AngularUi/package.json EclipsVault.AngularUi/package-lock.json ./
RUN npm ci --ignore-scripts
COPY EclipsVault.AngularUi/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src
COPY . .
RUN dotnet restore EclipsVault.Web/EclipsVault.Web.csproj --locked-mode
RUN dotnet publish EclipsVault.Web/EclipsVault.Web.csproj -c Release -o /app --no-restore
# The portable publish carries SkiaSharp's native library for 17 platforms (~465 MB). Keep only the
# one this container's runtime will load, and fail the build if it is missing so a broken avatar
# upload can't ship silently.
RUN rid="$(dotnet --info | awk '/^ *RID:/ { print $2; exit }')" \
 && find /app/runtimes -name 'libSkiaSharp*' -not -path "/app/runtimes/$rid/*" -delete \
 && test -f "/app/runtimes/$rid/native/libSkiaSharp.so"
# It also carries Microsoft.Data.SqlClient.SNI.runtime's Windows native library, under Microsoft Software
# License Terms. On Linux SqlClient uses its managed SNI and never loads it, so the image does not
# redistribute it. These are all the package's files; rm fails the build if a new version moves them.
RUN rm /app/runtimes/win-arm64/native/Microsoft.Data.SqlClient.SNI.dll \
       /app/runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll \
       /app/runtimes/win-x86/native/Microsoft.Data.SqlClient.SNI.dll \
 && test -z "$(find /app/runtimes -name 'Microsoft.Data.SqlClient.SNI*')"

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e AS runtime
WORKDIR /app

# Run as a non-root user. Use the `app` account the base image already ships (its UID is published
# as $APP_UID, currently 1654) rather than creating one: this image carries a shell but no `adduser`
# binary, so the previous `adduser ... --uid 10001 vault` failed with exit 127 and no image could be
# built at all. The image's own User field is unset, so declaring USER here is what actually drops
# root — without it the vault runs as root.
#
# Ownership is set during the copy instead of by a later `chown -R`, which would duplicate the whole
# publish output into a second layer for nothing.
COPY --from=build --chown=$APP_UID:$APP_UID /app .
COPY --from=ui --chown=$APP_UID:$APP_UID /src/EclipsVault.Web/wwwroot/ui ./wwwroot/ui
COPY --from=ui --chown=$APP_UID:$APP_UID /src/EclipsVault.Web/AngularUi ./AngularUi
# The licence notices of every NuGet package published above. notices.py check keeps the file in
# step with the SBOM; .github/notices/test-image.sh confirms it and the other notices are in the image.
COPY --chown=$APP_UID:$APP_UID THIRD-PARTY-NOTICES.txt ./
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "EclipsVault.Web.dll"]
