"""独立资源审计，无需启动Windows PowerShell；只返回资源摘要，不输出资源正文。"""
from pathlib import Path
import json
import subprocess
from build_support import ROOT, compile_csharp


def resources(executable):
    executable = Path(executable).resolve()
    assert executable.is_relative_to(ROOT)
    auditor = ROOT / "build/audit-payload.exe"
    compile_csharp(auditor, [ROOT / "tools/AuditPayload.cs"], references=["System.Web.Extensions.dll"])
    result = subprocess.run([str(auditor), str(executable)], capture_output=True, timeout=30, check=True)
    return json.loads(result.stdout.decode("utf-8-sig"))


if __name__ == "__main__":
    import sys
    print(json.dumps(resources(sys.argv[1]), ensure_ascii=False))
