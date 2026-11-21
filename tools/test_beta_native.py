"""Beta真实单文件安装、CubeMX再生成恢复硬件分组、ARM6.22 C/C++链接；绝不烧录。"""
from pathlib import Path
from xml.etree import ElementTree as E
import argparse
import copy
import hashlib
import json
import re
import shutil
import subprocess
import sys
import uuid

from keil_build import build
from settings import DEFAULTS
from version import DISPLAY_VERSION, SETUP_NAME, TOOL_DIRECTORY
from windows_integrity import medium

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def run(args, **kwargs):
    p = subprocess.run(list(map(str,args)), capture_output=True, timeout=300, **kwargs)
    if p.returncode:
        raise RuntimeError(str(args[0]) + " exit=" + str(p.returncode) + "\n" + (p.stdout+p.stderr).decode("utf-8", "replace"))
    return p


def set_value(device, key, value):
    # DataContractJsonSerializer在当前地图里将Dictionary表示为Key/Value数组。
    values = device["Values"]
    if isinstance(values, dict):
        values[key] = str(value)
    else:
        for item in values:
            if item["Key"] == key:
                item["Value"] = str(value)
                return
        values.append({"Key": key, "Value": str(value)})


def mixed_document(seed, fixtures):
    doc = copy.deepcopy(seed)
    doc["Version"] = 8
    kinds = ["team_cylinder", "team_jy901", "gpio_output", "pwm_servo", "custom"]
    devices = [copy.deepcopy(json.loads((fixtures / (kind+".rcsmap.json")).read_text(encoding="utf-8-sig"))["Devices"][0]) for kind in kinds]
    cylinder, gyro, gpio, pwm, custom = devices
    set_value(cylinder, "Port", "GPIOA")
    set_value(cylinder, "Pin", 0)
    set_value(gyro, "Uart", "huart3")
    set_value(gpio, "Port", "GPIOA")
    set_value(gpio, "Pin", 1)
    set_value(pwm, "Timer", "htim3")
    set_value(pwm, "Channel", 1)
    doc["Devices"] = devices
    doc["Application"].update(CanBus=1, BootDelayMs=2000)
    # 用GUI同等字段插入一次真实硬件动作；完成来自驱动估计，不宣称物理到位。
    point = doc["Points"][0]
    action = copy.deepcopy(point["DepartureActions"][0])
    action.update(Id=uuid.uuid4().hex, Kind=0, Name="native cylinder", DeviceId=cylinder["Id"],
                  OperationId="push", OperationValue=1, Start='if (!rcs_hw_command("'+cylinder["Id"]+'", "push", 1.0f)) return RCS_ACTION_ERROR;',
                  Condition='rcs_hw_done("'+cylinder["Id"]+'") > 0', Done="/* driver completion */",
                  Cancel='rcs_hw_cancel("'+cylinder["Id"]+'");', Timeout=5)
    point["DepartureActions"].append(action)
    doc["ActionIncludes"] = (doc.get("ActionIncludes") or "")+'\n#include "rcs_hw.h"\n'
    # 自建C/H模板也由状态机运行，证明C符号从C++桥接后真正参与链接。
    states = doc["Application"]["States"]
    route = next(s for s in states if s["Initial"])
    route["Initial"] = False
    operation = custom["Template"]["Operations"][0]["Id"]
    prepare = copy.deepcopy(route)
    prepare.update(Id=uuid.uuid4().hex, Name="native custom driver", Initial=True, Kind=0, Timeout=5,
                   Enter='if (!rcs_hw_require_command("'+custom["Id"]+'", "'+operation+'", 1.0f)) return;',
                   Tick="/* nonblocking */", Complete='rcs_hw_done("'+custom["Id"]+'") > 0',
                   Exit="/* done */", Cancel='rcs_hw_cancel("'+custom["Id"]+'");',
                   DeviceReferences=[custom["Id"]], HardwareOperations=[{"DeviceId":custom["Id"], "OperationId":operation, "Value":1}],
                   Transitions=[{"Id":uuid.uuid4().hex,"TargetId":route["Id"],"Condition":"complete","Enabled":True}])
    states.insert(0, prepare)
    doc["Application"]["Includes"] = (doc["Application"].get("Includes") or "")+'\n#include "rcs_hw.h"\n'
    return doc


