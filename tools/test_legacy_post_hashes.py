"""Regression: generated legacy helper whitelist must cover the previous release."""
from pathlib import Path
import hashlib
import json
import re

from version import VERSION, VERSION_CODE

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "tools/legacy_post_hashes.json"


def main():
    data = json.loads(SOURCE.read_text(encoding="utf-8"))
    assert data.get("schema") == 1, "unsupported whitelist schema"
    entries = data.get("entries")
    assert isinstance(entries, list) and len(entries) >= 20, "legacy hash table too small"
    paths = set()
    for entry in entries:
        assert isinstance(entry, dict), entry
        path = entry.get("path")
        digest = entry.get("sha256")
        assert isinstance(path, str) and "/foxmolika_PostGenerate.exe" in path, entry
        assert isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest), entry
        paths.add(path)
    previous = "{:03d}".format(int(VERSION_CODE) - 1)
    previous_path = "foxmolika_Tools_" + previous + "/foxmolika_PostGenerate.exe"
    assert previous_path in paths, "missing previous release helper: " + previous_path
    assert "foxmolika_Tools_085/foxmolika_PostGenerate.exe" in paths, "missing 0.8.5 helper"
    report = {
        "passed": True,
        "version": VERSION,
        "previous_helper": previous_path,
        "entries": len(entries),
        "unique_paths": len(paths),
        "sha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    }
    (ROOT / "build/legacy_post_hashes_validation.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print("PASS legacy helper whitelist: {} entries, previous {}".format(len(entries), previous_path))


if __name__ == "__main__":
    main()
