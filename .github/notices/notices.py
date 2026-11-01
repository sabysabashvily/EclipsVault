"""Third-party notices for the NuGet packages the image ships.

    fetch     download, at the commit each package's nuspec records, the upstream licence of every package that
              carries no licence file, and the NOTICE of every Apache-2.0 package, into upstream/
    generate  write THIRD-PARTY-NOTICES.txt at the repository root and sources.tsv beside this script
    check     fail when either committed file differs from what generate would write, when the SBOM and the
              notices file list different packages, when an Apache-2.0 NOTICE was never looked for, or when a
              licence text or copyright line is missing and not listed in known-gaps.tsv

Every text comes from the restored package or from an upstream file fetched at a recorded commit; nothing
here writes a licence or copyright line of its own. Needs sbom/sbom.json (bash .github/sbom/sbom.sh) and the
packages restored by `dotnet restore --locked-mode`. Standard library only, like the guardrail tests.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
SBOM = ROOT / "sbom" / "sbom.json"
NOTICES = ROOT / "THIRD-PARTY-NOTICES.txt"
SOURCES = HERE / "sources.tsv"
UPSTREAM = HERE / "upstream"
FETCHED = UPSTREAM / "fetched.tsv"
KNOWN_GAPS = HERE / "known-gaps.tsv"
NOT_IN_IMAGE = HERE / "not-in-image.tsv"

# The credit FreeType's licence (FTL) asks the documentation to carry, word for word.
FREETYPE_CREDIT = (
    "Portions of this software are copyright © The FreeType Project (www.freetype.org). "
    "All rights reserved."
)

# Tried in this order at the repository root; the first that exists is the upstream text.
LICENCE_NAMES = ("LICENSE", "LICENSE.txt", "LICENSE.TXT", "LICENSE.md", "License.txt", "license.txt")
NOTICE_NAMES = ("NOTICE", "NOTICE.txt", "NOTICE.md")

LICENCE_FILE = re.compile(r"(?i)^licen[cs]e")
OTHER_NOTICE_FILE = re.compile(r"(?i)^(third.?party.?notices?|notice)")
PACKAGE_LINE = re.compile(r"^Package: (\S+) (\S+)$", re.MULTILINE)
COPYRIGHT_LINE = re.compile(r"(?i)^(copyright\s*(\(c\)|©|\d{4})|(\(c\)|©)\s*\d{4})")
# A name fetch puts in a path: GitHub's own character set, and never "." or "..".
PATH_NAME = re.compile(r"^(?!\.\.?$)[A-Za-z0-9_.-]+$")
COMMIT = re.compile(r"^[0-9a-f]{40}$")
RULE = "=" * 78
THIN_RULE = "-" * 78


@dataclass(frozen=True)
class Component:
    name: str
    version: str
    licence: str

    @property
    def key(self) -> str:
        return f"{self.name} {self.version}"


@dataclass(frozen=True)
class Text:
    """One file's bytes and where they came from, as a reviewer would look them up."""

    data: bytes
    origin: str
    location: str

    @property
    def sha256(self) -> str:
        return hashlib.sha256(self.data).hexdigest()


@dataclass(frozen=True)
class Lookup:
    """What a search for one upstream file found: its text, or why there is none.

    `settled` is False when nobody has looked yet or the fetch failed, so the answer is still unknown.
    """

    text: Text | None
    note: str | None
    settled: bool


@dataclass(frozen=True)
class Notice:
    component: Component
    licence_label: str
    copyright: str | None
    copyright_origin: str
    licence: Lookup
    apache_notice: Lookup | None
    other_notices: tuple[Text, ...]
    # Why the image does not carry the package's files although the SBOM lists the package, if it does not.
    not_in_image: str | None = None


# ---- Inputs ---------------------------------------------------------------------------------------


