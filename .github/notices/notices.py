"""Third-party notices for the NuGet packages the image ships (issue #95).

    fetch     download, at the commit each package's nuspec records, the upstream licence of every package that
              carries no licence file, and the NOTICE of every Apache-2.0 package, into upstream/
    generate  write THIRD-PARTY-NOTICES.txt at the repository root and sources.tsv beside this script
    check     fail when either committed file differs from what generate would write, when the SBOM lists a
              package the notices file does not cover, or when a licence text is missing and not a known gap

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

# The credit FreeType's licence (FTL) asks the documentation to carry; issue #95 quotes it.
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
RULE = "=" * 78
THIN_RULE = "-" * 78


@dataclass(frozen=True)
class Component:
    name: str
    version: str
    licence: str


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
class Notice:
    component: Component
    copyright: str | None
    copyright_origin: str
    licence: Text | None
    gap: str | None
    apache_notice: Text | None
    apache_notice_absent: str | None
    other_notices: tuple[Text, ...]


# ---- Inputs ---------------------------------------------------------------------------------------


def nuget_components(sbom_path: Path) -> list[Component]:
    sbom = json.loads(sbom_path.read_text(encoding="utf-8"))
    components = []
    for entry in sbom.get("components", []):
        if not entry.get("purl", "").startswith("pkg:nuget/"):
            continue
        licences = [item.get("license", {}).get("id") or item.get("license", {}).get("name") or item.get("expression")
                    for item in entry.get("licenses", [])]
        components.append(Component(entry["name"], entry["version"], " OR ".join(filter(None, licences)) or "none stated"))
    return sorted(components, key=lambda c: (c.name.casefold(), c.version))


def package_directory(component: Component) -> Path:
    cache = Path(os.environ.get("NUGET_PACKAGES") or Path.home() / ".nuget" / "packages")
    return cache / component.name.lower() / component.version.lower()


def nuspec_of(directory: Path) -> dict[str, str]:
    path = next(directory.glob("*.nuspec"))
    text = path.read_text(encoding="utf-8-sig")
    facts = {}
    if licence := re.search(r'<license\s+type="(\w+)"[^>]*>([^<]*)</license>', text):
        facts["licence_type"], facts["licence_value"] = licence.group(1), licence.group(2).strip()
    if copyright_ := re.search(r"<copyright>([^<]*)</copyright>", text):
        facts["copyright"] = copyright_.group(1).strip()
    if repository := re.search(r"<repository\b([^>]*)/?>", text):
        attributes = dict(re.findall(r'(\w+)="([^"]*)"', repository.group(1)))
        facts["repository"] = attributes.get("url", "")
        facts["commit"] = attributes.get("commit", "")
    return facts


def github_repository(url: str) -> str | None:
    """owner/repo for a GitHub URL, or None when the nuspec points anywhere else."""
    match = re.match(r"https://github\.com/([^/]+)/([^/]+?)(?:\.git)?/?$", url or "")
    return f"{match.group(1)}/{match.group(2)}" if match else None


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
    for dll in sorted(directory.glob(f"lib/*/{name}.dll")):
        data = dll.read_bytes()
        key = "LegalCopyright".encode("utf-16-le") + b"\x00\x00"
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
    found = [line for line in lines
             if COPYRIGHT_LINE.match(line) and "yyyy" not in line]
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


def known_gaps() -> dict[str, str]:
    if not KNOWN_GAPS.exists():
        return {}
    gaps = {}
    for line in KNOWN_GAPS.read_text(encoding="utf-8").splitlines()[1:]:
        package, reason = line.split("\t", 1)
        gaps[package] = reason
    return gaps


# ---- Resolving one package -----------------------------------------------------------------------


def upstream_text(records, repository: str, commit: str, kind: str) -> tuple[Text | None, str | None]:
    """The fetched upstream file, or None with the reason no text exists or could be fetched."""
    rows = records.get((repository, commit, kind))
    if not rows:
        return None, f"no upstream {kind} fetched for {repository} at {commit}; run notices.py fetch"
    for row in rows:
        if row["status"] == "200":
            path = UPSTREAM / row["file"]
            return read_text_file(path, "upstream", row["url"]), None
    statuses = ", ".join(f"{row['url'].rsplit('/', 1)[1]} {row['status']}" for row in rows)
    if all(row["status"] == "404" for row in rows):
        return None, f"none at the root of {repository} at {commit} ({statuses})"
    return None, f"could not fetch from {repository} at {commit} ({statuses})"


def resolve(component: Component, records) -> Notice:
    directory = package_directory(component)
    facts = nuspec_of(directory)
    files = package_files(directory)
    relative = {p: p.relative_to(directory).as_posix() for p in files}
    repository = github_repository(facts.get("repository", ""))
    commit = facts.get("commit", "")

    licence, gap = None, None
    declared = directory / facts["licence_value"] if facts.get("licence_type") == "file" else None
    in_package = [p for p in files if LICENCE_FILE.match(relative[p].split("/")[0])]
    if declared and declared.is_file():
        licence = read_text_file(declared, "package", relative[declared])
    elif in_package:
        licence = read_text_file(in_package[0], "package", relative[in_package[0]])
    elif repository and commit:
        licence, gap = upstream_text(records, repository, commit, "licence")
    else:
        gap = "no licence file in the package and no repository commit in its nuspec to fetch one from"

    apache_notice, apache_absent = None, None
    if "Apache-2.0" in component.licence:
        if repository and commit:
            apache_notice, apache_absent = upstream_text(records, repository, commit, "notice")
        else:
            apache_absent = "no repository commit in the nuspec to look for a NOTICE at"

    licence_path = next((p for p in files if licence and licence.origin == "package" and relative[p] == licence.location), None)
    others = tuple(read_text_file(p, "package", relative[p]) for p in files
                   if p != licence_path and OTHER_NOTICE_FILE.match(p.name))

    if facts.get("copyright"):
        copyright_, origin = facts["copyright"], "nuspec <copyright>"
    elif (from_assembly := assembly_copyright(directory, component.name)):
        copyright_, origin = from_assembly, f"LegalCopyright of {component.name}.dll"
    elif licence and (from_licence := licence_copyright(licence)):
        copyright_, origin = from_licence, "licence text"
    else:
        copyright_, origin = None, "none stated by the nuspec, the assembly or the licence text"

    return Notice(component, copyright_, origin, licence, gap, apache_notice, apache_absent, others)


# ---- Output --------------------------------------------------------------------------------------


def describe(text: Text) -> str:
    if text.origin == "package":
        return f"{text.location} in the package"
    return f"upstream {text.location}"


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
        lines = [RULE, f"Package: {c.name} {c.version}", RULE, f"Licence: {c.licence}"]
        if notice.copyright:
            lines.append(f"Copyright: {notice.copyright} (from the {notice.copyright_origin})")
        else:
            lines.append(f"Copyright: {notice.copyright_origin}")
        sources.append(f"{c.name}\t{c.version}\t{c.licence}\tcopyright\t{notice.copyright_origin}\t{notice.copyright or '-'}\t-")
        if notice.licence:
            lines.append(f"Licence text: text {number(notice.licence)} below ({describe(notice.licence)})")
            sources.append(f"{c.name}\t{c.version}\t{c.licence}\tlicence\t{notice.licence.origin}\t{notice.licence.location}\t{notice.licence.sha256}")
        else:
            lines.append(f"Licence text: NOT YET SOURCED: {notice.gap}")
            sources.append(f"{c.name}\t{c.version}\t{c.licence}\tlicence\tgap\t{notice.gap}\t-")
        if notice.apache_notice:
            lines.append(f"Apache-2.0 NOTICE: text {number(notice.apache_notice)} below ({describe(notice.apache_notice)})")
            sources.append(f"{c.name}\t{c.version}\t{c.licence}\tnotice\tupstream\t{notice.apache_notice.location}\t{notice.apache_notice.sha256}")
        elif notice.apache_notice_absent:
            lines.append(f"Apache-2.0 NOTICE: {notice.apache_notice_absent}")
            sources.append(f"{c.name}\t{c.version}\t{c.licence}\tnotice\tnone\t{notice.apache_notice_absent}\t-")
        for other in notice.other_notices:
            lines.append(f"Other notices: text {number(other)} below ({describe(other)})")
            sources.append(f"{c.name}\t{c.version}\t{c.licence}\tother-notice\tpackage\t{other.location}\t{other.sha256}")
        sections.append("\n".join(lines))

    gaps = [n for n in notices if n.gap]
    header = [
        "EclipsVault third-party notices",
        "",
        "EclipsVault redistributes the third-party software listed below. This file holds, for every NuGet",
        "package in the image's software bill of materials, its copyright line and licence text, the NOTICE of",
        "each Apache-2.0 package that has one, and the other notices the packages themselves carry, among them",
        "the notices for the native libraries inside SkiaSharp's libSkiaSharp. Each text is copied unchanged",
        "from the package or from the package's source repository at the commit its package records;",
        ".github/notices/sources.tsv says which.",
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
    if gaps:
        header += ["", "Licence texts not yet sourced:"] + [f"  {n.component.name} {n.component.version}: {n.gap}" for n in gaps]

    body = []
    for index, text in enumerate(texts, start=1):
        body.append("\n".join([THIN_RULE, f"Text {index}: {describe(text)}", f"sha256 {text.sha256}", THIN_RULE]).encode() + b"\n")
        body.append(text.data if text.data.endswith(b"\n") else text.data + b"\n")
    document = ("\n".join(header) + "\n\n" + "\n\n".join(sections) + "\n\n").encode("utf-8") + b"\n".join(body)
    return document, "\n".join(sources) + "\n"


# ---- Commands ------------------------------------------------------------------------------------


def build(sbom_path: Path) -> tuple[list[Notice], bytes, str]:
    records = fetched_records()
    notices = [resolve(component, records) for component in nuget_components(sbom_path)]
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
    covered = set(PACKAGE_LINE.findall(notices_path.read_text(encoding="utf-8", errors="replace"))) if notices_path.exists() else set()
    return ([f"missing from {notices_path.name}: {name} {version}" for name, version in sorted(expected - covered)]
            + [f"in {notices_path.name} but not in the SBOM: {name} {version}" for name, version in sorted(covered - expected)])


def check(sbom_path: Path) -> int:
    problems = disagreements(sbom_path, NOTICES)
    notices, document, sources = build(sbom_path)
    if not problems and (not NOTICES.exists() or NOTICES.read_bytes() != document):
        problems.append(f"{NOTICES.name} is not what generate writes from the SBOM and the restored packages")
    if not SOURCES.exists() or SOURCES.read_text(encoding="utf-8") != sources:
        problems.append(f"{SOURCES.name} is not what generate writes")
    accepted = known_gaps()
    for notice in notices:
        key = f"{notice.component.name} {notice.component.version}"
        if notice.gap and key not in accepted:
            problems.append(f"no licence text for {key}: {notice.gap}")
        if not notice.gap and key in accepted:
            problems.append(f"{key} is listed in {KNOWN_GAPS.name} but now has a licence text; remove it there")
    for problem in problems:
        print(problem, file=sys.stderr)
    if problems:
        print("Fix: bash .github/sbom/sbom.sh, then python3 -I .github/notices/notices.py fetch (for a new or "
              "upgraded package without a licence file) and generate, and commit the result.", file=sys.stderr)
    return 1 if problems else 0


def fetch(sbom_path: Path) -> int:
    """Fetch what resolve would otherwise report as not fetched. Needs raw.githubusercontent.com."""
    records = fetched_records()
    wanted: set[tuple[str, str, str]] = set()
    for component in nuget_components(sbom_path):
        directory = package_directory(component)
        facts = nuspec_of(directory)
        repository = github_repository(facts.get("repository", ""))
        commit = facts.get("commit", "")
        if not (repository and commit):
            continue
        has_licence_file = (facts.get("licence_type") == "file"
                            or any(LICENCE_FILE.match(p.relative_to(directory).parts[0]) for p in package_files(directory)))
        if not has_licence_file:
            wanted.add((repository, commit, "licence"))
        if "Apache-2.0" in component.licence:
            wanted.add((repository, commit, "notice"))

    rows = [row for group in records.values() for row in group]
    for repository, commit, kind in sorted(wanted - set(records)):
        for name in LICENCE_NAMES if kind == "licence" else NOTICE_NAMES:
            url = f"https://raw.githubusercontent.com/{repository}/{commit}/{name}"
            try:
                with urllib.request.urlopen(url, timeout=30) as response:
                    data, status = response.read(), str(response.status)
            except urllib.error.HTTPError as error:
                data, status = b"", str(error.code)
            except urllib.error.URLError as error:
                data, status = b"", f"error:{type(error.reason).__name__}"
            file = f"{repository}/{commit}/{name}" if status == "200" else "-"
            if status == "200":
                (UPSTREAM / file).parent.mkdir(parents=True, exist_ok=True)
                (UPSTREAM / file).write_bytes(data)
            rows.append({"repository": repository, "commit": commit, "kind": kind, "url": url, "status": status, "file": file})
            print(f"{status} {url}")
            if status == "200":
                break

    UPSTREAM.mkdir(parents=True, exist_ok=True)
    columns = ["repository", "commit", "kind", "url", "status", "file"]
    ordered = sorted(rows, key=lambda r: (r["repository"], r["commit"], r["kind"], r["url"]))
    FETCHED.write_text("\n".join(["\t".join(columns)] + ["\t".join(r[c] for c in columns) for r in ordered]) + "\n", encoding="utf-8")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("command", choices=["fetch", "generate", "check"])
    parser.add_argument("--sbom", type=Path, default=SBOM)
    arguments = parser.parse_args(argv)
    return {"fetch": fetch, "generate": generate, "check": check}[arguments.command](arguments.sbom)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