def ioc_pairs(document):
    text = (ROOT / "tests/fixtures/tester123_no_can.ioc").read_text(encoding="utf-8")
    pairs = dict(line.split("=",1) for line in text.splitlines() if "=" in line)
    values = dict(DEFAULTS)
    settings = document["Settings"]
    for macro, key in [("RCS_WHEEL_DIAMETER_MM","Diameter"), ("RCS_WHEELBASE_MM","Wheelbase"),
                       ("RCS_TRACK_MM","Track"), ("RCS_OMNI_RADIUS_MM","OmniRadius"),
                       ("RCS_OMNI_THETA0_DEG","Theta0"), ("RCS_GEAR_RATIO","Gear"),
                       ("RCS_MAX_MOTOR_RPM","MaxRpm")]:
        values[macro] = settings[key]
    values.update(RCS_LAYOUT=settings["Layout"],RCS_CAN_BUS=1,RCS_KP=1)
    for i in range(4):
        values["RCS_DIRECTION_"+str(i)] = settings["Dir"+str(i)]
    prefix = "RCS.foxmolika.0.3.3."
    pairs[prefix+"IPParameters"] = "RoboticsJjfoxmolika,"+",".join(values)
    pairs.update({prefix+k: str(v) for k,v in values.items()})
    for i, peripheral in enumerate(["CAN1","NVIC","RCC","SYS","USART3","TIM3"]):
        pairs["Mcu.IP"+str(i)] = peripheral
    pairs["Mcu.IPNb"] = "6"
    pins = ["PA11","PA12","PB10","PB11","PA0","PA1","PA6"]
    for i,pin in enumerate(pins,2):
        pairs["Mcu.Pin"+str(i)] = pin
    pairs["Mcu.PinsNb"] = str(len(pins)+2)
    pairs["PA11.Mode"] = pairs["PA12.Mode"] = "CAN_Activate"
    pairs["PA11.Signal"], pairs["PA12.Signal"] = "CAN1_RX", "CAN1_TX"
    pairs["CAN1.IPParameters"] = "Prescaler,BS1,BS2,CalculateBaudRate,CalculateTimeBit,CalculateTimeQuantum"
    pairs.update({"CAN1.Prescaler":"2", "CAN1.BS1":"CAN_BS1_5TQ", "CAN1.BS2":"CAN_BS2_2TQ",
                  "CAN1.CalculateBaudRate":"1000000", "CAN1.CalculateTimeBit":"1000", "CAN1.CalculateTimeQuantum":"125.0"})
    pairs["PB10.Mode"] = pairs["PB11.Mode"] = "Asynchronous"
    pairs["PB10.Signal"], pairs["PB11.Signal"] = "USART3_TX", "USART3_RX"
    pairs.update({"USART3.IPParameters":"VirtualMode,BaudRate", "USART3.VirtualMode":"VM_ASYNC", "USART3.BaudRate":"115200"})
    for pin in ["PA0","PA1"]:
        pairs[pin+".Signal"] = "GPIO_Output"
        pairs[pin+".Locked"] = "true"
    pairs["PA6.Signal"] = "S_TIM3_CH1"
    pairs["SH.S_TIM3_CH1.0"] = "TIM3_CH1,PWM Generation1 CH1"
    pairs["SH.S_TIM3_CH1.ConfNb"] = "1"
    pairs["TIM3.Channel-PWM\\ Generation1\\ CH1"] = "TIM_CHANNEL_1"
    pairs["TIM3.Pulse-PWM\\ Generation1\\ CH1"] = "0"
    pairs["TIM3.IPParameters"] = "Channel-PWM Generation1 CH1,Prescaler,Period,AutoReloadPreload,Pulse-PWM Generation1 CH1"
    pairs["TIM3.Prescaler"], pairs["TIM3.Period"] = "15", "19999"
    pairs["TIM3.AutoReloadPreload"] = "TIM_AUTORELOAD_PRELOAD_ENABLE"
    for irq in ["CAN1_RX0","USART3"]:
        pairs["NVIC."+irq+"_IRQn"] = r"true\:0\:0\:false\:false\:true\:true\:true\:true"
    pairs["ProjectManager.HeapSize"] = "0x2000"
    pairs["ProjectManager.functionlistsort"] = "1-SystemClock_Config-RCC-false-HAL-false,2-MX_GPIO_Init-GPIO-false-HAL-true,3-MX_CAN1_Init-CAN1-false-HAL-true,4-MX_USART3_UART_Init-USART3-false-HAL-true,5-MX_TIM3_Init-TIM3-false-HAL-true"
    return pairs


