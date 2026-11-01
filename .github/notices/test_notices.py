"""Tests for notices.py: the notices file must cover exactly the NuGet packages the SBOM lists, and check must
refuse a notice it cannot stand behind."""

import json
import tempfile
import unittest
from pathlib import Path

import notices

LICENCE = notices.Text(b"Same licence\n", "package", "LICENSE.txt")
FOUND = notices.Lookup(LICENCE, None, True)
NO_NOTICE_AT_ROOT = notices.Lookup(None, "none at the root of serilog/serilog at abc (NOTICE 404)", True)
NOTICE_NOT_LOOKED_FOR = notices.Lookup(None, "no upstream notice fetched for serilog/serilog at abc", False)


def notice(name, copyright_="Copyright Example", licence=FOUND, apache_notice=None, licence_name="MIT",
           not_in_image=None):
    return notices.Notice(notices.Component(name, "1.0.0", licence_name), licence_name, copyright_,
                          "nuspec <copyright>" if copyright_ else "none stated", licence, apache_notice, (),
                          not_in_image)


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
    path.write_text("\n".join(f"Package: {name} {version}\nLicence: MIT\n" for name, version in packages),
                    encoding="utf-8")
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


class UnresolvedTests(unittest.TestCase):
    def test_an_apache_notice_nobody_looked_for_fails_the_check(self):
        problems = notices.unresolved([notice("Serilog", apache_notice=NOTICE_NOT_LOOKED_FOR)], {})

        self.assertEqual(["Apache-2.0 NOTICE of Serilog 1.0.0 not looked for: "
                          "no upstream notice fetched for serilog/serilog at abc"], problems)

    def test_an_apache_package_whose_repository_has_no_notice_passes(self):
        self.assertEqual([], notices.unresolved([notice("Serilog", apache_notice=NO_NOTICE_AT_ROOT)], {}))

    def test_a_missing_copyright_line_fails_unless_it_is_a_known_gap(self):
        missing = notice("Serilog.Sinks.File", copyright_=None)

        self.assertEqual(["no copyright for Serilog.Sinks.File 1.0.0: none stated"], notices.unresolved([missing], {}))
        self.assertEqual([], notices.unresolved([missing], {("Serilog.Sinks.File 1.0.0", "copyright"): "accepted"}))

    def test_a_missing_licence_text_fails_unless_it_is_a_known_gap(self):
        missing = notice("Microsoft.SqlServer.Server", licence=notices.Lookup(None, "no licence file", True))

        self.assertEqual(["no licence for Microsoft.SqlServer.Server 1.0.0: no licence file"],
                         notices.unresolved([missing], {}))
        self.assertEqual([], notices.unresolved([missing], {("Microsoft.SqlServer.Server 1.0.0", "licence"): "x"}))

    def test_a_known_gap_that_has_been_filled_must_leave_the_gap_list(self):
        problems = notices.unresolved([notice("QRCoder")], {("QRCoder 1.0.0", "licence"): "accepted"})

        self.assertEqual(["QRCoder 1.0.0 has a licence now; remove its licence line from known-gaps.tsv"], problems)


class FetchPathTests(unittest.TestCase):
    def test_a_github_url_becomes_owner_and_repository(self):
        self.assertEqual("serilog/serilog-sinks-debug",
                         notices.github_repository("https://github.com/serilog/serilog-sinks-debug.git"))

    def test_a_url_that_climbs_out_of_the_upstream_folder_is_refused(self):
        self.assertIsNone(notices.github_repository("https://github.com/../.."))
        self.assertIsNone(notices.github_repository("https://github.com/owner/.."))

    def test_only_a_full_commit_hash_counts_as_a_recorded_commit(self):
        self.assertTrue(notices.COMMIT.match("c00e17fdeb615d36c062569f7342f72c9ece4aa7"))
        self.assertFalse(notices.COMMIT.match("main"))
        self.assertFalse(notices.COMMIT.match(".."))


class NuspecTests(unittest.TestCase):
    def test_xml_entities_in_the_copyright_are_decoded(self):
        directory = Path(tempfile.mkdtemp())
        (directory / "example.nuspec").write_text(
            '<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
            "<metadata><copyright>A &amp; B &#169; 2020</copyright></metadata></package>", encoding="utf-8")

        self.assertEqual("A & B © 2020", notices.nuspec_of(directory)["copyright"])


class CopyrightLineTests(unittest.TestCase):
    def test_a_licence_copyright_line_with_a_year_is_taken(self):
        licence = notices.Text(b"MIT License\n\nCopyright (c) 2016 Example Ltd\n\nPermission is hereby granted",
                               "package", "LICENSE")

        self.assertEqual("Copyright (c) 2016 Example Ltd", notices.licence_copyright(licence))

    def test_apache_prose_and_its_appendix_template_are_not_copyright_lines(self):
        licence = notices.Text(b"copyright notice that is included in or attached to the work\n"
                               b"(c) You must retain, in the Source form of any Derivative Works\n"
                               b"Copyright [yyyy] [name of copyright owner]\n"
                               b"Copyright {yyyy} {name of copyright owner}\n", "upstream", "LICENSE")

        self.assertIsNone(notices.licence_copyright(licence))


class RenderTests(unittest.TestCase):
    def test_two_packages_with_the_same_licence_text_share_one_numbered_copy(self):
        document, _ = notices.render([notice("A"), notice("B")])

        self.assertEqual(1, document.count(b"Same licence\n"))
        self.assertEqual(2, document.count(b"Licence text: text 1 below (LICENSE.txt in the package)"))

    def test_a_package_without_a_sourced_licence_says_so_instead_of_holding_a_text(self):
        gap = notice("C", licence=notices.Lookup(None, "no licence file in the package", True))

        document, sources = notices.render([gap])

        self.assertIn(b"Licence text: NOT YET SOURCED: no licence file in the package", document)
        self.assertIn("C\t1.0.0\tMIT\tlicence\tgap\tno licence file in the package\t-", sources)

    def test_a_package_the_image_does_not_carry_says_so_in_its_entry(self):
        sni = notice("Microsoft.Data.SqlClient.SNI.runtime", not_in_image="the Dockerfile deletes its files")

        document, _ = notices.render([sni])

        self.assertIn(b"Package: Microsoft.Data.SqlClient.SNI.runtime 1.0.0\n" + notices.RULE.encode() +
                      b"\nIn the image: no; the Dockerfile deletes its files\n", document)

    def test_a_package_the_image_carries_has_no_such_line(self):
        document, _ = notices.render([notice("QRCoder")])

        self.assertNotIn(b"In the image:", document)

    def test_the_packages_left_out_of_the_image_are_read_by_name(self):
        path = Path(tempfile.mkdtemp()) / "not-in-image.tsv"
        path.write_text("package\treason\nMicrosoft.Data.SqlClient.SNI.runtime\tthe Dockerfile deletes its files\n",
                        encoding="utf-8")

        self.assertEqual({"Microsoft.Data.SqlClient.SNI.runtime": "the Dockerfile deletes its files"},
                         notices.not_in_image(path))

    def test_the_notices_file_carries_freetypes_credit_sentence_word_for_word(self):
        document, _ = notices.render([])

        self.assertIn("Portions of this software are copyright © The FreeType Project (www.freetype.org). "
                      "All rights reserved.".encode("utf-8"), document)


if __name__ == "__main__":
    unittest.main()