def nuget_components(sbom_path: Path) -> list[Component]:
    sbom = json.loads(sbom_path.read_text(encoding="utf-8"))
    components = []
    for entry in sbom.get("components", []):
        if not entry.get("purl", "").startswith("pkg:nuget/"):
            continue
        licences = [item.get("license", {}).get("id") or item.get("license", {}).get("name") or item.get("expression")
                    for item in entry.get("licenses", [])]
        licence = " OR ".join(filter(None, licences)) or "none stated in the SBOM"
        components.append(Component(entry["name"], entry["version"], licence))
    return sorted(components, key=lambda c: (c.name.casefold(), c.version))


def package_directory(component: Component) -> Path:
    cache = Path(os.environ.get("NUGET_PACKAGES") or Path.home() / ".nuget" / "packages")
    return cache / component.name.lower() / component.version.lower()


def nuspec_of(directory: Path) -> dict[str, str]:
    """The nuspec metadata notices.py uses. Parsed as XML, so entities such as &amp; come out decoded."""
    root = ElementTree.parse(next(directory.glob("*.nuspec"))).getroot()
    facts = {}
    for element in root.iter():
        tag = element.tag.rsplit("}", 1)[-1]
        if tag == "license":
            facts["licence_type"] = element.get("type", "")
            facts["licence_value"] = (element.text or "").strip()
        elif tag == "copyright" and (element.text or "").strip():
            facts["copyright"] = element.text.strip()
        elif tag == "repository":
            facts["repository"] = element.get("url", "")
            facts["commit"] = element.get("commit", "")
    return facts


def github_repository(url: str) -> str | None:
    """owner/repo for a GitHub URL, or None when the nuspec points anywhere else or names an unsafe path."""
    match = re.match(r"https://github\.com/([^/]+)/([^/]+?)(?:\.git)?/?$", url or "")
    if not match or not all(PATH_NAME.match(part) for part in match.groups()):
        return None
    return f"{match.group(1)}/{match.group(2)}"


def package_files(directory: Path) -> list[Path]:
    # Every file of the package except the nupkg itself and its hash: the extracted files are its content.
    return sorted(p for p in directory.rglob("*") if p.is_file() and not p.name.endswith((".nupkg", ".sha512")))


def read_text_file(path: Path, origin: str, location: str) -> Text:
    data = path.read_bytes()
    # A leading byte-order mark is an encoding marker, not text; everything after it is kept as it is.
    if data.startswith(b"\xef\xbb\xbf"):
        data = data[3:]
    return Text(data, origin, location)


def assembly_copyright(directory: Path, name: str) -> str | None:
    """The LegalCopyright string of the package's own assembly, from its Win32 version resource."""
    key = "LegalCopyright".encode("utf-16-le") + b"\x00\x00"
    for dll in sorted(directory.glob(f"lib/*/{name}.dll")):
        data = dll.read_bytes()
        at = data.find(key)
        if at < 0:
            continue
        start = at + len(key)
        # The value starts on the next 32-bit boundary of the resource, after zero padding.
        while start < len(data) and data[start:start + 2] == b"\x00\x00":
            start += 2
        end = data.find(b"\x00\x00", start)
        while end > 0 and (end - start) % 2:
            end = data.find(b"\x00\x00", end + 1)
        value = data[start:end].decode("utf-16-le", errors="strict").strip()
        if value:
            return value
    return None


def licence_copyright(licence: Text) -> str | None:
    """The licence text's own copyright lines, joined.

    A copyright line names a year or a copyright sign: Apache-2.0's prose ("copyright notice that is ...") is not
    one, and neither is its appendix template ("Copyright [yyyy] [name of copyright owner]").
    """
    lines = [line.strip() for line in licence.data.decode("utf-8", errors="replace").splitlines()]
    found = [line for line in lines if COPYRIGHT_LINE.match(line) and "yyyy" not in line]
    return "; ".join(found) or None


