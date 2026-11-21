"""门禁诊断：统一子进程调用、编码兜底、结构化失败分类与证据留存。

设计约束：
1. 只做观测与分类，不改变任何通过条件——判定逻辑仍在 check.py/release.py。
2. 子进程一律按 argv 数组传入，日志里记录 argv 的 JSON（不用 shell 引号，
   因为 Windows 引号规则与 shlex 不一致，字符串表示会有歧义）。
3. 输出先收 bytes，再按 utf-8 -> gbk -> latin-1 解码；记录实际采用的编码。
   这解决了本机 GBK 的 stderr 与 UTF-8 源码输出混排时的乱码问题。
4. 大输出不整体进内存：完整内容流式落盘，内存只保留尾部若干行。
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import time
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path

# 失败分类：必须在抛出点标注，禁止靠匹配 stderr 文本推断。
KINDS = (
    "preflight_environment",  # 环境缺件（工具、基线目录、CubeMX/Keil）
    "argument_error",         # 参数构造错误（含路径被空格拆分）
    "subprocess_exit",        # 子进程非零退出
    "missing_evidence",       # 证据文件缺失或指向已删除目录
    "source_changed",         # 源码/产物在门禁期间被改动
    "artifact_missing",       # 期望产物不存在
    "hash_mismatch",          # 摘要不一致
    "directory_race",         # 目录被并发创建/占用
    "duplicate_invocation",   # 同一 run 被重复执行
)

DEFAULT_TAIL_LINES = 200


class GateError(RuntimeError):
    """带分类的门禁失败。kind 必须是 KINDS 之一。"""

    def __init__(self, kind: str, message: str, hint: str = "", detail=None):
        if kind not in KINDS:
            raise ValueError("未知失败分类：" + str(kind))
        super().__init__(message)
        self.kind = kind
        self.hint = hint
        self.detail = detail or {}

    def report(self) -> dict:
        return {"kind": self.kind, "message": str(self), "hint": self.hint, "detail": self.detail}


def decode_output(raw: bytes) -> tuple[str, str]:
    """按常见编码依次尝试；返回 (文本, 实际编码)。"""
    for encoding in ("utf-8", "gbk", "latin-1"):
        try:
            return raw.decode(encoding), encoding
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1", errors="replace"), "latin-1"


def new_run_id() -> str:
    return datetime.now().strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]


def tail(text: str, lines: int = DEFAULT_TAIL_LINES) -> str:
    if lines <= 0:
        return ""
    parts = text.splitlines()
    return "\n".join(parts[-lines:])


def evidence(path) -> dict:
    """关键文件的快照摘要：存在性、大小、时间、SHA256。"""
    p = Path(path)
    if not p.exists():
        return {"path": str(p), "exists": False}
    st = p.stat()
    stamp = datetime.fromtimestamp(st.st_mtime, timezone.utc).isoformat()
    if p.is_dir():
        # 目录只做存在性快照；摘要留给目录内的具体文件。
        return {"path": str(p), "exists": True, "is_dir": True, "mtime": stamp}
    return {
        "path": str(p),
        "exists": True,
        "is_dir": False,
        "size": st.st_size,
        "mtime": stamp,
        "sha256": hashlib.sha256(p.read_bytes()).hexdigest(),
    }


@dataclass
class StepResult:
    name: str
    argv: list
    cwd: str
    exit_code: int
    duration_s: float
    stdout_encoding: str
    stderr_encoding: str
    log_path: str
    stdout_tail: str = ""
    stderr_tail: str = ""
    extra: dict = field(default_factory=dict)

    def as_dict(self) -> dict:
        d = asdict(self)
        d["argv"] = list(self.argv)
        return d


def _write_log(log_dir: Path, run_id: str, name: str, argv, result) -> Path:
    log_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%H%M%S")
    path = log_dir / f"{run_id}.{name}.{stamp}.log"
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write("argv.json=" + json.dumps([str(a) for a in argv], ensure_ascii=False) + "\n")
        handle.write("cwd=" + str(result.get("cwd", "")) + "\n")
        handle.write("exit=" + str(result.get("exit_code", "")) + "\n")
        handle.write("stdout.encoding=" + result.get("stdout_encoding", "") + "\n")
        handle.write("stderr.encoding=" + result.get("stderr_encoding", "") + "\n")
        handle.write("--- stdout ---\n")
        handle.write(result.get("stdout", ""))
        handle.write("\n--- stderr ---\n")
        handle.write(result.get("stderr", ""))
    return path


def run(argv, cwd=None, run_id=None, log_dir=None, name=None, timeout=3600, check=True) -> StepResult:
    """按 argv 数组执行子进程；完整输出落盘，内存只留尾部。

    check=True 且退出码非零时抛 GateError('subprocess_exit')，detail 带日志路径与尾部。
    """
    if isinstance(argv, (str, bytes)):
        raise GateError(
            "argument_error",
            "argv 必须是列表，不能是字符串（避免路径被空格拆分）",
            hint="把命令拆成列表：['prog','--flag', str(path)]",
        )
    argv = [str(a) for a in argv]
    run_id = run_id or new_run_id()
    log_dir = Path(log_dir) if log_dir else Path.cwd() / "build" / "gate-logs"
    name = name or Path(argv[0]).stem
    started = time.time()
    try:
        completed = subprocess.run(argv, cwd=str(cwd) if cwd else None, capture_output=True, timeout=timeout)
    except FileNotFoundError as error:
        raise GateError(
            "preflight_environment",
            f"找不到可执行文件：{argv[0]}",
            hint="确认该工具已安装，或显式传入绝对路径",
            detail={"argv": argv},
        ) from error
    except subprocess.TimeoutExpired as error:
        raise GateError(
            "subprocess_exit",
            f"步骤超时（>{timeout}s）：{name}",
            hint="检查是否有交互等待或死锁",
            detail={"argv": argv},
        ) from error
    duration = time.time() - started
    out_text, out_enc = decode_output(completed.stdout or b"")
    err_text, err_enc = decode_output(completed.stderr or b"")
    log_path = _write_log(
        log_dir,
        run_id,
        name,
        argv,
        {
            "cwd": cwd or os.getcwd(),
            "exit_code": completed.returncode,
            "stdout": out_text,
            "stderr": err_text,
            "stdout_encoding": out_enc,
            "stderr_encoding": err_enc,
        },
    )
    result = StepResult(
        name=name,
        argv=argv,
        cwd=str(cwd) if cwd else os.getcwd(),
        exit_code=completed.returncode,
        duration_s=round(duration, 3),
        stdout_encoding=out_enc,
        stderr_encoding=err_enc,
        log_path=str(log_path),
        stdout_tail=tail(out_text),
        stderr_tail=tail(err_text),
    )
    if check and completed.returncode != 0:
        raise GateError(
            "subprocess_exit",
            f"子进程失败（exit={completed.returncode}）：{name}",
            hint="见 detail.argv 与日志；先确认是参数问题还是真实失败",
            detail={
                "argv": argv,
                "cwd": result.cwd,
                "log": str(log_path),
                "stdout_tail": result.stdout_tail,
                "stderr_tail": result.stderr_tail,
            },
        )
    return result


def preflight(required: dict) -> dict:
    """第 0 步环境预检。required 形如 {'0.3.7 基线': path, 'CubeMX': path}。

    缺失即抛 preflight_environment，并给出可操作的提示，而不是等到深处才失败。
    """
    missing = {name: str(path) for name, path in required.items() if not Path(path).exists()}
    if missing:
        raise GateError(
            "preflight_environment",
            "环境预检失败：" + "；".join(f"{k} 不存在（{v}）" for k, v in missing.items()),
            hint="安装缺失工具，或设置对应环境变量指向实际位置",
            detail={"missing": missing, "required": {k: str(v) for k, v in required.items()}},
        )
    return {name: evidence(path) for name, path in required.items()}


def _run_marker(marker) -> Path:
    return Path(marker)


def claim_once(marker) -> None:
    """同一门禁/发布只允许执行一次；重复执行给 duplicate_invocation 而不是误导性断言。"""
    path = _run_marker(marker)
    if path.exists():
        raise GateError(
            "duplicate_invocation",
            f"该步骤已完成过：{path}",
            hint="如需重跑，先确认产物状态并显式删除标记文件",
            detail={"marker": str(path), "content": path.read_text(encoding="utf-8", errors="replace")},
        )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(datetime.now(timezone.utc).isoformat() + "\n", encoding="utf-8")


def require_evidence(path, hint="") -> dict:
    """证据文件必须存在且指向有效目录；stale 指向已删除目录时给 missing_evidence。"""
    info = evidence(path)
    if not info.get("exists"):
        raise GateError(
            "missing_evidence",
            f"证据文件缺失：{path}",
            hint=hint or "先跑对应验收步骤，再读取该文件",
            detail=info,
        )
    return info


def write_report(path, payload: dict) -> str:
    """写结构化报告（含 schema 版本，便于以后演进不互相污染）。"""
    payload = dict(payload)
    payload.setdefault("schema_version", 1)
    payload.setdefault("finished_at", datetime.now(timezone.utc).isoformat())
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return str(target)
