"""Exercise actual export/Keil/version helper code independently of the ramp generator.

The harness uses synthetic SEALED generated modules, never user projects or hardware.
The release's separate ramp tests compile and execute the actual generated C.
"""
from pathlib import Path
import hashlib,json,os,subprocess,sys,uuid
R=Path(__file__).resolve().parents[1]
HARNESS=r'''
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
namespace RcsMap {
 public sealed class MapDocument {public bool Terrain;public static MapDocument Parse(string text){return new MapDocument{Terrain=text.Trim()=="ramp"};}}
 static class TemplateGenerator {
  public static string RouteRevision(MapDocument d){return d.Terrain?"ramp":"flat";}
  public static Dictionary<string,string> Generate(MapDocument d){
   var files=new Dictionary<string,string>();
   foreach(var name in ProjectBinding.RouteNames(d.Terrain)){
    string body="/* owned "+name+" */\n";
    if(name=="rcs_nav.h"&&d.Terrain)body+="#include \"rcs_terrain.h\"\n";
    if(name=="rcs_actions.c")body+=UserRegions.Block("WP_KEEP","/* keep action */");
    if(name=="rcs_terrain.c")body+=UserRegions.Block("TERRAIN_KEEP","/* default terrain hook */");
    files[name]=UserRegions.Seal(body);
   }return files;
  }
 }
 static class Editor {public static void AtomicSave(string path,string text){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,text,new UTF8Encoding(false));}}
}
static class TerrainProjectHarness {
 static int checks;static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
 static void Throws(Action action,string text){bool caught=false;try{action();}catch{caught=true;}Check(caught,text);}
 static string Signature(string dir){return String.Join("\n",Directory.GetFiles(dir).OrderBy(p=>p).Select(p=>Path.GetFileName(p)+":"+Convert.ToBase64String(File.ReadAllBytes(p))).ToArray());}
 static XDocument Project(){return XDocument.Parse("<Project><Targets><Target><Groups><Group><GroupName>Chassis</GroupName><Files><File><FileName>rcs_chassis.c</FileName><FileType>1</FileType><FilePath>../Modules/rcs_chassis.c</FilePath></File></Files></Group><Group><GroupName>UserKeep</GroupName><Files/></Group></Groups><TargetOption><TargetArmAds><Cads><VariousControls><IncludePath>../User</IncludePath><Define>KEEP_USER</Define></VariousControls></Cads></TargetArmAds></TargetOption></Target></Targets></Project>");}
 static string[] Registered(XDocument doc){return doc.Descendants("File").Select(f=>(string)f.Element("FileName")).ToArray();}
 static void AddForeign(XDocument doc,string name){doc.Descendants("Files").First().Add(new XElement("File",new XElement("FileName",name),new XElement("FilePath","../User/"+name)));}
 static void Main(string[] args){string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"MDK-ARM"));string output=Path.Combine(root,"Application","foxmolikaRoute");string project=Path.Combine(root,"MDK-ARM","test.uvprojx");var binding=new ProjectBinding{Root=root,IocName="test.ioc",ProjectName="test"};var flat=new RcsMap.MapDocument();var ramp=new RcsMap.MapDocument{Terrain=true};
  RcsMap.Exporter.Export(flat,output);Check(Directory.GetFiles(output).Length==6,"flat6");Check(ProjectBinding.RequiredRouteFiles(output,false).Length==6,"inferflat");var xml=Project();binding.UpdateRoute(xml,project,false);Check(ProjectBinding.RouteFiles.All(n=>Registered(xml).Count(x=>x==n)==1),"registerflat6");
  string action=Path.Combine(output,"rcs_actions.c");File.WriteAllText(action,File.ReadAllText(action).Replace("/* keep action */","/* edited action */"),new UTF8Encoding(false));
  RcsMap.Exporter.Export(ramp,output);Check(Directory.GetFiles(output).Length==8,"upgrade8");Check(File.ReadAllText(action).Contains("edited action"),"upgrade preserves action");Check(ProjectBinding.RequiredRouteFiles(output,false).Length==8,"inferramp");binding.UpdateRoute(xml,project,false);Check(ProjectBinding.RouteNames(true).All(n=>Registered(xml).Count(x=>x==n)==1),"register8");
  foreach(var file in xml.Descendants("File").Where(f=>ProjectBinding.TerrainFiles.Contains((string)f.Element("FileName"))))Check((string)file.Element("FileType")==(((string)file.Element("FileName")).EndsWith(".c")?"1":"5"),"terrain C/header type");
  string before=xml.ToString();Check(binding.UpdateRoute(xml,project,false)==0&&xml.ToString()==before,"project idempotent");string same=Signature(output);RcsMap.Exporter.Export(ramp,output);Check(Signature(output)==same,"export idempotent");
  Directory.CreateDirectory(Path.Combine(root,"Modules"));Directory.CreateDirectory(Path.Combine(root,"User"));foreach(var name in new[]{"rcs_chassis.h","rcs_control.h","rcs_hal.h"})File.WriteAllText(Path.Combine(root,"Modules",name),"/* fixture */");File.WriteAllText(Path.Combine(root,"User","rcs_config.h"),"/* fixture */");File.WriteAllBytes(binding.Marker,binding.Bytes());Project().Save(project);KeilHeaders.Fix(project,message=>{});var disk=ProjectBinding.ReadXml(project);Check(ProjectBinding.RouteNames(true).All(n=>Registered(disk).Count(x=>x==n)==1),"actual Keil writer registers8");byte[] projectBytes=File.ReadAllBytes(project);KeilHeaders.Fix(project,message=>{});Check(File.ReadAllBytes(project).SequenceEqual(projectBytes),"actual Keil writer idempotent");
  var receipt=new XElement("Receipt");ProjectBinding.RecordGeneratedFiles(receipt,output);Check(receipt.Descendants("File").Count()==8,"manifest8");ProjectBinding.CheckGeneratedFiles(receipt,output);Check(true,"manifest passes");
  File.WriteAllText(action,File.ReadAllText(action).Replace("edited action","second user edit"),new UTF8Encoding(false));ProjectBinding.CheckGeneratedFiles(receipt,output);Check(true,"manifest permits USER CODE edits");
  string nav=Path.Combine(output,"rcs_nav.c"),saved=File.ReadAllText(nav);File.WriteAllText(nav,saved+"/* outside */\n");Throws(()=>ProjectBinding.CheckGeneratedFiles(receipt,output),"manifest rejects outer mutation");File.WriteAllText(nav,saved);
  string terrainC=Path.Combine(output,"rcs_terrain.c"),terrainH=Path.Combine(output,"rcs_terrain.h");byte[] tc=File.ReadAllBytes(terrainC),th=File.ReadAllBytes(terrainH);File.Delete(terrainH);before=xml.ToString();Throws(()=>binding.UpdateRoute(xml,project,false),"missing header fails before project");Check(xml.ToString()==before,"missing header preserves XML");projectBytes=File.ReadAllBytes(project);Throws(()=>KeilHeaders.Fix(project,message=>{}),"actual helper rejects missing optional");Check(File.ReadAllBytes(project).SequenceEqual(projectBytes),"actual helper leaves project bytes on failure");File.Delete(terrainC);Throws(()=>ProjectBinding.RequiredRouteFiles(output,false),"bothmissing still require8");File.WriteAllBytes(terrainC,tc);File.WriteAllBytes(terrainH,th);
  File.WriteAllText(terrainC,File.ReadAllText(terrainC).Replace("default terrain hook","manual terrain hook"),new UTF8Encoding(false));same=Signature(output);Throws(()=>RcsMap.Exporter.Export(flat,output),"refuse removing edited terrain");Check(Signature(output)==same,"edited downgrade preserves all");File.WriteAllBytes(terrainC,tc);
  RcsMap.Exporter.Export(flat,output);Check(Directory.GetFiles(output).Length==6,"downgrade6");Check(File.ReadAllText(action).Contains("second user edit"),"downgrade preserves shared action");binding.UpdateRoute(xml,project,false);Check(!Registered(xml).Any(n=>ProjectBinding.TerrainFiles.Contains(n)),"remove obsolete terrain refs");KeilHeaders.Fix(project,message=>{});Check(!Registered(ProjectBinding.ReadXml(project)).Any(n=>ProjectBinding.TerrainFiles.Contains(n)),"actual helper removes owned stale optional refs");Check(xml.Descendants("GroupName").Any(e=>e.Value=="UserKeep"),"keep unrelated group");Check(xml.Descendants("Define").Single().Value=="KEEP_USER","keep defines");
  File.WriteAllBytes(terrainC,tc);Throws(()=>ProjectBinding.RequiredRouteFiles(output,false),"stray terrain fails");File.Delete(terrainC);
  var foreign=Project();AddForeign(foreign,"rcs_terrain.c");before=foreign.ToString();Throws(()=>binding.UpdateRoute(foreign,project,true),"foreign optional rejected even previewflat");Check(foreign.ToString()==before,"foreign preview untouched");
  File.WriteAllText(Path.Combine(output,"user.c"),"keep");same=Signature(output);Throws(()=>RcsMap.Exporter.Export(ramp,output),"unknown extra refuses upgrade");Check(Signature(output)==same,"unknown extra preserved");File.Delete(Path.Combine(output,"user.c"));
  var sixReceipt=new XElement("Receipt");ProjectBinding.RecordGeneratedFiles(sixReceipt,output);RcsMap.Exporter.Export(ramp,output);Throws(()=>ProjectBinding.CheckGeneratedFiles(sixReceipt,output),"old6manifest cannot validate8");
  File.WriteAllText(terrainC,File.ReadAllText(terrainC)+"/* changed */\n");same=Signature(output);Throws(()=>RcsMap.Exporter.Export(flat,output),"outer terrain edit refuses downgrade");Check(Signature(output)==same,"outer terrain edit retained");
  string savedMap=Path.Combine(root,"saved-map.json"),freshOutput=Path.Combine(root,"canonical-route");File.WriteAllText(savedMap,"flat");RcsMap.RevisionGuard.SourceMapFile=savedMap;RcsMap.Exporter.Export(flat,freshOutput);RcsMap.RevisionGuard.Check(savedMap,freshOutput);Check(true,"matching saved map records revision");string oldReceipt=File.ReadAllText(RcsMap.RevisionGuard.Receipt(freshOutput));same=Signature(freshOutput);File.WriteAllText(savedMap,"ramp");Throws(()=>RcsMap.Exporter.Export(flat,freshOutput),"changed source cannot be certified as current export");Check(Signature(freshOutput)==same&&File.ReadAllText(RcsMap.RevisionGuard.Receipt(freshOutput))==oldReceipt,"canonical mismatch preserves source and receipt");RcsMap.RevisionGuard.SourceMapFile=null;
  Console.WriteLine("PASS "+checks+" optional-terrain export/project/manifest checks");
 }
}
'''
def main():
 sys.stdout.reconfigure(encoding='utf-8');area=R/'build'/('terrain_project_'+uuid.uuid4().hex[:8]);area.mkdir();source=area/'Harness.cs';source.write_text(HARNESS,encoding='utf-8');exe=area/'terrain-project-tests.exe'
 csc=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 args=[csc,'/nologo','/target:exe','/define:RCS_MAP_EDITOR','/reference:System.Core.dll','/reference:System.Xml.Linq.dll','/reference:System.Xml.dll','/out:'+str(exe),source,R/'keil_headers/ProjectBinding.cs',R/'keil_headers/KeilHeaders.cs',R/'map_editor/src/UserRegions.cs',R/'map_editor/src/Exporter.cs',R/'map_editor/src/RevisionGuard.cs']
 p=subprocess.run(list(map(str,args)),capture_output=True);assert p.returncode==0,p.stdout.decode(errors='replace')+p.stderr.decode(errors='replace')
 p=subprocess.run([str(exe),str(area/'owned fixture')],capture_output=True,timeout=35);assert p.returncode==0,p.stdout.decode(errors='replace')+p.stderr.decode(errors='replace');text=p.stdout.decode('utf-8',errors='replace').strip();assert 'PASS ' in text
 report={'passed':True,'report':text,'area':str(area),'source_sha256':{str(f.relative_to(R)):hashlib.sha256(f.read_bytes()).hexdigest() for f in [R/'keil_headers/ProjectBinding.cs',R/'keil_headers/KeilHeaders.cs',R/'map_editor/src/UserRegions.cs',R/'map_editor/src/Exporter.cs',R/'map_editor/src/RevisionGuard.cs']},'synthetic_sealed_files':True,'hardware_test':False}
 (R/'build/terrain_project_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(text)
if __name__=='__main__':main()