def fetched_records() -> dict[tuple[str, str, str], list[dict[str, str]]]:
    records: dict[tuple[str, str, str], list[dict[str, str]]] = {}
    if not FETCHED.exists():
        return records
    lines = FETCHED.read_text(encoding="utf-8").splitlines()
    header = lines[0].split("\t")
    for line in lines[1:]:
        row = dict(zip(header, line.split("\t")))
        records.setdefault((row["repository"], row["commit"], row["kind"]), []).append(row)
    return records


def known_gaps(path: Path = KNOWN_GAPS) -> dict[tuple[str, str], str]:
    """Accepted gaps, keyed by (package and version, licence or copyright)."""
    if not path.exists():
        return {}
    gaps = {}
    for line in path.read_text(encoding="utf-8").splitlines()[1:]:
        package, item, reason = line.split("\t", 2)
        gaps[(package, item)] = reason
    return gaps


def not_in_image(path: Path = NOT_IN_IMAGE) -> dict[str, str]:
    """Packages the SBOM lists whose files the Dockerfile deletes, by name, with the reason."""
    if not path.exists():
        return {}
    return dict(line.split("\t", 1) for line in path.read_text(encoding="utf-8").splitlines()[1:])


# ---- Resolving one package -----------------------------------------------------------------------


def upstream_lookup(records, repository: str, commit: str, kind: str) -> Lookup:
    rows = records.get((repository, commit, kind))
    if not rows:
        return Lookup(None, f"no upstream {kind} fetched for {repository} at {commit}; run notices.py fetch", False)
    for row in rows:
        if row["status"] == "200":
            return Lookup(read_text_file(UPSTREAM / row["file"], "upstream", row["url"]), None, True)
    statuses = ", ".join(f"{row['url'].rsplit('/', 1)[1]} {row['status']}" for row in rows)
    if all(row["status"] == "404" for row in rows):
        return Lookup(None, f"none at the root of {repository} at {commit} ({statuses})", True)
    return Lookup(None, f"could not fetch from {repository} at {commit} ({statuses}); run notices.py fetch", False)


def licence_of(directory: Path, facts: dict[str, str], files: list[Path], records) -> Lookup:
    """The package's own licence file, else its repository's at the recorded commit."""
    declared = directory / facts["licence_value"] if facts.get("licence_type") == "file" else None
    in_package = [p for p in files if LICENCE_FILE.match(p.relative_to(directory).parts[0])]
    if declared and declared.is_file():
        return Lookup(read_text_file(declared, "package", declared.relative_to(directory).as_posix()), None, True)
    if in_package:
        return Lookup(read_text_file(in_package[0], "package", in_package[0].relative_to(directory).as_posix()),
                      None, True)
    repository = github_repository(facts.get("repository", ""))
    commit = facts.get("commit", "")
    if repository and COMMIT.match(commit):
        return upstream_lookup(records, repository, commit, "licence")
    return Lookup(None, "no licence file in the package and no repository commit in its nuspec to fetch one from",
                  True)


def apache_notice_of(component: Component, facts: dict[str, str], records) -> Lookup | None:
    """Apache-2.0 asks redistributors to pass on the work's NOTICE, which lives in its repository."""
    if "Apache-2.0" not in component.licence:
        return None
    repository = github_repository(facts.get("repository", ""))
    commit = facts.get("commit", "")
    if repository and COMMIT.match(commit):
        return upstream_lookup(records, repository, commit, "notice")
    return Lookup(None, "no repository commit in the nuspec to look for a NOTICE at", False)


def other_notices_of(directory: Path, files: list[Path], licence: Lookup) -> tuple[Text, ...]:
    """The third-party notices a package carries besides its licence, such as SkiaSharp's native ones."""
    licence_location = licence.text.location if licence.text and licence.text.origin == "package" else None
    return tuple(read_text_file(p, "package", p.relative_to(directory).as_posix()) for p in files
                 if OTHER_NOTICE_FILE.match(p.name) and p.relative_to(directory).as_posix() != licence_location)


