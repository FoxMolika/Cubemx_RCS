"""把旧脚本写到固定位置的报告，登记为本轮实际产生的证据。"""
from pathlib import Path
import shutil

from gate_diagnostics import GateError, file_sha, read_json, require_file
from version import EDITOR_SETUP_NAME, PACK_NAME, SETUP_NAME, VERSION

EDITOR = "map_editor/dist/foxmolika_MapEditor.exe"
PACK = "dist/" + PACK_NAME
CH_ZIP = "dist/RCS.foxmolika.C_H." + VERSION + ".zip"
SETUP = "suite/dist/" + SETUP_NAME
ACCEPTANCE = "suite/build/acceptance.json"
INTEGRATION = "build/project_integration_validation.json"
TERRAIN = "build/terrain_project_integration_validation.json"
VERSION_REPORT = "build/version_validation.json"
BUILD_RECORDS = (
    "build/build-inputs/editor.json", "build/build-inputs/editor-setup.json",
    "build/build-inputs/suite.json",
)
PHASES = "map_editor/build/phased_validation.json"
WORKSPACE = "build/workspace_validation.json"
UI = tuple("build/ui/" + name for name in (
    "continuous1.png", "continuous2.png", "correction-templates.png", "hardware-profile.png",
    "shape-editing.png", "course_angles.png", "phases.png", "terrain0.png", "terrain1.png", "terrain2.png",
))

# 同一路径可在不同步骤更新。每次更新前保留原件，只有首次产出才移走旧文件。
PRODUCES = {
    ("tools/test_provenance.py",): ("build/provenance_validation.json",),
    ("tools/test_release_preflight.py",): ("build/release_preflight_validation.json",),
    ("map_editor/tools/build.py",): (EDITOR, "map_editor/dist/" + EDITOR_SETUP_NAME, *BUILD_RECORDS[:2],
                                    "map_editor/dist/使用说明.html", "map_editor/build/examples/连续通行示例.rcsmap.json",
                                    "map_editor/build/examples/坡道执行示例.rcsmap.json"),
    ("tools/verify.py",): ("build/host_validation.txt", PACK, CH_ZIP),
    ("tools/test_version.py",): (VERSION_REPORT,),
    ("tools/test_refactor.py",): ("build/refactor_validation.json",),
    ("tools/test_keil_options.py",): ("build/keil_options_validation.json",),
    ("tools/test_export_transaction.py",): ("build/export_transaction_validation.json",),
    ("tools/test_installer_io.py",): ("build/installer_io_validation.json",),
    ("tools/test_atomic_file.py",): ("build/atomic_file_validation.json",),
    ("tools/test_terrain_drive.py",): ("build/terrain_drive_validation.json", "build/ui/shape-editing.png"),
    ("tools/test_terrain_project.py",): ("build/terrain_project_validation.json",),
    ("tools/test_corrections.py",): ("build/correction_validation.json", "build/ui/correction-templates.png", "build/ui/hardware-profile.png"),
    ("tools/test_continuous.py",): ("build/continuous_validation.json", *UI[:2]),
    ("tools/test_workspace.py",): (WORKSPACE,),
    ("tools/test_terrain.py",): ("build/terrain_validation.json", *UI[6:]),
    ("tools/test_course_overlay.py",): ("build/course_validation.json", "build/ui/course_angles.png"),
    ("map_editor/tools/verify_phases.py", "--no-build"): (PHASES,),
    ("map_editor/tools/test_crash.py",): ("build/crash-validation/results.json",),
    ("tools/test_migration_080.py",): ("build/migration080.json",),
    ("tools/test_revision_guard.py",): ("build/revision_validation.json",),
    ("tools/test_route_project.py",): ("build/route_project_validation.json",),
    ("suite/tools/build.py",): (SETUP, "suite/build/manifest.xml", BUILD_RECORDS[2]),
    ("tools/test_version.py", "--include-suite"): (VERSION_REPORT,),
    ("tools/test_install_permissions.py",): ("build/permissions_validation.json",),
    ("suite/tools/test_transactions.py",): ("suite/build/transactions.json",),
    ("suite/tools/verify.py",): (ACCEPTANCE,),
    ("tools/verify_project_integration.py",): (INTEGRATION,),
    ("tools/finish_keil_validation.py",): (INTEGRATION,),
    ("tools/test_keil_guard.py",): (INTEGRATION, "build/keil_guard_validation.json"),
    ("tools/verify_project_integration.py", "--terrain"): (TERRAIN,),
    ("tools/finish_terrain_keil.py",): (TERRAIN,),
    ("suite/tools/finish_tests.py",): (ACCEPTANCE,),
}
REQUIRED = {name for names in PRODUCES.values() for name in names}
POST = "build/foxmolika_PostGenerate.exe"
POST_RECORD = "build/build-inputs/post-helper.json"
REQUIRED.update((POST, POST_RECORD))


