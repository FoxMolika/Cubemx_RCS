"""实际beta编辑器的模板/实例/GUI测试；不访问真实个人模板目录。"""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import uuid

ROOT=Path(__file__).resolve().parents[1]


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    editor=ROOT/"map_editor/dist/foxmolika_MapEditor.exe"
    area=ROOT/"build"/("hardware_ui_final_"+uuid.uuid4().hex[:8])
    area.mkdir(parents=True)
    result=subprocess.run([str(editor),"--hardware-selftest",str(area)],capture_output=True,timeout=90)
    text=(result.stdout+result.stderr).decode("utf-8","replace")
    assert result.returncode==0,text
    assert "hardware model/catalog/UI/template/action checks" in text,text
    assert len(list((area/"fixtures").glob("*.rcsmap.json")))==5
    evidence={"passed":True,"editor_sha256":hashlib.sha256(editor.read_bytes()).hexdigest(),
              "scope":"actual EXE model/catalog/template/action/UI checks; no physical hardware",
              "report":text.strip(),"area":str(area)}
    (ROOT/"build/hardware_ui_validation.json").write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding="utf-8")
    print(text.strip())


if __name__=="__main__":
    main()
