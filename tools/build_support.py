"""Windows 构建共用的少量操作；目标电脑运行安装器不需要 Python。"""
from pathlib import Path
import os
import subprocess
from version import sync_csharp_version
from gate_diagnostics import file_sha, write_report, GateError

ROOT = Path(__file__).resolve().parents[1]
XML_REFERENCES = ("System.Xml.Linq.dll", "System.Xml.dll", "System.Core.dll")

# 两个安装器必须携带同一套接入示例；增加示例只修改这一处。
EXAMPLE_FILES = (
    "示例路线.rcsmap.json", "立体地图示例.rcsmap.json",
    "连续通行示例.rcsmap.json", "坡道执行示例.rcsmap.json",
    "robot_route_task.c", "robot_route_task.h", "定位硬件档案示例.json",
    "foxmolika_observation_adapter.c", "foxmolika_observation_adapter.h",
    "terrain_brake_port.c", "terrain_brake_port.h",
)
GENERATED_EXAMPLES = (
    ("--demo", "示例路线.rcsmap.json"),
    ("--terrain-demo", "立体地图示例.rcsmap.json"),
    ("--continuous-demo", "连续通行示例.rcsmap.json"),
    ("--terrain-drive-demo", "坡道执行示例.rcsmap.json"),
)
GENERATED_EXAMPLE_DIRECTORY = ROOT / "map_editor/build/examples"


def csharp_compiler():
    """使用系统自带的 Framework 编译器，保持 C# 5/.NET Framework 兼容。"""
    compiler = Path(os.environ.get("WINDIR", "C:/Windows")) / (
        "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    )
    if not compiler.is_file():
        raise FileNotFoundError("缺少 .NET Framework C# 编译器：" + str(compiler))
    return compiler


def run(args, *, cwd=ROOT, timeout=300):
    """参数逐项传递，不通过 shell 拼接；失败时保留编译器原始诊断。"""
    command = [str(arg) for arg in args]
    result = subprocess.run(
        command, cwd=cwd, capture_output=True, text=True,
        errors="replace", timeout=timeout,
    )
    if result.returncode:
        raise RuntimeError(
            "命令失败（{}）：{}\n{}\n{}".format(
                result.returncode, command[0], result.stdout, result.stderr
            )
        )
    return result.stdout


def csharp_arguments(output, sources, *, target="exe", references=(),
                     defines=(), resources=None, manifest=None):
    """统一编译选项。资源字典的键是运行时代码使用的名称，不能随路径改名。"""
    args = [
        csharp_compiler(), "/nologo", "/target:" + target,
        "/platform:anycpu", "/optimize+", "/out:" + str(output),
    ]
    if defines:
        args.append("/define:" + ";".join(defines))
    if manifest is not None:
        args.append("/win32manifest:" + str(manifest))
    args.extend("/reference:" + name for name in references)
    args.extend(
        "/resource:" + str(path) + "," + name
        for name, path in (resources or {}).items()
    )
    # 所有前端及测试编译共用同一版本类，不依赖调用方手工记住额外源码。
    version_source = sync_csharp_version()
    source_files = list(sources)
    if not any(Path(path).resolve() == version_source.resolve() for path in source_files):
        source_files.append(version_source)
    # 工程登记/portable post共享硬件清单解析，不要求每个前端单独维护依赖。
    hardware_binding = ROOT / "keil_headers/HardwareBinding.cs"
    project_binding = ROOT / "keil_headers/ProjectBinding.cs"
    if hardware_binding.exists() and any(Path(p).resolve() == project_binding.resolve() for p in source_files):
        if not any(Path(p).resolve() == hardware_binding.resolve() for p in source_files):
            source_files.append(hardware_binding)
    args.extend(source_files)
    return args


def compiler_inputs(args, resources=None):
    """从实际传给 csc 的参数取得文件输入，资源名也是输入的一部分。"""
    paths = [args[0]]
    paths.extend(arg for arg in args[1:]
                 if isinstance(arg, os.PathLike) or not str(arg).startswith("/"))
    paths.extend(str(arg).split(":", 1)[1] for arg in args if str(arg).startswith("/win32manifest:"))
    # Framework 引用不变更产品的编译方式，但纳入证据。
    paths.extend(Path(args[0]).parent / str(arg).split(":", 1)[1]
                 for arg in args if str(arg).startswith("/reference:"))
    paths.extend((resources or {}).values())
    files = {str(Path(path).resolve()): file_sha(path) for path in paths}
    return {"argv": [str(arg) for arg in args], "inputs": files,
            "resources": {name: str(Path(path).resolve()) for name, path in (resources or {}).items()}}


def compile_csharp(output, sources, *, record=None, **options):
    Path(output).parent.mkdir(parents=True, exist_ok=True)
    args = csharp_arguments(output, sources, **options)
    if record is None:
        return run(args)
    inputs = compiler_inputs(args, options.get("resources"))
    log = run(args)
    if any(file_sha(path) != digest for path, digest in inputs["inputs"].items()):
        raise GateError("source_changed", "编译时输入文件发生变化：" + str(output))
    write_report(ROOT / "build/build-inputs" / (record + ".json"), {
        "schema_version": 1, **inputs, "output": str(Path(output).resolve()),
        "output_sha256": file_sha(output),
    })
    return log


def common_editor_payload():
    """返回独立编辑器和完整套件共用的资源，避免示例漏装或两处版本分叉。"""
    payload = {
        "examples/" + name: (
            GENERATED_EXAMPLE_DIRECTORY if name in {item[1] for item in GENERATED_EXAMPLES}
            else ROOT / "map_editor/examples"
        ) / name
        for name in EXAMPLE_FILES
    }
    payload.update({
        "sdk/" + name: ROOT / "firmware" / name
        for name in ("rcs_chassis.c", "rcs_chassis.h")
    })
    return payload


def generate_examples(editor):
    """用新编辑器生成打包示例到build；随机航点ID不能污染版本控制中的参考地图。"""
    GENERATED_EXAMPLE_DIRECTORY.mkdir(parents=True, exist_ok=True)
    for flag, name in GENERATED_EXAMPLES:
        run([editor, flag, GENERATED_EXAMPLE_DIRECTORY / name])
