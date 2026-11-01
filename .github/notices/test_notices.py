"""Tests for notices.py: the notices file must cover exactly the NuGet packages the SBOM lists."""

import json
import tempfile
import unittest
from pathlib import Path

import notices


def write_sbom(directory: Path, packages: list[tuple[str, str]]) -> Path:
    path = directory / "sbom.json"
    components = [{"name": name, "version": version, "purl": f"pkg:nuget/{name}@{version}",
                   "licenses": [{"license": {"id": "MIT"}}]} for name, version in packages]
    # An npm component sits in the same SBOM; the Angular build's own licence file covers it.
    components.append({"name": "rxjs", "version": "7.8.2", "purl": "pkg:npm/rxjs@7.8.2"})
    path.write_text(json.dumps({"components": components}), encoding="utf-8")
    return path


def write_notices(directory: Path, packages: list[tuple[str, str]]) -> Path:
    path = directory / "THIRD-PARTY-NOTICES.txt"
    path.write_text("\n".join(f"Package: {name} {version}\nLicence: MIT\n" for name, version in packages), encoding="utf-8")
    return path


class SbomAgreementTests(unittest.TestCase):
    def setUp(self):
        self.directory = Path(tempfile.mkdtemp())

    def test_a_package_the_sbom_lists_but_the_notices_file_lacks_is_reported_missing(self):
        sbom = write_sbom(self.directory, [("Serilog", "4.2.0"), ("QRCoder", "1.8.0")])
        notices_file = write_notices(self.directory, [("Serilog", "4.2.0")])

        problems = notices.disagreements(sbom, notices_file)

        self.assertEqual(["missing from THIRD-PARTY-NOTICES.txt: QRCoder 1.8.0"], problems)

    def test_an_upgraded_package_is_missing_at_its_new_version_and_stale_at_its_old_one(self):
        sbom = write_sbom(self.directory, [("Serilog", "4.3.0")])
        notices_file = write_notices(self.directory, [("Serilog", "4.2.0")])

        problems = notices.disagreements(sbom, notices_file)

        self.assertEqual(["missing from THIRD-PARTY-NOTICES.txt: Serilog 4.3.0",
                          "in THIRD-PARTY-NOTICES.txt but not in the SBOM: Serilog 4.2.0"], problems)

    def test_npm_components_are_not_expected_in_the_nuget_notices(self):
        sbom = write_sbom(self.directory, [("Serilog", "4.2.0")])
        notices_file = write_notices(self.directory, [("Serilog", "4.2.0")])

        self.assertEqual([], notices.disagreements(sbom, notices_file))

    def test_a_missing_notices_file_reports_every_package(self):
        sbom = write_sbom(self.directory, [("Serilog", "4.2.0")])

        problems = notices.disagreements(sbom, self.directory / "THIRD-PARTY-NOTICES.txt")

        self.assertEqual(["missing from THIRD-PARTY-NOTICES.txt: Serilog 4.2.0"], problems)


class CopyrightLineTests(unittest.TestCase):
    def test_a_licence_copyright_line_with_a_year_is_taken(self):
        licence = notices.Text(b"MIT License\n\nCopyright (c) 2016 Example Ltd\n\nPermission is hereby granted", "package", "LICENSE")

        self.assertEqual("Copyright (c) 2016 Example Ltd", notices.licence_copyright(licence))

    def test_apache_prose_and_its_appendix_template_are_not_copyright_lines(self):
        licence = notices.Text(b"copyright notice that is included in or attached to the work\n"
                               b"(c) You must retain, in the Source form of any Derivative Works\n"
                               b"Copyright [yyyy] [name of copyright owner]\n"
                               b"Copyright {yyyy} {name of copyright owner}\n", "upstream", "LICENSE")

        self.assertIsNone(notices.licence_copyright(licence))


class RenderTests(unittest.TestCase):
    def test_two_packages_with_the_same_licence_text_share_one_numbered_copy(self):
        text = notices.Text(b"Same licence\n", "package", "LICENSE.txt")
        first = notices.Notice(notices.Component("A", "1.0.0", "MIT"), "Copyright A", "nuspec <copyright>",
                               text, None, None, None, ())
        second = notices.Notice(notices.Component("B", "2.0.0", "MIT"), "Copyright B", "nuspec <copyright>",
                                text, None, None, None, ())

        document, _ = notices.render([first, second])

        self.assertEqual(1, document.count(b"Same licence\n"))
        self.assertEqual(2, document.count(b"Licence text: text 1 below (LICENSE.txt in the package)"))

    def test_a_package_without_a_sourced_licence_says_so_instead_of_holding_a_text(self):
        gap = notices.Notice(notices.Component("C", "1.0.0", "MIT"), "Copyright C", "nuspec <copyright>",
                             None, "no licence file in the package", None, None, ())

        document, sources = notices.render([gap])

        self.assertIn(b"Licence text: NOT YET SOURCED: no licence file in the package", document)
        self.assertIn("C\t1.0.0\tMIT\tlicence\tgap\tno licence file in the package\t-", sources)

    def test_the_freetype_credit_is_in_every_notices_file(self):
        document, _ = notices.render([])

        self.assertIn(notices.FREETYPE_CREDIT.encode("utf-8"), document)


if __name__ == "__main__":
    unittest.main()
