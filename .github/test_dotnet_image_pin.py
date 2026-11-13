"""Offline tests for dotnet-image-pin.py: the gate must go red, not only stay green."""

import importlib.util
import io
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

SCRIPT = Path(__file__).with_name("dotnet-image-pin.py")
spec = importlib.util.spec_from_file_location("dotnet_image_pin", SCRIPT)
pin = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pin)

# A trimmed 10.0 index: 10.0.15 is a security release, 10.0.16 is not, 10.0.14 is older.
INDEX = {
    "channel-version": "10.0",
    "releases": [
        {"security": False, "aspnetcore-runtime": {"version": "10.0.16"}, "sdk": {"version": "10.0.405"}},
        {"security": True, "aspnetcore-runtime": {"version": "10.0.15"}, "sdk": {"version": "10.0.404"},
         "sdks": [{"version": "10.0.404"}, {"version": "10.0.115"}]},
        {"security": False, "aspnetcore-runtime": {"version": "10.0.14"}, "sdk": {"version": "10.0.403"}},
        {"security": False, "aspnetcore-runtime": {"version": "10.0.0-rc.2.25502.107"}},
    ],
}

NODE = "FROM node:24.22.0-bookworm-slim@sha256:aaaa AS ui\n"


def dockerfile(sdk: str, aspnet: str | None) -> str:
    text = NODE + f"FROM mcr.microsoft.com/dotnet/sdk:{sdk}@sha256:bbbb AS build\n"
    if aspnet is not None:
        text += f"FROM mcr.microsoft.com/dotnet/aspnet:{aspnet}@sha256:cccc AS runtime\n"
    return text


def run(text: str) -> tuple[int, str]:
    """Run main() on a Dockerfile, with the fixture index in place of the network."""
    with tempfile.TemporaryDirectory() as folder:
        path = Path(folder) / "Dockerfile"
        path.write_text(text, encoding="utf-8")
        original_argv, original_fetch = sys.argv, pin.fetch_index
        sys.argv = ["dotnet-image-pin.py", str(path)]
        pin.fetch_index = lambda channel, problems: INDEX
        output = io.StringIO()
        try:
            with redirect_stdout(output):
                code = pin.main()
        finally:
            sys.argv, pin.fetch_index = original_argv, original_fetch
    return code, output.getvalue()


class DotnetImagePinTests(unittest.TestCase):
    def test_current_pins_pass_and_the_node_image_is_ignored(self):
        code, output = run(dockerfile("10.0.405", "10.0.16"))
        self.assertEqual(0, code, output)
        self.assertNotIn("node", output)

    def test_a_pin_behind_a_security_release_fails_naming_the_image(self):
        code, output = run(dockerfile("10.0.405", "10.0.14"))
        self.assertEqual(1, code)
        self.assertIn("::error::mcr.microsoft.com/dotnet/aspnet:10.0.14 is behind the latest patch 10.0.16. "
                      "The newer release 10.0.15 is a security release", output)

    def test_a_pin_behind_only_a_non_security_patch_warns_without_failing(self):
        code, output = run(dockerfile("10.0.405", "10.0.15"))
        self.assertEqual(0, code, output)
        self.assertIn("::warning::mcr.microsoft.com/dotnet/aspnet:10.0.15 is behind the latest patch 10.0.16, "
                      "which is not a security release", output)

    def test_a_tag_missing_from_the_index_fails(self):
        code, output = run(dockerfile("10.0.405", "10.0.99"))
        self.assertEqual(1, code)
        self.assertIn("aspnet:10.0.99 is not in the .NET 10.0 release index", output)

    def test_a_suffixed_tag_fails(self):
        code, output = run(dockerfile("10.0.405", "10.0.16-noble"))
        self.assertEqual(1, code)
        self.assertIn("aspnet:10.0.16-noble is not a plain patch tag", output)

    def test_a_dockerfile_without_an_aspnet_image_fails(self):
        code, output = run(dockerfile("10.0.405", None))
        self.assertEqual(1, code)
        self.assertIn("Dockerfile does not pin mcr.microsoft.com/dotnet/aspnet", output)

    def test_an_unreadable_index_fails(self):
        def unreadable(channel, problems):
            problems.append(("", "::error::Could not read the .NET 10.0 release index"))
            return None

        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "Dockerfile"
            path.write_text(dockerfile("10.0.405", "10.0.16"), encoding="utf-8")
            original_argv, original_fetch = sys.argv, pin.fetch_index
            sys.argv, pin.fetch_index = ["dotnet-image-pin.py", str(path)], unreadable
            try:
                with redirect_stdout(io.StringIO()):
                    code = pin.main()
            finally:
                sys.argv, pin.fetch_index = original_argv, original_fetch
        self.assertEqual(1, code)

    def test_previews_never_count_as_the_latest_patch(self):
        self.assertNotIn("10.0.0-rc.2.25502.107", pin.component_versions(INDEX, "aspnet"))


if __name__ == "__main__":
    unittest.main()
