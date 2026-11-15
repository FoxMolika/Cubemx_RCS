"""Repeatable 0.4 phase validation. Builds only new workspace; no hardware."""
from pathlib import Path
import copy,json,subprocess,sys,uuid
ROOT=Path(__file__).resolve().parents[1]
P=ROOT.parent

def run(args):
 p=subprocess.run([str(x) for x in args],capture_output=True,text=True,encoding="utf-8",errors="replace")
 if p.returncode:raise RuntimeError(p.stdout+p.stderr)
 return p.stdout

def main():
 sys.stdout.reconfigure(encoding="utf-8")
 if "--no-build" not in sys.argv:print(run([sys.executable,ROOT/"tools/build.py"]))
 area=ROOT/"build"/("phased_"+uuid.uuid4().hex[:8]);area.mkdir(parents=True)
 exe=ROOT/"dist/foxmolika_MapEditor.exe";demo=area/"demo.json"
 run([exe,"--demo",demo]);doc=json.loads(demo.read_text(encoding="utf-8-sig"));doc["Obstacles"]=[]
 doc["ActionIncludes"]="extern unsigned audit_enter[3], audit_done[3], audit_cancel[3];"
 def action(kind,value=0,index=None,direction=0):
  x=copy.deepcopy(doc["Points"][0]["DepartureActions"][0]);x.update(Id=uuid.uuid4().hex,Kind=kind,Value=value,Direction=direction,Name="phase test",Timeout=30)
  if index is not None:x.update(Start="++audit_enter[%d];"%index,Condition="elapsed_s >= 0.03f",Done="++audit_done[%d];"%index,Cancel="++audit_cancel[%d];"%index)
  return x
 points=[]
 for i,(x,y) in enumerate([(600,600),(1000,600),(1000,1000)]):
  p=copy.deepcopy(doc["Points"][0]);p.update(Id=uuid.uuid4().hex,X=x,Y=y,Yaw=0,ArrivalActions=[],DepartureActions=[],CorrectionMode=2,RunFinalDeparture=True,Dwell=0)
  points.append(p)
 points[0]["ArrivalActions"]=[action(1,90),action(0,index=0)]
 points[0]["DepartureActions"]=[action(0,index=1)]
 points[1]["ArrivalActions"]=[action(2,360,direction=-1),action(3,.05)]
 points[2]["DepartureActions"]=[action(0,index=2)]
 doc["Points"]=points;results=[]
 for layout in range(3):
  doc["Settings"]["Layout"]=layout;mapfile=area/("layout%d.json"%layout);folder=area/("layout%d"%layout)
  mapfile.write_text(json.dumps(doc,ensure_ascii=False),encoding="utf-8")
  run([exe,"--export",mapfile,folder]);run([sys.executable,P/"tools/style_check.py",folder])
  output=folder/"test_generated_phases.exe"
  args=["gcc","-std=c99","-O2","-Wall","-Wextra","-Werror","-pedantic","-I",folder,"-I",P/"firmware",ROOT/"tests/test_generated_phases.c",folder/"rcs_nav.c",folder/"rcs_route.c",folder/"rcs_actions.c",P/"firmware/rcs_chassis.c","-lm","-o",output]
  run(args);result=run([output]);print(result,end="");results.append(result.strip())
 unit=area/"test_phases.exe"
 run(["gcc","-std=c99","-O2","-Wall","-Wextra","-Werror","-pedantic","-I",ROOT/"firmware","-I",P/"firmware",ROOT/"tests/test_phases.c",ROOT/"firmware/rcs_nav.c",P/"firmware/rcs_chassis.c","-lm","-o",unit])
 unitlog=run([unit]);print(unitlog,end="")
 result={"actual_export_simulations":results,"unit_fault_phase_checks":unitlog,"area":str(area),"hardware_test":False}
 (ROOT/"build/phased_validation.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
 print("PASS evidence:",ROOT/"build/phased_validation.json")
if __name__=="__main__":main()
