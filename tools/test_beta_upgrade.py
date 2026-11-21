"""实际 beta EXE 的旧目录升级、USER CODE 和嵌套 Keil 选项回归；不运行硬件。"""
from pathlib import Path
from xml.etree import ElementTree as E
import argparse
import base64
import hashlib
import json
import re
import shutil
import subprocess
import sys
import uuid

from build_support import compile_csharp
from settings import DEFAULTS, config_header
from version import TOOL_DIRECTORY

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / "tests/fixtures/beta_upgrade"
CORE = ("rcs_route.c", "rcs_route.h", "rcs_nav.c", "rcs_nav.h", "rcs_actions.c", "rcs_actions.h")
LEGACY = CORE + ("rcs_app_task.c", "rcs_app_task.h", "rcs_feedback_port.c", "rcs_feedback_port.h",
    "rcs_can_port.c", "rcs_can_port.h", "rcs_app_config.h", "rcs_feedback_config.h", "rcs_observer_config.h")
CURRENT = LEGACY + ("app.c", "app.h", "machism.c", "machism.h")
TERRAIN = ("rcs_terrain.c", "rcs_terrain.h")
OFFICIAL082_SETUP = "188704b664bb250e7af12823a6f5cfa7b4cac09aa67fa159371e5526790b709a"
POST082 = "423301952b60be5dffead82f3f33dcbbb415ab41ca44940c0cda0d6bc1f04d0f"
POST083 = "550bbb4148c0de6310d754046deebb1d535c5d523f5b7bceac7a81e5bfb8ea97"
EXTRACT = r'''
using System;using System.IO;using System.Reflection;
static class Extract {
 static int Main(string[] a) {
  Console.OutputEncoding=new System.Text.UTF8Encoding(false);
  var assembly=Assembly.Load(File.ReadAllBytes(a[0]));
  if(a.Length==2&&a[1]=="--list") {foreach(string n in assembly.GetManifestResourceNames())Console.WriteLine(n);return 0;}
  Directory.CreateDirectory(a[1]);
  for(int i=2;i<a.Length;i++) {
   using(var input=assembly.GetManifestResourceStream(a[i])) {
    if(input==null)throw new IOException("Missing resource: "+a[i]);
    using(var output=File.Create(Path.Combine(a[1],a[i].Replace('/','_'))))input.CopyTo(output);
   }
  }
  return 0;
 }
}
'''

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def run(args, ok=True):
    p = subprocess.run(list(map(str, args)), cwd=ROOT, capture_output=True, timeout=120)
    if ok and p.returncode != 0:
        raise AssertionError("command failed: " + " ".join(map(str,args)) + "\n" +
            (p.stdout+p.stderr).decode("utf-8", errors="replace"))
    return p

def snapshot(directory):
    return {str(p.relative_to(directory)).replace("\\","/"):p.read_bytes()
            for p in Path(directory).rglob("*") if p.is_file()}

def edit_region(path, key, marker):
    text = Path(path).read_text(encoding="utf-8-sig")
    start = "/* USER CODE BEGIN " + key + " */\n"
    assert text.count(start) == 1, (path,key)
    text = text.replace(start, start + "/* " + marker + " */\n",1)
    Path(path).write_text(text,encoding="utf-8",newline="\n")

def signature(path):
    text=Path(path).read_text(encoding="utf-8-sig").replace("\r\n","\n").replace("\r","\n")
    footer=re.search(r"/\* RCS GENERATED SHA256 ([0-9a-f]{64}) \*/\n?\Z",text)
    assert footer is not None, path
    body=text[:footer.start()]
    body=re.sub(r"(?m)(^/\* USER CODE BEGIN ([A-Z0-9_]+) \*/\n).*?(?=^/\* USER CODE END \2 \*/)",
                lambda m:m.group(1),body,flags=re.S)
    assert hashlib.sha256(body.encode("utf-8")).hexdigest()==footer.group(1),path

