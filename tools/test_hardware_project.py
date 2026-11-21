"""真实硬件交付/Keil/恢复代码的确定性主机回归；仅地图/驱动生成使用明确夹具。"""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import sys
import uuid
from xml.etree import ElementTree as E
from build_support import ROOT, compile_csharp, XML_REFERENCES
from version import TOOL_DIRECTORY

HARNESS = r'''
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
namespace RcsMap {
 public sealed class MapDocument {
  public string Mode="on";
  public bool Changed;
  public List<DeviceInstance> Devices=new List<DeviceInstance>();
  public static MapDocument Parse(string s){return new MapDocument{Mode=s.Trim()};}
 }
 public sealed class DeviceParameter {public string Key,Type;}
 public sealed class DeviceHooks {public string OnUartRx="";}
 public sealed class DeviceTemplate {public int MinHeapBytes;public string DriverKind="custom";public List<DeviceParameter> Parameters=new List<DeviceParameter>();public DeviceHooks Hooks=new DeviceHooks();}
 public sealed class DeviceInstance {public bool Enabled=true;public string Id=new String('a',32),Name="fixture",TemplateId="fixture",TemplateVersion="1.0.0",TemplateSha256=new String('b',64);public DeviceTemplate Template=new DeviceTemplate();public Dictionary<string,string> Values=new Dictionary<string,string>();public string Get(string key){string v;return Values.TryGetValue(key,out v)?v:"";}}
 static class HardwareRules {internal static bool HasDevices(MapDocument d){return d.Mode!="off";}internal static IEnumerable<DeviceInstance> Devices(MapDocument d){return d.Devices;}}
 static class TemplateGenerator {
  internal static string RouteRevision(MapDocument d){return HardwareBinding.Hash(Encoding.UTF8.GetBytes(d.Mode+(d.Changed?"new":"")));}
  internal static Dictionary<string,string> Generate(MapDocument d){
   var r=new Dictionary<string,string>();foreach(string n in ProjectBinding.RouteNames(false))r[n]=UserRegions.Seal("/* "+n+" "+d.Mode+(d.Changed?"new":"")+" */\n");return r;
  }
 }
 static class HardwareGenerator {
  internal static Dictionary<string,string> Generate(MapDocument d){
   var r=new Dictionary<string,string>();if(d.Mode=="off")return r;
   r["rcs_hw.cpp"]="#include \"rcs_hw.h\"\nint fixture_hw(void){return "+(d.Changed?"2":"1")+";}\n";
   r["rcs_hw.h"]="#pragma once\nint fixture_hw(void);\n";
   r["rcs_hw_config.h"]="#pragma once\n#define FIXTURE_CONFIG 1\n";
   r["team/team.c"]="#include \"team.h\"\nint team(void){return 1;}\n";
   r["team/team.h"]="#pragma once\nint team(void);\n";return r;
  }
 }
 static class Editor {public static void AtomicSave(string p,string s){ProjectBinding.AtomicWrite(p,Encoding.UTF8.GetBytes(s));}}
}
static class HardwareProjectHarness {
 static int checks;
 static string last="startup";
 static void Check(bool b,string name){last=name;if(!b)throw new Exception(name);checks++;}
 static string Error(Action a){try{a();}catch(Exception e){checks++;return e.Message;}throw new Exception("expected failure");}
 static Dictionary<string,string> Snapshot(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories)
  .Where(p=>!p.Contains(".foxmolika-hardware-txn-")&&!p.EndsWith(HardwareBinding.PendingName))
  .ToDictionary(p=>ProjectBinding.Relative(root,p),p=>HardwareBinding.Hash(File.ReadAllBytes(p)));}
 static bool Same(Dictionary<string,string>a,Dictionary<string,string>b){return a.Count==b.Count&&a.All(e=>b.ContainsKey(e.Key)&&b[e.Key]==e.Value);}
 static XDocument Project(){return XDocument.Parse("<Project><Targets><Target><TargetName>HW</TargetName><uAC6>1</uAC6><TargetOption><TargetCommonOption><BeforeMake/></TargetCommonOption><TargetArmAds><Cads><VariousControls><IncludePath>../Modules</IncludePath><Define>USER_KEEP</Define><MiscControls>-DUSER_TARGET=1</MiscControls></VariousControls></Cads></TargetArmAds></TargetOption><Groups><Group><GroupName>Chassis</GroupName><Files><File><FileName>rcs_chassis.c</FileName><FileType>1</FileType><FilePath>../Modules/rcs_chassis.c</FilePath><FileOption><FileArmAds><Cads><VariousControls><IncludePath/></VariousControls></Cads></FileArmAds></FileOption></File></Files></Group></Groups></Target></Targets></Project>");}
 static int Main(string[] args){Console.OutputEncoding=new UTF8Encoding(false);try{Run(args);return 0;}catch(Exception e){Console.WriteLine("FAIL after "+last+": "+e.GetType().Name+" "+e.Message);if(e.InnerException!=null)Console.WriteLine(e.InnerException.GetType().Name+" "+e.InnerException.Message);return 2;}}
 static void Run(string[] args){
  string root=Path.Combine(args[0],"owned project");Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"MDK-ARM"));Directory.CreateDirectory(Path.Combine(root,"Modules"));
  foreach(string n in new[]{"rcs_chassis.c","rcs_chassis.h","rcs_control.h","rcs_hal.h","rcs_config.h"})File.WriteAllText(Path.Combine(root,"Modules",n),"/* fixture */\n");
  string ioc=Path.Combine(root,"test.ioc");File.WriteAllText(ioc,"ProjectManager.ProjectName=test\n");
  string uv=Path.Combine(root,"MDK-ARM/test.uvprojx");Project().Save(uv);
  var binding=ProjectBinding.Plan(ioc,File.ReadAllText(ioc));File.WriteAllBytes(binding.Marker,binding.Bytes());
  string route=binding.RouteDirectory,hw=Path.Combine(root,HardwareBinding.Relative);
  var on=new RcsMap.MapDocument();RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations());
  Check(Directory.GetFiles(route).Length==19,"route19");Check(File.Exists(Path.Combine(hw,"hardware.xml")),"manifest");
  var parsed=ProjectBinding.ReadXml(uv);Check(parsed.Descendants("FileName").Count(e=>e.Value=="rcs_hw.cpp")==1,"cpp registered once");
  Check(File.ReadAllText(uv).StartsWith("<?xml"),"project XML declaration retained for UV4");
  Check(parsed.Descendants("File").Single(e=>(string)e.Element("FileName")=="rcs_hw.cpp").Element("FileType").Value=="8","cpp type8");
  var cppEntry=parsed.Descendants("File").Single(e=>(string)e.Element("FileName")=="rcs_hw.cpp");
  Check(cppEntry.Descendants("MiscControls").Single().Value=="-fexceptions","generated cpp exceptions enabled");
  Check(ProjectBinding.TargetCompilerOptions(parsed.Descendants("Target").First()).Element("MiscControls").Value=="-DUSER_TARGET=1","target misc retained");
  Check(!parsed.Descendants("File").Single(e=>(string)e.Element("FileName")=="team.c").Descendants("MiscControls").Any(),"C source does not inherit hardware exception flag");
  Check(parsed.Descendants("File").Single(e=>(string)e.Element("FileName")=="rcs_hw.h").Element("FileType").Value=="5","header type5");
  Check(parsed.Descendants("IncludePath").First().Value.Contains("foxmolikaHardware/team"),"nested include");
  var before=Snapshot(root);RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations());Check(Same(before,Snapshot(root)),"repeat export byte stable");
  parsed=ProjectBinding.ReadXml(uv);cppEntry=parsed.Descendants("File").Single(e=>(string)e.Element("FileName")=="rcs_hw.cpp");
  cppEntry.Descendants("MiscControls").Single().Value="-DUSER_FILE=7 -fno-exceptions";parsed.Save(uv);
  RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations());
  parsed=ProjectBinding.ReadXml(uv);cppEntry=parsed.Descendants("File").Single(e=>(string)e.Element("FileName")=="rcs_hw.cpp");
  Check(cppEntry.Descendants("MiscControls").Single().Value=="-DUSER_FILE=7 -fno-exceptions -fexceptions","user file flags retained while enabling required exceptions");
  cppEntry.Descendants("MiscControls").Single().Remove();parsed.Save(uv);KeilHeaders.Fix(uv,s=>{});
  parsed=ProjectBinding.ReadXml(uv);Check(parsed.Descendants("File").Single(e=>(string)e.Element("FileName")=="rcs_hw.cpp").Descendants("MiscControls").Single().Value=="-fexceptions","post restores removed cpp option");
  byte[] selectedAC6=File.ReadAllBytes(uv);parsed.Descendants("Target").First().SetElementValue("uAC6","0");parsed.Save(uv);before=Snapshot(root);
  string arm5=Error(()=>RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations()));
  Check(arm5.Contains("6.22")&&Same(before,Snapshot(root)),"ARM5 cpp target rejected clearly before writes");File.WriteAllBytes(uv,selectedAC6);
  // 真实bound预检不能把GUI的Configured标志当成CubeMX外设已配置。
  var device=new RcsMap.DeviceInstance();device.Template.MinHeapBytes=8192;on.Devices.Add(device);before=Snapshot(root);
  string gate=Error(()=>RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations()));
  Check(gate.Contains("0x2000")&&Same(before,Snapshot(root)),"insufficient heap refuses before all writes");
  File.AppendAllText(ioc,"ProjectManager.HeapSize=0x2000\n");
  device.Template.Parameters.Add(new RcsMap.DeviceParameter{Key="Uart",Type="uart"});device.Values["Uart"]="huart3";device.Template.Hooks.OnUartRx="/* receive */";
  before=Snapshot(root);gate=Error(()=>RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations()));
  Check(gate.Contains("USART")||gate.Contains("UART"),"missing UART actionable error");Check(Same(before,Snapshot(root)),"missing peripheral preserves files");
  File.AppendAllText(ioc,"Mcu.IP0=USART3\n");gate=Error(()=>RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations()));Check(gate.Contains("全局中断"),"UART IRQ required");
  File.AppendAllText(ioc,"NVIC.USART3_IRQn=true\\:0\\:0\\:false\n");
  device.Template.Parameters.Add(new RcsMap.DeviceParameter{Key="Timer",Type="timer"});device.Values["Timer"]="htim4";
  gate=Error(()=>RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations()));Check(gate.Contains("TIM4"),"missing timer actionable error");
  File.AppendAllText(ioc,"Mcu.IP1=TIM4\n");RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations());
  Check(HardwareBinding.Load(root,false).Devices.Count==1,"manifest pins device source/version/hash");
  on.Devices.Clear();RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations());
  // 已有同名不同目录硬件模块，必须在暂存或项目改写之前拒绝。
  byte[] cleanProject=File.ReadAllBytes(uv);parsed=ProjectBinding.ReadXml(uv);
  parsed.Descendants("Groups").First().Add(new XElement("Group",new XElement("GroupName","UserKeep"),new XElement("Files",
   new XElement("File",new XElement("FileName","rcs_hw.cpp"),new XElement("FileType","8"),new XElement("FilePath","../User/rcs_hw.cpp")))));
  parsed.Save(uv);before=Snapshot(root);Error(()=>RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations()));
  Check(Same(before,Snapshot(root)),"foreign same-name cpp refused without writes");File.WriteAllBytes(uv,cleanProject);
  // 真实PostGenerate核心恢复被手删的分组，不重复添加任何头/源文件。
  parsed=ProjectBinding.ReadXml(uv);parsed.Descendants("Group").Single(g=>(string)g.Element("GroupName")==HardwareBinding.Relative).Remove();parsed.Save(uv);
  KeilHeaders.Fix(uv,s=>{});parsed=ProjectBinding.ReadXml(uv);Check(parsed.Descendants("FileName").Count(e=>e.Value=="rcs_hw.cpp")==1,"post restores cpp");
  byte[] project=File.ReadAllBytes(uv);KeilHeaders.Fix(uv,s=>{});Check(project.SequenceEqual(File.ReadAllBytes(uv)),"post idempotent");
  string cpp=Path.Combine(hw,"rcs_hw.cpp");byte[] original=File.ReadAllBytes(cpp);File.AppendAllText(cpp,"// USER CUSTOM\n");before=Snapshot(root);
  Error(()=>RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations()));Check(Same(before,Snapshot(root)),"active user edit preserved");
  var off=new RcsMap.MapDocument{Mode="off"};RcsMap.HardwareExport.Export(off,route,binding,new RcsMap.HardwareExport.Operations());
  Check(!ProjectBinding.ReadXml(uv).Descendants("GroupName").Any(e=>e.Value==HardwareBinding.Relative),"cancel removes group");
  string retired=Directory.GetDirectories(Path.GetDirectoryName(hw),"foxmolikaHardware.retired-*").Single();
  Check(File.ReadAllText(Path.Combine(retired,"rcs_hw.cpp")).Contains("USER CUSTOM")&&File.Exists(Path.Combine(retired,"rcs_hw_config.h")),"retired preserves edited driver and config");
  Check(HardwareBinding.Load(root,false).Files.Count==0,"cancel active manifest empty");
  RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations());Check(File.ReadAllBytes(cpp).SequenceEqual(original),"readd clean active retains retirement");
  // 任意提交步骤失败均按原始快照反向恢复。
  var changed=new RcsMap.MapDocument{Changed=true};before=Snapshot(root);
  var operations=new RcsMap.HardwareExport.Operations{BeforeCommit=(i,p)=>{if(i==3)throw new IOException("injected commit");}};
  Error(()=>RcsMap.HardwareExport.Export(changed,route,binding,operations));Check(Same(before,Snapshot(root)),"commit failure restores all source and project bytes");
  Check(!File.Exists(Path.Combine(Path.GetDirectoryName(route),HardwareBinding.PendingName)),"successful rollback removes journal");
  // 回滚失败保留可恢复日志，portable登记遇到pending时失败且不写工程。
  operations=new RcsMap.HardwareExport.Operations{BeforeCommit=(i,p)=>{if(i==3)throw new IOException("commit");},BeforeRollback=(i,p)=>{throw new IOException("rollback");}};
  string message=Error(()=>RcsMap.HardwareExport.Export(changed,route,binding,operations));Check(message.Contains("备份")&&message.Contains("恢复日志"),"rollback diagnostic points to retained backups");
  project=File.ReadAllBytes(uv);Error(()=>KeilHeaders.Fix(uv,s=>{}));Check(project.SequenceEqual(File.ReadAllBytes(uv)),"pending blocks post writes");
  RcsMap.HardwareExport.Recover(on,route,binding);Check(Same(before,Snapshot(root)),"next export recovery restores exact bytes");
  // 源地图改动不属于事务写集合，失败恢复绝不能抹掉用户新的地图内容。
  string maps=Path.Combine(root,"Maps");Directory.CreateDirectory(maps);string map=Path.Combine(maps,"map.json");File.WriteAllText(map,"on");RcsMap.RevisionGuard.SourceMapFile=map;
  RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations());HardwareBinding.CheckVersion(root);Check(true,"bound revision checks hardware");
  before=Snapshot(root);operations=new RcsMap.HardwareExport.Operations{BeforeCommit=(i,p)=>{if(i==2)File.WriteAllText(map,"off");}};
  var next=new RcsMap.MapDocument{Mode="on",Changed=false};
  // 令配置变化但保持源地图可解析的fixture语义。
  File.WriteAllText(map,"new");next.Mode="new";before=Snapshot(root);
  Error(()=>RcsMap.HardwareExport.Export(next,route,binding,operations));Check(File.ReadAllText(map)=="off","external map edit retained");
  File.WriteAllText(map,"new");Check(Same(before,Snapshot(root)),"transaction files restored independently of map edit");
  RcsMap.RevisionGuard.SourceMapFile=null;
  // 恶意交付清单不允许越界、DTD、未知字段或重复路径。
  string manifest=Path.Combine(hw,HardwareBinding.ManifestName);byte[] saved=File.ReadAllBytes(manifest);
  foreach(string invalid in new[]{"<FoxmolikaHardware Schema='1' Revision='"+new string('a',64)+"'><File Path='../escape.c' Sha256='"+new string('a',64)+"'/></FoxmolikaHardware>","<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///C:/secret'>]><FoxmolikaHardware Schema='1' Revision='"+new string('a',64)+"'/>","<FoxmolikaHardware Schema='1' Revision='"+new string('a',64)+"' Surprise='1'/>"}) {
   File.WriteAllText(manifest,invalid);project=File.ReadAllBytes(uv);Error(()=>KeilHeaders.Fix(uv,s=>{}));Check(project.SequenceEqual(File.ReadAllBytes(uv)),"invalid manifest preserves Keil");
  }
  File.WriteAllBytes(manifest,saved);
  File.WriteAllText(Path.Combine(hw,"foreign.c"),"user");before=Snapshot(root);Error(()=>RcsMap.HardwareExport.Export(on,route,binding,new RcsMap.HardwareExport.Operations()));Check(Same(before,Snapshot(root)),"unknown file preserved");File.Delete(Path.Combine(hw,"foreign.c"));
  // 第一次导出尚未来得及写hardware.xml即中断，取消所有设备后重导出仍应恢复pending。
  string first=Path.Combine(args[0],"first_run/route");
  operations=new RcsMap.HardwareExport.Operations{BeforeCommit=(i,p)=>{if(i==3)throw new IOException("first commit");},BeforeRollback=(i,p)=>{throw new IOException("first rollback");}};
  Error(()=>RcsMap.HardwareExport.Export(new RcsMap.MapDocument(),first,null,operations));
  Check(!File.Exists(Path.Combine(Path.GetDirectoryName(first),"foxmolikaHardware/hardware.xml")),"first failed export has no manifest yet");
  RcsMap.Exporter.Export(off,first);
  Check(!File.Exists(Path.Combine(Path.GetDirectoryName(first),HardwareBinding.PendingName))&&Directory.GetFiles(first).Length==19,"cancel then export recovers orphan pending");
  Check(!Directory.Exists(Path.Combine(Path.GetDirectoryName(first),"foxmolikaHardware")),"zero hardware leaves no first-run hardware directory");
  Console.WriteLine("PASS "+checks+" hardware file/project/recovery assertions");
 }
}
'''

