"""Run isolated crash-injection subprocesses. No real editor session is crashed.

The editor's explicit test root is underneath this checkout; fallback reports
use the user's temporary directory and are marked so startup recovery ignores
them. No diagnostics are uploaded or emailed.
"""
from pathlib import Path
import hashlib
import json
import subprocess
import os

ROOT=Path(__file__).resolve().parents[1]

def main():
    exe=ROOT/'dist/foxmolika_MapEditor.exe'
    output=ROOT.parent/'build/crash-validation'
    output.mkdir(parents=True,exist_ok=True)
    temporary=output/'temporary';temporary.mkdir(exist_ok=True)
    env=dict(os.environ);env.update(TEMP=str(temporary),TMP=str(temporary))
    p=subprocess.run([str(exe),'--crash-selftest',str(output)],capture_output=True,env=env,
                     text=True,encoding='utf-8',errors='replace',timeout=60)
    assert p.returncode==0,p.stdout+p.stderr
    text=(output/'crash-validation.txt').read_text(encoding='utf-8-sig')
    assert 'PASS 54 crash checks' in text,text
    sources=['CrashReporter.cs','CrashTests.cs','Program.cs']
    result={'passed':True,'checks':54,'no_upload':True,
            'subprocess_injections':['UI','worker','concurrent workers','CLI'],
            'stdout':p.stdout,'evidence':text,
            'editor_SHA256':hashlib.sha256(exe.read_bytes()).hexdigest(),
            'source_SHA256':{name:hashlib.sha256((ROOT/'src'/name).read_bytes()).hexdigest() for name in sources}}
    (output/'results.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(text)

if __name__=='__main__':main()
