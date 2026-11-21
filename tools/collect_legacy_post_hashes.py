"""Regenerate the generated legacy PostGenerate whitelist.

Sources:
  * tests/fixtures/legacy_post_*.base64.txt (historical official helpers)
  * 交付/release_<version>/*Setup*.exe (previous local/GitHub release assets)

Run before a release build. The checked-in tools/legacy_post_hashes.json is the
fallback for clean clones that do not carry the release directories.
"""
from pathlib import Path
import base64
import hashlib
import json
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
TARGET = ROOT / "tools/legacy_post_hashes.json"


def load():
    data = json.loads(TARGET.read_text(encoding="utf-8"))
    assert data.get("schema") == 1
    return {(e["path"], e["sha256"]) for e in data["entries"]}


def code(name):
    match = re.match(r"legacy_post_(\d{3})", name)
    if not match:
        raise ValueError(name)
    return match.group(1)


def extract_setup(path):
    script = ROOT / "tools/extract_post_hash.ps1"
    result = subprocess.run(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script), str(path)],
        capture_output=True, text=True, timeout=120,
    )
    if result.returncode != 0:
        raise RuntimeError(path.name + "\n" + result.stdout + result.stderr)
    digest = result.stdout.strip().splitlines()[-1].strip().lower()
    if not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise RuntimeError("unexpected digest from " + path.name + ": " + digest)
    return digest


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    entries = load()
    for fixture in sorted((ROOT / "tests/fixtures").glob("legacy_post_*.base64.txt")):
        digest = hashlib.sha256(base64.b64decode(fixture.read_text(encoding="ascii"))).hexdigest()
        entries.add(("foxmolika_Tools_" + code(fixture.name) + "/foxmolika_PostGenerate.exe", digest))

    releases = ROOT / "交付"
    if releases.is_dir():
        for folder in sorted(releases.glob("release_*")):
            match = re.search(r"(\d+)\.(\d+)\.(\d+)", folder.name)
            if not match:
                continue
            version_code = "".join(match.groups())
            for setup in sorted(folder.glob("*Setup*.exe")):
                digest = extract_setup(setup)
                entries.add(("foxmolika_Tools_" + version_code + "/foxmolika_PostGenerate.exe", digest))

    ordered = sorted(entries, key=lambda item: (item[0], item[1]))
    data = {
        "schema": 1,
        "generated": "2026-11-21",
        "entries": [{"path": path, "sha256": digest} for path, digest in ordered],
    }
    TARGET.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("PASS generated {} legacy helper entries -> {}".format(len(ordered), TARGET))


if __name__ == "__main__":
    main()
