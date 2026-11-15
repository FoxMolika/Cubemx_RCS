"""Label only explicit owned build artifacts, never the workspace or user profile."""
from pathlib import Path
import ctypes as C
from ctypes import wintypes as W
import subprocess
ROOT=Path(__file__).resolve().parents[1]
A=C.WinDLL('advapi32',use_last_error=True);K=C.WinDLL('kernel32',use_last_error=True)
A.GetNamedSecurityInfoW.argtypes=[W.LPWSTR,C.c_int,W.DWORD,C.c_void_p,C.c_void_p,C.POINTER(C.c_void_p),C.POINTER(C.c_void_p),C.POINTER(C.c_void_p)]
A.GetNamedSecurityInfoW.restype=W.DWORD
K.LocalFree.argtypes=[C.c_void_p];K.LocalFree.restype=C.c_void_p
def security(path):
 dacl=C.c_void_p();sacl=C.c_void_p();sd=C.c_void_p()
 code=A.GetNamedSecurityInfoW(str(Path(path).resolve()),1,0x14,None,None,C.byref(dacl),C.byref(sacl),C.byref(sd))
 if code:raise OSError(code,'Cannot read integrity label',str(path))
 try:
  descriptor=C.string_at(dacl.value,C.c_ushort.from_address(dacl.value+2).value) if dacl.value else b''
  labels=[]
  if sacl.value:
   count=C.c_ushort.from_address(sacl.value+4).value;offset=8
   for _ in range(count):
    ace=sacl.value+offset;kind=C.c_ubyte.from_address(ace).value;flags=C.c_ubyte.from_address(ace+1).value;size=C.c_ushort.from_address(ace+2).value
    if kind==17:
     sid=ace+8;n=C.c_ubyte.from_address(sid+1).value;rid=C.c_uint32.from_address(sid+8+4*(n-1)).value;labels.append((rid,flags))
    offset+=size
  effective=next((rid for rid,flags in labels if not flags&16),labels[0][0] if labels else 8192)
  return effective,descriptor,labels
 finally:K.LocalFree(sd)
def medium(path,children=False):
 path=Path(path).resolve();assert path.is_relative_to(ROOT) and path!=ROOT and not path.is_symlink(),path
 before=security(path)[1]
 p=subprocess.run(['icacls',str(path),'/setintegritylevel','(OI)(CI)M' if children else 'M'],capture_output=True)
 if p.returncode:raise RuntimeError('Cannot set Medium label for owned artifact: '+str(path)+' '+p.stdout.decode(errors='replace')+p.stderr.decode(errors='replace'))
 rid,after,_=security(path);assert rid==8192 and before==after,('Integrity/DACL verification failed',str(path),rid)
 return rid
