"""当前构建版本的唯一来源；历史夹具与迁移白名单保留各自发布版本。"""
VERSION = "0.8.1"
VERSION_CODE = "".join(VERSION.split("."))
ASSEMBLY_VERSION = VERSION + ".0"
RELEASE_DATE = "2026-11-16"
TOOL_DIRECTORY = "foxmolika_Tools_" + VERSION_CODE
PACK_NAME = "RCS.foxmolika." + VERSION + ".pack"
SETUP_NAME = "foxmolika_CubeMX_Setup_" + VERSION + "_win-x64.exe"
EDITOR_SETUP_NAME = "foxmolika_MapEditor_Setup_" + VERSION + "_win-x64.exe"


def sync_csharp_version():
    """构建前生成共享C#常量；版本只在本文件定义，生成文件不手工维护。"""
    from pathlib import Path
    root = Path(__file__).resolve().parents[1]
    target = root / "keil_headers/BuildVersion.cs"
    text = """// 自动生成自tools/version.py；请勿直接修改。\ninternal static class BuildVersion\n{{\n    internal const string Version = \"{version}\";\n    internal const string AssemblyVersion = \"{assembly}\";\n    internal const string ToolDirectory = \"{directory}\";\n    internal const string ToolCode = \"{code}\";\n}}\n""".format(version=VERSION, assembly=ASSEMBLY_VERSION, directory=TOOL_DIRECTORY, code=VERSION_CODE)
    if not target.exists() or target.read_text(encoding="utf-8") != text:
        target.write_text(text, encoding="utf-8")
    return target


def release_asset_names():
    """同一清单供本地校验、SHA256与GitHub上传使用，只包含当前版本。"""
    return (
        SETUP_NAME, PACK_NAME,
        "foxmolika_CubeMX_Source_" + VERSION + ".zip",
        "foxmolika_CubeMX_Installer_" + VERSION + ".zip",
        "RCS.foxmolika.C_H." + VERSION + ".zip",
        "foxmolika_Validation_" + VERSION + ".json",
        "foxmolika_Changes_" + VERSION + ".md",
    )
