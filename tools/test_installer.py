"""Run the actual binary installer in a project-local test profile, with spaced Chinese paths."""
from pathlib import Path
from xml.etree import ElementTree as E
import hashlib,json,os,subprocess,sys
from settings import ROOT
from version import VERSION

AREA=ROOT/'build/安装验收 空间'
INSTALL=AREA/'安装目录'
HOME=AREA/'CubeMX 用户配置'

def run_install():
    exe=ROOT/('dist/RCS_Chassis_CubeMX_Setup_'+VERSION+'_win-x64.exe')
    cube=os.environ['STM32CubeMX_PATH']
    p=subprocess.Popen([str(exe),'/S','/CUBEMX='+cube,'/TESTHOME='+str(HOME),'/D='+str(INSTALL)],cwd=ROOT)
    print('Installer started:',p.pid,flush=True)
    try:code=p.wait(timeout=210)
    except subprocess.TimeoutExpired:p.kill();raise RuntimeError('Installer test timed out')
    print('Installer exit:',code,flush=True)
    if code:raise RuntimeError('Installer failed; inspect '+str(INSTALL))
    marker=INSTALL/'install-success.txt';assert marker.is_file()
    receipt=E.parse(INSTALL/'install-receipt.xml').getroot()
    assert receipt.attrib['Version']==VERSION
    assert receipt.attrib['Isolated'].lower()=='true'
    packdir=Path(receipt.attrib['PackDirectory'])
    assert packdir.is_relative_to(AREA)
    assert (packdir/('CubeMX/RCS.Chassis.'+VERSION+'_Modes.xml')).is_file()
    return packdir

if __name__=='__main__':
    AREA.mkdir(parents=True,exist_ok=True)
    pack=run_install()
    if '--repeat' in sys.argv:assert run_install()==pack
    evidence={'binary_installer':'passed','version':VERSION,'install_directory':str(INSTALL),'isolated_profile':str(HOME),'pack_directory':str(pack),'spaced_chinese_paths':'passed','repeat_install':'passed' if '--repeat' in sys.argv else 'not run'}
    (ROOT/'build/installer_validation.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(evidence,ensure_ascii=False,indent=2))
