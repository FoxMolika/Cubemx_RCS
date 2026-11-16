"""真实TEST4文件选项结构回归：关联/导出/生成后助手均只能使用目标级编译选项。"""
from version import TOOL_DIRECTORY
from pathlib import Path
from xml.etree import ElementTree as E
import copy
import hashlib
import json
import shutil
import subprocess
import sys
import uuid

from build_support import ROOT, compile_csharp, XML_REFERENCES


def run(args, expected=0):
    p = subprocess.run(list(map(str, args)), capture_output=True, timeout=60)
    assert p.returncode == expected, (args, p.returncode, p.stdout, p.stderr)
    return (p.stdout + p.stderr).decode("utf-8", errors="replace")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    area = ROOT / "build" / ("keil-options_" + uuid.uuid4().hex[:8])
    root = area / "project"
    (root / "MDK-ARM").mkdir(parents=True)
    (root / "Core/Inc").mkdir(parents=True)
    (root / "Modules").mkdir()
    (root / "Maps").mkdir()
    for name in ("rcs_chassis.c", "rcs_control.c", "rcs_hal.c", "rcs_chassis.h", "rcs_control.h", "rcs_hal.h"):
        shutil.copyfile(ROOT / "firmware" / name, root / "Modules" / name)
    (root / "Core/Inc/rcs_config.h").write_text("/* target include only */\n")
    ioc = root / "tester123.ioc"
    shutil.copyfile(ROOT / "tests/fixtures/tester123_no_can.ioc", ioc)
    project = root / "MDK-ARM/tester123.uvprojx"
    shutil.copyfile(ROOT / "tests/fixtures/keil_compiler_options.uvprojx", project)
    editor = ROOT / "map_editor/dist/foxmolika_MapEditor.exe"
    mapfile = root / "Maps/route.json"
    checks = []

    # 用修复前源文件编译同一调用，证明这份工程结构确实触发原始异常。
    old_source = area / "OldProjectBinding.cs"
    old_source.write_bytes(subprocess.run(
        ["git", "show", "29521a5:keil_headers/ProjectBinding.cs"], cwd=ROOT,
        capture_output=True, check=True).stdout)
    harness = area / "Reproduce.cs"
    harness.write_text('''using System;using System.IO;using System.Xml.Linq;
class Reproduce {static int Main(string[] args){try{
var b=new ProjectBinding{Root=args[0],IocName="tester123.ioc",ProjectName="tester123"};b.UpdateRoute(XDocument.Load(args[1]),args[1],true);
return 1;}catch(InvalidOperationException e){Console.WriteLine(e.GetType().Name);return 0;}}}''')
    exe = area / "reproduce.exe"
    compile_csharp(exe, [harness, old_source], references=XML_REFERENCES)
    assert "InvalidOperationException" in run([exe, root, project])
    checks.append("baseline exception reproduced on TEST4-derived nested options")

    def overrides(tree):
        return [E.tostring(x) for x in tree.findall(".//FileOption") + tree.findall(".//GroupOption")]

    initial = overrides(E.parse(project))
    run([editor, "--demo", mapfile])
    run([editor, "--bind-project", mapfile, ioc])
    run([editor, "--export-linked", mapfile])
    before = project.read_bytes()
    for _ in range(2):
        run([editor, "--bind-project", mapfile, ioc])
        run([editor, "--export-linked", mapfile])
        assert project.read_bytes() == before
    tree = E.parse(project)
    assert overrides(tree) == initial
    include = tree.find("./Targets/Target/TargetOption/TargetArmAds/Cads/VariousControls/IncludePath")
    assert include.text.split(";").count("../Application/foxmolikaRoute") == 1
    assert tree.find("./Targets/Target/TargetOption/TargetArmAds/Cads/VariousControls/Define").text == "KEEP_TARGET"
    assert len(tree.findall(".//File[FileName='rcs_config.h']")) == 1
    checks.append("associate/export three times byte-idempotent; file/group settings retained; target include added once")
    post = root / (TOOL_DIRECTORY+"/foxmolika_PostGenerate.exe")
    for _ in range(2):
        run([post])
        assert project.read_bytes() == before
    checks.append("real post-generation EXE repeated twice; target config header found despite file options appearing first")

    for terrain in (False, True):
        if terrain:
            run([editor, "--terrain-drive-demo", mapfile])
            run([editor, "--bind-project", mapfile, ioc])
            run([editor, "--export-linked", mapfile])
        tree = E.parse(project)
        targets = tree.find("Targets")
        if len(targets) == 1:
            second = copy.deepcopy(targets[0])
            second.find("TargetName").text = "SecondTarget"
            targets.append(second)
        tree.write(project, encoding="utf-8", xml_declaration=True)
        run([post])
        before = project.read_bytes()
        run([editor, "--bind-project", mapfile, ioc])
        run([editor, "--export-linked", mapfile])
        run([post])
        assert project.read_bytes() == before
        for target in E.parse(project).findall("./Targets/Target"):
            names = [f.findtext("FileName") for f in target.findall(".//File")]
            for name in ("rcs_route.c", "rcs_route.h", "rcs_nav.c", "rcs_nav.h", "rcs_actions.c", "rcs_actions.h"):
                assert names.count(name) == 1
            if terrain:
                assert names.count("rcs_terrain.c") == names.count("rcs_terrain.h") == 1
        checks.append(("terrain8" if terrain else "flat6") + " multi-target nested settings idempotent")

    valid = project.read_bytes()
    for tagpath in ("TargetOption", "TargetOption/TargetArmAds", "TargetOption/TargetArmAds/Cads",
                    "TargetOption/TargetArmAds/Cads/VariousControls"):
        for duplicate in (False, True):
            tree = E.ElementTree(E.fromstring(valid))
            target = tree.find("./Targets/Target")
            block = target.find(tagpath)
            parent = target if "/" not in tagpath else target.find(tagpath.rsplit("/", 1)[0])
            if duplicate:
                parent.append(copy.deepcopy(block))
            else:
                parent.remove(block)
            tree.write(project, encoding="utf-8", xml_declaration=True)
            malformed = project.read_bytes()
            text = run([post], 2)
            assert "Keil目标" in text and "Sequence contains" not in text
            assert project.read_bytes() == malformed
            run([editor, "--associate-project", ioc], 2)
            assert project.read_bytes() == malformed
            checks.append(("duplicate " if duplicate else "missing ") + tagpath + " rejected with project bytes retained")
    project.write_bytes(valid)
    report = {"passed": True, "checks": checks, "editor_sha256": hashlib.sha256(editor.read_bytes()).hexdigest(),
              "post_sha256": hashlib.sha256(post.read_bytes()).hexdigest(), "hardware_test": False,
              "fixture": "sanitized TEST4 FileOption; group override; target options ordered after Groups"}
    (ROOT / "build/keil_options_validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PASS", len(checks), "Keil target/file/group compiler option regressions; actual editor and post EXEs")


if __name__ == "__main__":
    main()
