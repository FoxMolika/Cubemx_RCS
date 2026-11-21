"""把本地beta教程编为自包含HTML；仅使用标准库，安装后离线可读。"""
from pathlib import Path
import base64
import html
import re

ROOT = Path(__file__).resolve().parents[1]


def inline(text):
    return re.sub(r"`([^`]+)`", r"<code>\1</code>", html.escape(text))


def main():
    source = ROOT / "docs/0.8.4.beta使用说明.md"
    blocks, paragraph, code = [], [], None
    def flush():
        if paragraph:
            blocks.append("<p>" + inline(" ".join(paragraph)) + "</p>")
            paragraph.clear()
    for line in source.read_text(encoding="utf-8").splitlines():
        if line.startswith("```"):
            flush()
            if code is None:
                code = []
            else:
                blocks.append("<pre><code>" + html.escape("\n".join(code)) + "</code></pre>")
                code = None
        elif code is not None:
            code.append(line)
        elif line.startswith("#"):
            flush()
            level = len(line) - len(line.lstrip("#"))
            blocks.append("<h{0}>{1}</h{0}>".format(level, inline(line[level:].strip())))
        elif line.startswith("- "):
            flush()
            blocks.append("<ul><li>" + inline(line[2:]) + "</li></ul>")
        elif line in {"<!--HARDWARE_IMAGE-->", "<!--TEMPLATE_IMAGE-->"}:
            flush()
            image = ROOT / "build/hardware-ui" / ("manager.png" if "HARDWARE" in line else "template.png")
            if image.exists():
                data = base64.b64encode(image.read_bytes()).decode("ascii")
                blocks.append('<figure><img alt="硬件管理与模板创建界面" src="data:image/png;base64,' + data + '"></figure>')
        elif line == "<!--APPLICATION_IMAGE-->":
            flush()
            image = ROOT / "build/ui/application.png"
            if image.exists():
                data = base64.b64encode(image.read_bytes()).decode("ascii")
                blocks.append('<figure><img alt="应用与状态机编辑界面" src="data:image/png;base64,' + data + '"><figcaption>应用启动参数、状态列表和用户代码分开编辑。</figcaption></figure>')
        elif not line.strip():
            flush()
        else:
            paragraph.append(line)
    flush()
    assert code is None, "未闭合代码块"
    page = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>foxmolika 0.8.4.beta 使用说明</title>
<style>body{margin:auto;max-width:980px;padding:32px;font:16px/1.8 "Microsoft YaHei",sans-serif;color:#24334a;background:#f5f7fa}h1,h2{line-height:1.4;color:#143a63}h2{margin-top:42px;border-top:1px solid #ccd8e5;padding-top:22px}pre{overflow:auto;padding:22px;background:#14263c;color:#eff7ff;border-radius:8px;font:14px/1.7 Consolas,monospace}p,li{max-width:88ch}ul{margin:6px 0}code{font-family:Consolas,monospace}img{width:100%;height:auto;border:1px solid #c5d2e3}figure{margin:24px 0}figcaption{font-size:14px;color:#51657a}@media print{body{background:white;padding:0;font-size:11pt}pre{white-space:pre-wrap;color:black;background:#eef1f4}h2{break-after:avoid}img{max-height:180mm;object-fit:contain}}</style>
<body><main>''' + "\n".join(blocks) + "</main></body></html>\n"
    target = source.with_suffix(".html")
    target.write_text(page, encoding="utf-8")
    print(target)


if __name__ == "__main__":
    main()
