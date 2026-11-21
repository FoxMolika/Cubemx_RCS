"""校验本轮证据与源码后整理本地beta交付；没有发布、推送或烧录入口。"""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import sys
import zipfile

from check import HOST_STEPS
from version import DISPLAY_VERSION, SETUP_NAME, EDITOR_SETUP_NAME
from windows_integrity import medium

ROOT=Path(__file__).resolve().parents[1]


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def read(name):
    return json.loads((ROOT/"build"/name).read_text(encoding="utf-8"))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    host=read("check-host.json")
    assert host["passed"] and host["continued_from"] is None
    assert host["completed"]==[list(s) for s in HOST_STEPS]
    editor=ROOT/"map_editor/dist/foxmolika_MapEditor.exe"
    setup=ROOT/"suite/dist"/SETUP_NAME
    native=read("beta_native_validation.json")
    assert native["passed"] and not native["prototype"] and native["source_stable"]
    assert native["installer_sha256"]==sha(setup)
    for name,digest in native["source_sha256"].items():
        assert sha(ROOT/name)==digest,name
    ui=read("hardware_ui_validation.json")
    project=read("hardware_project_validation.json")
    assert ui["passed"] and ui["editor_sha256"]==sha(editor)
    assert project["passed"] and project["actual_executable"]["editor_sha256"]==sha(editor)
    out=ROOT/"交付";out.mkdir(exist_ok=True)
    for p,name in [(editor,editor.name),(setup,setup.name),
                   (ROOT/"map_editor/dist"/EDITOR_SETUP_NAME,EDITOR_SETUP_NAME),
                   (ROOT/"docs/0.8.4使用说明.html","使用说明.html"),
                   (ROOT/"docs/0.8.4使用说明.md","使用说明.md")]:
        shutil.copyfile(p,out/name)
        if p.suffix.lower()==".exe":medium(out/name)
    examples=out/"示例地图";examples.mkdir(exist_ok=True)
    generated=ROOT/"build/delivery_hardware_examples";generated.mkdir(exist_ok=True)
    subprocess.run([str(editor),"--hardware-fixture",str(generated)],check=True)
    for p in generated.glob("*.rcsmap.json"):
        d=json.loads(p.read_text(encoding="utf-8-sig"));d.pop("ProjectIoc",None)
        for device in d.get("Devices",[]):
            device["Enabled"]=False;device["Configured"]=False
        if d.get("Application"):
            d["Application"].update(Enabled=False,AutoStart=False)
        (examples/p.name).write_text(json.dumps(d,ensure_ascii=False),encoding="utf-8")
    (examples/"先读我.md").write_text("示例是待配置地图：已清除工程关联，关闭设备和自动运行。请先关联自己的.ioc、填写实际外设/接线/标定，并确认后启用设备。队内C++模板需0x2000堆和ARM6.22完整C++库；源码异常选项自动登记。\n",encoding="utf-8")
    case=native["cases"][0];work=Path(case["directory"])
    code=out/"实际编译代码示例"
    for relative,names in [("foxmolikaRoute",case["route_files"]),("foxmolikaHardware",case["hardware_files"])]:
        target=code/relative;target.mkdir(parents=True,exist_ok=True)
        for name in names:
            p=work/"Application"/relative/name
            dest=target/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(p,dest)
    (code/"说明.md").write_text("这是本轮ARM6.22实际编译的源文件快照，供阅读接入方式；不是可原样烧录的通用配置。实际使用请从GUI向自己的工程导出，不需要手动逐个添加这些文件。完整隔离验收工程路径见验证记录中的native报告；不提供AXF烧录文件。\n",encoding="utf-8")
    records=out/"验证记录";records.mkdir(exist_ok=True)
    reportnames=["check-host.json","hardware_ui_validation.json","hardware_runtime_validation.json",
                 "hardware_app_validation.json","hardware_project_validation.json","beta_compatibility_validation.json",
                 "beta_native_validation.json","beta_installer_validation.json"]
    for name in reportnames:
        data=read(name)
        assert data["passed"],name
        shutil.copyfile(ROOT/"build"/name,records/name)
    (out/"验证摘要.md").write_text("""# 0.8.4本轮交付验证

- 完整host门禁通过；无硬件v3/v7/坡道地图与实际0.8.3.beta输出逐字节一致。
- 实际GUI模板/实例/资源/快照/动作检查通过；8组实际生成驱动场景、11个App整链场景通过。
- 文件交付、回滚/恢复、用户修改保护、C/C++登记与编译选项保持均有确定性测试；原生流程还会删除分组再生成验证恢复。
- 单文件一体安装器向工作区内全新CubeMX配置自动导入并校验包，真实CubeMX生成两次。
- 混合工程包含队内气缸、JY901、GPIO/PWM及自建C/H模板；所有C/C++真正由ARM6.22编译链接，0错误。3条ST HAL闪存驱动Banks未使用警告原样保留；生成代码无警告。
- C++异常支持由产品按硬件.cpp自动登记；测试未偷偷追加此编译选项。选择6.22、完整C++库和0x2000堆是明确工程配置要求。
- 未连接或烧录实车，不把时间估计当实际到位。未声称RM3508/GM6020/VESC/STD外设层已经自动适配。

本地新目录与源码包独立交付；没有推送GitHub或更改原beta/两份队内ZIP。
""",encoding="utf-8")
    archive=out/("foxmolika_Source_"+DISPLAY_VERSION+".zip")
    excluded={".git","build","dist","pack","交付","__pycache__",".vs"}
    count=0
    with zipfile.ZipFile(archive,"w",zipfile.ZIP_DEFLATED) as z:
        for folder,dirs,files in os.walk(ROOT):
            dirs[:]=sorted(n for n in dirs if n not in excluded)
            for name in sorted(files):
                p=Path(folder)/name
                if name==".git" or p.suffix.lower() in {".pyc",".exe",".axf",".o",".pdb"} or "build_log" in name:continue
                z.writestr(p.relative_to(ROOT).as_posix(),p.read_bytes());count+=1
    inventory={p.relative_to(out).as_posix():sha(p) for p in out.rglob("*") if p.is_file() and p.name not in {"交付清单.json","SHA256SUMS.txt"}}
    manifest={"version":DISPLAY_VERSION,"published":False,"hardware_test":False,"source_files":count,"sha256":inventory}
    (out/"交付清单.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding="utf-8")
    (out/"SHA256SUMS.txt").write_text("".join(v+"  "+k+"\n" for k,v in sorted(inventory.items())),encoding="utf-8")
    print(out)


if __name__=="__main__":
    main()
