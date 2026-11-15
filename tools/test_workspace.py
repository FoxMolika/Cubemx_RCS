"""The actual main-window workflow that used to be blocked by a modal dialog."""
from pathlib import Path
import hashlib,json,re,subprocess,sys,uuid
R=Path(__file__).resolve().parents[1]
def main():
 sys.stdout.reconfigure(encoding='utf-8');area=R/'build'/('workspace_'+uuid.uuid4().hex[:8]);area.mkdir();exe=R/'map_editor/dist/foxmolika_MapEditor.exe'
 p=subprocess.run([str(exe),'--workspace-selftest',str(area)],capture_output=True,timeout=45);assert p.returncode==0,p.stderr.decode('gbk',errors='replace')
 text=(area/'workspace-selftest.txt').read_text();checks=int(re.search(r'PASS (\d+)',text).group(1));assert checks>=41
 doc=area/'shared-draft.rcsmap.json';before=doc.read_bytes();output=area/'export-must-stay-empty';output.mkdir();p=subprocess.run([str(exe),'--export',str(doc),str(output)],capture_output=True,timeout=30);assert p.returncode==2 and not list(output.iterdir()) and doc.read_bytes()==before
 report={'passed':True,'editor_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'checks':checks,'main_window_only':True,'shared_document_and_global_history':True,'inserted_transition_and_links_pass_geometry':True,'unfinished_draft_round_trip':True,'nonplanar_export_rejected_without_mutation':True,'area':str(area)}
 (R/'build/workspace_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(text.strip(),'; rejected export preserves draft and destination')
if __name__=='__main__':main()
