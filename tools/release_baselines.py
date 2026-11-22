"""历史资产快照保持独立于当前候选产物。"""
import os
from pathlib import Path
import re
import subprocess

from gate_diagnostics import GateError, file_sha, read_json, require_file
from version import VERSION

VERSIONS = ("0.3.7", "0.4.0", "0.4.1", "0.5.0", "0.5.1", "0.5.2", "0.6.0", "0.6.1", "0.7.0", "0.8.0")


def baseline_paths(root, version):
    parent = Path(os.environ.get("RCS_BASELINE_ROOT", str(root.parent)))
    manifests = Path(os.environ.get("RCS_BASELINE_MANIFESTS", str(root / "build")))
    directory = parent / ("foxmolika_CubeMX_" + version)
    if version == "0.3.7" and "RCS_BASELINE_0037" in os.environ:
        directory = Path(os.environ["RCS_BASELINE_0037"])
    suffix = "_release" if version == "0.8.0" else ""
    return directory, manifests / ("original_" + version.replace(".", "") + suffix + "_SHA256.json")


def previous_tag(root):
    result = subprocess.run(["git", "tag", "--merged", "HEAD", "--list", "v*"],
                            cwd=root, capture_output=True, text=True, check=True)
    current = tuple(map(int, VERSION.split(".")))
    tags = [tag for tag in result.stdout.splitlines() if re.fullmatch(r"v\d+\.\d+\.\d+", tag)]
    earlier = {tuple(map(int, tag[1:].split("."))): tag for tag in tags}
    eligible = [key for key in earlier if key < current]
    if not eligible:
        raise GateError("preflight_environment", "缺少候选提交可达的上一正式版本标签",
                        "从项目仓库取回历史标签和提交")
    return earlier[max(eligible)]


def preflight(root):
    require_file(root / ("变更说明_" + VERSION + ".md"))
    missing = []
    for version in VERSIONS:
        directory, manifest = baseline_paths(root, version)
        if not directory.is_dir() or not manifest.is_file():
            missing.append({"version": version, "directory": str(directory), "manifest": str(manifest)})
    if missing:
        raise GateError("preflight_environment", "缺少历史资产目录或 SHA256 快照",
                        "设置 RCS_BASELINE_ROOT 和 RCS_BASELINE_MANIFESTS 指向可信归档",
                        {"missing": missing})
    return previous_tag(root)


def verify(root):
    counts = {}
    for version in VERSIONS:
        directory, manifest = baseline_paths(root, version)
        snapshot = read_json(manifest)
        files = snapshot.get("files") if version == "0.8.0" else snapshot
        if not isinstance(files, dict) or not files:
            raise GateError("stale_evidence", "历史摘要清单无效：" + str(manifest))
        for name, digest in files.items():
            path = (directory / name).resolve()
            if not path.is_relative_to(directory.resolve()):
                raise GateError("stale_evidence", "历史清单引用了归档外路径：" + name)
            if file_sha(require_file(path)) != digest:
                raise GateError("hash_mismatch", f"{version} 历史资产摘要不一致：{name}")
        counts[version] = len(files)
    return counts
