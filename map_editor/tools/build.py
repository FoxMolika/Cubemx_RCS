"""Build native .NET Framework binaries. Python is a BUILD dependency only."""
from pathlib import Path
import hashlib,os,subprocess,zipfile,sys,base64,shutil
sys.stdout.reconfigure(encoding='utf-8')
ROOT=Path(__file__).resolve().parents[1]
CSC=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
def run(args):
    p=subprocess.run([str(x) for x in args],cwd=ROOT,capture_output=True,text=True,errors='replace')
    if p.returncode:raise RuntimeError(p.stdout+'\n'+p.stderr)
    return p.stdout
def main():
    dist=ROOT/'dist';dist.mkdir(exist_ok=True)
    sys.path.insert(0,str(ROOT.parent/'tools'))
    from build_integration import post_helper
    post=post_helper()
    guide=(ROOT/'docs/使用说明.html').read_text(encoding='utf-8')
    image=ROOT/'docs/editor_ui.png'
    if image.is_file():guide=guide.replace('<!--PREVIEW-->','<p>编辑器实际界面：上方绿色区域是工程关联入口。</p><img alt="foxmolika地图编辑器工程关联界面" src="data:image/png;base64,'+base64.b64encode(image.read_bytes()).decode('ascii')+'">')
    (dist/'使用说明.html').write_text(guide,encoding='utf-8')
    resources={'rcs_nav.c':ROOT/'firmware/rcs_nav.c','rcs_nav.h':ROOT/'firmware/rcs_nav.h','使用说明.html':dist/'使用说明.html'}
    resources.update({'templates/'+p.name:p for p in (ROOT/'templates').glob('*.tpl')})
    exe=dist/'foxmolika_MapEditor.exe'
    args=[CSC,'/nologo','/target:winexe','/define:RCS_MAP_EDITOR','/platform:anycpu','/optimize+','/reference:System.Xml.Linq.dll','/reference:System.Xml.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Core.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:System.Runtime.Serialization.dll','/resource:'+str(post)+',payload/foxmolika_PostGenerate.exe','/out:'+str(exe)]
    args+=['/resource:'+str(p)+',assets/'+n for n,p in resources.items()]
    args+=sorted((ROOT/'src').glob('*.cs'))
    args+=[ROOT.parent/'keil_headers'/name for name in ['ProjectSetup.cs','KeilHeaders.cs','ProjectBinding.cs']]
    print(run(args))
    print(exe)
    # Build the standalone managed installer; no Python or runtime downloads.
    run([exe,'--demo',ROOT/'examples/示例路线.rcsmap.json'])
    payload={"foxmolika_MapEditor.exe":exe,"使用说明.html":dist/'使用说明.html',"LICENSE.txt":ROOT.parent/'LICENSE.txt',"examples/示例路线.rcsmap.json":ROOT/'examples/示例路线.rcsmap.json',
      "examples/robot_route_task.c":ROOT/'examples/robot_route_task.c',"examples/robot_route_task.h":ROOT/'examples/robot_route_task.h',"sdk/rcs_chassis.c":ROOT.parent/'firmware/rcs_chassis.c',"sdk/rcs_chassis.h":ROOT.parent/'firmware/rcs_chassis.h'}
    setup=dist/'foxmolika_MapEditor_Setup_0.4.1_win-x64.exe'
    args=[CSC,'/nologo','/target:winexe','/platform:anycpu','/optimize+','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Core.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:System.Xml.Linq.dll','/out:'+str(setup)]
    args+=['/resource:'+str(p)+',payload/'+n for n,p in payload.items()];args+=[ROOT/'installer.cs'];print(run(args));print(setup)
    shutil.copyfile(ROOT/'examples/示例路线.rcsmap.json',dist/'示例路线.rcsmap.json')
    (dist/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in [exe,setup,dist/'使用说明.html',dist/'示例路线.rcsmap.json']),encoding='utf-8')
if __name__=='__main__':main()
