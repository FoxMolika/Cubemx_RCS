"""统一检查入口：host 检查源码，native 检查工具链，release 检查历史发布证据。"""
from pathlib import Path
import argparse
import re
import os
import shutil
import sys

from build_support import ROOT, csharp_compiler
import gate_diagnostics as gd
from cubemx_runner import cube_directory
from keil_build import uv4_executable
from provenance import capture, assert_current
from release_baselines import preflight as baseline_preflight
from release_evidence import before_step, record_outputs, live_acceptance, verify_prefix
from version import VERSION

# 顺序具有依赖关系：先冻结编辑器，再测试，最后构建并验证一体安装器。
# 纯主机检查不用 CubeMX/Keil；安装事务虽有模拟项，也须先构建套件，归入 native。
HOST_STEPS = (
    ("tools/test_gate_diagnostics.py",),
    ("tools/test_build_inputs.py",),
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
        baseline_preflight(ROOT)


def require_source(source):
    try:
        assert_current(source, ROOT)
    except RuntimeError as error:
        raise gd.GateError("source_changed", str(error), "从干净的候选标签重新执行") from error


def release_destination(root, source):
    commit = source.get("commit")
    if not isinstance(commit, str) or not re.fullmatch(r"(?:[0-9a-f]{40}|[0-9a-f]{64})", commit):
        raise gd.GateError("stale_evidence", "发布来源的提交摘要无效")
    return root / "build/releases" / ("v" + VERSION) / commit


def verify_subprocess_steps(report, expected, *, require_logs=False):
    """按顺序核对前序子进程记录，包括同一脚本的不同参数。"""
    records = report.get("steps")
    if not isinstance(records, list) or len(records) != len(expected):
        raise gd.GateError("stale_evidence", "发布报告的子进程明细数量不符",
                           "重新运行完整 release 门禁")
    for step, record in zip(expected, records):
        argv = record.get("argv") if isinstance(record, dict) else None
        duration = record.get("duration_s") if isinstance(record, dict) else None
        if (not isinstance(argv, list) or len(argv) != len(step) + 1 or not argv[0]
                or not isinstance(argv[0], str) or argv[1:] != list(step) or record.get("status") != "passed"
                or type(record.get("exit_code")) is not int or record["exit_code"] != 0
                or type(duration) not in (int, float) or not 0 <= duration < float("inf")
                or not all(isinstance(record.get(key), str) and record[key]
                           for key in ("stdout_log", "stderr_log"))):
            raise gd.GateError("stale_evidence", "发布报告的子进程明细无效：" + " ".join(step),
                               "重新运行完整 release 门禁")
        if require_logs:
            gd.require_file(record["stdout_log"])
            gd.require_file(record["stderr_log"])


def resume_prefix(prior, source, steps, root):
    expected = [list(step) for step in steps[:-1]]
    if not (prior.get("schema_version") == 2 and prior.get("level") == "release"
            and prior.get("status") == "failed" and prior.get("source_provenance") == source
            and prior.get("failed_step") == list(RELEASE_STEPS[0])
            and prior.get("completed") == expected):
        raise gd.GateError("stale_evidence", "没有同一标签下完整通过且仅打包失败的记录",
                           "重新运行 python tools/check.py --level release")
    verify_subprocess_steps(prior, expected, require_logs=True)
    verify_prefix(root, prior)
    return expected


def run_checks(level, start_from=None):
    if sys.flags.optimize:
        raise gd.GateError("argument_error", "门禁必须启用 Python 断言",
                           "去掉 -O 和 PYTHONOPTIMIZE 后重跑")
    with gd.workspace_lock(ROOT / "build"):
        return _run_checks(level, start_from)


def _run_checks(level, start_from):
    steps = steps_for(level)
    if level == "release" and start_from not in (None, RELEASE_STEPS[0][0]):
        raise gd.GateError("argument_error", "正式发布只允许续跑最后一步")
    if start_from and not any(step[0] == start_from for step in steps):
        raise gd.GateError("argument_error", "该层级没有此检查步骤：" + start_from)
    run_id = gd.new_run_id()
    area = ROOT / "build/gate-runs" / run_id
    area.mkdir(parents=True)
    latest = ROOT / "build" / ("check-" + level + ".json")
    resume = start_from and level == "release"
    report = {"schema_version": 2, "run_id": run_id, "level": level, "status": "running",
              "started_at": gd.now(), "passed": False, "continued_from": start_from,
              "completed": [], "verified_prior_prefix": False, "steps": [], "evidence": {}}

    def save(update_latest=True):
        gd.write_report(area / "report.json", report)
        if update_latest:
            gd.write_report(latest, report)

    print("门禁记录 " + str(area), flush=True)
    current = ("preflight",)
    source = None
    # 已有候选或无效续跑只记录本次尝试，不覆盖可用的前序记录。
    replace_latest = False
    save(False)
    try:
        prior = gd.read_json(latest) if resume else None
        preflight(level)
        if level == "release":
            try:
                source = capture(ROOT)
            except RuntimeError as error:
                raise gd.GateError("source_changed", str(error), "确认工作区干净且 HEAD 对应候选标签") from error
            report["source_provenance"] = source
            destination = release_destination(ROOT, source)
            if destination.exists():
                raise gd.GateError("duplicate_invocation", "此标签与提交已有冻结候选：" + str(destination),
                                   "运行 python tools/publish_release.py 核对已有候选")
            if prior is not None:
                report["completed"] = resume_prefix(prior, source, steps, ROOT)
                report["verified_prior_prefix"] = True
                report["resumed_run_id"] = prior["run_id"]
                # 打包在当前进程执行。子进程明细及其原始路径来自前序验收。
                report["steps"] = prior["steps"].copy()
                report["evidence"] = prior["evidence"].copy()
                report["evidence_paths"] = prior["evidence_paths"].copy()
                report["installed_editor_sha256"] = prior["installed_editor_sha256"]
        if start_from:
            index = next(i for i, step in enumerate(steps) if step[0] == start_from)
            steps = steps[index:]
        replace_latest = True
        save()
        for number, current in enumerate(steps, 1):
            if source is not None:
                require_source(source)
                before_step(ROOT, area, current, report["evidence"])
                if current == ("suite/tools/finish_tests.py",):
                    report["installed_editor_sha256"] = live_acceptance(ROOT, report["evidence"])
            directory = area / "steps" / (f"{number:02d}-" + Path(current[0]).stem)
            report["active_step"] = list(current)
            report["active_step_directory"] = str(directory)
            save()
            print("\nRUN " + " ".join(current), flush=True)
            if current in RELEASE_STEPS:
                from release import package
                directory.mkdir(parents=True)
                package(report, directory, destination)
            else:
                outcome = gd.run([sys.executable, *current], cwd=ROOT, directory=directory)
                report["steps"].append(outcome)
                if outcome["exit_code"]:
                    for key in ("stdout", "stderr"):
                        print(outcome[key]["tail"], file=sys.stderr)
                    raise gd.GateError("subprocess_exit", "步骤失败：" + current[0],
                                       "查看该步骤的 stdout.log 和 stderr.log",
                                       {"exit_code": outcome["exit_code"], "directory": str(directory)})
            if source is not None:
                require_source(source)
                record_outputs(ROOT, directory, current, report)
            report["completed"].append(list(current))
            save()
    except (Exception, KeyboardInterrupt) as error:
        if not isinstance(error, gd.GateError):
            kind = "preflight_environment" if current == ("preflight",) else "internal_error"
            error = gd.GateError(kind, str(error) or type(error).__name__, detail={"type": type(error).__name__})
        report.update(status="failed", failed_step=list(current), error=error.report(),
                      finished_at=gd.now())
        save(replace_latest)
        raise error
    report.pop("active_step", None)
    report.pop("active_step_directory", None)
    report.update(passed=True, status="passed", finished_at=gd.now())
    save()
    print("PASS " + level + " checks", flush=True)


def prune_successful(build, keep):
    """保留最近 N 次开发验收，其余只清理明确标为通过的门禁记录目录。"""
    runs = (build / "gate-runs").resolve()
    if not runs.is_dir():
        return []
    eligible = []
    for path in runs.iterdir():
        if (not re.fullmatch(r"\d{8}-\d{6}-[0-9a-f]{12}", path.name)
                or not path.is_dir() or path.resolve() != path):
            continue
        try:
            report = gd.read_json(path / "report.json")
        except (gd.GateError, OSError):
            continue
        if (report.get("schema_version") == 2 and report.get("run_id") == path.name
                and report.get("status") == "passed" and report.get("passed") is True
                and report.get("level") in ("host", "native")):
            stamp = report.get("finished_at")
            if isinstance(stamp, str):
                eligible.append((stamp, path))
    removed = []
    for _, path in sorted(eligible, reverse=True)[keep:]:
        if path.resolve() != path or not path.is_relative_to(runs):
            raise gd.GateError("directory_race", "清理目录已被移动：" + str(path))
        shutil.rmtree(path)
        removed.append(path.name)
    return removed


def main(argv=None):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--level", choices=("host", "native", "release"), default="host")
    parser.add_argument("--from", dest="start_from", help="从指定脚本继续；要求此前产物仍在且一致")
    parser.add_argument("--prune-successful", type=int, metavar="N", help="只保留最近 N 次成功的 host/native 门禁记录")
    parser.add_argument("--list", action="store_true", help="只列出检查顺序，不运行或检查环境")
    args = parser.parse_args(argv)
    if args.prune_successful is not None:
        if args.prune_successful < 0 or args.list or args.start_from is not None:
            parser.error("--prune-successful 需要非负整数，且不能与 --list/--from 同用")
        with gd.workspace_lock(ROOT / "build"):
            for run_id in prune_successful(ROOT / "build", args.prune_successful):
                print("已清理 " + run_id)
        return
    if args.list:
        for step in steps_for(args.level):
            print(" ".join(step))
        return
    try:
        run_checks(args.level, args.start_from)
    except gd.GateError as error:
        code = error.detail.get("exit_code") or 2
        parser.exit(code, f"[{error.kind}] {error}\n{error.hint}\n")


if __name__ == "__main__":
    main()
