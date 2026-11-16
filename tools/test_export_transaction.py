"""编译真实Exporter，确定性验证目录事务失败路径；不依赖随机压力测试或产品故障开关。"""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import uuid

from build_support import ROOT, XML_REFERENCES, compile_csharp
from test_terrain_project import HARNESS


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    area = ROOT / "build" / ("export_transaction_" + uuid.uuid4().hex[:8])
    area.mkdir(parents=True)
    model = area / "ModelFixture.cs"
    # 已有工程事务测试的密封6/8模块夹具；只替代地图/模板，不替代待测Exporter实现。
    model.write_text(HARNESS.split("static class TerrainProjectHarness")[0], encoding="utf-8")
    source = ROOT / "map_editor/tests/test_export_transaction.cs"
    actual = [ROOT / "map_editor/src" / name for name in ("Exporter.cs", "UserRegions.cs", "RevisionGuard.cs")]
    actual += [ROOT / "keil_headers" / name for name in ("ProjectBinding.cs", "KeilHeaders.cs")]
    exe = area / "export-transaction-tests.exe"
    compile_csharp(exe, [model, source, *actual], references=XML_REFERENCES, defines=["RCS_MAP_EDITOR"])
    p = subprocess.run([str(exe), str(area / "cases")], capture_output=True, timeout=35)
    output = p.stdout.decode("utf-8", errors="replace") + p.stderr.decode("utf-8", errors="replace")
    assert p.returncode == 0, output
    report = {
        "passed": True, "report": output.strip(), "synthetic_sealed_modules": True,
        "real_directory_lock": True, "deterministic_fault_injection": True,
        "source_sha256": {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in actual + [source]},
        "hardware_test": False, "area": str(area),
    }
    (ROOT / "build/export_transaction_validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(output.strip())


if __name__ == "__main__":
    main()
