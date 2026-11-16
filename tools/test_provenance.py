"""在隔离Git仓库验证发布来源约束；不修改真实仓库的标签或配置。"""
import hashlib
import json
from pathlib import Path
import subprocess
import uuid
import warnings
import zipfile

import provenance

ROOT = Path(__file__).resolve().parents[1]
VERSION = "9.8.7"
ASSETS = ["fixture_9.8.7.zip", "fixture_9.8.7.json"]


def main():
    build = (ROOT / "build").resolve()
    case = (build / ("provenance_" + uuid.uuid4().hex[:10])).resolve()
    if not case.is_relative_to(build):
        raise RuntimeError("测试夹具越出build目录。")
    repo = case / "repo"
    repo.mkdir(parents=True)
    passed = []

    def git(*args):
        result = subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True)
        if result.returncode:
            raise RuntimeError("夹具Git失败：" + result.stderr)
        return result.stdout.strip()

    def capture(**kwargs):
        return provenance.capture(root=repo, version=VERSION, assets=ASSETS, **kwargs)

    def current(record, **kwargs):
        return provenance.assert_current(record, root=repo, version=VERSION, assets=ASSETS, **kwargs)

    def verify(path, record):
        return provenance.verify_source_archive(path, record, root=repo, version=VERSION, assets=ASSETS)

    def rejects(name, action):
        try:
            action()
        except (RuntimeError, ValueError):
            passed.append(name)
        else:
            raise AssertionError("应当拒绝：" + name)

    git("init", "-q")
    git("config", "user.name", "Provenance fixture")
    git("config", "user.email", "fixture@example.invalid")
    (repo / ".gitignore").write_text("/build/\n/dist/\n", encoding="utf-8")
    (repo / "main.c").write_text("/* 队内代码 */\nint main(void) { return 0; }\n", encoding="utf-8")
    (repo / "tools").mkdir()
    (repo / "tools/version.py").write_text('VERSION = "' + VERSION + '"\n', encoding="utf-8")
    (repo / "publisher.py").write_text("# 原始发布脚本\n", encoding="utf-8")
    git("add", ".")
    git("commit", "-q", "-m", "fixture source")
    git("tag", "v" + VERSION)
    record = capture()
    assert record["commit"] == git("rev-parse", "HEAD")
    assert record["tree"] == git("rev-parse", "HEAD^{tree}")
    assert record["asset_names"] == ASSETS
    assert current(record) == record
    passed.append("clean commit and tag")

    (repo / "build").mkdir()
    (repo / "build/uncommitted-secret.txt").write_text("must not enter source archive", encoding="utf-8")
    assert capture() == record
    output = repo / "dist/source.zip"
    provenance.create_source_archive(output, record, root=repo, version=VERSION, assets=ASSETS)
    pristine = output.read_bytes()
    with zipfile.ZipFile(output) as archive:
        entries = {name: archive.read(name) for name in archive.namelist()}
    assert "build/uncommitted-secret.txt" not in entries
    assert set(entries) == set(record["source_files_sha256"]) | {provenance.METADATA}
    for name, digest in record["source_files_sha256"].items():
        assert hashlib.sha256(entries[name]).hexdigest() == digest
    assert verify(output, record) == record
    provenance.create_source_archive(output, record, root=repo, version=VERSION, assets=ASSETS)
    assert pristine == output.read_bytes()
    passed.append("ignored cache excluded and deterministic Git source archive")

    rejects("output outside ignored directory", lambda: provenance.create_source_archive(repo / "source.zip", record, root=repo, version=VERSION, assets=ASSETS))
    rejects("output outside checkout", lambda: provenance.create_source_archive(case / "outside.zip", record, root=repo, version=VERSION, assets=ASSETS))

    source = repo / "main.c"
    original = source.read_bytes()
    source.write_bytes(original + b"/* dirty */\n")
    rejects("tracked modification", capture)
    git("add", "main.c")
    rejects("staged modification", capture)
    git("restore", "--staged", "main.c")
    source.write_bytes(original)
    untracked = repo / "untracked.txt"
    untracked.write_text("untracked", encoding="utf-8")
    rejects("untracked source", capture)
    untracked.unlink()
    assert capture() == record

    git("checkout", "-q", "--detach", record["commit"])
    assert capture() == record
    passed.append("detached checkout at release tag")
    rejects("wrong requested version", lambda: provenance.capture(root=repo, version="9.8.6", require_tag=False, assets=ASSETS))
    rejects("asset names changed after capture", lambda: provenance.assert_current(record, root=repo, version=VERSION, assets=["other.zip"]))

    def write_archive(name, data, extra=None):
        path = repo / "dist" / (name + ".zip")
        with zipfile.ZipFile(path, "w") as archive:
            for filename, content in data.items():
                archive.writestr(filename, content)
            if extra:
                archive.writestr(*extra)
        return path

    modified = dict(entries)
    modified["main.c"] = b"int main(void) { return 7; }\n"
    rejects("modified source bytes", lambda: verify(write_archive("modified", modified), record))
    forged = json.loads(modified[provenance.METADATA])
    forged["source_files_sha256"]["main.c"] = hashlib.sha256(modified["main.c"]).hexdigest()
    modified[provenance.METADATA] = json.dumps(forged).encode()
    rejects("self-consistent forged manifest", lambda: verify(write_archive("forged", modified), record))
    missing = dict(entries)
    del missing["main.c"]
    rejects("missing tracked source", lambda: verify(write_archive("missing", missing), record))
    rejects("extra source", lambda: verify(write_archive("extra", entries, ("extra.c", b"extra")), record))
    rejects("extra empty directory", lambda: verify(write_archive("directory", entries, ("extra/", b"")), record))
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", UserWarning)
        duplicate = write_archive("duplicate", entries, ("main.c", entries["main.c"]))
    rejects("duplicate ZIP member", lambda: verify(duplicate, record))
    malformed = dict(entries)
    malformed[provenance.METADATA] = b"{bad json"
    rejects("malformed provenance", lambda: verify(write_archive("malformed", malformed), record))
    broken = repo / "dist/broken.zip"
    broken.write_bytes(b"not zip")
    rejects("invalid archive", lambda: verify(broken, record))

    # 模拟发布脚本先用于上传、随后才提交：新HEAD仍被旧标签/旧记录拒绝。
    (repo / "publisher.py").write_text("# 已修改的发布脚本\n", encoding="utf-8")
    git("add", "publisher.py")
    git("commit", "-q", "-m", "fixture changed publisher")
    rejects("old tag does not point to new publisher commit", capture)
    rejects("old source record after publisher commit", lambda: current(record))
    git("tag", "-f", "v" + VERSION)
    rejects("retagging cannot reuse old source record", lambda: current(record))
    fresh = capture()
    rejects("archive from previous commit", lambda: verify(output, fresh))
    git("tag", "-d", "v" + VERSION)
    rejects("missing required release tag", capture)
    assert capture(require_tag=False)["tag_required"] is False
    passed.append("untagged preflight explicitly marked")

    report = dict(status="PASS", checks=len(passed), cases=passed, fixture=str(repo),
                  note="仅隔离Git仓库来源测试，不代表产品编译或原生验收。",
                  source_sha256={name: hashlib.sha256((ROOT / "tools" / name).read_bytes()).hexdigest() for name in ("provenance.py", "test_provenance.py")})
    (build / "provenance_validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("PASS " + str(len(passed)) + " source/tag/archive provenance checks")


if __name__ == "__main__":
    main()
