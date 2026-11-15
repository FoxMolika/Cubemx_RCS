"""Build our example through the registered uVision installation.
No license values are read or copied; uVision supplies its own compiler environment.
"""
from pathlib import Path
import argparse,subprocess,winreg
from settings import ROOT
from fix_mdk_project import fix

def find_uvision():
    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,r'SOFTWARE\Keil\Products\MDK',0,winreg.KEY_READ|winreg.KEY_WOW64_32KEY) as key:
        arm=Path(winreg.QueryValueEx(key,'Path')[0])
    exe=arm.parent/'UV4/UV4.exe'
    if not exe.is_file():raise FileNotFoundError(exe)
    return exe

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--uvision',type=Path);args=ap.parse_args()
    exe=args.uvision or find_uvision();fix()
    project=ROOT/'native/MDK-ARM/native.uvprojx';log=ROOT/'build/logs/keil_build_ac6.log'
    log.parent.mkdir(parents=True,exist_ok=True)
    p=subprocess.run([str(exe),'-r',str(project),'-t','native','-j0','-o',str(log)],cwd=project.parent)
    if log.exists():
        data=log.read_bytes()
        if data.startswith((b'\xff\xfe',b'\xfe\xff')):text=data.decode('utf-16',errors='replace')
        else:
            try:text=data.decode('utf-8')
            except UnicodeError:text=data.decode('gb18030',errors='replace')
        print(text)
    print('uVision exit code:',p.returncode)
    return p.returncode
if __name__=='__main__':raise SystemExit(main())
