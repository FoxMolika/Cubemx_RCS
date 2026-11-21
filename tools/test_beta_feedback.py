"""Compile the actual beta CAN/feedback ports with deterministic CAN-angle and IRQ fixtures."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "map_editor/firmware"
STUB = ROOT / "tests/beta_feedback_hal"
HARNESS = ROOT / "tests/test_beta_feedback.c"

def run(args, expected=0):
    p = subprocess.run(list(map(str, args)), cwd=ROOT, capture_output=True, text=True,
                       encoding="utf-8", errors="replace", timeout=60)
    if p.returncode != expected:
        raise AssertionError("command failed:\n" + " ".join(map(str,args)) + "\n" + p.stdout + p.stderr)
    return p

def main():
    sys.stdout.reconfigure(encoding="utf-8")
    area = ROOT / "build" / ("beta_feedback_" + uuid.uuid4().hex[:10])
    area.mkdir(parents=True)
    reports = []
    cases = [
        ("omni3_can2", 1, 2, (3, 1, 2, 4), []),
        ("mecanum_can1", 0, 1, (1, 2, 3, 4), []),
        ("omni4_permuted", 2, 2, (4, 2, 1, 3), []),
        ("existing_callback", 1, 2, (3, 1, 2, 4), ["-DRCS_CAN_PORT_DEFINE_HAL_CALLBACK=0"]),
        ("only_defines", 1, 2, (3, 1, 2, 4), ["-DRCS_FEEDBACK_WHEELS=3", "-DRCS_FEEDBACK_CAN_BUS=2", "-DRCS_FEEDBACK_MOTOR_IDS={3,1,2,4}"]),
        ("duplicate_ids", 1, 2, (3, 3, 2, 4), ["-DEXPECT_VALID=0"]),
        ("unsafe_gap", 0, 1, (1, 2, 3, 4), ["-DEXPECT_VALID=0", "-DRCS_FEEDBACK_MAX_GAP_MS=100"]),
        ("no_chassis", 0, 1, (1, 2, 3, 4), ["-DEXPECT_VALID=0", "-DRCS_NO_CHASSIS=1"]),
    ]
    for label, layout, bus, ids, defines in cases:
        case = area / label
        case.mkdir()
        config = case / "rcs_config.h"
        config_text = "#pragma once\n" + "\n".join([
            "#define RCS_LAYOUT " + str(layout), "#define RCS_CAN_BUS " + str(bus),
            "#define RCS_MAX_MOTOR_RPM 3000", "#define RCS_FEEDBACK_TIMEOUT_MS 100",
            "#define RCS_DIRECTION_0 -1",
            *["#define RCS_MOTOR_ID_" + str(i) + " " + str(value) for i, value in enumerate(ids)]
        ]) + "\n"
        if label != "only_defines":
            config.write_text(config_text,encoding="utf-8")
        exe = case / "feedback.exe"
        args = ["gcc", "-std=c99", "-Wall", "-Wextra", "-Werror", "-O2",
                "-I", case, "-I", STUB, "-I", SOURCE, *defines, HARNESS,
                SOURCE / "rcs_feedback_port.c", SOURCE / "rcs_can_port.c", "-lm", "-o", exe]
        run(args)
        p = run([exe])
        print(p.stdout.strip())
        reports.append({"case":label, "report":p.stdout.strip()})
    # A pre-existing global callback must be an explicit conflict, never silently replaced.
    extra = area / "duplicate_callback.c"
    extra.write_text('#include "rcs_hal.h"\nvoid HAL_CAN_RxFifo0MsgPendingCallback(CAN_HandleTypeDef *can) {(void)can;}\n',encoding="utf-8")
    base = area / "mecanum_can1"
    p = subprocess.run(["gcc", "-std=c99", "-I", str(base), "-I", str(STUB), "-I", str(SOURCE),
                        str(HARNESS), str(extra), str(SOURCE/"rcs_feedback_port.c"),
                        str(SOURCE/"rcs_can_port.c"), "-lm", "-o", str(area/"must-not-link.exe")],
                       cwd=ROOT,capture_output=True,text=True,encoding="utf-8",errors="replace",timeout=30)
    assert p.returncode != 0 and "HAL_CAN_RxFifo0MsgPendingCallback" in p.stderr
    reports.append({"case":"duplicate_callback", "report":"PASS duplicate global callback rejected"})
    print(reports[-1]["report"])
    evidence = {"passed":True, "cases":reports, "hardware":False,
                "source_sha256":{str(p.relative_to(ROOT)).replace("\\","/"):hashlib.sha256(p.read_bytes()).hexdigest()
                 for p in [*(SOURCE/n for n in ["rcs_feedback_port.c","rcs_feedback_port.h",
                    "rcs_feedback_config.h","rcs_can_port.c","rcs_can_port.h"]), HARNESS,
                    STUB/"rcs_hal.h", Path(__file__)]}}
    (ROOT/"build/beta_feedback_validation.json").write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding="utf-8")
    print("PASS",len(reports),"beta feedback/CAN configurations and callback ownership checks")

if __name__ == "__main__":
    main()
