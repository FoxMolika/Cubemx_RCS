"""只读核对当前已发布本地产物、清单和源码包；不重写发布证据。"""
from pathlib import Path
import hashlib,json,zipfile
from version import VERSION,PACK_NAME,SETUP_NAME
R=Path(__file__).resolve().parents[1]
def main():
 report=json.loads((R/'dist'/('发布核验_'+VERSION+'.json')).read_text(encoding='utf-8'))
 sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
 for name,key in [(SETUP_NAME,'installer_sha256'),(PACK_NAME,'pack_sha256'),('foxmolika_CubeMX_Source_'+VERSION+'.zip','source_zip_sha256')]:assert sha(R/'dist'/name)==report[key],name
 with zipfile.ZipFile(R/'dist'/('foxmolika_CubeMX_Source_'+VERSION+'.zip')) as z:
  assert not any('/build/' in '/'+name or '/dist/' in '/'+name or name.lower().endswith(('.exe','.dll','.log','.htm')) for name in z.namelist())
 print('PASS current release hashes and source ZIP exclusions: '+VERSION)
if __name__=='__main__':main()