def before_step(root, area, step, recorded):
    for name in PRODUCES.get(step, ()):
        path = root / name
        if name in recorded:
            verify_file(root, name, recorded[name])
        elif path.is_file():
            target = area / "prior" / name
            target.parent.mkdir(parents=True, exist_ok=True)
            path.rename(target)


def verify_file(root, name, digest):
    path = require_file(root / name)
    actual = file_sha(path)
    if actual != digest:
        raise GateError("hash_mismatch", "本轮证据摘要变化：" + name,
                        "重跑完整门禁", {"path": name, "expected": digest, "actual": actual})


def record_outputs(root, area, step, report):
    names = PRODUCES.get(step, ())
    if step == ("map_editor/tools/build.py",):
        names += (POST, POST_RECORD)
    # 路径引用中后续实际要读取的文件也封存摘要。
    if step == ("tools/test_workspace.py",):
        names += (referenced_file(root, WORKSPACE, "workspace-minimum.png"),)
    if step == ("map_editor/tools/verify_phases.py", "--no-build"):
        names += (referenced_file(root, PHASES, "layout0/rcs_nav.c"),)
    for name in names:
        path = require_file(root / name)
        target = area / "evidence" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        report["evidence"][name] = file_sha(target)
        report.setdefault("evidence_paths", {})[name] = target.relative_to(root).as_posix()
        verify_file(root, name, report["evidence"][name])


def referenced_file(root, report, file):
    area = read_json(root / report).get("area")
    if not isinstance(area, str) or not area:
        raise GateError("stale_evidence", "证据未登记工作目录：" + report)
    path = (Path(area) / file).resolve()
    if not path.is_relative_to(root.resolve()):
        raise GateError("stale_evidence", "证据文件不在本工作区：" + str(path))
    return path.relative_to(root.resolve()).as_posix()


def verify_prefix(root, report):
    recorded = report.get("evidence", {})
    needed = REQUIRED | {referenced_file(root, WORKSPACE, "workspace-minimum.png"),
                         referenced_file(root, PHASES, "layout0/rcs_nav.c")}
    if not needed.issubset(recorded):
        raise GateError("stale_evidence", "缺少本轮产出证据", "重新运行完整 release 门禁",
                        {"missing": sorted(needed - set(recorded))})
    paths = report.get("evidence_paths", {})
    for name in needed:
        relative = paths.get(name)
        if not isinstance(relative, str) or not (root / relative).resolve().is_relative_to((root / "build/gate-runs").resolve()):
            raise GateError("stale_evidence", "本轮封存证据路径无效：" + name)
        verify_file(root, relative, recorded[name])
        verify_file(root, name, recorded[name])


def live_acceptance(root, recorded):
    """卸载步骤前验证安装态。卸载完成后以封存的安装态和卸载结果为凭。"""
    verify_file(root, ACCEPTANCE, recorded[ACCEPTANCE])
    proof = read_json(root / ACCEPTANCE)
    location = proof.get("installed_directory")
    if not isinstance(location, str) or not location:
        raise GateError("stale_evidence", "验收记录缺少安装路径")
    path = Path(location).resolve() / Path(EDITOR).name
    if not path.is_relative_to((root / "suite/build").resolve()):
        raise GateError("stale_evidence", "验收安装路径不在 suite/build：" + str(path))
    if file_sha(require_file(path)) != recorded[EDITOR]:
        raise GateError("hash_mismatch", "卸载前安装的编辑器与本轮构建不一致")
    return recorded[EDITOR]
