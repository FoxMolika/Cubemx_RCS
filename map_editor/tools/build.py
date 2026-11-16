"""构建编辑器和独立安装器；不依赖 CubeMX，也不下载运行时。"""
from pathlib import Path
import base64
import hashlib
import shutil
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT.parent / "tools"))
from build_integration import post_helper
from build_support import compile_csharp, common_editor_payload, generate_examples
from version import VERSION
from windows_integrity import medium


def editor_guide(dist):
    guide = (ROOT / "docs/使用说明.html").read_text(encoding="utf-8")
    image = ROOT / "docs/editor_ui.png"
    if image.is_file():
        preview = (
            '<p>编辑器实际界面：上方绿色区域是工程关联入口。</p>'
            '<img alt="foxmolika地图编辑器工程关联界面" src="data:image/png;base64,'
            + base64.b64encode(image.read_bytes()).decode("ascii") + '">'
        )
        guide = guide.replace("<!--PREVIEW-->", preview)
    target = dist / "使用说明.html"
    target.write_text(guide, encoding="utf-8")
    return target


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    dist = ROOT / "dist"
    dist.mkdir(exist_ok=True)
    guide = editor_guide(dist)
    resources = {"payload/foxmolika_PostGenerate.exe": post_helper()}
    resources.update({
        "assets/" + name: ROOT / "firmware" / name
        for name in ("rcs_nav.c", "rcs_nav.h", "rcs_terrain.c", "rcs_terrain.h")
    })
    resources["assets/使用说明.html"] = guide
    # 子目录也按相对路径嵌入，模板代码可按职责分组而不丢失资源。
    resources.update({
        "assets/templates/" + path.relative_to(ROOT / "templates").as_posix(): path
        for path in sorted((ROOT / "templates").rglob("*.tpl"))
    })
    exe = dist / "foxmolika_MapEditor.exe"
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
    # EXE 必须能从普通用户目录写 AppData；仅设置自有产物的完整性标签。
    medium(exe)
    print(exe)
    generate_examples(exe)

    payload = common_editor_payload()
    payload.update({
        "foxmolika_MapEditor.exe": exe,
        "使用说明.html": guide,
        "LICENSE.txt": ROOT.parent / "LICENSE.txt",
    })
    setup = dist / ("foxmolika_MapEditor_Setup_" + VERSION + "_win-x64.exe")
    print(compile_csharp(
        setup, [ROOT / "installer.cs"], target="winexe",
        references=(
            "System.Windows.Forms.dll", "System.Drawing.dll", "System.Core.dll",
            "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll",
            "System.Xml.Linq.dll",
        ), resources={"payload/" + name: path for name, path in payload.items()},
    ))
    medium(setup)
    print(setup)
    example = dist / "示例路线.rcsmap.json"
    shutil.copyfile(payload["examples/示例路线.rcsmap.json"], example)
    (dist / "SHA256SUMS.txt").write_text("".join(
        hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.name + "\n"
        for path in (exe, setup, guide, example)
    ), encoding="utf-8")


if __name__ == "__main__":
    main()
