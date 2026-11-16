from version import VERSION_CODE
"""Descriptive style metrics; intentionally no fabricated similarity score."""
from pathlib import Path
import zipfile,hashlib,json,sys
from style_check import code_mask,CORE
R=Path(__file__).resolve().parents[1]
def metrics(texts):
 lines=[];comments=0;tabs=0
 for text in texts:
  raw=text.splitlines();clean=code_mask(text,False).splitlines()
  for a,b in zip(raw,clean):
   if not a.strip():continue
   lines.append(len(a));comments+=bool(a.strip() and not b.strip());tabs+='\t' in a
 return {'nonblank_lines':len(lines),'mean_characters_per_nonblank_line':round(sum(lines)/len(lines),2),'comment_only_line_percent':round(comments*100/len(lines),2),'lines_with_TAB':tabs}
def main():
 sys.stdout.reconfigure(encoding='utf-8');result={'meaning':'These are style descriptors, not functional/API similarity percentages.','archives':{}}
 reference=json.loads((R/'tests/fixtures/style/reference_style_audit.json').read_text(encoding='utf-8'))
 for file,selector in [('RCS_Template_F407_hal.zip',lambda n:'/RCS/RCS_HAL/' in n),('RCS_Template_F407_std.zip',lambda n:'/USER/' in n and '/.cmsis/' not in n)]:
  p=Path.home()/'Downloads'/file
  if not p.is_file():
   result['archives'][file]=dict(reference['archives'][file],basis='Recorded strict-decoding audit snapshot; original archive unavailable on this machine.');continue
  with zipfile.ZipFile(p) as z:
   names=[n for n in z.namelist() if n.endswith(('.c','.h')) and selector(n)];texts=[];decoded=[];skipped=[]
   for n in names:
    b=z.read(n)
    for encoding in ['utf-8-sig','gb18030']:
     try:
      value=b.decode(encoding);texts.append(value);decoded.append({'name':n,'encoding':encoding});break
     except UnicodeDecodeError:pass
    else:skipped.append({'name':n,'reason':'Neither strict UTF-8 nor GB18030; excluded from metrics rather than replacing bytes.'})
   result['archives'][file]={'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'selected_files':names,'decoded_files':decoded,'excluded_files':skipped,'metrics':metrics(texts)}
 for label,folder in [('baseline_034',R.parent/'RCS_Chassis_CubeMX_0.3.4/firmware'),('deepseek_candidate',R.parent/'RCS_Chassis_CubeMX_Fix/firmware'),('generated_'+VERSION_CODE,R/'firmware')]:
  result[label]=metrics([(folder/n).read_text(encoding='utf-8') for n in CORE]) if folder.is_dir() else dict(reference[label],basis='Recorded reference snapshot; reference source directory unavailable.')
 result['scope']='HAL framework files and standard USER files only; vendor CMSIS and test libraries excluded.'
 (R/'build').mkdir(exist_ok=True);(R/'build/style_audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps({k:v for k,v in result.items() if k not in ['archives']},ensure_ascii=False,indent=2))
if __name__=='__main__':main()