def copyright_of(component: Component, directory: Path, facts: dict[str, str],
                 licence: Lookup) -> tuple[str | None, str]:
    """The copyright line the package states, and which part of it states it."""
    if facts.get("copyright"):
        return facts["copyright"], "nuspec <copyright>"
    if (from_assembly := assembly_copyright(directory, component.name)):
        return from_assembly, f"LegalCopyright of {component.name}.dll"
    if licence.text and (from_licence := licence_copyright(licence.text)):
        return from_licence, "licence text"
    return None, "none stated by the nuspec, the assembly or the licence text"


def licence_label_of(component: Component, facts: dict[str, str]) -> str:
    if component.licence == "none stated in the SBOM" and facts.get("licence_type") == "file":
        return f"none stated in the SBOM; the package declares the file {facts['licence_value']}"
    return component.licence


def resolve(component: Component, records, left_out: dict[str, str]) -> Notice:
    directory = package_directory(component)
    facts = nuspec_of(directory)
    files = package_files(directory)
    licence = licence_of(directory, facts, files, records)
    copyright_, origin = copyright_of(component, directory, facts, licence)
    return Notice(component, licence_label_of(component, facts), copyright_, origin, licence,
                  apache_notice_of(component, facts, records), other_notices_of(directory, files, licence),
                  left_out.get(component.name))


# ---- Output --------------------------------------------------------------------------------------


def describe(text: Text) -> str:
    if text.origin == "package":
        return f"{text.location} in the package"
    return f"upstream {text.location}"


def source_row(c: Component, item: str, origin: str, location: str, sha256: str = "-") -> str:
    return "\t".join([c.name, c.version, c.licence, item, origin, location, sha256])


def render(notices: list[Notice]) -> tuple[bytes, str]:
    """The notices file and the source list. Identical texts appear once and are referred to by number."""
    numbers: dict[str, int] = {}
    texts: list[Text] = []

    def number(text: Text) -> int:
        if text.sha256 not in numbers:
            numbers[text.sha256] = len(texts) + 1
            texts.append(text)
        return numbers[text.sha256]

    sections = []
    sources = ["package\tversion\tlicence\titem\torigin\tlocation\tsha256"]
    for notice in notices:
        c = notice.component
        lines = [RULE, f"Package: {c.name} {c.version}", RULE]
        if notice.not_in_image:
            lines.append(f"In the image: no; {notice.not_in_image}")
            sources.append(source_row(c, "image", "not-in-image.tsv", notice.not_in_image))
        lines.append(f"Licence: {notice.licence_label}")
        if notice.copyright:
            lines.append(f"Copyright: {notice.copyright} (from the {notice.copyright_origin})")
        else:
            lines.append(f"Copyright: {notice.copyright_origin}")
        sources.append(source_row(c, "copyright", notice.copyright_origin, notice.copyright or "-"))
        if notice.licence.text:
            text = notice.licence.text
            lines.append(f"Licence text: text {number(text)} below ({describe(text)})")
            sources.append(source_row(c, "licence", text.origin, text.location, text.sha256))
        else:
            lines.append(f"Licence text: NOT YET SOURCED: {notice.licence.note}")
            sources.append(source_row(c, "licence", "gap", notice.licence.note))
        if notice.apache_notice and notice.apache_notice.text:
            text = notice.apache_notice.text
            lines.append(f"Apache-2.0 NOTICE: text {number(text)} below ({describe(text)})")
            sources.append(source_row(c, "notice", "upstream", text.location, text.sha256))
        elif notice.apache_notice:
            lines.append(f"Apache-2.0 NOTICE: {notice.apache_notice.note}")
            sources.append(source_row(c, "notice", "none", notice.apache_notice.note))
        for other in notice.other_notices:
            lines.append(f"Other notices: text {number(other)} below ({describe(other)})")
            sources.append(source_row(c, "other-notice", "package", other.location, other.sha256))
        sections.append("\n".join(lines))

    header = [
        "EclipsVault third-party notices",
        "",
        "EclipsVault redistributes the third-party software listed below. This file holds, for every NuGet",
        "package in the image's software bill of materials, its licence text, its copyright line where the",
        "package or its licence states one, the NOTICE of each Apache-2.0 package that has one, and the other",
        "notices the packages themselves carry, among them the notices for the native libraries inside",
        "SkiaSharp's libSkiaSharp. Each text is copied unchanged from the package or from the package's source",
        "repository at the commit its package records; .github/notices/sources.tsv in the EclipsVault source",
        "repository says which.",
        "",
        FREETYPE_CREDIT,
        "",
        "Other notices in the image:",
        "  /app/wwwroot/ui/3rdpartylicenses.txt  the Angular UI's runtime packages",
        "  /app/wwwroot/fonts/OFL.txt            the Geist fonts (SIL Open Font License)",
        "  /usr/share/dotnet/LICENSE.txt and /usr/share/dotnet/ThirdPartyNotices.txt  .NET and ASP.NET Core",
        "  /usr/share/doc/*/copyright            the OS packages of the Ubuntu base image",
        "",
        f"Packages: {len(notices)}. Distinct texts: {len(texts)}.",
    ]
    unsourced = [n for n in notices if not n.licence.text]
    if unsourced:
        header += ["", "Licence texts not yet sourced:"]
        header += [f"  {n.component.key}: {n.licence.note}" for n in unsourced]
    without_copyright = [n for n in notices if not n.copyright]
    if without_copyright:
        header += ["", "Packages that state no copyright line in their nuspec, assembly or licence text:"]
        header += [f"  {n.component.key}" for n in without_copyright]

    body = []
    for index, text in enumerate(texts, start=1):
        banner = [THIN_RULE, f"Text {index}: {describe(text)}", f"sha256 {text.sha256}", THIN_RULE]
        body.append("\n".join(banner).encode() + b"\n")
        body.append(text.data if text.data.endswith(b"\n") else text.data + b"\n")
    document = ("\n".join(header) + "\n\n" + "\n\n".join(sections) + "\n\n").encode("utf-8") + b"\n".join(body)
    return document, "\n".join(sources) + "\n"


