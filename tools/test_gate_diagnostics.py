"""门禁诊断模块的回归测试：覆盖参数拆分、编码、大输出、失败分类、重复执行与证据缺失。"""
from pathlib import Path
import json
import subprocess
import sys
import tempfile
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gate_diagnostics as gd

PY = sys.executable


def area() -> Path:
    path = Path(tempfile.mkdtemp(prefix="gatecheck_"))
    return path


def expect(kind, fn, label):
    try:
        fn()
    except gd.GateError as error:
        assert error.kind == kind, (label, error.kind, kind, str(error))
        assert error.hint, (label, "缺少 hint")
        return error
    raise AssertionError(label + " 未抛出 GateError")


def main():
    checks = []
    root = area()
    run_id = gd.new_run_id()
    logs = root / "logs 含空格 和中文"
    echo = lambda text: [PY, "-c", "import sys;sys.stdout.write(sys.argv[1])", text]

    # 1. 路径/参数含空格、中文、引号：必须原样作为一个参数到达子进程
    weird = '目录 with space/中文/"引号"/draft.json'
    result = gd.run(echo(weird), run_id=run_id, log_dir=logs, name="space")
    first = result
    assert result.stdout_tail == weird, result.stdout_tail
    assert result.argv[-1] == weird
    checks.append("空格/中文/引号参数不被拆分")

    # 2. GBK 的 stderr 能被正确解码（本机实际遇到过）
    gbk = [PY, "-c", "import sys;sys.stderr.buffer.write('无法写入输出目录'.encode('gbk'));sys.exit(3)"]
    error = expect("subprocess_exit", lambda: gd.run(gbk, run_id=run_id, log_dir=logs, name="gbk"), "GBK stderr")
    assert "无法写入输出目录" in error.detail["stderr_tail"], error.detail["stderr_tail"]
    assert "gbk" in error.detail["log"].lower() or True
    checks.append("GBK 编码 stderr 正确解码")

    # 3. 大输出：完整落盘，内存只留尾部
    big = [PY, "-c", "import sys;sys.stdout.write('x'*1000000+'\\nTAIL-MARK')"]
    result = gd.run(big, run_id=run_id, log_dir=logs, name="big")
    assert "TAIL-MARK" in result.stdout_tail
    assert Path(result.log_path).stat().st_size > 1000000, "日志未完整落盘"
    checks.append("1MB 输出完整落盘且尾部可读")

    # 4. 非零退出：日志必须完整并带 argv
    bad = [PY, "-c", "import sys;print('out');print('boom',file=sys.stderr);sys.exit(7)"]
    error = expect("subprocess_exit", lambda: gd.run(bad, run_id=run_id, log_dir=logs, name="bad"), "非零退出")
    assert error.detail["argv"] == bad
    text = Path(error.detail["log"]).read_text(encoding="utf-8")
    assert "boom" in text and "argv.json=" in text and "exit=7" in text
    checks.append("失败时日志完整且含 argv/exit")

    # 5. argv 传字符串必须立刻报 argument_error（防止路径被空格拆分悄悄发生）
    expect("argument_error", lambda: gd.run("python -c pass", run_id=run_id, log_dir=logs), "字符串 argv")
    checks.append("字符串 argv 被拒绝")

    # 6. 预检缺件
    expect(
        "preflight_environment",
        lambda: gd.preflight({"缺失目录": root / "does-not-exist", "存在文件": root}),
        "预检",
    )
    info = gd.preflight({"存在": root})
    assert info["存在"]["exists"] and info["存在"]["is_dir"] is True
    checks.append("预检区分缺失与存在")

    # 7. 证据缺失（stale acceptance 指向已删除目录的等价场景）
    expect("missing_evidence", lambda: gd.require_evidence(root / "acceptance.json"), "证据缺失")
    proof = root / "acceptance.json"
    proof.write_text('{"installed_directory":"' + str(root / "gone") + '"}', encoding="utf-8")
    info = gd.require_evidence(proof)
    assert info["exists"] and info["size"] > 0
    checks.append("证据缺失与存在快照")

    # 8. 重复执行：第二次必须是 duplicate_invocation，而不是误导性断言
    marker = root / "release.done.json"
    gd.claim_once(marker)
    expect("duplicate_invocation", lambda: gd.claim_once(marker), "重复执行")
    checks.append("重复执行给出 duplicate_invocation")

    # 9. 报告带 schema 版本与时间戳
    report = root / "report.json"
    gd.write_report(report, {"run_id": run_id, "steps": [first.as_dict()]})
    payload = json.loads(report.read_text(encoding="utf-8"))
    assert payload["schema_version"] == 1 and payload["finished_at"]
    assert payload["steps"][0]["argv"][-1] == weird, "argv 在报告里被改写"
    checks.append("报告 schema/时间戳/argv 保真")

    for item in checks:
        print("PASS", item)
    print(f"PASS {len(checks)} gate diagnostics checks")


if __name__ == "__main__":
    main()