def actual_editor(area):
    editor = ROOT / "map_editor/dist/foxmolika_MapEditor.exe"
    if not editor.exists():
        return None
    def run(*args, expected=0):
        result = subprocess.run([str(editor), *map(str,args)], capture_output=True, timeout=60)
        assert result.returncode == expected, (args, result.returncode, (result.stdout+result.stderr).decode("utf-8", "replace"))
        return result
    root = area / "actual_exe"
    (root / "MDK-ARM").mkdir(parents=True)
    (root / "Modules").mkdir()
    (root / "Maps").mkdir()
    fixtures = root / "fixtures"
    run("--hardware-fixture", fixtures)
    ioc = root / "tester123.ioc"
    shutil.copyfile(ROOT / "tests/fixtures/tester123_no_can.ioc", ioc)
    for source in (ROOT / "firmware").glob("*"):
        if source.suffix in (".c", ".h"):
            shutil.copyfile(source, root / "Modules" / source.name)
    shutil.copyfile(ROOT / "examples/rcs_config.h", root / "Modules/rcs_config.h")
    # .NET harness也包含文件级Cads；实际EXE用同一最小合法MDK结构，原生生成另有独立验收。
    xml = E.fromstring("<Project><Targets><Target><TargetName>tester123</TargetName><uAC6>1</uAC6><TargetOption><TargetCommonOption><BeforeMake/></TargetCommonOption><TargetArmAds><Cads><VariousControls><IncludePath>../Modules</IncludePath><Define>USER_KEEP</Define></VariousControls></Cads></TargetArmAds></TargetOption><Groups><Group><GroupName>Chassis</GroupName><Files><File><FileName>rcs_chassis.c</FileName><FileType>1</FileType><FilePath>../Modules/rcs_chassis.c</FilePath><FileOption><FileArmAds><Cads><VariousControls><IncludePath/></VariousControls></Cads></FileArmAds></FileOption></File></Files></Group></Groups></Target></Targets></Project>")
    project = root / "MDK-ARM/tester123.uvprojx"
    E.ElementTree(xml).write(project, encoding="utf-8", xml_declaration=True)
    mapfile = root / "Maps/map.rcsmap.json"
    shutil.copyfile(fixtures / "gpio_output.rcsmap.json", mapfile)
    run("--bind-project", mapfile, ioc)
    run("--export-linked", mapfile)
    hardware = root / "Application/foxmolikaHardware"
    post = root / TOOL_DIRECTORY / "foxmolika_PostGenerate.exe"
    def invoke_post(*args, expected=0):
        p = subprocess.run([str(post), *map(str,args)], capture_output=True, timeout=60)
        assert p.returncode == expected, (p.returncode, (p.stdout+p.stderr).decode("utf-8", "replace"))
    invoke_post("--check-route", root)
    def names():
        files = E.parse(project).findall(".//File")
        for name, kind in [("rcs_hw.cpp", "8"), ("rcs_hw.h", "5"), ("rcs_hw_config.h", "5")]:
            matches = [f for f in files if f.findtext("FileName") == name]
            assert len(matches) == 1 and matches[0].findtext("FileType") == kind
    names()
    xml = E.parse(project)
    for groups in xml.findall(".//Groups"):
        for group in list(groups):
            if group.findtext("GroupName") == "Application/foxmolikaHardware":
                groups.remove(group)
    xml.write(project, encoding="utf-8", xml_declaration=True)
    invoke_post()
    names()
    def snapshot():
        return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob("*") if p.is_file()}
    run("--export-linked", mapfile)
    before = snapshot()
    run("--export-linked", mapfile)
    assert snapshot() == before
    driver = hardware / "rcs_hw.cpp"
    driver.write_text(driver.read_text(encoding="utf-8") + "\n// USER_DRIVER_KEEP\n", encoding="utf-8")
    before = snapshot()
    run("--export-linked", mapfile, expected=2)
    assert snapshot() == before
    document = json.loads(mapfile.read_text(encoding="utf-8"))
    document["Devices"] = []
    mapfile.write_text(json.dumps(document, ensure_ascii=False), encoding="utf-8")
    run("--export-linked", mapfile)
    assert not any(g.findtext("GroupName") == "Application/foxmolikaHardware" for g in E.parse(project).findall(".//Group"))
    retired = list((root / "Application").glob("foxmolikaHardware.retired-*"))
    assert retired and "USER_DRIVER_KEEP" in (retired[-1] / "rcs_hw.cpp").read_text(encoding="utf-8")
    invoke_post("--check-route", root)
    return {"editor_sha256": hashlib.sha256(editor.read_bytes()).hexdigest(), "post_sha256": hashlib.sha256(post.read_bytes()).hexdigest(),
            "cases": ["actual GUI-generated fixture exports hardware", "portable prebuild checks both revisions", "post restores cpp and headers", "repeat export byte stable", "active user driver edit preserved", "cancel retains edited source/config in retired directory"], "directory": str(root)}

