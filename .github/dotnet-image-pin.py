"""Fail when a pinned .NET SDK or ASP.NET image is behind the latest patch.

The production image loads framework libraries from the ASP.NET runtime, so a
security fix reaches the vault only when that pin moves. NuGet audit does not
see those libraries. The SDK image is the build stage and is held to the same
rule. A finding is fixed by moving the pin; this script has no allowlist.

Microsoft publishes the index named here. The channel is the pin's major.minor
(10.0.15 is channel 10.0):

    https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json

Usage, from the repository root:

    python3 -I .github/dotnet-image-pin.py [Dockerfile]
"""

from __future__ import annotations

import json
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCKERFILE = ROOT / "Dockerfile"
INDEX_HOST = "dotnetcli.blob.core.windows.net"
INDEX_URL = f"https://{INDEX_HOST}/dotnet/release-metadata/{{channel}}/releases.json"
PRODUCTS = ("sdk", "aspnet")

# The digest is optional so a tag can be compared before its digest is filled in.
# A suffix such as -noble is rejected: the index has nothing to compare it with.
FROM_LINE = re.compile(r"^FROM\s+mcr\.microsoft\.com/dotnet/(sdk|aspnet):(\S+)", re.MULTILINE)
PLAIN_TAG = re.compile(r"^(\d+\.\d+\.\d+)(?:@sha256:[0-9a-fA-F]+)?$")


def main() -> int:
    dockerfile = dockerfile_path()
    try:
        text = dockerfile.read_text(encoding="utf-8")
    except OSError as error:
        print(f"::error::Could not read {dockerfile}: {error}")
        return 1

    pins, problems = parse_pins(text)
    seen = {product for product, _version in pins} | {product for product, _tag in problems}
    for product in PRODUCTS:
        if product not in seen:
            problems.append((product, f"::error::Dockerfile does not pin mcr.microsoft.com/dotnet/{product}."))

    indexes: dict[str, dict | None] = {}
    for product, version in pins:
        channel = channel_of(version)
        if channel not in indexes:
            indexes[channel] = fetch_index(channel, problems)
        index = indexes[channel]
        if index is None:
            continue
        problem = behind_latest(product, version, component_versions(index, product))
        if problem is None:
            print(f"ok {image_ref(product, version)} is the latest .NET {channel} patch")
        else:
            problems.append((product, problem))

    for _product, problem in problems:
        print(problem)
    return 1 if problems else 0


def dockerfile_path() -> Path:
    if len(sys.argv) == 1:
        return DOCKERFILE
    if len(sys.argv) == 2:
        return Path(sys.argv[1])
    print("usage: python3 -I .github/dotnet-image-pin.py [Dockerfile]", file=sys.stderr)
    raise SystemExit(2)


def parse_pins(text: str) -> tuple[list[tuple[str, str]], list[tuple[str, str]]]:
    """Split Dockerfile FROM lines into (product, version) pins and error lines.

    A tag the release index cannot name is an error, not a skip. Skipping it
    would let a renamed tag sail through the check.
    """
    pins = []
    problems = []
    for product, token in FROM_LINE.findall(text):
        parsed = PLAIN_TAG.fullmatch(token)
        if parsed is None:
            tag = token.split("@", 1)[0]
            problems.append((
                product,
                f"::error::{image_ref(product, tag)} is not a plain patch tag, so the release index cannot check it.",
            ))
            continue
        pins.append((product, parsed.group(1)))
    return pins, problems


def channel_of(version: str) -> str:
    major, minor, _patch = version.split(".")
    return f"{major}.{minor}"


def fetch_index(channel: str, problems: list[tuple[str, str]]) -> dict | None:
    url = INDEX_URL.format(channel=channel)
    request = urllib.request.Request(url, headers={"Accept": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            final = urllib.parse.urlparse(response.geturl())
            if final.hostname != INDEX_HOST or final.scheme != "https":
                problems.append(("", f"::error::The .NET {channel} release index redirected off {INDEX_HOST}."))
                return None
            payload = json.load(response)
    except (urllib.error.URLError, json.JSONDecodeError, TimeoutError, UnicodeError) as error:
        problems.append(("", f"::error::Could not read the .NET {channel} release index at {url}: {error}"))
        return None
    releases = payload.get("releases") if isinstance(payload, dict) else None
    if (
        not isinstance(payload, dict)
        or payload.get("channel-version") != channel
        or not isinstance(releases, list)
    ):
        problems.append(("", f"::error::The document at {url} is not the .NET {channel} release index."))
        return None
    return payload


def component_versions(index: dict, product: str) -> dict[str, bool]:
    """Stable versions of this image, and whether the release that shipped each one is a security release."""
    found: dict[str, bool] = {}
    for release in index["releases"]:
        if not isinstance(release, dict):
            continue
        security = release.get("security") is True
        for version in versions_in(release, product):
            if stable(version):
                found[version] = found.get(version, False) or security
    return found


def versions_in(release: dict, product: str) -> list[object]:
    if product == "aspnet":
        runtime = release.get("aspnetcore-runtime")
        return [runtime.get("version") if isinstance(runtime, dict) else None]
    sdk = release.get("sdk")
    versions: list[object] = [sdk.get("version") if isinstance(sdk, dict) else None]
    for entry in release.get("sdks") or []:
        versions.append(entry.get("version") if isinstance(entry, dict) else None)
    return versions


def stable(version: object) -> bool:
    if not isinstance(version, str):
        return False
    parts = version.split(".")
    return len(parts) == 3 and all(part.isdecimal() for part in parts)


def behind_latest(product: str, pinned: str, versions: dict[str, bool]) -> str | None:
    """An error line when the pin is missing or older than the latest patch, else None.

    A version the index does not list is not treated as newer than latest.
    That comparison would accept a typo.
    """
    image = image_ref(product, pinned)
    channel = channel_of(pinned)
    if not versions:
        return f"::error::{image} cannot be checked: the .NET {channel} release index lists no stable version."
    if pinned not in versions:
        return f"::error::{image} is not in the .NET {channel} release index."
    newer = [version for version in versions if version_key(version) > version_key(pinned)]
    if not newer:
        return None
    latest = max(versions, key=version_key)
    security_releases = [version for version in newer if versions[version]]
    if versions[latest]:
        reason = f"behind the latest patch {latest}, a security release"
    elif security_releases:
        missed = max(security_releases, key=version_key)
        reason = f"behind the latest patch {latest}. The newer release {missed} is a security release"
    else:
        reason = f"behind the latest patch {latest}"
    return f"::error::{image} is {reason}. Move the pin in the Dockerfile."


def version_key(version: str) -> tuple[int, int, int]:
    major, minor, patch = version.split(".")
    return int(major), int(minor), int(patch)


def image_ref(product: str, tag: str) -> str:
    return f"mcr.microsoft.com/dotnet/{product}:{tag}"


if __name__ == "__main__":
    raise SystemExit(main())
