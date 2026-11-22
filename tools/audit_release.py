"""只读核对发布门禁、资产摘要及 Git 标签对应的完整源码包。"""
from pathlib import Path
import hashlib
import re

from provenance import assert_current, capture, verify_source_archive
from gate_diagnostics import read_json, require_file
from version import VERSION, PACK_NAME, SETUP_NAME, release_asset_names

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(require_file(path, "artifact_missing").read_bytes()).hexdigest()


def candidate_directory(root, record):
    from check import release_destination
    return release_destination(Path(root), record)


def local_preflight(root=ROOT):
    """只读取当前干净标签对应的冻结候选。"""
    root = Path(root)
    dist = candidate_directory(root, capture(root=root))
    return verify_candidate(root, dist, read_json(dist / "gate.json"))


def verify_candidate(root, dist, gate):
    names = list(release_asset_names())
    report = read_json(dist / ("foxmolika_Validation_" + VERSION + ".json"))
    if (gate.get("schema_version") != 2 or gate.get("status") != "passed"
            or not gate.get("run_id") or gate["run_id"] != report.get("gate_run_id")):
        raise RuntimeError("发布核验与冻结门禁的运行编号或状态不一致。")
    from check import steps_for, verify_subprocess_steps
    if not report.get("passed") or report.get("version") != VERSION:
        raise RuntimeError("发布核验未通过或版本不一致。")
    expected = steps_for("release")
    if not gate.get("passed") or gate.get("completed") != [list(step) for step in expected]:
        raise RuntimeError("完整 release 门禁尚未通过。")
    verify_subprocess_steps(gate, expected[:-1])
    if gate.get("continued_from") is not None and not gate.get("verified_prior_prefix"):
        raise RuntimeError("缺少续跑前序证据。")
    record = report.get("source_provenance")
    if not isinstance(record, dict) or record != gate.get("source_provenance"):
        raise RuntimeError("发布核验与 release 门禁的来源声明不一致。")
    if record.get("tag_required") is not True:
        raise RuntimeError("正式发布必须绑定版本标签。")
    assert_current(record, root=root, version=VERSION, assets=names)
    declaration = read_json(dist / ("foxmolika_Provenance_" + VERSION + ".json"))
    if declaration != record:
        raise RuntimeError("独立来源附件与门禁声明不一致。")
    checksums = {}
    for line in require_file(dist / "SHA256SUMS.txt").read_text(encoding="utf-8").splitlines():
        match = re.fullmatch(r"([0-9a-f]{64})  ([A-Za-z0-9_.-]+)", line)
        if not match:
            raise RuntimeError("SHA256SUMS 格式无效。")
        digest, name = match.groups()
        if name not in names or name in checksums or sha(dist / name) != digest:
            raise RuntimeError("发布资产缺失、重复或摘要不一致：" + name)
        checksums[name] = digest
    if set(checksums) != set(names):
        raise RuntimeError("SHA256SUMS 与规范资产清单不一致。")
    for name, key in [(SETUP_NAME, "installer_sha256"), (PACK_NAME, "pack_sha256"),
                      ("foxmolika_CubeMX_Source_" + VERSION + ".zip", "source_zip_sha256")]:
        if checksums[name] != report.get(key):
            raise RuntimeError("发布核验与资产摘要不一致：" + name)
    verify_source_archive(dist / ("foxmolika_CubeMX_Source_" + VERSION + ".zip"), record,
                          root=root, version=VERSION, assets=names)
    # 清单本身也上传校验，但不递归登记自己的摘要。
    checksums["SHA256SUMS.txt"] = sha(dist / "SHA256SUMS.txt")
    return record, checksums


def main():
    local_preflight()
    print("PASS clean tagged source, complete source ZIP, release evidence and asset hashes: " + VERSION)


if __name__ == "__main__":
    main()
