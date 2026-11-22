"""门禁的子进程日志、结构化错误与工作区互斥。"""
from __future__ import annotations

from contextlib import contextmanager
from datetime import datetime, timezone
import errno
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time
import uuid

KINDS = (
    "preflight_environment", "argument_error", "subprocess_exit",
    "missing_evidence", "source_changed", "artifact_missing", "hash_mismatch",
    "directory_race", "duplicate_invocation", "stale_evidence", "internal_error",
)
TAIL_BYTES = 16384
DEFAULT_TAIL_LINES = 200


class GateError(RuntimeError):
    def __init__(self, kind, message, hint="", detail=None):
        if kind not in KINDS:
            raise ValueError("未知失败分类：" + str(kind))
        super().__init__(message)
        self.kind = kind
        self.hint = hint
        self.detail = detail or {}

    def report(self):
        return {"kind": self.kind, "message": str(self), "hint": self.hint, "detail": self.detail}


def now():
    return datetime.now(timezone.utc).isoformat()


def new_run_id():
    return datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:12]


def file_sha(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def require_file(path, kind="missing_evidence"):
    path = Path(path)
    if not path.is_file():
        raise GateError(kind, "缺少文件：" + str(path), "重新执行生成该文件的步骤", {"path": str(path)})
    return path


def read_json(path):
    try:
        value = json.loads(require_file(path).read_text(encoding="utf-8"))
    except (UnicodeError, ValueError) as error:
        raise GateError("stale_evidence", "证据 JSON 无效：" + str(path)) from error
    if not isinstance(value, dict):
        raise GateError("stale_evidence", "证据必须为 JSON 对象：" + str(path))
    return value


def write_report(path, payload):
    """同目录暂存后替换。读者只能看到上次或本次完整记录。"""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name("." + path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        for attempt in range(5):
            try:
                os.replace(temp, path)
                break
            except PermissionError:
                if attempt == 4:
                    raise
                time.sleep(0.025 * (attempt + 1))
    finally:
        temp.unlink(missing_ok=True)


@contextmanager
def workspace_lock(build):
    """操作系统持有锁。退出或崩溃均释放句柄，锁文件本身长期保留。"""
    path = Path(build) / "gate.lock"
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a+b") as stream:
        stream.seek(0)
        try:
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as error:
            if error.errno not in (errno.EACCES, errno.EAGAIN, errno.EDEADLK):
                raise
            raise GateError("duplicate_invocation", "工作区已有门禁或发布正在执行",
                            "等待该进程结束后重试", {"lock": str(path)}) from error
        yield


def read_tail(path):
    with path.open("rb") as stream:
        size = stream.seek(0, os.SEEK_END)
        stream.seek(max(0, size - TAIL_BYTES))
        data = stream.read(TAIL_BYTES)
    clipped = size > TAIL_BYTES
    for encoding in ("utf-8", "gbk", "latin-1"):
        if encoding == "utf-8" and clipped:
            data_for_decode = data.lstrip(bytes(range(0x80, 0xC0)))
        else:
            data_for_decode = data
        try:
            decoded = data_for_decode.decode(encoding)
        except UnicodeDecodeError:
            continue
        lines = decoded.splitlines()
        tail = "\n".join(lines[-DEFAULT_TAIL_LINES:]).encode("utf-8")
        return {"tail": tail[-TAIL_BYTES:].decode("utf-8", errors="ignore"), "encoding": encoding,
                "bytes": size, "truncated": clipped or len(lines) > DEFAULT_TAIL_LINES or len(tail) > TAIL_BYTES}


def run(argv, *, cwd, directory, timeout=3600):
    """完整字节流直接写文件。报告中的文本只用于快速查看。"""
    if not isinstance(argv, (tuple, list)) or not argv or not all(isinstance(a, (str, os.PathLike)) for a in argv):
        raise GateError("argument_error", "argv 必须是非空的字符串或路径列表",
                        "逐项传参，保留参数内部的空格")
    argv = [str(a) for a in argv]
    directory = Path(directory)
    directory.mkdir(parents=True)
    out = directory / "stdout.log"
    err = directory / "stderr.log"
    result = {"argv": argv, "cwd": str(cwd), "started_at": now(),
              "stdout_log": str(out), "stderr_log": str(err), "exit_code": None,
              "status": "running"}
    record = directory / "step.json"
    write_report(record, result)
    started = time.monotonic()
    failure = None
    try:
        with out.open("xb") as stdout, err.open("xb") as stderr:
            child_env = dict(os.environ, PYTHONUNBUFFERED="1", PYTHONIOENCODING="utf-8")
            child_env.pop("PYTHONOPTIMIZE", None)
            completed = subprocess.run(argv, cwd=cwd, stdout=stdout, stderr=stderr, timeout=timeout, env=child_env)
            result["exit_code"] = completed.returncode
        result["status"] = "passed" if completed.returncode == 0 else "failed"
    except subprocess.TimeoutExpired:
        result["status"] = "timed_out"
        failure = ("subprocess_exit", f"步骤超过 {timeout} 秒", "查看超时前已写出的日志")
    except OSError as error:
        result["status"] = "failed"
        failure = ("preflight_environment", f"无法运行 {argv[0]}（cwd={cwd}）：{error}", "检查工具、路径和权限")
    finally:
        result.update(finished_at=now(), duration_s=round(time.monotonic() - started, 3))
        for key, path in (("stdout", out), ("stderr", err)):
            if path.is_file():
                result[key] = read_tail(path)
        write_report(record, result)
    if failure:
        raise GateError(*failure, detail=result)
    return result
