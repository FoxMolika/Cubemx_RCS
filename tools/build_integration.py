"""两个前端共用同一个生成后助手，避免工程关联工具内容不一致。"""
import hashlib
from build_support import ROOT, XML_REFERENCES, csharp_arguments, run
from version import sync_csharp_version


def post_helper():
    area = ROOT / "build"
    area.mkdir(exist_ok=True)
    sources = [ROOT / "keil_headers" / name for name in (
        "KeilHeaders.cs", "ProjectBinding.cs", "HardwareBinding.cs",
    )]
    exe = area / "foxmolika_PostGenerate.exe"
    stamp = area / "post-source.sha256"
    args = csharp_arguments(
        exe, sources, references=XML_REFERENCES, defines=("RCS_POSTGEN",),
    )
    # 编译选项和源码一起参与缓存键；引用或宏变化时必须重新编译。
    digest = hashlib.sha256(
        b"".join(path.read_bytes() for path in sources + [sync_csharp_version()])
        + "\n".join(map(str, args)).encode("utf-8")
    ).hexdigest()
    if not exe.is_file() or not stamp.is_file() or stamp.read_text() != digest:
        run(args)
        stamp.write_text(digest, encoding="ascii")
    return exe
