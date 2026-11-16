"""对实际基线EXE做输出比较；唯一允许差异是明确复现的坡道复位修复。"""
from pathlib import Path
import copy
import hashlib
import io
import json
import subprocess
import sys
import uuid
import zipfile

from build_support import ROOT, compile_csharp

BASELINE = "aaeeca5e6805304536d8c22c648774248d97649d"
RESET_FIX = b"    n->terrain.active = 0;\n    n->terrain.fault = RCS_TERRAIN_OK;\n"


def run(args, timeout=120):
    result = subprocess.run(list(map(str, args)), cwd=ROOT, capture_output=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError(result.stdout.decode("utf-8", errors="replace") +
                           result.stderr.decode("utf-8", errors="replace"))
    return result.stdout.decode("utf-8", errors="replace").strip()


def baseline_editor(area):
    """使用仓库内历史对象，无需网络或相邻旧安装目录；缓存绑定提交和EXE摘要。"""
    cache = ROOT / "build/refactor-baseline"
    receipt = cache / "baseline.json"
    editor = cache / "map_editor/dist/foxmolika_MapEditor.exe"
    if receipt.exists() and editor.exists():
        evidence = json.loads(receipt.read_text())
        if evidence == {"revision": BASELINE, "editor_sha256": digest(editor)}:
            return editor
    archive = subprocess.run(["git", "archive", "--format=zip", BASELINE], cwd=ROOT,
                             capture_output=True, check=True)
    # 只解包git产生的相对路径，拒绝链接/越界项；不删除此前的测试目录。
    target = area / "baseline"
    target.mkdir()
    with zipfile.ZipFile(io.BytesIO(archive.stdout)) as z:
        for name in z.namelist():
            assert (target / name).resolve().is_relative_to(target.resolve()), name
        z.extractall(target)
    run([sys.executable, target / "map_editor/tools/build.py"])
    (target / "baseline.json").write_text(json.dumps({
        "revision": BASELINE, "editor_sha256": digest(target / "map_editor/dist/foxmolika_MapEditor.exe")
    }))
    # 首次缓存使用目录重命名；已存在但不符时保留现场并使用本轮独立目录。
    if not cache.exists():
        target.rename(cache)
        return editor
    return target / "map_editor/dist/foxmolika_MapEditor.exe"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def template_contract(area):
    source = area / "TemplateContract.cs"
    source.write_text(r'''
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RcsMap;
namespace RcsMap {
 static class Exporter {
  internal static string Root;
  internal static readonly Dictionary<string,string> Edits=new Dictionary<string,string>();
  public static string Resource(string name) {return Edits.ContainsKey(name)?Edits[name]:File.ReadAllText(Path.Combine(Root,name.StartsWith("templates/")?name:"firmware/"+name));}
 }
}
static class Contract {
 static int count;
 static void Check(bool ok){if(!ok)throw new Exception("template contract failed");count++;}
 static void Reject(Action action){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected);}
 static void Main(string[] args){Exporter.Root=args[0];
  string block="/* RCS TEMPLATE BEGIN TEST */\noriginal\n/* RCS TEMPLATE END TEST */\n";
  Check(TemplateBlocks.Render(block,"test",new[]{"TEST"},null)=="original\n");
  Check(TemplateBlocks.Render(block.Replace("\n","\r\n"),"test",new[]{"TEST"},null)=="original\n");
  Reject(()=>TemplateBlocks.Render(block+block,"test",new[]{"TEST"},null));
  Reject(()=>TemplateBlocks.Render(block.Replace("END TEST","END WRONG"),"test",new[]{"TEST"},null));
  Reject(()=>TemplateBlocks.Render(block.Replace("BEGIN TEST","BEGIN TEST extra"),"test",new[]{"TEST"},null));
  Reject(()=>TemplateBlocks.Render("","test",new[]{"TEST"},null));
  Reject(()=>TemplateBlocks.Render(block,"test",new[]{"OTHER"},null));
  Reject(()=>TemplateBlocks.Render(block,"test",new[]{"TEST"},new Dictionary<string,string>()));
  Reject(()=>TemplateBlocks.Definitions(block+"unowned","test"));
  Reject(()=>TemplateBlocks.Render(block.Replace("original\n",block),"test",new[]{"TEST"},null));
  Check(TemplateBlocks.Render(block,"test",new[]{"TEST"},new Dictionary<string,string>{{"TEST",block}})==block);
  Check(SourceTemplates.Substitute("@VALUE@",new Dictionary<string,string>{{"VALUE","@UNDEFINED@"}})=="@UNDEFINED@");
  // 遍历实际模板中的每个插槽：误删或重名必须明确失败，而非只检查最终三个字符串。
  foreach(string file in new[]{"rcs_nav.c","rcs_nav.h","rcs_route.c","rcs_actions.c","rcs_actions.h"}){
   string resource=file.StartsWith("rcs_nav")?file:"templates/"+file+".tpl";
   string original=Exporter.Resource(resource);
   var definitions=TemplateBlocks.Definitions(Exporter.Resource("templates/terrain_"+file.Substring(4)+".tpl"),file);
   string[] expected=definitions.Keys.ToArray();
   Check(TemplateBlocks.Render(original,file,expected,null).Length>0);
   Check(TemplateBlocks.Render(original,file,expected,definitions).Length>0);
   foreach(var key in expected){
    string missing=Regex.Replace(original,@"(?ms)^/\* RCS TEMPLATE BEGIN "+key+@" \*/\r?\n.*?^/\* RCS TEMPLATE END "+key+@" \*/\r?\n", "");
    Check(missing!=original);
    Reject(()=>TemplateBlocks.Render(missing,file,expected,null));
    Exporter.Edits[resource]=original.Replace("/* RCS TEMPLATE BEGIN "+key+" */","/* removed begin */");
    Reject(()=>SourceTemplates.Render(file,new Dictionary<string,string>(),false));
    Reject(()=>SourceTemplates.Render(file,new Dictionary<string,string>(),true));
   }
   Exporter.Edits.Remove(resource);
  }
  Console.WriteLine("PASS "+count+" template contracts: required slots, duplicates, malformed blocks and single-pass insertion");
 }
}
''', encoding="utf-8")
    exe = area / "template-contract.exe"
    compile_csharp(exe, [source, ROOT / "map_editor/src/TemplateBlocks.cs",
                        ROOT / "map_editor/src/SourceTemplates.cs"], references=["System.Core.dll"])
    return run([exe, ROOT / "map_editor"])


def compare(old, current):
    before = {p.name: p.read_bytes() for p in old.iterdir()}
    after = {p.name: p.read_bytes() for p in current.iterdir()}
    assert before.keys() == after.keys()
    for name, content in before.items():
        actual = after[name]
        if name == "rcs_nav.c" and "rcs_terrain.c" in after:
            assert actual.count(RESET_FIX) == 1
            # 除两条复位语句及随之改变的生成签名外，其余字节必须完全相同。
            assert actual.replace(RESET_FIX, b"").split(b"/* RCS GENERATED SHA256 ")[0] == \
                   content.split(b"/* RCS GENERATED SHA256 ")[0], name
        else:
            assert content == actual, name
    return len(after)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    area = ROOT / "build" / ("refactor_" + uuid.uuid4().hex[:8])
    area.mkdir(parents=True)
    baseline = baseline_editor(area)
    editor = ROOT / "map_editor/dist/foxmolika_MapEditor.exe"
    contract = template_contract(area)
    run([baseline, "--demo", area / "flat.json"])
    run([baseline, "--terrain-drive-fixture", area / "fixtures"])
    run([baseline, "--correction-fixture", area / "fixtures"])
    flat = json.loads((area / "flat.json").read_text(encoding="utf-8-sig"))
    cases = {}
    for layout in range(3):
        document = copy.deepcopy(flat)
        document["Settings"]["Layout"] = layout
        cases["flat" + str(layout)] = document
    for file in (area / "fixtures").glob("*.json"):
        cases[file.stem] = json.loads(file.read_text(encoding="utf-8-sig"))
    tokens = copy.deepcopy(flat)
    tokens["ActionIncludes"] = '/* @UNDEFINED@ */\nconst char *template_literal = "/* RCS TEMPLATE BEGIN LITERAL */";'
    cases["user-tokens"] = tokens
    terrain = cases["terrain-layout0"]
    header = copy.deepcopy(terrain)
    header["TerrainDrive"]["BrakeHeader"] = "terrain_brake_port.h"
    cases["brake-header"] = header
    reverse = copy.deepcopy(terrain)
    reverse["Points"].reverse()
    cases["reverse"] = reverse
    shaped = copy.deepcopy(terrain)
    shaped["Terrain"]["Surfaces"][0]["Outline"] = [
        {"X": 0, "Y": 0}, {"X": 1, "Y": .2}, {"X": 1, "Y": .8}, {"X": 0, "Y": 1}]
    cases["shaped"] = shaped
    for name in ("course040", "migration037"):
        fixture = ROOT / "tests/fixtures" / name / "map.rcsmap.json"
        if fixture.exists(): cases[name] = json.loads(fixture.read_text(encoding="utf-8-sig"))
    counts = {}
    for name, document in cases.items():
        source = area / (name + ".json")
        source.write_text(json.dumps(document, ensure_ascii=False), encoding="utf-8")
        old, current = area / (name + "-before"), area / (name + "-after")
        run([baseline, "--export", source, old])
        run([editor, "--export", source, current])
        counts[name] = compare(old, current)
    recovery = []
    for label in ("before", "after"):
        folder = area / ("terrain-layout0-" + label)
        exe = area / ("reset-" + label + ".exe")
        args = ["gcc", "-std=c99", "-Wall", "-Wextra", "-Werror", "-pedantic", "-I", folder,
                "-I", ROOT / "firmware", ROOT / "map_editor/tests/test_terrain_reset.c"]
        if label == "before": args.append("-DEXPECT_LEGACY_BUG")
        args += [folder / name for name in ("rcs_route.c", "rcs_nav.c", "rcs_actions.c", "rcs_terrain.c")]
        args += [ROOT / "firmware/rcs_chassis.c", "-lm", "-o", exe]
        run(args)
        recovery.append(run([exe]))
    report = {"passed": True, "baseline_revision": BASELINE, "editor_sha256": digest(editor),
              "cases": counts, "template_contract": contract, "reset_regression": recovery,
              "allowed_change": "terrain nav explicit reset clears active/fault; all other generated bytes equal",
              "area": str(area), "hardware_test": False}
    (ROOT / "build/refactor_validation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(contract)
    print("PASS", len(counts), "actual export comparisons; flat byte-identical; terrain reset-only change")
    print("\n".join(recovery))


if __name__ == "__main__":
    main()
