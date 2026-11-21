"""冻结构建→host→单文件安装→隔离CubeMX/Keil→交付；没有任何远端发布。"""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import sys
import uuid

from version import EDITOR_SETUP_NAME, VERSION
from windows_integrity import medium

ROOT=Path(__file__).resolve().parents[1]


def standalone():
    area=ROOT/"build"/("beta_installer_"+uuid.uuid4().hex[:8]);area.mkdir(parents=True)
    single=area/"single";single.mkdir();exe=single/EDITOR_SETUP_NAME
    shutil.copyfile(ROOT/"map_editor/dist"/EDITOR_SETUP_NAME,exe);medium(exe)
    assert list(single.iterdir())==[exe]
    target=area/"installed"
    for extra in [[],[]]:
        subprocess.run([str(exe),"/S","/D="+str(target),*extra],check=True,timeout=60)
    editor=ROOT/"map_editor/dist/foxmolika_MapEditor.exe"
    digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
    assert digest(editor)==digest(target/editor.name)
    assert (target/"使用说明.html").read_bytes()==(ROOT/"docs/用户手册.html").read_bytes()
    subprocess.run([str(exe),"/S","/UNINSTALL","/D="+str(target)],check=True,timeout=60)
    assert not target.exists()
    (ROOT/"build/beta_installer_validation.json").write_text(json.dumps({"passed":True,"single_file":True,
        "install_repeat_uninstall":True,"setup_sha256":digest(exe),"editor_sha256":digest(editor)},indent=2),encoding="utf-8")
    print("PASS standalone beta single-file install/repeat/uninstall",flush=True)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    for command in [("tools/check.py","--level","host"),("suite/tools/build.py",),
                    ("tools/test_version.py","--include-suite")]:
        print("RUN",*command,flush=True)
        subprocess.run([sys.executable,*command],cwd=ROOT,check=True)
    standalone()
    for script in ["tools/test_beta_native.py","tools/build_beta_delivery.py"]:
        subprocess.run([sys.executable,script],cwd=ROOT,check=True)


if __name__=="__main__":
    main()
