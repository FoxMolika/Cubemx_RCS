"""统一检查入口：host 检查源码，native 检查工具链，release 检查历史发布证据。"""
from pathlib import Path
import argparse
import json
import os
import shutil
import subprocess
import sys

from build_support import ROOT, csharp_compiler
from cubemx_runner import cube_directory
from keil_build import uv4_executable
from provenance import capture, assert_current

# 顺序具有依赖关系：先冻结编辑器，再测试，最后构建并验证一体安装器。
# 纯主机检查不用 CubeMX/Keil；安装事务虽有模拟项，也须先构建套件，归入 native。
HOST_STEPS = (
    ("tools/test_provenance.py",),
    ("tools/test_release_preflight.py",),
    ("tools/sync_versions.py",),
    ("map_editor/tools/build.py",),
    ("tools/verify.py",),
    ("tools/check_guides.py",),
    ("tools/test_version.py",),
    ("tools/test_legacy_post_hashes.py",),
    ("tools/test_hardware_ui.py",),
    ("tools/test_hardware_runtime.py",),
    ("tools/test_hardware_app.py",),
    ("tools/test_hardware_project.py",),
    ("tools/test_beta_compatibility.py",),
    ("tools/test_machine.py",),
    ("tools/test_beta_feedback.py",),
    ("tools/test_feedback_gap_us.py",),
    ("tools/test_beta_app.py",),
    ("tools/test_beta_upgrade.py",),
    ("tools/test_refactor.py",),
    ("tools/test_keil_options.py",),
    ("tools/test_export_transaction.py",),
    ("tools/test_installer_io.py",),
    ("tools/test_atomic_file.py",),
    ("tools/test_terrain_drive.py",),
    ("tools/test_terrain_project.py",),
    ("tools/test_corrections.py",),
    ("tools/test_continuous.py",),
    ("tools/test_workspace.py",),
    ("tools/test_terrain.py",),
    ("tools/test_course_overlay.py",),
    ("map_editor/tools/verify_phases.py", "--no-build"),
    ("map_editor/tools/test_crash.py",),
    ("tools/test_migration_080.py",),
    ("tools/test_revision_guard.py",),
    ("tools/test_route_project.py",),
)
NATIVE_STEPS = (
    ("suite/tools/build.py",),
    ("tools/test_version.py", "--include-suite"),
    ("tools/test_install_permissions.py",),
    ("tools/test_project_setup.py",),
    ("suite/tools/test_transactions.py",),
    ("suite/tools/verify.py",),
    ("tools/verify_project_integration.py",),
    ("tools/finish_keil_validation.py",),
    ("tools/test_keil_guard.py",),
    ("tools/verify_project_integration.py", "--terrain"),
    ("tools/finish_terrain_keil.py",),
    ("suite/tools/finish_tests.py",),
)
RELEASE_STEPS = (("tools/release.py",),)
ARCHIVE_VERSIONS = ("0.3.7", "0.4.0", "0.4.1", "0.5.0", "0.5.1", "0.5.2", "0.6.0", "0.6.1", "0.7.0")


def steps_for(level):
    if level == "host":
        return HOST_STEPS
    if level == "native":
        return HOST_STEPS + NATIVE_STEPS
    if level == "release":
        return HOST_STEPS + NATIVE_STEPS + RELEASE_STEPS
    raise ValueError("未知检查层级：" + level)