def source_hashes():
    paths = [ROOT / "tools/test_beta_native.py", ROOT / "map_editor/src/HardwareGenerator.cs",
             ROOT / "map_editor/src/HardwareModel.cs", ROOT / "map_editor/src/HardwareExport.cs",
             ROOT / "keil_headers/HardwareBinding.cs", ROOT / "map_editor/templates/rcs_app_task.c.tpl",
             ROOT / "map_editor/src/TemplateGenerator.cs", ROOT / "map_editor/src/SourceTemplates.cs",
             ROOT / "map_editor/src/MachineGenerator.cs", ROOT / "map_editor/src/HardwareOperations.cs"]
    paths += list((ROOT / "map_editor/hardware").rglob("*"))
    return {p.relative_to(ROOT).as_posix(): sha(p) for p in paths if p.is_file()}


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prototype", action="store_true", help="提早发现工具链缺口；该报告不能用于最终交付")
    args = parser.parse_args()
    area = ROOT / "build" / ("beta_native_"+uuid.uuid4().hex[:8])
    area.mkdir()
    home, installed = area / "isolated_home", area / "installed_suite"
    single = area / "single_file"
    single.mkdir()
    setup = single / SETUP_NAME
    shutil.copyfile(ROOT / "suite/dist" / SETUP_NAME, setup)
    medium(setup)
    assert list(single.iterdir()) == [setup]
    report = {"version":DISPLAY_VERSION,"passed":False,"prototype":args.prototype,"hardware_test":False,
              "single_file_install":True,"isolated_home":str(home),"installer_sha256":sha(setup),
              "source_sha256":source_hashes(),"cases":[]}
    reportfile = ROOT / "build/beta_native_validation.json"
    def save():
        reportfile.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    save()
    run([setup,"/S","/TESTHOME="+str(home),"/D="+str(installed),"/LOG="+str(area / "install.log")])
    receipt = E.parse(installed / "install-success.xml").getroot()
    assert receipt.attrib["Registered"].lower() == receipt.attrib["FilesVerified"].lower() == "true"
    assert Path(receipt.attrib["Home"]).resolve() == home.resolve()
    editor = installed / "foxmolika_MapEditor.exe"
    assert sha(editor) == sha(ROOT / "map_editor/dist/foxmolika_MapEditor.exe")
    report["editor_sha256"] = sha(editor)
    cube = Path(receipt.attrib["Cube"])
    fw = Path.home() / "STM32Cube/Repository/STM32Cube_FW_F4_V1.28.3"
    run([editor,"--machine-fixture",area / "machine.json"])
    run([editor,"--hardware-fixture",area / "fixtures"])
    seed = json.loads((area / "machine.json").read_text(encoding="utf-8-sig"))
    document = mixed_document(seed,area / "fixtures")
    work = area / "team_mixed"
    work.mkdir()
    ioc = work / "tester123.ioc"
    pairs = ioc_pairs(document)
    ioc.write_text("# beta native isolated compile fixture\n"+"\n".join(k+"="+v for k,v in pairs.items())+"\n",encoding="utf-8")
    run([editor,"--associate-project",ioc])
    def generate(tag):
        script = work / (tag+".script")
        script.write_text('config load "'+ioc.as_posix()+'"\nproject setCustomFWPath "'+fw.as_posix()+'"\nconfig save\nproject generate\nexit\n',encoding="utf-8")
        p = run([cube / "jre/bin/java.exe","-Dfile.encoding=UTF-8","-DMX_UPDATER_SKIP_UPDATE=true",
                 "-Duser.home="+str(home),"-Djava.util.prefs.PreferencesFactory=WorkspacePreferencesFactory",
                 "-cp",str(installed / "validation")+";"+str(cube / "STM32CubeMX.exe"),
                 "com.st.microxplorer.maingui.STM32CubeMX","-q",script],cwd=cube)
        (work / (tag+".log")).write_bytes(p.stdout+p.stderr)
        assert (work / "MDK-ARM/tester123.uvprojx").is_file()
    generate("first")
    uv = work / "MDK-ARM/tester123.uvprojx"
    # 硬件C++包明确需要AC6；先选择工具链再绑定，生成器不静默更换用户编译器。
    shutil.copyfile(uv,work / "cube-project-before-compiler-selection.uvprojx")
    selected = E.parse(uv)
    for target in selected.findall(".//Targets/Target"):
        for key,value in [("pArmCC","6220000::V6.22::ARMCLANG"),("pCCUsed","6220000::V6.22::ARMCLANG"),("uAC6","1")]:
            node = target.find(key)
            if node is None:
                node = E.Element(key)
                target.insert(list(target).index(target.find("TargetOption")),node)
            node.text = value
        micro = target.find(".//TargetArmAds/ArmAdsMisc/useUlib")
        if micro is not None:
            micro.text = "0"
    selected.write(uv,encoding="utf-8",xml_declaration=True)
    mainfile = work / "Core/Src/main.c"
    body = mainfile.read_text(encoding="utf-8")
    for marker,insertion in [("Includes",'#include "app.h"'),("2","if (!App_Init()) { Error_Handler(); }"),("3","App_Task();")]:
        token = "/* USER CODE BEGIN "+marker+" */"
        assert body.count(token) == 1
        body = body.replace(token,token+"\n"+insertion)
    mainfile.write_bytes(body.replace("\r\n","\n").replace("\n","\r\n").encode("utf-8"))
    mainhash = sha(mainfile)
    mapfile = work / "route.rcsmap.json"
    mapfile.write_text(json.dumps(document,ensure_ascii=False),encoding="utf-8")
    run([editor,"--bind-project",mapfile,ioc])
    run([editor,"--export-linked",mapfile])
    route = work / "Application/foxmolikaRoute"
    hardware = work / "Application/foxmolikaHardware"
    route_names = sorted(p.name for p in route.iterdir())
    assert len(route_names) == 19
    manifest = E.parse(hardware / "hardware.xml").getroot()
    hardware_names = [e.attrib["Path"] for e in manifest.findall("File")]
    assert {"rcs_cylinder.cpp","rcs_gyro.cpp","rcs_hw.cpp","rcs_hw_callbacks.c","my_device.c","my_device.h"}.issubset(hardware_names)
    for entry in manifest.findall("File"):
        assert sha(hardware / entry.attrib["Path"]) == entry.attrib["Sha256"]
    def check_registration():
        files = E.parse(uv).findall(".//File")
        for filename in route_names+hardware_names:
            matches = [f for f in files if f.findtext("FileName") == Path(filename).name]
            assert len(matches) == 1, filename
            extension = Path(filename).suffix.lower()
            expected = "1" if extension == ".c" else "8" if extension == ".cpp" else "5"
            assert matches[0].findtext("FileType") == expected, filename
            if extension == ".cpp":
                assert "-fexceptions" in (matches[0].findtext("FileOption/FileArmAds/Cads/VariousControls/MiscControls") or ""), filename
    check_registration()
    hashes = {p.relative_to(work).as_posix():sha(p) for p in [*route.iterdir(),*(hardware / n for n in hardware_names),hardware / "hardware.xml"]}
    tree = E.parse(uv)
    for groups in tree.findall(".//Groups"):
        for group in list(groups):
            if group.findtext("GroupName") in ["Application/foxmolikaRoute","Application/foxmolikaHardware"]:
                groups.remove(group)
    for include in tree.findall(".//TargetOption/TargetArmAds/Cads/VariousControls/IncludePath"):
        include.text = ";".join(p for p in (include.text or "").split(";") if "foxmolikaHardware" not in p and "foxmolikaRoute" not in p)
    tree.write(uv,encoding="utf-8",xml_declaration=True)
    generate("repeat")
    assert sha(mainfile) == mainhash
    check_registration()
    assert all(sha(work / n)==value for n,value in hashes.items())
    post = work / TOOL_DIRECTORY / "foxmolika_PostGenerate.exe"
    run([post,"--check-route",work])
    irq = (work / "Core/Src/stm32f4xx_it.c").read_text(encoding="utf-8")
    for handler in ["CAN1_RX0_IRQHandler","USART3_IRQHandler"]:
        assert "void "+handler+"(void)" in irq
    maintext = mainfile.read_text(encoding="utf-8")
    for function in ["MX_CAN1_Init();","MX_USART3_UART_Init();","MX_TIM3_Init();"]:
        assert function in maintext
    coretext = "\n".join(p.read_text(encoding="utf-8") for p in (work / "Core/Src").glob("*.c"))
    assert "htim3.Init.Prescaler = 15;" in coretext and "htim3.Init.Period = 19999;" in coretext
    assert "0x2000" in ioc.read_text(encoding="utf-8")
    assert all(t.findtext("uAC6") == "1" for t in E.parse(uv).findall(".//Targets/Target")), "CubeMX regeneration lost selected ARM6 compiler"
    result = build(uv,work / "keil-private.log")
    raw = (work / "keil-private.log").read_text(encoding="utf-8",errors="replace")
    warnings = [line for line in raw.splitlines() if "warning:" in line.lower()]
    report["cases"].append({"name":"team_mixed","directory":str(work),"route_files":route_names,"hardware_files":hardware_names,
                            "devices":[{"id":d["Id"],"template":d["TemplateId"],"template_sha256":d["TemplateSha256"]} for d in document["Devices"]],
                            "map_sha256":sha(mapfile),"project_sha256":sha(uv),"post_sha256":sha(post),"generated_sha256":hashes,
                            "main_preserved":True,"regenerated_restored_files":True,
                            "compiler_selected_explicitly":"ARM6.22, full C++ library; no extra exception flags added by test",
                            "keil":result["summary"],"warnings":warnings})
    save()
    assert result["exit"] in (0,1) and any("0 Error(s)," in s for s in result["summary"]), result
    assert any("V6.22" in s for s in result["summary"])
    assert all("Drivers/STM32F4xx_HAL_Driver/Src/stm32f4xx_hal_flash_ex.c" in line and "unused parameter 'Banks' [-Wunused-parameter]" in line for line in warnings), warnings
    for filename in route_names+hardware_names:
        if Path(filename).suffix.lower() in [".c",".cpp"]:
            assert re.search(r"compiling\s+"+re.escape(Path(filename).name),raw,re.I), filename
    assert result["axf_files"]
    report["cases"][0]["axf_sha256"] = sha(result["axf_files"][0])
    report["source_stable"] = source_hashes() == report["source_sha256"]
    if not args.prototype:
        assert report["source_stable"], "验证期间源码变化；本轮仅作为诊断，冻结后重新验证"
    report["passed"] = not args.prototype
    save()
    print("PASS beta native mixed: real team cylinder/JY901 + GPIO/PWM + custom C/H; CubeMX twice, all C/C++ registered and ARM6.22 linked; "+str(len(warnings))+" ST HAL warnings; no board/flashing",flush=True)


if __name__ == "__main__":
    main()
