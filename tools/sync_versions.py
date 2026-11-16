"""从唯一版本配置生成C#常量和当前HTML教程；历史说明不参与替换。"""
from pathlib import Path
from version import VERSION, VERSION_CODE, sync_csharp_version

ROOT = Path(__file__).resolve().parents[1]


def main():
    sync_csharp_version()
    templates = ROOT / "docs/templates"
    values = {"@VERSION@": VERSION, "@VERSION_CODE@": VERSION_CODE}
    def render(name):
        result = (templates / name).read_text(encoding="utf-8")
        for token, value in values.items():
            result = result.replace(token, value)
        return result
    usage, editor = render("usage.html.in"), render("editor.html.in")
    standalone = editor.replace('<p><a href="地图编辑器说明.html">地图编辑器说明</a> · <a href="底盘接入说明.md">底盘接入接口</a></p>', '')
    for name, body in {
        "docs/使用说明.html": usage, "suite/docs/使用说明.html": usage,
        "docs/地图编辑器说明.html": editor, "suite/docs/地图编辑器说明.html": editor,
        "map_editor/docs/使用说明.html": standalone,
    }.items():
        path = ROOT / name
        if not path.exists() or path.read_text(encoding="utf-8") != body:
            path.write_text(body, encoding="utf-8")
    print("PASS synchronized version metadata and current tutorials: " + VERSION)


if __name__ == "__main__":
    main()
