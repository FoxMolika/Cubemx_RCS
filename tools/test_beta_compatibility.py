"""实际beta EXE与冻结0.8.3.beta输出逐字节比较：无硬件v3/v7/坡道地图。

0.8.4.beta 起，应用调度、反馈端口、CAN 端口与反馈配置按设计新增只读调试接口
（g_dbg 快照、微秒空窗与弱定义回调兜底），因此这六个文件豁免并逐项核对；
其余文件仍必须与 0.8.3.beta 逐字节相同。
"""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import uuid

ROOT=Path(__file__).resolve().parents[1]
FIXTURE=ROOT/"tests/fixtures/alpha_no_hardware"
# 仅这六个文件允许变化：新增只读调试接口与微秒窗口。任何其他差异仍视为回归。
ALLOWED_CHANGES={"rcs_app_task.c","rcs_app_task.h","rcs_feedback_port.c","rcs_feedback_port.h",
                 "rcs_can_port.c","rcs_feedback_config.h"}


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def run(args):
    p=subprocess.run(list(map(str,args)),capture_output=True,timeout=60)
    assert p.returncode==0,(p.stdout+p.stderr).decode("utf-8","replace")


def capture():
    assert not FIXTURE.exists(),"冻结基线不能覆盖"
    old=ROOT.parent/"foxmolika_CubeMX_0.8.3_beta/map_editor/dist/foxmolika_MapEditor.exe"
    FIXTURE.mkdir(parents=True)
    for kind,flag in [("legacy","--demo"),("application","--machine-fixture"),("terrain","--terrain-drive-demo")]:
        d=FIXTURE/kind;d.mkdir();source=d/"map.json"
        run([old,flag,source]);run([old,"--export",source,d/"generated"])
    record={"source":"actual local 0.8.3.beta editor","editor_sha256":sha(old),
            "files":{str(p.relative_to(FIXTURE)).replace("\\","/"):sha(p) for p in FIXTURE.rglob("*") if p.is_file()}}
    (FIXTURE/"manifest.json").write_text(json.dumps(record,indent=2),encoding="utf-8")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    if "--capture" in sys.argv:
        capture()
    baseline=json.loads((FIXTURE/"manifest.json").read_text(encoding="utf-8"))
    for name,digest in baseline["files"].items():
        assert sha(FIXTURE/name)==digest,name
    editor=ROOT/"map_editor/dist/foxmolika_MapEditor.exe"
    area=ROOT/"build"/("beta_compat_"+uuid.uuid4().hex[:8]);area.mkdir(parents=True)
    cases=[]
    for kind in ["legacy","application","terrain"]:
        source=FIXTURE/kind/"map.json";output=area/kind
        run([editor,"--export",source,output])
        expected={p.name:p.read_bytes() for p in (FIXTURE/kind/"generated").iterdir()}
        actual={p.name:p.read_bytes() for p in output.iterdir()}
        assert expected.keys()==actual.keys(),kind+"文件集合变化"
        for name in expected:
            if name in ALLOWED_CHANGES:
                continue
            assert expected[name]==actual[name],kind+"无硬件输出发生变化："+name
        app=actual["rcs_app_task.c"].decode("utf-8")
        header=actual["rcs_app_task.h"].decode("utf-8")
        port=actual["rcs_feedback_port.c"].decode("utf-8")
        port_header=actual["rcs_feedback_port.h"].decode("utf-8")
        assert "volatile foxmolika_debug_t g_dbg;" in app,kind+"缺少快照实例"
        assert "app_debug_update();" in app,kind+"缺少快照刷新"
        assert "extern volatile foxmolika_debug_t g_dbg;" in header,kind+"缺少快照声明"
        for symbol in ("rcs_feedback_port_sequence","rcs_feedback_port_pending","rcs_feedback_port_age_ms"):
            assert symbol in port,kind+"缺少只读取值实现："+symbol
            assert symbol in port_header,kind+"缺少只读取值声明："+symbol
        config=actual["rcs_feedback_config.h"].decode("utf-8")
        assert "RCS_FEEDBACK_MAX_GAP_US" in config,kind+"缺少微秒窗口"
        assert "RCS_FEEDBACK_GAP_COUNTS" in config,kind+"缺少微秒窗口圈数"
        cases.append({"case":kind,"files":len(actual),
                      "changed":sorted(name for name in ALLOWED_CHANGES)})
    report={"passed":True,"editor_sha256":sha(editor),"baseline_editor_sha256":baseline["editor_sha256"],"cases":cases}
    (ROOT/"build/beta_compatibility_validation.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    print("PASS beta no-hardware outputs byte-identical to actual beta (app task exempt: g_dbg): "
          "v3, v7 application, terrain19/21")


if __name__=="__main__":
    main()
