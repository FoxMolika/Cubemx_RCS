"""微秒帧间隔窗口回归：13680rpm 下 1035µs 必须通过、2200µs 必须锁存。"""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "map_editor/firmware"
BASE = ROOT / "tests/beta_feedback_hal"
DWT = ROOT / "tests/beta_feedback_dwt"
HARNESS = ROOT / "tests/test_feedback_gap_us.c"


def run(args, expected=0):
    p = subprocess.run(list(map(str, args)), cwd=ROOT, capture_output=True, text=True,
                       encoding="utf-8", errors="replace", timeout=60)
    if p.returncode != expected:
        raise AssertionError("command failed:\n" + " ".join(map(str, args)) + "\n" + p.stdout + p.stderr)
    return p


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    area = ROOT / "build" / ("feedback_gap_us_" + uuid.uuid4().hex[:10])
    area.mkdir(parents=True)
    case = area / "case"
    case.mkdir()
    (case / "rcs_config.h").write_text(
        "#pragma once\n"
        "#define RCS_LAYOUT 0\n"
        "#define RCS_CAN_BUS 1\n"
        "#define RCS_MAX_MOTOR_RPM 13680\n"
        "#define RCS_FEEDBACK_TIMEOUT_MS 100\n"
        "#define RCS_MOTOR_ID_0 1\n"
        "#define RCS_MOTOR_ID_1 2\n"
        "#define RCS_MOTOR_ID_2 3\n"
        "#define RCS_MOTOR_ID_3 4\n",
        encoding="utf-8")
    exe = area / "gap_us.exe"
    run(["gcc", "-std=c99", "-Wall", "-Wextra", "-Werror", "-O2",
         "-I", case, "-I", DWT, "-I", BASE, "-I", SOURCE,
         HARNESS, SOURCE / "rcs_feedback_port.c", "-lm", "-o", exe])
    output = run([exe]).stdout.strip()
    print(output)
    evidence = {
        "passed": True,
        "case": "13680rpm microsecond window",
        "detail": output,
        "source_sha256": {
            str(p.relative_to(ROOT)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in [SOURCE / "rcs_feedback_port.c", SOURCE / "rcs_feedback_config.h",
                      HARNESS, DWT / "rcs_hal.h", Path(__file__)]
        },
    }
    (ROOT / "build/feedback_gap_us_validation.json").write_text(
        json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
