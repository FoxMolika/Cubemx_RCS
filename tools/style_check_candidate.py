"""对生成代码做队内风格检查；不依赖 clang-format，可在任意 Python 环境运行。"""
import re
import sys
from pathlib import Path

FILES = [
    "rcs_chassis.h",
    "rcs_chassis.c",
    "rcs_control.h",
    "rcs_control.c",
    "rcs_hal.h",
    "rcs_hal.c",
]
MAX_LINE = 110
FUNC_DECL = re.compile(
    r"^(?:unsigned|int|void|float|rcs_state|rcs_fault)\s+(?:rcs_|RCS_)\w+\s*\("
)
FUNC_STATIC = re.compile(
    r"^static\s+(?:unsigned|int|void|float|rcs_state|rcs_fault)\s+\w+\s*\("
)


def _code_part(line):
    """去掉行尾注释和字符串，返回用于统计分号的代码部分。"""
    line = re.sub(r'"(?:\\.|[^"\\])*"', '""', line)
    line = re.sub(r"//.*$", "", line)
    line = re.sub(r"/\*.*?\*/", "", line)
    return line


def _has_doc_before(lines, index, span=16):
    start = max(0, index - span)
    return any(lines[i].lstrip().startswith("/**") for i in range(start, index))


def check(firmware_dir):
    """返回问题描述列表；空列表表示全部通过。"""
    root = Path(firmware_dir)
    problems = []

    for name in FILES:
        path = root / name
        if not path.is_file():
            problems.append(f"{name}: 文件不存在")
            continue

        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            problems.append(f"{name}: 不是合法 UTF-8")
            continue

        if "\t" in text:
            problems.append(f"{name}: 含有 TAB，队内要求只用空格")

        if not text.startswith("/**"):
            problems.append(f"{name}: 缺少 Doxygen 文件头")

        if "@file" not in text[:400] or "@brief" not in text[:400]:
            problems.append(f"{name}: 文件头缺少 @file 或 @brief")

        lines = text.replace("\r\n", "\n").split("\n")

        for number, line in enumerate(lines, 1):
            if len(line) > MAX_LINE:
                problems.append(f"{name}:{number}: 行长 {len(line)} 超过 {MAX_LINE}")
            if line != line.rstrip():
                problems.append(f"{name}:{number}: 行尾有空白")

            code = _code_part(line)
            if code.count(";") >= 2 and "for (" not in code and not line.lstrip().startswith("#define"):
                problems.append(f"{name}:{number}: 一行多条语句")

            stripped = line.lstrip()
            if FUNC_STATIC.match(line) and not _has_doc_before(lines, number - 1):
                problems.append(f"{name}:{number}: 静态函数缺少 Doxygen 注释")

            if name.endswith(".h") and FUNC_DECL.match(line) and not _has_doc_before(lines, number - 1):
                problems.append(f"{name}:{number}: 导出函数声明缺少 Doxygen 注释")

        if name.endswith(".h"):
            if "#pragma once" not in text:
                problems.append(f"{name}: 头文件应使用 #pragma once")
            if re.search(r"^#ifndef\s+RCS_\w+_H\b", text, re.M):
                problems.append(f"{name}: 不应再使用 include guard")
            if 'extern "C"' not in text:
                problems.append(f"{name}: 头文件缺少 extern \"C\"")

        if name.endswith(".c"):
            first_include = re.search(r"^#include\s+(.+)$", text, re.M)
            expected = name[:-2] + ".h"
            if not first_include or expected not in first_include.group(1):
                problems.append(f"{name}: 第一个 include 应为 \"{expected}\"")

    return problems


def main():
    root = Path(__file__).resolve().parents[1] / "firmware"
    problems = check(root)
    if problems:
        print("FAIL 代码风格检查")
        for item in problems:
            print("  " + item)
        return 1
    print("PASS 代码风格检查：Doxygen 文件头/函数注释、每行一条语句、无 TAB、#pragma once、行长 ≤ %d" % MAX_LINE)
    return 0


if __name__ == "__main__":
    sys.exit(main())
