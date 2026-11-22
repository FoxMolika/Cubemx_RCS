"""离线 Git 夹具验证真实发布预检；不联网、不读取凭据、不创建远端发布。"""
from copy import deepcopy
from pathlib import Path
import hashlib
import json
import subprocess
import uuid
import zipfile

import audit_release
from check import steps_for
from provenance import capture, create_source_archive
from publish_release import remote_tag, verify_remote_tag
from version import VERSION, PACK_NAME, SETUP_NAME, release_asset_names

ROOT = Path(__file__).resolve().parents[1]


def main():
    area = ROOT / "build" / ("release-preflight-" + uuid.uuid4().hex)
    area.mkdir(parents=True)

    def git(*args):
        result = subprocess.run(["git", "-C", str(area), *args], capture_output=True, check=True)
        return result.stdout.decode("utf-8").strip()

    def write_json(path, data):
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")

    git("init", "--quiet")
    git("config", "user.name", "Offline fixture")
    git("config", "user.email", "fixture@example.invalid")
    git("config", "core.autocrlf", "false")
    (area / "tools").mkdir()
    (area / ".gitignore").write_text("/dist/\n/build/\n", encoding="utf-8")
    (area / "tools/version.py").write_text('VERSION = "' + VERSION + '"\n', encoding="utf-8")
    publishing_script = area / "tools/publish_release.py"
    publishing_script.write_text("# release script fixture\n", encoding="utf-8")
    git("add", ".")
    git("commit", "--quiet", "-m", "fixture source")
    git("tag", "v" + VERSION)
    names = list(release_asset_names())
    record = capture(root=area, version=VERSION, assets=names)
    candidate = audit_release.candidate_directory(area, record)
    stage = area / "build/stage"
    stage.mkdir(parents=True)
    dist = stage
    for name in names:
        (dist / name).write_bytes(("fixture asset: " + name).encode("ascii"))
    source_name = "foxmolika_CubeMX_Source_" + VERSION + ".zip"
    validation_name = "foxmolika_Validation_" + VERSION + ".json"
    declaration_name = "foxmolika_Provenance_" + VERSION + ".json"
    create_source_archive(dist / source_name, record, root=area, version=VERSION, assets=names)
    report = {"passed": True, "version": VERSION, "source_provenance": record,
              "installer_sha256": audit_release.sha(dist / SETUP_NAME),
              "pack_sha256": audit_release.sha(dist / PACK_NAME),
              "source_zip_sha256": audit_release.sha(dist / source_name), "gate_run_id": "offline"}
    steps = [list(step) for step in steps_for("release")]
    records = [{"argv": ["fixture-python", *step], "status": "passed", "exit_code": 0,
                "duration_s": 0.1, "stdout_log": f"fixture/{index}/stdout.log",
                "stderr_log": f"fixture/{index}/stderr.log"}
               for index, step in enumerate(steps[:-1])]
    gate = {"schema_version": 2, "status": "passed", "run_id": "offline", "passed": True,
            "completed": steps, "steps": records,
            "continued_from": None, "source_provenance": record}
    write_json(dist / validation_name, report)
    write_json(dist / declaration_name, record)

    def checksums():
        (dist / "SHA256SUMS.txt").write_text("".join(
            audit_release.sha(dist / name) + "  " + name + "\n" for name in names), encoding="utf-8")

    checksums()
    from release import freeze_candidate
    from gate_diagnostics import GateError
    def fail_commit():
        raise RuntimeError("injected failure")

    try:
        freeze_candidate(stage, candidate, gate, fail_commit)
    except RuntimeError:
        assert stage.is_dir() and not candidate.exists()
    else:
        raise AssertionError("failed freeze was committed")
    freeze_candidate(stage, candidate, gate, lambda: None)
    try:
        freeze_candidate(candidate, candidate, gate, lambda: None)
    except GateError as error:
        assert error.kind == "duplicate_invocation"
    else:
        raise AssertionError("existing candidate was overwritten")
    dist = candidate
    snapshots = {path: path.read_bytes() for path in dist.iterdir()}
    snapshots[dist / "gate.json"] = (dist / "gate.json").read_bytes()
    checks = []

    def reject(label, action):
        for path, content in snapshots.items():
            path.write_bytes(content)
        action()
        try:
            audit_release.local_preflight(root=area)
        except (RuntimeError, ValueError):
            checks.append(label)
        else:
            raise AssertionError("预检错误放行：" + label)

    found, digests = audit_release.local_preflight(root=area)
    assert found == record and set(digests) == set(names) | {"SHA256SUMS.txt"}
    checks.append("valid clean tagged source and complete archive")

    def untagged():
        weakened = dict(record, tag_required=False)
        write_json(dist / validation_name, dict(report, source_provenance=weakened))
        write_json(dist / "gate.json", dict(gate, source_provenance=weakened))
        write_json(dist / declaration_name, weakened)
        checksums()
    reject("untagged development record refused", untagged)

    def inconsistent():
        changed = dict(record, commit="0" * 40)
        write_json(dist / "gate.json", dict(gate, source_provenance=changed))
    reject("different gate and validation provenance refused", inconsistent)

    def tampered_zip():
        # 同步外层摘要，确保拒绝的是包内源码，不是仅碰巧哈希未更新。
        with zipfile.ZipFile(dist / source_name, "a") as archive:
            archive.writestr("extra-source.txt", "uncommitted source")
        write_json(dist / validation_name, dict(report, source_zip_sha256=audit_release.sha(dist / source_name)))
        checksums()
    reject("extra ZIP source rejected despite matching outer hashes", tampered_zip)

    def missing_name():
        path = dist / "SHA256SUMS.txt"
        path.write_text("\n".join(path.read_text(encoding="utf-8").splitlines()[1:]) + "\n", encoding="utf-8")
    reject("missing canonical checksum name refused", missing_name)

    def missing_step():
        changed = deepcopy(gate)
        changed["completed"] = changed["completed"][:-1]
        write_json(dist / "gate.json", changed)
    reject("old PASS with incomplete release steps refused", missing_step)
    reject("mixed run IDs refused", lambda: write_json(dist / "gate.json", dict(gate, run_id="older")))
    reject("completed checklist without subprocess records refused",
           lambda: write_json(dist / "gate.json", dict(gate, steps=[])))
    reordered = [records[1], records[0], *records[2:]]
    reject("out of order subprocess records refused",
           lambda: write_json(dist / "gate.json", dict(gate, steps=reordered)))


    original_script = publishing_script.read_bytes()
    reject("dirty publication script after build refused",
           lambda: publishing_script.write_bytes(original_script + b"# changed after build\n"))
    publishing_script.write_bytes(original_script)
    for path, content in snapshots.items():
        path.write_bytes(content)
    publishing_script.write_bytes(original_script + b"# committed after build\n")
    git("add", "tools/publish_release.py")
    git("commit", "--quiet", "-m", "changed publication script")
    # 即使新提交也打上同名标签，旧门禁不能给新提交背书（仅操作隔离夹具）。
    git("tag", "-f", "v" + VERSION)
    reject("new committed script and moved fixture tag refused", lambda: None)

    def fake_api(path):
        return {
            "/git/ref/tags/v" + VERSION: {"object": {"type": "tag", "sha": "annotated"}},
            "/git/tags/annotated": {"object": {"type": "commit", "sha": record["commit"]}},
            "/git/commits/" + record["commit"]: {"tree": {"sha": record["tree"]}},
        }[path]
    assert remote_tag(fake_api, "v" + VERSION) == {"reference_sha": "annotated", "commit": record["commit"]}
    verify_remote_tag(fake_api, record)
    checks.append("annotated remote tag resolves to actual commit and tree")
    try:
        verify_remote_tag(fake_api, dict(record, commit="f" * 40))
    except RuntimeError:
        checks.append("wrong remote tag commit refused")
    else:
        raise AssertionError("错误远端标签被放行。")
    report_path = ROOT / "build/release_preflight_validation.json"
    write_json(report_path, {"passed": True, "checks": checks, "network": False,
                             "source_sha256": {"tools/" + name: hashlib.sha256((ROOT / "tools" / name).read_bytes()).hexdigest()
                                               for name in ("audit_release.py", "publish_release.py", "provenance.py", "test_release_preflight.py")}})
    print("PASS", len(checks), "offline release provenance/preflight checks")


if __name__ == "__main__":
    main()