def main():
    sys.stdout.reconfigure(encoding="utf-8")
    area = ROOT / "build" / ("hardware_project_" + uuid.uuid4().hex[:8])
    area.mkdir(parents=True)
    harness = area / "Harness.cs"
    harness.write_text(HARNESS, encoding="utf-8")
    sources = [ROOT / "map_editor/src" / n for n in ["HardwareExport.cs", "Exporter.cs", "UserRegions.cs", "RevisionGuard.cs"]]
    sources += [ROOT / "keil_headers" / n for n in ["ProjectBinding.cs", "KeilHeaders.cs", "HardwareBinding.cs"]]
    exe = area / "hardware-project-tests.exe"
    output = compile_csharp(exe, [harness, *sources], references=XML_REFERENCES, defines=["RCS_MAP_EDITOR"])
    assert not output.strip(), output
    p = subprocess.run([str(exe), str(area)], capture_output=True, timeout=60)
    text = (p.stdout + p.stderr).decode("utf-8", errors="replace")
    assert p.returncode == 0, text
    hardware = area / "owned project/Application/foxmolikaHardware"
    for compiler, source, include, target in [
        ("g++", hardware / "rcs_hw.cpp", hardware, area / "hw.o"),
        ("gcc", hardware / "team/team.c", hardware / "team", area / "team.o"),
    ]:
        result = subprocess.run([compiler, "-Wall", "-Wextra", "-Werror", "-pedantic", "-I", str(include), "-c", str(source), "-o", str(target)], capture_output=True)
        assert result.returncode == 0, result.stderr.decode("utf-8", "replace")
    actual = actual_editor(area)
    report = {"passed": True, "scope": "actual hardware transaction, ownership, Keil registration and recovery code; deterministic map/driver fixture plus real editor when built", "hardware": False,
              "actual_executable": actual,
              "report": text.strip(), "area": str(area), "source_sha256": {str(f.relative_to(ROOT)): hashlib.sha256(f.read_bytes()).hexdigest() for f in sources}}
    (ROOT / "build/hardware_project_validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(text.strip())

if __name__ == "__main__":
    main()
