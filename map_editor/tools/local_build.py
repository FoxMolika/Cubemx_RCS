"""本地 0.8.3 构建：复用仓库的编译参数与内嵌资源，输出到 build/local-0.8.3。

不写 dist：dist 里已有的 exe 是更高完整性级别的文件，受限进程无法覆盖。
发布流程仍应使用 map_editor/tools/build.py。
"""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT.parent / "tools"))
sys.path.insert(0, str(ROOT / "tools"))

from build_support import compile_csharp  # noqa: E402
from build_integration import post_helper  # noqa: E402
from version import VERSION  # noqa: E402

out_dir = ROOT / "build" / ("local-" + VERSION)
out_dir.mkdir(parents=True, exist_ok=True)
exe = out_dir / "foxmolika_MapEditor.exe"

guide = ROOT / "docs" / "使用说明.html"
if not guide.is_file():
    raise SystemExit("缺少 docs/使用说明.html")

resources = {"payload/foxmolika_PostGenerate.exe": post_helper()}
resources.update({
    "assets/" + name: ROOT / "firmware" / name
    for name in ("rcs_nav.c", "rcs_nav.h", "rcs_terrain.c", "rcs_terrain.h",
                 "rcs_feedback_port.c", "rcs_feedback_port.h",
                 "rcs_can_port.c", "rcs_can_port.h",
                 "rcs_app_config.h", "rcs_feedback_config.h", "rcs_observer_config.h")
})
resources["assets/使用说明.html"] = guide
resources.update({
    "assets/templates/" + path.relative_to(ROOT / "templates").as_posix(): path
    for path in sorted((ROOT / "templates").rglob("*.tpl"))
})

sources = sorted((ROOT / "src").glob("*.cs")) + [
    ROOT.parent / "keil_headers" / name
    for name in ("ProjectSetup.cs", "KeilHeaders.cs", "ProjectBinding.cs")
]

print(compile_csharp(
    exe, sources, target="winexe", defines=("RCS_MAP_EDITOR",),
    references=(
        "System.Xml.Linq.dll", "System.Xml.dll", "System.Windows.Forms.dll",
        "System.Drawing.dll", "System.Core.dll", "System.IO.Compression.dll",
        "System.IO.Compression.FileSystem.dll", "System.Runtime.Serialization.dll",
    ), resources=resources,
))
print("OUT", exe, exe.stat().st_size)

# 控制台版：同一套源码与资源，target=exe，用来在没有桌面/无法启动 winexe 时看自测输出。
console = out_dir / "foxmolika_MapEditor_console.exe"
print(compile_csharp(
    console, sources, target="exe", defines=("RCS_MAP_EDITOR",),
    references=(
        "System.Xml.Linq.dll", "System.Xml.dll", "System.Windows.Forms.dll",
        "System.Drawing.dll", "System.Core.dll", "System.IO.Compression.dll",
        "System.IO.Compression.FileSystem.dll", "System.Runtime.Serialization.dll",
    ), resources=resources,
))
print("OUT", console, console.stat().st_size)