def preflight(level):
    """尽早说明缺失条件；不下载工具、不创建历史证据、不把缺项算作通过。"""
    if os.name != "nt":
        raise RuntimeError("当前编辑器验收需要 Windows/.NET Framework/WinForms。")
    csharp_compiler()
    missing = [name for name in ("git", "gcc", "g++", "nm") if not shutil.which(name)]
    if missing:
        raise RuntimeError("PATH 中缺少主机工具：" + ", ".join(missing))
    if level in ("native", "release"):
        cube = cube_directory()
        required = (
            cube / "STM32CubeMX.exe", cube / "jre/bin/javac.exe", cube / "jre/bin/java.exe",
            uv4_executable(),
            Path.home() / "STM32Cube/Repository/STM32Cube_FW_F4_V1.28.3",
        )
        absent = [str(path) for path in required if not path.exists()]
        if absent:
            raise RuntimeError("原生验收缺少工具/固件：\n" + "\n".join(absent))
    if level == "release":
        absent = []
        for version in ARCHIVE_VERSIONS:
            directory = ROOT.parent / ("foxmolika_CubeMX_" + version)
            snapshot = ROOT / "build" / ("original_" + version.replace(".", "") + "_SHA256.json")
            if not directory.is_dir() or not snapshot.is_file():
                absent.append(str(snapshot) + " / " + str(directory))
        if absent:
            raise RuntimeError(
                "历史发布核验需要维护者事前保存的原版目录及 SHA256 快照。\n"
                "全新克隆请先运行 --level host 或 --level native；release 不会跳过证据检查。\n"
                + "\n".join(absent)
            )


def run_checks(level, start_from=None):
    steps = steps_for(level)
    # 正式发布从首步绑定干净标签；开发host/native仍允许未提交的工作区。
    source = capture(ROOT) if level == "release" else None
    verified_prefix = []
    previous_evidence = None
    if start_from:
        # 继续执行由操作者明确指定；前序产物的哈希仍由后续验收脚本校验。
        matches = [i for i, step in enumerate(steps) if step[0] == start_from]
        if not matches:
            raise ValueError("该层级没有此检查步骤：" + start_from)
        prior_path = ROOT / "build" / ("check-" + level + ".json")
        if prior_path.exists():
            prior = json.loads(prior_path.read_text(encoding="utf-8"))
            prefix = [list(step) for step in steps[:matches[0]]]
            if (prior.get("level") == level and prior.get("failed_step") == list(steps[matches[0]])
                    and prior.get("completed") == prefix and level == "release"
                    and start_from == "tools/release.py"):
                assert prior.get("source_provenance") == source, "源码已变化，必须重新完整验收"
                # 只续接失败的最后发布步骤；发布脚本仍逐项复核当前产物及测试源哈希。
                verified_prefix = prefix
                previous_evidence = prior
        steps = steps[matches[0]:]
    area = ROOT / "build"
    area.mkdir(exist_ok=True)
    report_path = area / ("check-" + level + ".json")
    report = {"level": level, "passed": False, "continued_from": start_from,
              "completed": verified_prefix, "verified_prior_prefix": bool(verified_prefix)}
    if source is not None:
        report["source_provenance"] = source
    if previous_evidence is not None:
        (area / ("check-" + level + "-before-resume.json")).write_text(
            json.dumps(previous_evidence, indent=2), encoding="utf-8")
    # 开始就写未通过，防止失败后残留的旧 PASS 报告被误用。
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    try:
        preflight(level)
    except (RuntimeError, FileNotFoundError) as error:
        report["failed_step"] = ["preflight"]
        report["error"] = str(error)
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        raise
    for step in steps:
        if source is not None:
            assert_current(source, ROOT)
        print("\nRUN " + " ".join(step), flush=True)
        result = subprocess.run([sys.executable, *step], cwd=ROOT)
        if result.returncode:
            report["failed_step"] = list(step)
            report["exit_code"] = result.returncode
            report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
            raise SystemExit(result.returncode)
        report["completed"].append(list(step))
        if source is not None:
            assert_current(source, ROOT)
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    report["passed"] = True
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("PASS " + level + " checks", flush=True)


def main(argv=None):
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--level", choices=("host", "native", "release"), default="host")
    parser.add_argument("--from", dest="start_from", help="从指定脚本继续；要求此前产物仍在且一致")
    parser.add_argument("--list", action="store_true", help="只列出检查顺序，不运行或检查环境")
    args = parser.parse_args(argv)
    if args.list:
        for step in steps_for(args.level):
            print(" ".join(step))
        return
    try:
        run_checks(args.level, args.start_from)
    except (RuntimeError, FileNotFoundError, ValueError) as error:
        parser.exit(2, str(error) + "\n")


if __name__ == "__main__":
    main()
