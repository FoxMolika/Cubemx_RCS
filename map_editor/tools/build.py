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
from version import VERSION, EDITOR_SETUP_NAME
from windows_integrity import medium
from sync_versions import main as sync_versions


def editor_guide(dist):
    # beta统一入口必须包含App/main/状态机说明，不能继续嵌入旧版教程。
    beta = ROOT.parent / "docs/0.8.4使用说明.html"
    guide = (beta if beta.exists() else ROOT / "docs/使用说明.html").read_text(encoding="utf-8")
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
    sync_versions()
    dist = ROOT / "dist"
    dist.mkdir(exist_ok=True)
    guide = editor_guide(dist)
    resources = {"payload/foxmolika_PostGenerate.exe": post_helper()}
    resources.update({
        "assets/" + name: ROOT / "firmware" / name
        for name in ("rcs_nav.c", "rcs_nav.h", "rcs_terrain.c", "rcs_terrain.h",
                     # 应用层静态模块：无地图数据，随生成器一起分发。
                     "rcs_feedback_port.c", "rcs_feedback_port.h",
                     "rcs_can_port.c", "rcs_can_port.h",
                     "rcs_app_config.h", "rcs_feedback_config.h", "rcs_observer_config.h")
    })
    resources["assets/使用说明.html"] = guide
    # 子目录也按相对路径嵌入，模板代码可按职责分组而不丢失资源。
    resources.update({
        "assets/templates/" + path.relative_to(ROOT / "templates").as_posix(): path
        for path in sorted((ROOT / "templates").rglob("*.tpl"))
    })
    resources.update({
        "assets/hardware/" + path.relative_to(ROOT / "hardware").as_posix(): path
        for path in sorted((ROOT / "hardware").rglob("*")) if path.is_file()
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
    setup = dist / EDITOR_SETUP_NAME
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
