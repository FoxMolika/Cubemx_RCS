"""Actual portable EXE/map/prebuild checks in fresh owned project fixtures."""
from pathlib import Path
from xml.etree import ElementTree as E
import hashlib,json,shutil,subprocess,sys,uuid,re,base64
R=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 sys.stdout.reconfigure(encoding='utf-8');area=R/'build'/('revision_'+uuid.uuid4().hex[:8]);area.mkdir()
 exe=R/'map_editor/dist/foxmolika_MapEditor.exe';checks=[]
 def run(args,expected=0):
  p=subprocess.run([str(x) for x in args],capture_output=True,timeout=35)
  assert p.returncode==expected,(args,p.returncode,p.stderr)
 def fixture(name):
  root=area/name;(root/'MDK-ARM').mkdir(parents=True);(root/'Maps').mkdir();(root/'Modules').mkdir()
  ioc=root/'tester123.ioc';ioc.write_bytes((R/'tests/fixtures/tester123_no_can.ioc').read_bytes())
  for f in (R/'firmware').glob('*'):shutil.copyfile(f,root/'Modules'/f.name)
  xml=E.Element('Project');target=E.SubElement(E.SubElement(xml,'Targets'),'Target');E.SubElement(target,'TargetName').text='Test'
  options=E.SubElement(target,'TargetOption');common=E.SubElement(options,'TargetCommonOption');before=E.SubElement(common,'BeforeMake')
  for n,v in [('RunUserProg1','0'),('UserProg1Name',''),('nStopB1X','0'),('RunUserProg2','1'),('UserProg2Name','user-owned-tool.exe'),('nStopB2X','3')]:E.SubElement(before,n).text=v
  c=E.SubElement(E.SubElement(E.SubElement(options,'TargetArmAds'),'Cads'),'VariousControls');E.SubElement(c,'IncludePath').text='../Modules';E.SubElement(c,'Define').text='KEEP_DEFINE'
  group=E.SubElement(E.SubElement(target,'Groups'),'Group');E.SubElement(group,'GroupName').text='Chassis';files=E.SubElement(group,'Files')
  for n in ['rcs_chassis.c','rcs_control.c','rcs_hal.c']:
   f=E.SubElement(files,'File');E.SubElement(f,'FileName').text=n;E.SubElement(f,'FileType').text='1';E.SubElement(f,'FilePath').text='../Modules/'+n
  uv=root/'MDK-ARM/tester123.uvprojx';E.ElementTree(xml).write(uv,encoding='utf-8',xml_declaration=True)
  m=root/'Maps/map.rcsmap.json';run([exe,'--demo',m]);return root,ioc,uv,m
 root,ioc,uv,m=fixture('space 中文 project');run([exe,'--bind-project',m,ioc]);run([exe,'--export-linked',m])
 tool=root/'foxmolika_Tools_051/foxmolika_PostGenerate.exe';route=root/'Application/foxmolikaRoute'
 run([tool,'--check-route',root]);run([exe,'--check-map',m,route]);checks.append('same saved map/export passes both standalone and portable prebuild checks')
 before=E.parse(uv).find('.//BeforeMake');assert before.findtext('RunUserProg1')=='1' and before.findtext('nStopB1X')=='2'
 assert '--check-route' in before.findtext('UserProg1Name')
 assert before.findtext('UserProg1Name').endswith('--check-route ".."')
 assert before.findtext('UserProg2Name')=='user-owned-tool.exe' and before.findtext('nStopB2X')=='3';checks.append('registered stop-on-error prebuild keeps foreign command unchanged')
 old=(route/'rcs_route.c').read_bytes();doc=json.loads(m.read_text(encoding='utf-8'));doc['Points'][1]['X']+=10;m.write_text(json.dumps(doc,ensure_ascii=False),encoding='utf-8')
 run([tool,'--check-route',root],2);run([exe,'--check-map',m,route],2);assert (route/'rcs_route.c').read_bytes()==old
 checks.append('saved map changed without export fails and never rewrites route automatically')
 run([exe,'--export-linked',m]);run([tool,'--check-route',root]);checks.append('re-export advances revision then passes')
 old=(route/'rcs_route.c').read_bytes();(route/'rcs_route.c').write_bytes(old+b'\n/* configuration tampered */\n');run([tool,'--check-route',root],2);(route/'rcs_route.c').write_bytes(old);run([tool,'--check-route',root]);checks.append('route configuration mutation detected; restoring exact source passes')
 moved=area/'moved project';shutil.copytree(root,moved);run([moved/'foxmolika_Tools_051/foxmolika_PostGenerate.exe','--check-route',moved])
 command=E.parse(moved/'MDK-ARM/tester123.uvprojx').findtext('.//BeforeMake/UserProg1Name');quoted=re.findall(r'"([^"]+)"',command);cwd=moved/'MDK-ARM'
 run([(cwd/quoted[0].replace('\\','/')).resolve(),'--check-route',(cwd/quoted[1]).resolve()]);checks.append('relative prebuild command survives renaming/copying whole project')
 legacy,lioc,luv,lmap=fixture('upgrade037');text=lioc.read_text(encoding='utf-8');text=text.replace('0.3.3','0.3.7').replace('ProjectManager.UAScriptAfterPath=','ProjectManager.UAScriptAfterPath=foxmolika_Tools_037/foxmolika_PostGenerate.exe');lioc.write_text(text,encoding='utf-8')
 oldtool=legacy/'foxmolika_Tools_037/foxmolika_PostGenerate.exe';oldtool.parent.mkdir();oldtool.write_bytes(base64.b64decode((R/'tests/fixtures/legacy_post_037.base64.txt').read_text()));oldhash=sha(oldtool)
 run([exe,'--associate-project',lioc]);assert sha(oldtool)==oldhash and (legacy/'foxmolika_Tools_051/foxmolika_PostGenerate.exe').is_file();checks.append('official037 helper upgrades by exact SHA without overwriting old helper')
 legacy,lioc,luv,lmap=fixture('upgrade040');text=lioc.read_text(encoding='utf-8').replace('0.3.3','0.4.0').replace('ProjectManager.UAScriptAfterPath=','ProjectManager.UAScriptAfterPath=foxmolika_Tools_040/foxmolika_PostGenerate.exe');lioc.write_text(text,encoding='utf-8')
 oldtool=legacy/'foxmolika_Tools_040/foxmolika_PostGenerate.exe';oldtool.parent.mkdir();oldtool.write_bytes(base64.b64decode((R/'tests/fixtures/legacy_post_040.base64.txt').read_text()));oldhash=sha(oldtool)
 run([exe,'--associate-project',lioc]);assert sha(oldtool)==oldhash and (legacy/'foxmolika_Tools_051/foxmolika_PostGenerate.exe').is_file();checks.append('official040 helper upgrades by exact SHA without overwriting old helper')
 legacy,lioc,luv,lmap=fixture('upgrade041');text=lioc.read_text(encoding='utf-8').replace('0.3.3','0.4.1').replace('ProjectManager.UAScriptAfterPath=','ProjectManager.UAScriptAfterPath=foxmolika_Tools_041/foxmolika_PostGenerate.exe');lioc.write_text(text,encoding='utf-8')
 oldtool=legacy/'foxmolika_Tools_041/foxmolika_PostGenerate.exe';oldtool.parent.mkdir();oldtool.write_bytes(base64.b64decode((R/'tests/fixtures/legacy_post_041.base64.txt').read_text()));oldhash=sha(oldtool)
 run([exe,'--associate-project',lioc]);assert sha(oldtool)==oldhash and (legacy/'foxmolika_Tools_051/foxmolika_PostGenerate.exe').is_file();checks.append('official041 helper upgrades by exact SHA without overwriting old helper')
 legacy,lioc,luv,lmap=fixture('upgrade050');text=lioc.read_text(encoding='utf-8').replace('0.3.3','0.5.0').replace('ProjectManager.UAScriptAfterPath=','ProjectManager.UAScriptAfterPath=foxmolika_Tools_050/foxmolika_PostGenerate.exe');lioc.write_text(text,encoding='utf-8')
 oldtool=legacy/'foxmolika_Tools_050/foxmolika_PostGenerate.exe';oldtool.parent.mkdir();oldtool.write_bytes(base64.b64decode((R/'tests/fixtures/legacy_post_050.base64.txt').read_text()));oldhash=sha(oldtool)
 run([exe,'--associate-project',lioc]);assert sha(oldtool)==oldhash and (legacy/'foxmolika_Tools_051/foxmolika_PostGenerate.exe').is_file();checks.append('official050 helper upgrades by exact SHA without overwriting old helper')
 report={'passed':True,'checks':checks,'editor_sha256':sha(exe),'post_sha256':sha(R/'build/foxmolika_PostGenerate.exe'),'area':str(area),'actual_Keil_rebuild':'separate native validation'}
 (R/'build/revision_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print('PASS',len(checks),'revision/prebuild/037 upgrade checks')
if __name__=='__main__':main()