# ---- Commands ------------------------------------------------------------------------------------


def build(sbom_path: Path) -> tuple[list[Notice], bytes, str]:
    records = fetched_records()
    left_out = not_in_image()
    notices = [resolve(component, records, left_out) for component in nuget_components(sbom_path)]
    document, sources = render(notices)
    return notices, document, sources


def generate(sbom_path: Path) -> int:
    _, document, sources = build(sbom_path)
    NOTICES.write_bytes(document)
    SOURCES.write_text(sources, encoding="utf-8")
    return 0


def disagreements(sbom_path: Path, notices_path: Path) -> list[str]:
    """Packages the SBOM lists that the notices file lacks, and packages it covers that the SBOM does not list."""
    expected = {(c.name, c.version) for c in nuget_components(sbom_path)}
    text = notices_path.read_text(encoding="utf-8", errors="replace") if notices_path.exists() else ""
    covered = set(PACKAGE_LINE.findall(text))
    return ([f"missing from {notices_path.name}: {name} {version}" for name, version in sorted(expected - covered)]
            + [f"in {notices_path.name} but not in the SBOM: {name} {version}"
               for name, version in sorted(covered - expected)])


def unresolved(notices: list[Notice], accepted: dict[tuple[str, str], str]) -> list[str]:
    """Licence texts, copyright lines and Apache NOTICE searches still open, apart from the accepted gaps."""
    problems = []
    for notice in notices:
        key = notice.component.key
        gaps = {"licence": not notice.licence.text, "copyright": not notice.copyright}
        for item, missing in gaps.items():
            if missing and (key, item) not in accepted:
                reason = notice.licence.note if item == "licence" else notice.copyright_origin
                problems.append(f"no {item} for {key}: {reason}")
            if not missing and (key, item) in accepted:
                problems.append(f"{key} has a {item} now; remove its {item} line from {KNOWN_GAPS.name}")
        if notice.apache_notice and not notice.apache_notice.settled:
            problems.append(f"Apache-2.0 NOTICE of {key} not looked for: {notice.apache_notice.note}")
    return problems


