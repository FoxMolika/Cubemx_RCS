"""Build a self-contained Windows Forms installer with embedded payloads."""
import hashlib,os,shutil,subprocess,zipfile
from pathlib import Path
from xml.etree import ElementTree as E
from settings import ROOT
from version import VERSION
from build_pack import build

def checked(args):
    p=subprocess.run([str(x) for x in args],cwd=ROOT,capture_output=True,text=True,encoding='utf-8',errors='replace')
    if p.returncode:raise RuntimeError(p.stdout+'\n'+p.stderr)
    return p.stdout

def main():
    pack=build();b=ROOT/'build';b.mkdir(exist_ok=True)
    manifest=E.Element('Pack',Vendor='RCS',Name='Chassis',Version=VERSION,PackSha256=hashlib.sha256(pack.read_bytes()).hexdigest())
    files=E.SubElement(manifest,'Files')
    with zipfile.ZipFile(pack) as z:
        for n in z.namelist():
            E.SubElement(files,'File',Name=n,Sha256=hashlib.sha256(z.read(n)).hexdigest())
    E.indent(manifest);E.ElementTree(manifest).write(b/'payload-manifest.xml',encoding='utf-8',xml_declaration=True)
    for f in ['installer/PackManager.cs','installer/Setup.cs']:
        assert VERSION in (ROOT/f).read_text(encoding='utf-8'),f+' still names another version; update it before building'
    csc=Path(os.environ.get('WINDIR',r'C:\Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    checked([csc,'/nologo','/target:exe','/platform:anycpu','/optimize+','/reference:System.Xml.Linq.dll','/reference:System.Management.dll','/out:'+str(b/'RcsPackManager.exe'),ROOT/'installer/PackManager.cs'])
    cube=Path(os.environ.get('STM32CubeMX_PATH',str(Path.home()/'AppData/Local/Programs/STM32CubeMX')))
    checked([cube/'jre/bin/javac.exe','-d',b/'java',ROOT/'tools/WorkspacePreferencesFactory.java'])
    exe=ROOT/('dist/RCS_Chassis_CubeMX_Setup_'+VERSION+'_win-x64.exe')
    resources={
      'RcsPackManager.exe':b/'RcsPackManager.exe','payload-manifest.xml':b/'payload-manifest.xml',
      pack.name:pack,'使用说明.html':ROOT/'docs/使用说明.html','LICENSE.txt':ROOT/'LICENSE.txt',
      'validation/WorkspacePreferencesFactory.class':b/'java/WorkspacePreferencesFactory.class',
      'validation/WorkspacePreferencesFactory$MemoryPreferences.class':b/'java/WorkspacePreferencesFactory$MemoryPreferences.class'}
    args=[csc,'/nologo','/target:winexe','/platform:anycpu','/optimize+','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Core.dll','/out:'+str(exe)]
    args+=['/resource:'+str(p)+',payload/'+name for name,p in resources.items()]
    args+=[ROOT/'installer/Setup.cs']
    output=checked(args);(b/'logs/installer_build.log').write_text(output or 'Managed Windows Forms installer build succeeded.\n',encoding='utf-8')
    if not exe.is_file():raise RuntimeError('Installer was not produced')
    from build_pack import write_sums
    write_sums()
    with (ROOT/'dist/SHA256SUMS.txt').open('a',encoding='ascii') as f:f.write(hashlib.sha256(exe.read_bytes()).hexdigest()+'  '+exe.name+'\n')
    print(exe);print('bytes:',exe.stat().st_size)
if __name__=='__main__':main()
