"""Remove empty File placeholders and select the locally registered AC6 compiler.
Only the project's own native example is modified; raw input is retained in build.
"""
from xml.etree import ElementTree as E
from settings import ROOT
def fix():
    p=ROOT/'native/MDK-ARM/native.uvprojx'
    tree=E.parse(p);count=0;changed=False
    for target in tree.findall('.//Targets/Target'):
        for name,value in [('pArmCC','6220000::V6.22::ARMCLANG'),('pCCUsed','6220000::V6.22::ARMCLANG'),('uAC6','1')]:
            node=target.find(name)
            if node is None:
                node=E.Element(name);target.insert(list(target).index(target.find('TargetOption')),node)
            if node.text!=value:node.text=value;changed=True
    for files in tree.findall('.//Group/Files'):
        for f in list(files):
            if f.tag=='File' and not f.findtext('FileName') and not f.findtext('FilePath'):
                files.remove(f);count+=1
    if count or changed:
        backup=ROOT/'build/logs/native_before_project_fix.uvprojx';backup.parent.mkdir(parents=True,exist_ok=True);backup.write_bytes(p.read_bytes())
        E.register_namespace('xsi','http://www.w3.org/2001/XMLSchema-instance')
        tree.write(p,encoding='utf-8',xml_declaration=True)
    print(f'MDK example: removed {count} empty File records; selected Arm Compiler 6.22; raw project retained in build/logs.')
if __name__=='__main__':fix()