def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--editor",default=str(ROOT/"map_editor/dist/foxmolika_MapEditor.exe"))
    args=ap.parse_args()
    editor=Path(args.editor).resolve()
    assert editor.is_file(),"先构建实际beta编辑器；不能以旧EXE代替"
    area=ROOT/"build"/("beta_upgrade_"+uuid.uuid4().hex[:10]);area.mkdir(parents=True)
    extractor_source=area/"Extract.cs";extractor_source.write_text(EXTRACT,encoding="utf-8")
    extractor=area/"extract.exe";compile_csharp(extractor,[extractor_source])
    checks=[]
    original_editor_sha=sha(editor)
    # 冻结实际EXE资源，不能拿已过时的缓存给当前源码背书。
    resources=["assets/templates/"+n for n in ("rcs_app_task.c.tpl","app.c.tpl","machism.c.tpl",
        "rcs_app_config.h.tpl","rcs_feedback_config.h.tpl")]
    resources += ["assets/"+n for n in ("rcs_feedback_port.c","rcs_can_port.c")]
    embedded=area/"beta-resources";run([extractor,editor,embedded,*resources])
    for name in resources:
        source=ROOT/"map_editor"/(name[7:] if name.startswith("assets/templates/") else "firmware/"+name[7:])
        assert (embedded/name.replace("/","_")).read_bytes()==source.read_bytes(), \
            "beta EXE资源已过时，请重新构建："+str(source)
    checks.append("actual beta executable resources match current app/machine/feedback source")

    # 夹具由实际082/083 EXE产生并已核验来源；不要求别的final目录或旧安装器存在。
    manifest_path=FIXTURES/"manifest.json"
    fixture_manifest=json.loads(manifest_path.read_text(encoding="utf-8"))
    assert fixture_manifest["fixture_format"]==1
    assert fixture_manifest["provenance"]["082"]["installer_sha256"]==OFFICIAL082_SETUP
    present={p.relative_to(FIXTURES).as_posix() for p in FIXTURES.rglob("*") if p.is_file() and p!=manifest_path}
    assert present==set(fixture_manifest["files"])
    for name,digest in fixture_manifest["files"].items():
        path=(FIXTURES/name).resolve()
        assert path.is_relative_to(FIXTURES.resolve()) and sha(path)==digest,name
    helpers=area/"legacy-helpers";helpers.mkdir()
    post082=helpers/"post082.exe";old_post=helpers/"post083.exe"
    for label,path,digest in [("082",post082,POST082),("083",old_post,POST083)]:
        path.write_bytes(base64.b64decode((FIXTURES/("post_"+label+".base64.txt")).read_bytes()))
        assert sha(path)==digest==fixture_manifest["provenance"][label]["post_sha256"]
    checks.append("portable real-output fixtures and exact legacy helper hashes match captured provenance")

    current_outputs={}
    for generation,names in [("core",CORE),("app",LEGACY)]:
        for terrain in (False,True):
            tag=generation+("8" if generation=="core" and terrain else "6" if generation=="core" else "17" if terrain else "15")
            case=area/tag;case.mkdir();m=case/"map.rcsmap.json";out=case/"generated"
            shutil.copyfile(FIXTURES/tag/"map.json",m)
            shutil.copytree(FIXTURES/tag/"generated",out)
            expected=set(names+(TERRAIN if terrain else ()))
            assert {p.name for p in out.iterdir()}==expected,(tag,list(out.iterdir()))
            for p in out.iterdir():signature(p)
            edit_region(out/"rcs_actions.c","RCS_ACTION_GLOBALS","PRESERVED_"+tag+"_ACTION")
            if generation=="app":
                edit_region(out/"rcs_app_task.c","APP_CONTEXT","PRESERVED_"+tag+"_CONTEXT")
            old_map=m.read_bytes()
            run([editor,"--export",m,out])
            assert m.read_bytes()==old_map
            assert {p.name for p in out.iterdir()}==set(CURRENT+(TERRAIN if terrain else ()))
            assert "PRESERVED_"+tag+"_ACTION" in (out/"rcs_actions.c").read_text(encoding="utf-8")
            if generation=="app":
                assert "PRESERVED_"+tag+"_CONTEXT" in (out/"rcs_app_task.c").read_text(encoding="utf-8")
            stable=snapshot(out);run([editor,"--export",m,out]);assert snapshot(out)==stable
            current_outputs[(generation,terrain)]=(m,out)
            checks.append("actual "+tag+" signed output upgrades to "+str(len(stable))+" files; USER CODE and repeated-export bytes preserved")
    # 模板安全语义已变且用户也改过该钩子时，必须拒绝，不能静默套旧运行逻辑。
    case=area/"changed-safety-hook";case.mkdir();m=case/"map.json";out=case/"generated"
    shutil.copyfile(FIXTURES/"app15/map.json",m)
    shutil.copytree(FIXTURES/"app15/generated",out)
    edit_region(out/"rcs_app_task.c","APP_RUN_STEP","USER_MODIFIED_OLD_RUN_POLICY")
    before=snapshot(out);before_map=m.read_bytes()
    assert run([editor,"--export",m,out],ok=False).returncode!=0
    assert snapshot(out)==before and m.read_bytes()==before_map
    checks.append("modified obsolete safety hook rejects upgrade and preserves every original byte")

    for deleted in [("app.c",),("machism.h",),("app.c","app.h"),("rcs_app_task.c",)]:
        case=area/("partial-"+str(len(checks)));case.mkdir()
        srcmap,src=current_outputs[("app",False)]
        m=case/"map.json";shutil.copyfile(srcmap,m);out=case/"generated";shutil.copytree(src,out)
        for name in deleted:(out/name).unlink()
        before=snapshot(out);source=m.read_bytes()
        assert run([editor,"--export",m,out],ok=False).returncode!=0
        assert snapshot(out)==before and m.read_bytes()==source
        checks.append("partial current directory "+",".join(deleted)+" refused without mutation")

    def project(label,old_version=None,helper=None,foreign_hook=None):
        root=area/label
        (root/"MDK-ARM").mkdir(parents=True);(root/"Core/Inc").mkdir(parents=True)
        (root/"Core/Src").mkdir();(root/"Modules").mkdir();(root/"Maps").mkdir()
        for p in (ROOT/"firmware").glob("*"):
            if p.is_file():shutil.copyfile(p,root/"Modules"/p.name)
        (root/"Core/Inc/rcs_config.h").write_text(config_header(DEFAULTS),encoding="utf-8")
        (root/"Core/Src/main.c").write_text("/* original entry */\n",encoding="utf-8")
        ioc=root/"tester123.ioc"
        text=(ROOT/"tests/fixtures/tester123_no_can.ioc").read_text(encoding="utf-8-sig")
        if old_version:text=text.replace("0.3.3",old_version)
        if helper is not None:
            hook="foxmolika_Tools_"+old_version.replace(".","")+"/foxmolika_PostGenerate.exe"
            hp=root/hook;hp.parent.mkdir();hp.write_bytes(helper)
            text=text.replace("ProjectManager.UAScriptAfterPath=","ProjectManager.UAScriptAfterPath="+hook)
        elif foreign_hook:
            hp=root/foreign_hook;hp.write_bytes(b"user script")
            text=text.replace("ProjectManager.UAScriptAfterPath=","ProjectManager.UAScriptAfterPath="+foreign_hook)
        ioc.write_text(text,encoding="utf-8")
        uv=root/"MDK-ARM/tester123.uvprojx"
        shutil.copyfile(ROOT/"tests/fixtures/keil_compiler_options.uvprojx",uv)
        return root,ioc,uv

    for version,helper in [("0.8.2",post082),("0.8.3",old_post)]:
        root,ioc,uv=project("upgrade-helper-"+version,version,helper.read_bytes())
        before=helper.read_bytes();oldpath=root/("foxmolika_Tools_"+version.replace(".","")+"/foxmolika_PostGenerate.exe")
        run([editor,"--associate-project",ioc])
        assert oldpath.read_bytes()==before and (root/TOOL_DIRECTORY/"foxmolika_PostGenerate.exe").is_file()
        assert TOOL_DIRECTORY in ioc.read_text(encoding="utf-8")
        stable=ioc.read_bytes();run([editor,"--associate-project",ioc]);assert ioc.read_bytes()==stable
        checks.append("official "+version+" helper migrates to beta path by exact SHA; prior helper retained; association twice stable")
    for version in ("0.8.2","0.8.3"):
        root,ioc,uv=project("unknown-helper-"+version,version,b"unrecognized modified helper")
        before=snapshot(root)
        assert run([editor,"--associate-project",ioc],ok=False).returncode!=0
        assert snapshot(root)==before
        checks.append("unknown "+version+" helper rejected before any project mutation")
    root,ioc,uv=project("foreign-script",foreign_hook="team_generate.cmd")
    before=snapshot(root)
    assert run([editor,"--associate-project",ioc],ok=False).returncode!=0
    assert snapshot(root)==before
    checks.append("foreign generation script preserved exactly")

    root,ioc,uv=project("real-nested-options")
    def overrides():
        tree=E.parse(uv)
        return [E.tostring(x) for x in tree.findall(".//FileOption")+tree.findall(".//GroupOption")]
    original_overrides=overrides()
    for terrain in (False,True):
        m=root/"Maps/map.json";run([editor,"--terrain-drive-demo" if terrain else "--demo",m])
        run([editor,"--bind-project",m,ioc]);run([editor,"--export-linked",m])
        stable=uv.read_bytes()
        run([editor,"--bind-project",m,ioc]);run([editor,"--export-linked",m])
        assert uv.read_bytes()==stable and overrides()==original_overrides
        generated=root/"Application/foxmolikaRoute"
        names=CURRENT+(TERRAIN if terrain else ())
        tree=E.parse(uv)
        for target in tree.findall("./Targets/Target"):
            items=target.findall(".//File")
            for name in names:
                matching=[f for f in items if f.findtext("FileName")==name]
                assert len(matching)==1,(name,matching)
                f=matching[0];assert f.findtext("FileType")==("1" if name.endswith(".c") else "5")
                assert f.findtext("./FileOption/CommonProperty/IncludeInBuild") in (None,"1")
            include=target.find("TargetOption/TargetArmAds/Cads/VariousControls/IncludePath").text
            assert include.split(";").count("../Application/foxmolikaRoute")==1
            assert target.findtext("TargetOption/TargetArmAds/Cads/VariousControls/Define")=="KEEP_TARGET"
        stub=root/"compile-stub";stub.mkdir(exist_ok=True)
        text=(ROOT/"tests/beta_feedback_hal/rcs_hal.h").read_text(encoding="utf-8")
        text+='\n#include "rcs_control.h"\nint RCS_Init(CAN_HandleTypeDef*);\nint RCS_Arm(void);\nvoid RCS_Lock(void);\nint RCS_SetVelocity(float,float,float);\nvoid RCS_Process(void);\nrcs_state RCS_State(void);\nrcs_fault RCS_Fault(void);\n'
        (stub/"rcs_hal.h").write_text(text,encoding="utf-8")
        (stub/"rcs_config.h").write_text(config_header(DEFAULTS),encoding="utf-8")
        for source in generated.glob("*.c"):
            run(["gcc","-std=c99","-Wall","-Wextra","-Werror","-I",stub,"-I",generated,
                 "-I",ROOT/"firmware","-c",source,"-o",stub/(source.stem+".o")])
        checks.append(str(len(names))+" actual generated files registered once with nested file/group Cads preserved; all .c compile")
    assert sha(editor)==original_editor_sha,"测试期间beta EXE被重新构建，请重跑以冻结单一版本"
    report={"passed":True,"checks":checks,"editor_sha256":original_editor_sha,
        "legacy_sources":fixture_manifest["provenance"],"fixture_manifest_sha256":sha(manifest_path),
        "legacy_helpers":{"082":sha(post082),"083":sha(old_post)},
        "hardware":False,"scope":"actual EXE migration plus generated C unit compilation; no board/Keil native claim",
        "area":str(area),"source_sha256":{str(p.relative_to(ROOT)).replace("\\","/"):sha(p)
            for p in [Path(__file__),ROOT/"map_editor/src/UserRegions.cs",ROOT/"keil_headers/ProjectBinding.cs",
                       ROOT/"keil_headers/ProjectSetup.cs"]}}
    (ROOT/"build/beta_upgrade_validation.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    print("PASS",len(checks),"actual beta legacy/partial/project upgrade scenarios")

if __name__=="__main__":
    main()
