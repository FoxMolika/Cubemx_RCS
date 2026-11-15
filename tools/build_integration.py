"""One cached post-generation binary shared by both native front ends."""
from pathlib import Path
import hashlib,os,subprocess
ROOT=Path(__file__).resolve().parents[1]
def post_helper():
    area=ROOT/'build';area.mkdir(exist_ok=True)
    sources=[ROOT/'keil_headers/KeilHeaders.cs',ROOT/'keil_headers/ProjectBinding.cs']
    digest=hashlib.sha256(b''.join(p.read_bytes() for p in sources)+b'RCS_POSTGEN;anycpu;optimized;v1').hexdigest()
    exe=area/'foxmolika_PostGenerate.exe';stamp=area/'post-source.sha256'
    if not exe.is_file() or not stamp.is_file() or stamp.read_text()!=digest:
        csc=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        args=[csc,'/nologo','/target:exe','/define:RCS_POSTGEN','/platform:anycpu','/optimize+',
              '/reference:System.Xml.Linq.dll','/reference:System.Xml.dll','/reference:System.Core.dll','/out:'+str(exe),*sources]
        p=subprocess.run([str(x) for x in args],capture_output=True,text=True,errors='replace')
        if p.returncode:raise RuntimeError(p.stdout+p.stderr)
        stamp.write_text(digest)
    return exe