def check(sbom_path: Path) -> int:
    problems = disagreements(sbom_path, NOTICES)
    notices, document, sources = build(sbom_path)
    if not problems and (not NOTICES.exists() or NOTICES.read_bytes() != document):
        problems.append(f"{NOTICES.name} is not what generate writes from the SBOM and the restored packages")
    if not SOURCES.exists() or SOURCES.read_text(encoding="utf-8") != sources:
        problems.append(f"{SOURCES.name} is not what generate writes")
    problems += unresolved(notices, known_gaps())
    for problem in problems:
        print(problem, file=sys.stderr)
    if problems:
        print("Fix: bash .github/sbom/sbom.sh, then python3 -I .github/notices/notices.py fetch and generate, "
              "and commit the result.", file=sys.stderr)
    return 1 if problems else 0


def wanted_fetches(sbom_path: Path) -> set[tuple[str, str, str]]:
    """(repository, commit, licence or notice) for every upstream file resolve would look up."""
    wanted = set()
    for component in nuget_components(sbom_path):
        directory = package_directory(component)
        facts = nuspec_of(directory)
        repository = github_repository(facts.get("repository", ""))
        commit = facts.get("commit", "")
        if not (repository and COMMIT.match(commit)):
            continue
        has_licence_file = (facts.get("licence_type") == "file"
                            or any(LICENCE_FILE.match(p.relative_to(directory).parts[0])
                                   for p in package_files(directory)))
        if not has_licence_file:
            wanted.add((repository, commit, "licence"))
        if "Apache-2.0" in component.licence:
            wanted.add((repository, commit, "notice"))
    return wanted


def download(url: str) -> tuple[bytes, str]:
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            return response.read(), str(response.status)
    except urllib.error.HTTPError as error:
        return b"", str(error.code)
    except urllib.error.URLError as error:
        return b"", f"error:{type(error.reason).__name__}"


def fetch(sbom_path: Path) -> int:
    """Fetch what resolve would otherwise report as not looked for. Needs raw.githubusercontent.com."""
    records = fetched_records()
    settled = {key for key, rows in records.items() if all(row["status"] in ("200", "404") for row in rows)}
    # A search that failed on the network is repeated whole: its earlier answers say nothing.
    rows = [row for key, group in records.items() if key in settled for row in group]
    for repository, commit, kind in sorted(wanted_fetches(sbom_path) - settled):
        for name in LICENCE_NAMES if kind == "licence" else NOTICE_NAMES:
            url = f"https://raw.githubusercontent.com/{repository}/{commit}/{name}"
            data, status = download(url)
            file = f"{repository}/{commit}/{name}" if status == "200" else "-"
            if status == "200":
                target = (UPSTREAM / file).resolve()
                if not target.is_relative_to(UPSTREAM.resolve()):
                    raise ValueError(f"refusing to write {target}, outside {UPSTREAM}")
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
            rows.append({"repository": repository, "commit": commit, "kind": kind, "url": url,
                         "status": status, "file": file})
            print(f"{status} {url}")
            # Only a 404 says this name is absent; after anything else a later name could stand in for it.
            if status != "404":
                break

    UPSTREAM.mkdir(parents=True, exist_ok=True)
    columns = ["repository", "commit", "kind", "url", "status", "file"]
    ordered = sorted(rows, key=lambda r: (r["repository"], r["commit"], r["kind"], r["url"]))
    lines = ["\t".join(columns)] + ["\t".join(r[c] for c in columns) for r in ordered]
    FETCHED.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("command", choices=["fetch", "generate", "check"])
    parser.add_argument("--sbom", type=Path, default=SBOM)
    arguments = parser.parse_args(argv)
    return {"fetch": fetch, "generate": generate, "check": check}[arguments.command](arguments.sbom)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
