#!/usr/bin/env bash
# Checks that a built EclipsVault image carries the licence notices of everything it redistributes:
# its own third-party notices file, byte for byte as committed, the Angular UI's and the fonts' licence
# files, the .NET runtime's notices, and a copyright file for every OS package the base image installs.
#
# Usage, from the repository root: bash .github/notices/test-image.sh <image>
set -euo pipefail

image=${1:?usage: test-image.sh <image>}

# The image's entrypoint is the vault itself, so each check runs through sh instead. No network and no pull:
# the checks read only the image that was just built.
docker run --rm --network none --pull never --entrypoint sh "$image" -c '
  failed=0
  for file in \
      /app/THIRD-PARTY-NOTICES.txt \
      /app/wwwroot/ui/3rdpartylicenses.txt \
      /app/wwwroot/fonts/OFL.txt \
      /usr/share/dotnet/LICENSE.txt \
      /usr/share/dotnet/ThirdPartyNotices.txt; do
    if [ -s "$file" ]; then echo "present: $file"; else echo "MISSING: $file"; failed=1; fi
  done

  # A package may point its copyright at a sibling from the same source package. Ubuntu 24.04 openssl
  # links to ../libssl3/copyright, which the 64-bit time rename moved to libssl3t64. So the rule is per
  # source package: at least one of its installed packages has a readable copyright file.
  covered=$(dpkg-query -W -f="\${Package} \${source:Package}\n" | while read -r package source; do
    [ -r "/usr/share/doc/$package/copyright" ] && echo "$source"
  done | sort -u)
  sources=$(dpkg-query -W -f="\${source:Package}\n" | sort -u)
  uncovered=""
  for source in $sources; do
    printf "%s\n" "$covered" | grep -qxF "$source" || uncovered="$uncovered $source"
  done
  if [ -n "$uncovered" ]; then
    echo "MISSING: no readable /usr/share/doc/*/copyright for source packages:$uncovered"; failed=1
  else
    echo "present: a copyright file for each of the $(printf "%s\n" "$sources" | wc -l) OS source packages"
  fi
  exit $failed
'

# The file in the image must be the committed one that notices.py check keeps in step with the SBOM.
if docker run --rm --network none --pull never --entrypoint cat "$image" /app/THIRD-PARTY-NOTICES.txt \
    | cmp -s - THIRD-PARTY-NOTICES.txt; then
  echo "identical: /app/THIRD-PARTY-NOTICES.txt and the committed THIRD-PARTY-NOTICES.txt"
else
  echo "DIFFERENT: /app/THIRD-PARTY-NOTICES.txt is not the committed THIRD-PARTY-NOTICES.txt"
  exit 1
fi
