"""Native CubeMX acceptance using the exact profile populated by the binary installer."""
from pathlib import Path
import hashlib,json,re,sys
from settings import ROOT
from version import VERSION
from cubemx_runner import run
from test_installer import HOME,INSTALL

AREA=ROOT/'build/native_acceptance'

def seed(name,with_pack=False):
    AREA.mkdir(parents=True,exist_ok=True)
    props=dict(l.split('=',1) for l in (ROOT/'native/native.ioc').read_text().splitlines() if '=' in l)
    if with_pack:props={k.replace('0.1.0',VERSION):v.replace('0.1.0',VERSION) for k,v in props.items()}
    else:
        pins=[v for k,v in props.items() if re.fullmatch(r'Mcu.Pin\d+',k) and not v.startswith('VP_RCS.')]
        props={k:v for k,v in props.items() if not (k.startswith(('RCS.','VP_RCS.','Mcu.ThirdParty')) or re.fullmatch(r'Mcu.Pin\d+',k))}
        props['Mcu.ThirdPartyNb']='0';props['Mcu.PinsNb']=str(len(pins))
        props.update({'Mcu.Pin'+str(i):v for i,v in enumerate(pins)})
    props['ProjectManager.ProjectName']=name;props['ProjectManager.ProjectFileName']=name+'.ioc'
    props['ProjectManager.TargetToolchain']='MDK-ARM V5.39'
    folder=AREA/name;folder.mkdir(exist_ok=True)
    path=folder/(name+'.ioc');path.write_text('\n'.join(k+'='+v for k,v in sorted(props.items()))+'\n',encoding='utf-8')
    return path

def script(name,lines):
    p=AREA/(name+'.script');p.write_text('\n'.join(lines+['exit'])+'\n',encoding='utf-8');return p

def generated(root,filename):
    hits=list(root.rglob(filename));assert len(hits)==1,(filename,hits);return hits[0]

def check_files(folder,layout,diameter):
    h=generated(folder,'rcs_config.h').read_text(encoding='utf-8')
    assert re.search(r'^#define RCS_LAYOUT '+str(layout)+r'\s*$',h,re.M)
    assert re.search(r'^#define RCS_WHEEL_DIAMETER_MM '+str(diameter)+r'\s*$',h,re.M)
    assert '${' not in h
    for p in (ROOT/'firmware').iterdir():
        if p.suffix in ('.c','.h'):assert generated(folder,p.name).read_bytes()==p.read_bytes(),p.name
    main=generated(folder,'main.c').read_bytes()
    assert b'RCS_' not in main and b'rcs_' not in main
    return hashlib.sha256(main).hexdigest()

def first():
    baseline=seed('baseline2');path=seed('chassis2',True);base=baseline.parent;active=path.parent
    custom='project setCustomFWPath "C:/Users/FoxMolia/STM32Cube/Repository/STM32Cube_FW_F4_V1.28.3"'
    run(script('native_baseline',['config load "'+baseline.as_posix()+'"',custom,'project generate']),home=HOME)
    lines=['config load "'+path.as_posix()+'"',custom,
      'set ip parameters RCS.Chassis.'+VERSION+' RCS_LAYOUT 0',
      'set ip parameters RCS.Chassis.'+VERSION+' RCS_WHEEL_DIAMETER_MM 120',
      'config save','project generate']
    output=run(script('native_first',lines),home=HOME)
    digest=check_files(active,0,120)
    assert generated(base,'main.c').read_bytes()==generated(active,'main.c').read_bytes(),'Enabling component changed main.c'
    (AREA/'main.sha256').write_text(digest)
    print('PASS native Mecanum Generate Code; independent C/H; main.c exactly matches disabled-component baseline')

def remaining():
    active=AREA/'chassis2';ioc=generated(active,'chassis2.ioc')
    for layout,diameter in [(1,160),(2,200)]:
        output=run(script('native_layout_'+str(layout),[
          'config load "'+ioc.as_posix()+'"',
          'set ip parameters RCS.Chassis.'+VERSION+' RCS_LAYOUT '+str(layout),
          'set ip parameters RCS.Chassis.'+VERSION+' RCS_WHEEL_DIAMETER_MM '+str(diameter),
          'config save','project generate']),home=HOME)
        digest=check_files(active,layout,diameter)
        assert digest==(AREA/'main.sha256').read_text()
        print('PASS native layout',layout,'diameter',diameter,'main.c unchanged',flush=True)
    report={'version':VERSION,'cubemx':'6.18.1','installation':'actual binary installer, Chinese/spaced isolated profile',
      'native_generation':['Mecanum X / 120 mm','Omni3 / 160 mm','Omni4 / 200 mm'],
      'parameters':'actual CubeMX-generated macros checked after each change',
      'module_files':'3 C + 3 H copied byte-for-byte; config H rendered by CubeMX',
      'main_c':'byte-identical to disabled-component baseline for all three generations',
      'no_offline_generator':'native CubeMX importer and project generate commands only',
      'main_sha256':digest,'hardware_test':'not performed'}
    (ROOT/'build/cubemx_acceptance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2))

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8',errors='replace')
    if '--remaining' in sys.argv:remaining()
    else:first()
