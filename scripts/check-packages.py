"""Validate local packages; --release also requires confirmed publication metadata."""
import argparse
from pathlib import Path
import re
import xml.etree.ElementTree as ET
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
TFMS = {"net481", "net8.0", "net10.0"}


def check(directory, release=False):
    version = ET.parse(ROOT / "build/Version.props").findtext(".//BrowserDockVersion")
    if release:
        expected = {f"{name}.{version}.{extension}" for name in ("BrowserDock", "BrowserDock.Legacy") for extension in ("nupkg", "snupkg")}
        actual = {path.name for path in directory.glob("*.*nupkg")}
        assert actual == expected, "Release directory must contain only the two packages and their symbols"
    for package_id in ("BrowserDock", "BrowserDock.Legacy"):
        stem = directory / f"{package_id}.{version}"
        with ZipFile(str(stem) + ".nupkg") as package:
            names = package.namelist()
            spec = ET.fromstring(package.read(f"{package_id}.nuspec"))
            ns = {"n": spec.tag.split("}")[0][1:]}
            metadata = spec.find("n:metadata", ns)
            value = lambda name: metadata.findtext(f"n:{name}", namespaces=ns)
            assert value("id") == package_id, "Package identity mismatch"
            assert value("version") == version, "Package version mismatch"
            assert value("readme") == "README.md", "Package README missing"
            assert package.read("THIRD-PARTY-NOTICES.md"), "Dependency notices missing"
            readme = package.read("README.md").decode()
            assert not re.search(r"\]\((?!https://|#)[^)]+\)", readme), "Relative package README link"
            libs = {name for name in names if name.startswith("lib/")}
            assert libs == {f"lib/{tfm}/{package_id}.dll" for tfm in TFMS}, "Unexpected runtime payload"
            assert not any(name.endswith((".exe", ".trx", ".py")) for name in names), "Unexpected payload"
            dependencies = metadata.findall("n:dependencies/n:group", ns)
            if package_id == "BrowserDock.Legacy":
                assert len(dependencies) == 3, "Missing dependency target"
                for group in dependencies:
                    dep = group.find("n:dependency[@id='BrowserDock']", ns)
                    assert dep is not None and dep.get("version") == version, "Core dependency mismatch"
            if release:
                props = ET.parse(ROOT / "build/Package.props")
                authors = props.findtext(".//Authors")
                url = props.findtext(".//RepositoryUrl")
                license_id = props.findtext(".//PackageLicenseExpression")
                assert authors and value("authors") == authors, "Confirm package author in build/Package.props"
                assert url and url.startswith("https://github.com/"), "Confirm public repository URL"
                assert value("projectUrl") == url, "Project URL mismatch"
                repository = metadata.find("n:repository", ns)
                assert repository is not None and repository.get("url") == url, "Repository metadata missing"
                assert re.fullmatch(r"[a-f0-9]{40}", repository.get("commit", "")), "Commit metadata missing"
                license_node = metadata.find("n:license", ns)
                assert license_id and license_node is not None and license_node.text == license_id, "Confirm SPDX license"
                assert license_node.get("type") == "expression", "SPDX expression required"
                assert package.read("LICENSE") == (ROOT / "LICENSE").read_bytes(), "License mismatch"
        with ZipFile(str(stem) + ".snupkg") as symbols:
            pdbs = {name for name in symbols.namelist() if name.endswith(".pdb")}
            assert pdbs == {f"lib/{tfm}/{package_id}.pdb" for tfm in TFMS}, "Missing portable symbols"
        print(f"PASS {package_id} {version} ({'release' if release else 'local candidate'})")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--release", action="store_true")
    args = parser.parse_args()
    try:
        check(args.directory, args.release)
    except (AssertionError, OSError, KeyError) as error:
        parser.exit(1, f"Package validation failed: {error}\n")
