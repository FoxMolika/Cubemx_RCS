"""发布来源校验：源码、标签和资产名称必须属于同一次干净提交。"""
import ast
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
METADATA = "BUILD_PROVENANCE.json"
FIELDS = ("schema", "version", "tag", "tag_required", "commit", "tree", "asset_names", "source_files_sha256")


def _git(root, *args, allow_failure=False):
    result = subprocess.run(["git", "-C", str(root), *args], capture_output=True)
    if result.returncode and not allow_failure:
        raise RuntimeError("Git来源检查失败：" + result.stderr.decode("utf-8", errors="replace").strip())
    return result


def _settings(version, assets):
    if version is None or assets is None:
        import version as current
        version = current.VERSION if version is None else version
        assets = current.release_asset_names() if assets is None else assets
    if not isinstance(version, str) or not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("发布版本必须为三段数字。")
    assets = list(assets)
    if not assets or len(set(assets)) != len(assets):
        raise ValueError("发布资产清单不能为空或含重复名称。")
    if any(not isinstance(name, str) or not name.isascii() or not re.fullmatch(r"[A-Za-z0-9_.-]+", name) or name in (".", "..") for name in assets):
        raise ValueError("发布资产必须使用唯一的ASCII文件名。")
    return version, assets


def _files(root, commit):
    """只读取Git归档中的blob，不读取工作区及其构建缓存。"""
    raw = _git(root, "archive", "--format=zip", commit).stdout
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        files = {}
        for item in archive.infolist():
            if item.is_dir():
                continue
            path = PurePosixPath(item.filename)
            if path.is_absolute() or ".." in path.parts or "\\" in item.filename or item.filename in files:
                raise RuntimeError("Git归档包含不安全或重复的文件路径。")
            if item.filename == METADATA:
                raise RuntimeError("仓库不能跟踪保留的来源声明文件：" + METADATA)
            files[item.filename] = archive.read(item)
    return files


def _manifest(files):
    return {name: hashlib.sha256(data).hexdigest() for name, data in sorted(files.items())}


def capture(root=ROOT, version=None, require_tag=True, assets=None):
    """采集干净源码身份；正式发布必须存在指向HEAD的本地版本标签。"""
    root = Path(root).resolve()
    top = Path(_git(root, "rev-parse", "--show-toplevel").stdout.decode().strip()).resolve()
    if top != root:
        raise ValueError("来源目录必须是Git工作区根目录。")
    if _git(root, "status", "--porcelain=v1", "-z", "--untracked-files=normal").stdout:
        raise RuntimeError("发布要求干净工作区：存在已修改、已暂存或未跟踪文件。")
    version, assets = _settings(version, assets)
    commit = _git(root, "rev-parse", "--verify", "HEAD^{commit}").stdout.decode().strip()
    tree = _git(root, "rev-parse", "--verify", "HEAD^{tree}").stdout.decode().strip()
    tag = "v" + version
    if require_tag:
        found = _git(root, "rev-parse", "--verify", "refs/tags/" + tag + "^{commit}", allow_failure=True)
        if found.returncode or found.stdout.decode().strip() != commit:
            raise RuntimeError("发布标签" + tag + "必须存在且指向当前HEAD。")
    files = _files(root, commit)
    # 版本既要来自调用方，也要能在实际归档的版本源码中得到验证。
    if "tools/version.py" in files:
        nodes = ast.parse(files["tools/version.py"].decode("utf-8-sig")).body
        versions = [ast.literal_eval(node.value) for node in nodes if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "VERSION" for t in node.targets)]
        if versions != [version]:
            raise RuntimeError("Git源码中的VERSION与发布版本不一致。")
    return dict(schema=1, version=version, tag=tag, tag_required=bool(require_tag), commit=commit, tree=tree, asset_names=assets, source_files_sha256=_manifest(files))


def assert_current(record, root=ROOT, version=None, assets=None):
    """每个发布阶段重新取证，拒绝提交、标签、清单或工作区在阶段间变化。"""
    if not isinstance(record, dict) or any(key not in record for key in FIELDS) or record["schema"] != 1 or not isinstance(record["tag_required"], bool):
        raise ValueError("发布来源声明缺少字段或格式无效。")
    current = capture(root=root, version=version, require_tag=record["tag_required"], assets=assets)
    if any(record[key] != current[key] for key in FIELDS):
        raise RuntimeError("发布来源已变化：提交、标签、版本、资产清单或源码摘要不一致。")
    return current


def verify_source_archive(output, record, root=ROOT, version=None, assets=None):
    """对照真实Git提交逐字节核验；不把压缩包自己的声明当成证据。"""
    current = assert_current(record, root=root, version=version, assets=assets)
    files = _files(root, current["commit"])
    try:
        with zipfile.ZipFile(output) as archive:
            names = archive.namelist()
            if len(names) != len(set(names)) or set(names) != set(files) | {METADATA}:
                raise RuntimeError("源码包文件集合不匹配，存在缺失、额外或重复项。")
            if json.loads(archive.read(METADATA).decode("utf-8")) != current:
                raise RuntimeError("源码包来源声明与Git取证不一致。")
            for name, data in files.items():
                if archive.read(name) != data:
                    raise RuntimeError("源码包文件与Git提交不一致：" + name)
    except (zipfile.BadZipFile, UnicodeError, json.JSONDecodeError, KeyError) as error:
        raise RuntimeError("源码包损坏或来源声明无效。") from error
    return current


def create_source_archive(output, record, root=ROOT, version=None, assets=None):
    """创建只含提交源码和来源声明的压缩包；输出仅允许落在忽略目录。"""
    root = Path(root).resolve()
    current = assert_current(record, root=root, version=version, assets=assets)
    output = Path(output).resolve()
    try:
        relative = output.relative_to(root).as_posix()
    except ValueError as error:
        raise ValueError("源码包输出必须位于工作区的Git忽略目录。") from error
    if _git(root, "check-ignore", "-q", "--", relative, allow_failure=True).returncode:
        raise ValueError("源码包输出必须被Git忽略，避免污染发布源码。")
    output.parent.mkdir(parents=True, exist_ok=True)
    files = _files(root, current["commit"])
    # 固定zip时间和顺序，同一提交重复出包得到相同字节。
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(files.items()):
            archive.writestr(zipfile.ZipInfo(name), data, compress_type=zipfile.ZIP_DEFLATED)
        archive.writestr(zipfile.ZipInfo(METADATA), json.dumps(current, ensure_ascii=False, sort_keys=True, indent=2) + "\n", compress_type=zipfile.ZIP_DEFLATED)
    verify_source_archive(output, current, root=root, version=version, assets=assets)
    return output
