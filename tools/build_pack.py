"""Build a native CubeMX-enhanced CMSIS pack; no external Python packages."""
from pathlib import Path
from xml.etree import ElementTree as E
import hashlib,zipfile,json
from settings import ROOT,PARAMS,DEFAULTS,CONFIG_TAIL,CONFIG_PREFIX,config_header
from configurator import module_files
from version import VERSION

NS='http://mcd.rou.st.com/modules.php?name=mcu'
MODE='RoboticsJjfoxmolika'; PACK='RCS.foxmolika'; REL=PACK+'.'+VERSION

def xml(root):
    E.indent(root,space='  ')
    return E.tostring(root,encoding='utf-8',xml_declaration=True)
def child(e,tag,**attrs): return E.SubElement(e,tag,{k:str(v) for k,v in attrs.items()})

def metadata(version=VERSION):
    rel=PACK+'.'+version
    pd=E.Element('package',schemaVersion='1.6.3')
    for k,v in [('vendor','RCS'),('name','foxmolika'),('description','foxmolika F407 HAL chassis: Mecanum X and tangent Omni'),('url','https://example.invalid/foxmolika/'),('license','LICENSE.txt')]: child(pd,k).text=v
    releases=child(pd,'releases');child(releases,'release',version=version,date='2026-11-10').text='0.3.7: unified chassis/map project association; automatically register route 3C3H and include paths in MDK, restore after CubeMX regeneration, preserve user code. Firmware C APIs unchanged.'
    keys=child(pd,'keywords');child(keys,'keyword').text='IMPLEMENTED_WITH_STM32CUBEMX'
    conds=child(pd,'conditions');cond=child(conds,'condition',id='STM32F407');child(cond,'require',Dname='STM32F407*')
    comps=child(pd,'components');comp=child(comps,'component',Cclass='Robotics',Cgroup='foxmolika',Cversion=version,condition='STM32F407',maxInstances='1')
    child(comp,'description').text='foxmolika 底盘生成器：麦轮 / 全向轮，F407 HAL + C620'
    files=child(comp,'files')
    for p in sorted((ROOT/'firmware').glob('*')):
        child(files,'file',category='source' if p.suffix=='.c' else 'header',name='Middlewares/Third_Party/RCS_foxmolika/'+p.name)
    child(files,'file',category='doc',name='Documentation/README.md')
    modes=E.Element('IP',xmlns=NS,DBVersion='V4.0',IPType='Thirdparty',IpGroup='Additional Software',Name=PACK,Version=version)
    mode=child(modes,'RefMode',Abstract='true',Name=MODE);child(mode,'ConfigForMode').text=MODE;child(mode,'Parameter',Name=MODE)
    for p in PARAMS: child(mode,'Parameter',Added='true',Name=p[0])
    logic=child(modes,'ModeLogicOperator',Name='OR');m=child(logic,'Mode',Name=MODE);logic2=child(m,'ModeLogicOperator',Name='OR');m2=child(logic2,'Mode',Name=MODE);sig=child(m2,'SignalLogicalOp',Name='AND');child(sig,'Signal',Name='VS_'+MODE+'_'+version)
    child(modes,'RefSignal',Name='VS_'+MODE+'_'+version,Virtual='true')
    enable=child(modes,'RefParameter',Name=MODE,Comment='foxmolika Chassis component',DefaultValue='false',Group='Software component selection',TabName=MODE,Type='boolean',Visible='false')
    child(enable,'PossibleValue',Value='true',Comment='ENABLE',Semaphore='SEM'+MODE);child(enable,'PossibleValue',Value='false',Comment='DISABLE')
    for key,label,default,typ,lo,hi,group,opts in PARAMS:
        attrs=dict(Added='true',Name=key,Comment=label,DefaultValue=default,Group=group,TabName='底盘参数',Type=typ)
        if not opts: attrs.update(Min=lo,Max=hi)
        p=child(modes,'RefParameter',**attrs)
        child(p,'Description').text=label+'。轮序和接线要求见 Documentation/README.md。'
        for val,comment in opts or []:child(p,'PossibleValue',Value=val,Comment=comment)
    configs=E.Element('IP',xmlns=NS,DBVersion='V4.0',Name='foxmolika',Version=version,RootFolder='Packs/RCS/foxmolika/'+version+'/')
    ref=child(configs,'RefConfig',Name=MODE);child(ref,'Component',Name='Robotics:foxmolika:$'+MODE);child(ref,'ConfigFile',Name='RCS_CONFIG')
    f=child(configs,'RefConfigFile',Added='true',Name='RCS_CONFIG',Template='rcs_config_h.ftl');child(f,'File',Category='header',Condition='all',Name='./rcs_config.h')
    for key,label,*rest in PARAMS:child(f,'Argument',Added='true',AddressOf='false',Comment=label,GenericType='simple',Name=key,OptimizationCondition='equal')
    rc=child(configs,'RefComponent',Cclass='Robotics',Cgroup='foxmolika',Cversion=version.replace('.','Gg'))
    for p in sorted((ROOT/'firmware').glob('*')):child(rc,'File',Category='source' if p.suffix=='.c' else 'header',Condition='',Name='Middlewares/Third_Party/RCS_foxmolika/'+p.name)
    ftl='[#ftl]\n'+CONFIG_PREFIX+'''[#list SWIPdatas as SWIP]
[#if SWIP.defines??]
[#list SWIP.defines as definition]
#define ${definition.name} ${definition.value}
[/#list]
[/#if]
[/#list]
'''+CONFIG_TAIL
    return {PACK+'.pdsc':xml(pd),'CubeMX/'+rel+'_Modes.xml':xml(modes),'CubeMX/'+rel+'_Configs.xml':xml(configs),'CubeMX/templates/rcs_config_h.ftl':ftl.encode()}

