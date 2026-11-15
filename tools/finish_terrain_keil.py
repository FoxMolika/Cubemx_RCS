"""Compile actual installed-editor8-file terrain output in native CubeMX MDK project."""
from pathlib import Path
import hashlib,json,re,sys
from keil_build import build
R=Path(__file__).resolve().parents[1]
def main():
 sys.stdout.reconfigure(encoding='utf-8');path=R/'build/terrain_project_integration_validation.json';report=json.loads(path.read_text(encoding='utf-8'));project=Path(report['project_directory'])/'MDK-ARM/tester123.uvprojx'
 result=build(project,R/'build/keil080-terrain-private.log');assert result['exit']==0,result['summary'];assert result['axf_files'];assert any('0 Error(s), 0 Warning(s)' in line for line in result['summary'])
 raw=(R/'build/keil080-terrain-private.log').read_text(encoding='utf-8',errors='replace')
 for name in ['rcs_route.c','rcs_nav.c','rcs_actions.c','rcs_terrain.c']:assert re.search(r'compiling\s+'+re.escape(name),raw,re.I),name
 assert report['terrain_mode'] and report['registered_route_file_count']==8
 report.update(keil='pass: ARM Compiler 6.22, 0 errors, 0 warnings',terrain_source_compiled=True,keil_summary=result['summary'],axf_sha256=hashlib.sha256(Path(result['axf_files'][0]).read_bytes()).hexdigest())
 path.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print('PASS native terrain4C4H registration/regeneration and actual ARM6.22 build;0errors0warnings; no flashing')
if __name__=='__main__':main()
