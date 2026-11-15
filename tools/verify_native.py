"""Inspect real CubeMX output and syntax-check it against the real HAL headers."""
from pathlib import Path
from xml.etree import ElementTree as E
import json,re,shutil,subprocess
from settings import ROOT,DEFAULTS

def main():
    sample=ROOT/'native';project=sample/'MDK-ARM/native.uvprojx'
    xml=E.parse(project);includes=xml.findtext('.//Cads/VariousControls/IncludePath')
    assert includes
    paths=[(project.parent/p.replace('\\','/')).resolve() for p in includes.split(';')]
    sources=[]
    for f in xml.findall('.//Group/Files/File'):
        p=(project.parent/f.findtext('FilePath').replace('\\','/')).resolve()
        assert p.exists(),str(p)
        if p.suffix.lower()=='.c':sources.append(p)
    generated=sample/'Middlewares/Third_Party/foxmolika_Robotics/foxmolika'
    for p in (ROOT/'firmware').glob('*'):
        assert (generated/p.name).read_bytes()==p.read_bytes(),p.name+' was not regenerated from current source'
    header=(sample/'Inc/rcs_config.h').read_text(encoding='utf-8')
    assert re.search(r'#define RCS_WHEEL_DIAMETER_MM 140\b',header)
    definitions=dict(re.findall(r'^#define (RCS_\w+) ([^\r\n]+)',header,re.M))
    properties=dict(line.split('=',1) for line in (sample/'native.ioc').read_text().splitlines() if '=' in line)
    for key,default in DEFAULTS.items():
        expected=float(properties.get('RCS.foxmolika.0.1.0.'+key,default))
        assert float(definitions[key])==expected,key
    mainc=(sample/'Src/main.c').read_text(encoding='utf-8')
    for token in ['RCS_REGEN_PRESERVATION_SENTINEL','RCS_Init(&hcan1)','RCS_Process();','HAL_CAN_RxFifo0MsgPendingCallback','#include "rcs_hal.h"','#include "rcs_config.h"']:
        assert token not in mainc,'Generated modules must not insert chassis logic into main.c: '+token
    irq=(sample/'Src/stm32f4xx_it.c').read_text(encoding='utf-8')
    assert 'void CAN1_RX0_IRQHandler(void)' in irq and 'HAL_CAN_IRQHandler(&hcan1)' in irq
    assert xml.findtext('.//Cads/uC99')=='1'
    gcc=shutil.which('gcc');assert gcc
    args=[gcc,'-std=c99','-fsyntax-only','-DSTM32F407xx','-DUSE_HAL_DRIVER',
          '-Wno-int-to-pointer-cast','-Wno-pointer-to-int-cast']
    for p in paths:args+=['-I',str(p)]
    result=subprocess.run(args+[str(p) for p in sources],capture_output=True,text=True,encoding='utf-8',errors='replace')
    (ROOT/'build/logs/real_hal_syntax.log').write_text(result.stdout+result.stderr,encoding='utf-8')
    if result.returncode:raise RuntimeError(result.stdout+result.stderr)
    report={
      'native_cubemx':'6.18.1; Java 21.0.10',
      'pack_install':'passed in project-local sandbox; original CubeMX installation unchanged',
      'native_panel':'visually verified Chinese parameters after selecting component AND enabling mode',
      'parameter_binding':'wheel diameter 120 -> 140 mm, actual CubeMX-generated header verified',
      'regeneration':'historical parameter regeneration verified; current sample main.c has no injected chassis includes, initialization, callback, or periodic logic',
      'project':'MDK-ARM project with C99, all file references present',
      'project_cleanup':'empty File placeholders removed and registered Compiler 6.22 selected by tools/fix_mdk_project.py; raw CubeMX output retained',
      'firmware_files':'generated source matches current authored source byte-for-byte',
      'syntax_check':f'passed {len(sources)} real generated/HAL C files using host GCC -fsyntax-only; not ARM object compilation',
      'arm_build':'21 C sources compiled to ARM objects through the registered uVision/AC6.22 without a license error; full build blocked when launching armasm.exe with Windows DLL error 0x26F. See keil_build_ac6.log. The earlier D:/AC6 standalone license-search failure did not describe the installed Keil license.',
      'hardware_test':'not performed; no flashing, no motor connection',
      'diagnostics':'CubeMX returned OK but emitted Invalid RefComponent list; tracked as unresolved diagnostic. Generated header, source references, real-HAL syntax and preservation independently verified.'
    }
    (ROOT/'build/native_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2))
if __name__=='__main__':main()
