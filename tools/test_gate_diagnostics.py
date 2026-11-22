"""门禁故障注入。使用独立目录，不调用原生工具或发布器。"""
from pathlib import Path
import os
import subprocess
import sys
from tempfile import TemporaryDirectory
from unittest.mock import patch

import check
import gate_diagnostics as gd
import release
from check import ROOT, prune_successful, resume_prefix, steps_for
from release_evidence import ACCEPTANCE, EDITOR, before_step, live_acceptance, record_outputs


def rejects(kind, action):
    try:
        action()
    except gd.GateError as error:
        assert error.kind == kind, error.report()
        return error
    raise AssertionError("未拒绝：" + kind)


def verify_resume_inherits_records(root, outcome):
    """走续跑入口，核对传到打包和完成报告的确是原始记录。"""
    root.mkdir()
    source = {"commit": "1" * 40}
    subprocess_step = tuple(outcome["argv"][1:])
    final = check.RELEASE_STEPS[0]
    plan = (subprocess_step, final)
    latest = root / "build/check-release.json"
    prior = {"schema_version": 2, "level": "release", "status": "failed",
             "run_id": "previous-attempt", "source_provenance": source,
             "completed": [list(subprocess_step)], "failed_step": list(final),
             "steps": [outcome], "evidence": {}, "evidence_paths": {},
             "installed_editor_sha256": "fixture"}
    with patch.multiple(check, ROOT=root, steps_for=lambda level: plan,
                        preflight=lambda level: None, capture=lambda path: source,
                        require_source=lambda record: None, verify_prefix=lambda path, report: None):
        gd.write_report(latest, dict(prior, steps=[]))
        rejects("stale_evidence", lambda: check.run_checks("release", final[0]))
        missing = dict(outcome, stdout_log=str(root / "missing.log"))
        gd.write_report(latest, dict(prior, steps=[missing]))
        rejects("missing_evidence", lambda: check.run_checks("release", final[0]))
        gd.write_report(latest, prior)

        def package(report, directory, destination):
            assert report["resumed_run_id"] == prior["run_id"]
            assert report["steps"] == prior["steps"]

        with patch.object(release, "package", side_effect=package) as packaging:
            check.run_checks("release", final[0])
            assert packaging.call_count == 1
        finished = gd.read_json(latest)
        assert finished["passed"] and finished["verified_prior_prefix"]
        assert finished["steps"] == prior["steps"]
        assert finished["completed"] == [list(step) for step in plan]


def main():
    with TemporaryDirectory(prefix="gate-test-", dir=ROOT / "build") as temporary:
        root = Path(temporary)
        def run(name, code, args=(), **options):
            return gd.run([sys.executable, "-c", code, *args], cwd=root,
                          directory=root / name, **options)

        argument = '中文 with spaces "quoted"'
        result = run("argv", "import sys; print(sys.argv[1])", (argument,))
        assert result["exit_code"] == 0 and result["stdout"]["tail"] == argument
        assert gd.read_json(root / "argv/step.json")["argv"][-1] == argument
        rejects("argument_error", lambda: gd.run("python -c pass", cwd=root, directory=root / "bad"))
        result = run("gbk", "import sys;sys.stderr.buffer.write('无法写入目录'.encode('gbk'));sys.exit(7)")
        assert result["exit_code"] == 7 and result["stderr"]["encoding"] == "gbk"
        assert result["stderr"]["tail"] == "无法写入目录"
        result = run("big", "print('x'*1_000_000+'TAIL')")
        assert result["stdout"]["truncated"] and result["stdout"]["tail"].endswith("TAIL")
        assert len(result["stdout"]["tail"].encode()) <= gd.TAIL_BYTES
        assert Path(result["stdout_log"]).stat().st_size > 1_000_000
        error = rejects("subprocess_exit", lambda: run("timeout", "import time;print('PARTIAL');time.sleep(20)", timeout=1))
        assert error.detail["status"] == "timed_out" and error.detail["stdout"]["tail"] == "PARTIAL"
        assert gd.read_json(root / "timeout/step.json")["status"] == "timed_out"
        first = run("repeat-1", "print('first')")
        run("repeat-2", "print('second')")
        assert (root / "repeat-1/stdout.log").read_text().strip() == "first"
        verify_resume_inherits_records(root / "resume", first)

        contender = ("from pathlib import Path; from gate_diagnostics import workspace_lock\n"
                     "with workspace_lock(Path('.')): print('ACQUIRED',flush=True)")
        command = [sys.executable, "-c", contender]
        env = dict(os.environ, PYTHONPATH=str(ROOT / "tools"), PYTHONIOENCODING="utf-8")
        with gd.workspace_lock(root):
            blocked = subprocess.run(command, cwd=root, env=env, capture_output=True, timeout=10)
            assert blocked.returncode and b"ACQUIRED" not in blocked.stdout
        unlocked = subprocess.run(command, cwd=root, env=env, capture_output=True, timeout=10)
        assert unlocked.returncode == 0 and b"ACQUIRED" in unlocked.stdout

        report_path = root / ACCEPTANCE
        install = root / "suite/build/install"
        gd.write_report(report_path, {"installed_directory": str(install)})
        step = ("suite/tools/verify.py",)
        run_area = root / "run"
        before_step(root, run_area, step, {})
        assert not report_path.exists() and (run_area / "prior" / ACCEPTANCE).exists()
        rejects("missing_evidence", lambda: record_outputs(root, run_area, step, {"evidence": {}}))
        gd.write_report(report_path, {"installed_directory": str(install)})
        report = {"evidence": {EDITOR: "placeholder"}}
        record_outputs(root, run_area, step, report)
        rejects("missing_evidence", lambda: live_acceptance(root, report["evidence"]))
        install.mkdir()
        editor = install / Path(EDITOR).name
        editor.write_bytes(b"installed")
        rejects("hash_mismatch", lambda: live_acceptance(root, report["evidence"]))
        report["evidence"][EDITOR] = gd.file_sha(editor)
        assert live_acceptance(root, report["evidence"]) == gd.file_sha(editor)
        gd.write_report(report_path, {"installed_directory": str(install), "stale": True})
        rejects("hash_mismatch", lambda: before_step(root, run_area, ("suite/tools/finish_tests.py",), report["evidence"]))
        rejects("stale_evidence", lambda: resume_prefix({"schema_version": 1}, {}, steps_for("release"), root))
        saved = {}
        for index, status, level in ((1, "passed", "host"), (2, "passed", "native"),
                                     (3, "failed", "host"), (4, "passed", "release")):
            run_id = f"20261122-00120{index}-{index:012x}"
            path = root / "gate-runs" / run_id
            gd.write_report(path / "report.json", {"schema_version": 2, "run_id": run_id,
                            "status": status, "level": level, "passed": status == "passed",
                            "finished_at": f"2026-11-22T00:12:0{index}Z"})
            saved[index] = path
        assert prune_successful(root, 1) == [saved[1].name]
        assert not saved[1].exists() and all(saved[n].exists() for n in (2, 3, 4))
    print("PASS gate logs, timeout, process lock, fresh evidence, resume and retention")


if __name__ == "__main__":
    main()
