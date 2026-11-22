"""两个前端共用同一个生成后助手，避免工程关联工具内容不一致。"""
from build_support import ROOT, XML_REFERENCES, csharp_arguments, compiler_inputs, run
from gate_diagnostics import GateError, file_sha, read_json, write_report


def post_helper():
    area = ROOT / "build"
    area.mkdir(exist_ok=True)
    sources = [ROOT / "keil_headers" / name for name in (
        "KeilHeaders.cs", "ProjectBinding.cs", "HardwareBinding.cs",
    )]
    exe = area / "foxmolika_PostGenerate.exe"
    stamp = area / "build-inputs/post-helper.json"
    args = csharp_arguments(
        exe, sources, references=XML_REFERENCES, defines=("RCS_POSTGEN",),
    )
    inputs = compiler_inputs(args)
    try:
        prior = read_json(stamp)
    except GateError:
        prior = {}
    if exe.is_file() and prior == {**inputs, "output_sha256": file_sha(exe)}:
        return exe
    run(args)
    if compiler_inputs(args) != inputs:
        raise GateError("source_changed", "生成后助手编译时输入发生变化")
    write_report(stamp, {**inputs, "output_sha256": file_sha(exe)})
    return exe