def write_sums():
    """Hash every current release artifact; never drop lines written by other build steps."""
    dist=ROOT/'dist';names=['RCS.foxmolika.'+VERSION+'.pack','RCS.foxmolika.C_H.'+VERSION+'.zip',
                         'foxmolika_CubeMX_Setup_'+VERSION+'_win-x64.exe','foxmolika_CubeMX_Source_'+VERSION+'.zip','tester123_fixed_'+VERSION+'.zip']
    lines=[hashlib.sha256((dist/n).read_bytes()).hexdigest()+'  '+n+'\n' for n in names if (dist/n).is_file()]
    (dist/'SHA256SUMS.txt').write_text(''.join(lines),encoding='ascii')

def build():
    # A pack must never bypass the same policy used by direct C/H export.
    module_files(DEFAULTS)
    tree=ROOT/'pack';tree.mkdir(exist_ok=True)
    data=metadata()
    # Remove CubeMX metadata of other versions so the expanded tree matches the .pack.
    for stale in (tree/'CubeMX').glob('RCS.foxmolika.*.xml'):
        if stale.name not in data and 'CubeMX/'+stale.name not in data:stale.unlink()
    for p in sorted((ROOT/'firmware').glob('*')):data['Middlewares/Third_Party/RCS_foxmolika/'+p.name]=p.read_bytes()
    data['LICENSE.txt']=(ROOT/'LICENSE.txt').read_bytes()
    data['Documentation/README.md']=(ROOT/'docs/接入说明.md').read_bytes()
    data['Documentation/Validation.md']=(ROOT/'docs/验证记录.md').read_bytes()
    # The project README links to repository paths; inside the installed pack those
    # targets do not exist. Keep the text but drop the link syntax in this copy.
    import re as _re
    proto=_re.sub(r'\[([^\]]+)\]\([^)]+\)',r'\1',(ROOT/'README.md').read_text(encoding='utf-8'))
    data['Documentation/Prototype.md']=proto.encode('utf-8')
    data['Documentation/使用说明.html']=(ROOT/'docs/使用说明.html').read_bytes()
    for name,b in data.items():
        p=tree/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
    dist=ROOT/'dist';dist.mkdir(exist_ok=True);out=dist/(REL+'.pack')
    with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
        for name,b in sorted(data.items()):
            info=zipfile.ZipInfo(name,date_time=(2026,11,9,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;z.writestr(info,b)
    standalone=dist/('RCS.foxmolika.C_H.'+VERSION+'.zip')
    with zipfile.ZipFile(standalone,'w',zipfile.ZIP_DEFLATED) as z:
        for name,b in sorted(module_files(DEFAULTS).items()):
            info=zipfile.ZipInfo(name,date_time=(2026,11,9,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;z.writestr(info,b)
    write_sums()
    (ROOT/'examples/default.json').write_text(json.dumps(DEFAULTS,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (ROOT/'examples/rcs_config.h').write_text(config_header(DEFAULTS),encoding='utf-8')
    print(out)
    return out
if __name__=='__main__':build()
