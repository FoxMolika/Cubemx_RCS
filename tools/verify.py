"""Repeatable host validation. Does not flash or connect to real hardware."""
import hashlib,json,math,os,re,shutil,subprocess,sys,tempfile,zipfile
from pathlib import Path
from xml.etree import ElementTree as E
from settings import ROOT,DEFAULTS,PARAMS,validate,config_header,preview
from build_pack import build,VERSION
from configurator import export,inspect_ioc

def run(args):
    p=subprocess.run([str(a) for a in args],cwd=ROOT,capture_output=True,text=True,encoding='utf-8',errors='replace')
    print(p.stdout,end='')
    if p.returncode:raise RuntimeError(p.stderr or 'command failed: '+str(args))
    return p.stdout

def main():
    from style_check import main as style_main
    assert style_main()==0,'generated C style'
    b=ROOT/'build';b.mkdir(exist_ok=True);evidence=[]
    gcc=shutil.which('gcc')
    if not gcc:raise RuntimeError('需要 GCC 主机编译器（例如本机 w64devkit）')
    strict=['-std=c99','-O2','-Wall','-Wextra','-Werror','-pedantic','-I',ROOT/'firmware']
    source=[ROOT/'firmware/rcs_chassis.c',ROOT/'firmware/rcs_control.c']
    run([gcc,*strict,*source,ROOT/'tests/test_core.c','-lm','-o',b/'test_core.exe'])
    evidence.append(run([b/'test_core.exe']).strip())
    stub=b/'stub';stub.mkdir(exist_ok=True);cfg=dict(DEFAULTS);cfg['RCS_KP']=1
    (stub/'rcs_config.h').write_text(config_header(cfg),encoding='utf-8')
    run([gcc,*strict,'-I',ROOT/'tests','-I',stub,*source,ROOT/'firmware/rcs_hal.c',ROOT/'tests/test_hal.c','-lm','-o',b/'test_hal.exe'])
    evidence.append(run([b/'test_hal.exe']).strip())
    missing=b/'missing_can';missing.mkdir(exist_ok=True)
    (missing/'stm32f4xx_hal.h').write_text('#include <stdint.h>\n',encoding='ascii')
    p=subprocess.run([gcc,'-std=c99','-fsyntax-only','-I',str(missing),'-I',str(ROOT/'firmware'),str(ROOT/'firmware/rcs_hal.c')],capture_output=True,text=True,errors='replace')
    assert p.returncode and 'RCS: CAN HAL is disabled' in p.stderr and 'unknown type' not in p.stderr,p.stderr
    evidence.append('PASS missing CAN HAL yields an actionable configuration error without unknown-type cascade')
    # Compile-time guards in rcs_config.h reject invalid cross-field CubeMX settings.
    probe=stub/'probe.c';probe.write_text('#include "rcs_config.h"\nint rcs_probe;\n',encoding='ascii')
    bad_cases=[{'RCS_MOTOR_ID_1':1},{'RCS_COMMAND_TIMEOUT_MS':5},{'RCS_FEEDBACK_TIMEOUT_MS':4,'RCS_PERIOD_MS':5},
               {'RCS_DIRECTION_2':0},{'RCS_LAYOUT':2,'RCS_MOTOR_ID_3':2}]
    for case in bad_cases:
        values=dict(DEFAULTS);values.update(case)
        text=config_header({k:DEFAULTS[k] for k in DEFAULTS})
        for k,v in case.items():text=re.sub(r'^#define '+k+r' .*$','#define '+k+' '+str(v),text,flags=re.M)
        (stub/'rcs_config.h').write_text(text,encoding='utf-8')
        p=subprocess.run([gcc,'-std=c99','-fsyntax-only','-I',ROOT/'firmware','-I',ROOT/'tests','-I',stub,probe],capture_output=True,text=True,errors='replace')
        if p.returncode==0 or '#error' not in p.stderr:raise AssertionError('invalid config compiled: '+str(case))
    # Omni3 ignores W3, so a duplicate W3 ID must still compile.
    text=config_header({**DEFAULTS,'RCS_LAYOUT':1,'RCS_MOTOR_ID_3':1})
    (stub/'rcs_config.h').write_text(text,encoding='utf-8')
    run([gcc,'-std=c99','-fsyntax-only','-Werror','-I',ROOT/'firmware','-I',ROOT/'tests','-I',stub,probe])
    (stub/'rcs_config.h').write_text(config_header(cfg),encoding='utf-8')
    gxx=shutil.which('g++')
    if gxx:
        cxx=stub/'cxx_link.cpp';cxx.write_text('#include "rcs_hal.h"\n#include "rcs_config.h"\nint main(){return (int)RCS_State();}\n',encoding='ascii')
        run([gxx,'-std=c++11','-Wall','-Werror','-I',ROOT/'tests','-I',stub,'-I',ROOT/'firmware','-c',cxx,'-o',b/'cxx_link.o'])
        syms=run(['nm',b/'cxx_link.o'])
        assert ' U RCS_State' in syms and '_Z' not in syms.split('RCS_State')[0].splitlines()[-1],'C++ must see C linkage'
    evidence.append('PASS compile-time config guards (duplicate ID, timeout<=period, bad direction, Omni3 W3 ignored); C++ extern "C" linkage')
    for changes in [{'RCS_MOTOR_ID_1':1},{'RCS_GEAR_RATIO':0},{'RCS_KP':float('nan')},{'RCS_DIRECTION_0':0},{'RCS_PERIOD_MS':200},{'RCS_COMMAND_TIMEOUT_MS':5},{'surprise':1}]:
        try:validate({**DEFAULTS,**changes})
        except ValueError:pass
        else:raise AssertionError('invalid config accepted: '+str(changes))
    v=validate(DEFAULTS);rpm,scale=preview(v,1,0,0);assert all(abs(x-3000)<1e-4 for x in rpm) and scale<1
    folder=b/'export-check'
    shutil.rmtree(folder,ignore_errors=True);folder.mkdir()
    try:
        entry=Path(folder)/'main.c';entry.write_bytes(b'/* existing application entry: unchanged */\r\n')
        original_entry=entry.read_bytes()
        out=Path(folder)/'module';export(v,out);before=(out/'rcs_config.h').read_bytes()
        expected={'rcs_chassis.c','rcs_chassis.h','rcs_control.c','rcs_control.h','rcs_hal.c','rcs_hal.h','rcs_config.h'}
        assert {p.name for p in out.iterdir()}==expected
        assert entry.read_bytes()==original_entry
        for p in out.iterdir():
            text=p.read_text(encoding='utf-8')
            assert not re.search(r'\b(?:int|void)\s+main\s*\(',text)
            assert not re.search(r'\bvoid\s+HAL_CAN_RxFifo0MsgPendingCallback\s*\(',text)
        try:export(v,out)
        except ValueError:pass
        else:raise AssertionError('existing export overwritten')
        assert (out/'rcs_config.h').read_bytes()==before
        sample=Path(folder)/'test.ioc';sample.write_text('Mcu.Name=STM32F407V(E-G)Tx\nMxCube.Version=6.18.1\nPB8.Signal=CAN1_RX\nPB9.Signal=CAN1_TX\nCAN1.CalculateBaudRate=1000000\nNVIC.CAN1_RX0_IRQn=true\\:0\\:0\n')
        raw=sample.read_bytes();report=inspect_ioc(sample,v);assert not report['warnings'];assert sample.read_bytes()==raw
    finally:
        shutil.rmtree(folder,ignore_errors=True)
    evidence.append('PASS configuration validation, numeric preview, read-only IOC check, export overwrite protection; export is exactly 3 C + 4 H files and existing main.c stays byte-identical')
    pack=build();first=hashlib.sha256(pack.read_bytes()).hexdigest();build();assert hashlib.sha256(pack.read_bytes()).hexdigest()==first
    with zipfile.ZipFile(ROOT/('dist/RCS.Chassis.C_H.'+VERSION+'.zip')) as z:
        assert set(z.namelist())==expected
        assert z.read('rcs_config.h')==config_header(DEFAULTS).encode('utf-8')
    with zipfile.ZipFile(pack) as z:
        names=set(z.namelist());assert all(not n.startswith('/') and '..' not in Path(n).parts for n in names)
        pd=E.fromstring(z.read('RCS.Chassis.pdsc'))
        for f in pd.findall('.//file'):assert f.attrib['name'] in names,f.attrib
        for f in names:
            if f.endswith('.xml'):E.fromstring(z.read(f))
        ns={'x':'http://mcd.rou.st.com/modules.php?name=mcu'}
        modes=E.fromstring(z.read('CubeMX/RCS.Chassis.'+VERSION+'_Modes.xml'))
        config=E.fromstring(z.read('CubeMX/RCS.Chassis.'+VERSION+'_Configs.xml'))
        refs={p.attrib['Name'] for p in modes.findall('x:RefParameter',ns)}
        assert set(DEFAULTS)<=refs
        assert {p.attrib['Name'] for p in config.findall('.//x:Argument',ns)}==set(DEFAULTS)
    evidence.append('PASS pack XML references, parameter coverage, deterministic SHA256 '+first)
    text='\n'.join(evidence)+'\n'
    (b/'host_validation.txt').write_text(text,encoding='utf-8');print(text)
if __name__=='__main__':main()
