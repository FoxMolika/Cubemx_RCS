"""用真实 CSC 验证模板的内容变化进入资源和生成结果。"""
import os
from pathlib import Path
import subprocess
from tempfile import TemporaryDirectory

from build_support import ROOT, compiler_inputs, csharp_arguments, run


def main():
    with TemporaryDirectory(prefix="template-input-", dir=ROOT / "build") as temp:
        area = Path(temp)
        source = area / "Render.cs"
        template = area / "sample.tpl"
        exe = area / "Render.exe"
        resource = "assets/templates/sample.tpl"
        source.write_text(
            'using System; using System.IO; using System.Reflection; '
            'class Render { static void Main() { using (var r = new StreamReader('
            'Assembly.GetExecutingAssembly().GetManifestResourceStream("assets/templates/sample.tpl"))) '
            'Console.Write(r.ReadToEnd().Replace("@VALUE@", "42")); } }',
            encoding="utf-8")
        resources = {resource: template}
        template.write_bytes(b"value=@VALUE@")
        args = csharp_arguments(exe, [source], resources=resources)
        first = compiler_inputs(args, resources)
        os.utime(template, (1_700_000_000, 1_700_000_000))
        assert compiler_inputs(args, resources) == first
        run(args)
        assert subprocess.check_output([str(exe)], cwd=area) == b"value=42"
        template.write_bytes(b"changed=@VALUE@")
        second = compiler_inputs(args, resources)
        assert second["inputs"][str(template.resolve())] != first["inputs"][str(template.resolve())]
        run(args)
        assert subprocess.check_output([str(exe)], cwd=area) == b"changed=42"
    print("PASS actual CSC: template-only change reaches embedded resource and executable output")


if __name__ == "__main__":
    main()
