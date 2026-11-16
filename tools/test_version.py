"""检查真实主机产物的版本来源；套件只在显式--include-suite时纳入，不能冒充native验收。"""
from pathlib import Path
from xml.etree import ElementTree as E
import argparse
import hashlib
import importlib.util
import json
import re
import subprocess
import sys
import uuid
import zipfile

import build_pack
from build_support import ROOT, XML_REFERENCES, csharp_arguments
from version import ASSEMBLY_VERSION, PACK_NAME, SETUP_NAME, TOOL_DIRECTORY, VERSION, VERSION_CODE


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def constants(path):
    return dict(re.findall(r'internal const string (\w+) = "([^"]*)";', path.read_text(encoding="utf-8")))


def metadata_versions(data, version):
    """同时检查文件名、CubeMX模式/配置、组件与PDSC版本，防止只改压缩包外部名称。"""
    prefix = "CubeMX/RCS.foxmolika." + version
    modes = E.fromstring(data[prefix + "_Modes.xml"])
    configs = E.fromstring(data[prefix + "_Configs.xml"])
    pdsc = E.fromstring(data["RCS.foxmolika.pdsc"])
    assert modes.attrib["Version"] == version
    assert configs.attrib["Version"] == version
    assert configs.attrib["RootFolder"] == "Packs/RCS/foxmolika/" + version + "/"
    assert any(node.attrib.get("Name") == "VS_RoboticsJjfoxmolika_" + version for node in modes.iter())
    assert pdsc.find("./releases/release").attrib["version"] == version
    assert pdsc.find("./components/component").attrib["Cversion"] == version
    component_versions = [node.attrib["Cversion"] for node in configs.iter() if "Cversion" in node.attrib]
    assert component_versions == [version.replace(".", "Gg")]


def assembly_versions(area, paths):
    # AssemblyName读取元数据，不执行被测EXE，也不依赖csc重新编译测试壳。
    probe = area / "ReadAssemblyVersions.ps1"
    probe.write_text('''$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$rows = @($args | ForEach-Object {
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($_)
    [PSCustomObject]@{ Path=$_; AssemblyVersion=[System.Reflection.AssemblyName]::GetAssemblyName($_).Version.ToString(); FileVersion=$info.FileVersion }
})
ConvertTo-Json -InputObject $rows -Compress
''', encoding="utf-8")
    p = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(probe),
                        *[str(path) for path in paths.values()]], capture_output=True, timeout=30)
    assert p.returncode == 0, p.stderr.decode("utf-8", errors="replace")
    rows = json.loads(p.stdout.decode("utf-8-sig"))
    assert len(rows) == len(paths)
    result = {}
    for (name, path), row in zip(paths.items(), rows):
        assert Path(row["Path"]) == path
        assert row["AssemblyVersion"] == row["FileVersion"] == ASSEMBLY_VERSION, (name, row)
        result[name] = {"assembly_version": row["AssemblyVersion"], "file_version": row["FileVersion"],
                        "sha256": sha(path)}
    return result


def alternate_version(area):
    """隔离复制版本模块证明改一个来源会传播，不修改真实版本文件及已构建产物。"""
    source = ROOT / "tools/version.py"
    source_digest = sha(source)
    text, count = re.subn(r'(?m)^VERSION = [\'"][^\'"]+[\'"]$', 'VERSION = "9.8.7"',
                          source.read_text(encoding="utf-8"))
    assert count == 1
    isolated = area / "alternate"
    (isolated / "tools").mkdir(parents=True)
    (isolated / "keil_headers").mkdir()
    file = isolated / "tools/version.py"
    file.write_text(text, encoding="utf-8")
    spec = importlib.util.spec_from_file_location("isolated_build_version", file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    expected = {"Version": "9.8.7", "AssemblyVersion": "9.8.7.0",
                "ToolDirectory": "foxmolika_Tools_987", "ToolCode": "987"}
    generated = module.sync_csharp_version()
    assert generated.is_relative_to(isolated)
    assert constants(generated) == expected
    assert module.PACK_NAME == "RCS.foxmolika.9.8.7.pack"
    assert module.SETUP_NAME == "foxmolika_CubeMX_Setup_9.8.7_win-x64.exe"
    metadata_versions(build_pack.metadata(version=module.VERSION), module.VERSION)
    assert sha(source) == source_digest
    return expected


def main(argv=None):
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--include-suite", action="store_true", help="另核验真实套件EXE和构建后的权限清单")
    args = parser.parse_args(argv)
    area = ROOT / "build" / ("version_" + uuid.uuid4().hex[:8])
    area.mkdir(parents=True)

    version_source = ROOT / "keil_headers/BuildVersion.cs"
    expected = {"Version": VERSION, "AssemblyVersion": ASSEMBLY_VERSION,
                "ToolDirectory": TOOL_DIRECTORY, "ToolCode": VERSION_CODE}
    assert constants(version_source) == expected
    assert VERSION_CODE == VERSION.replace(".", "")
    assert ASSEMBLY_VERSION == VERSION + ".0"

    paths = {"editor": ROOT / "map_editor/dist/foxmolika_MapEditor.exe",
             "post_helper": ROOT / "build/foxmolika_PostGenerate.exe"}
    if args.include_suite:
        paths["suite"] = ROOT / "suite/dist" / SETUP_NAME
    binaries = assembly_versions(area, paths)

    metadata_versions(build_pack.metadata(), VERSION)
    pack = ROOT / "dist" / PACK_NAME
    with zipfile.ZipFile(pack) as archive:
        metadata_versions({name: archive.read(name) for name in archive.namelist()}, VERSION)
        assert {name for name in archive.namelist() if name.startswith("CubeMX/RCS.foxmolika.")} == {
            "CubeMX/RCS.foxmolika." + VERSION + "_Modes.xml",
            "CubeMX/RCS.foxmolika." + VERSION + "_Configs.xml"}

    template = (ROOT / "suite/setup.manifest").read_text(encoding="utf-8")
    assert template.count("@ASSEMBLY_VERSION@") == 1
    manifest = E.fromstring(template.replace("@ASSEMBLY_VERSION@", ASSEMBLY_VERSION))
    ns = {"a": "urn:schemas-microsoft-com:asm.v1", "t": "urn:schemas-microsoft-com:asm.v3"}
    assert manifest.find("a:assemblyIdentity", ns).attrib["version"] == ASSEMBLY_VERSION
    assert manifest.find(".//t:requestedExecutionLevel", ns).attrib["level"] == "asInvoker"
    if args.include_suite:
        built = E.parse(ROOT / "suite/build/setup.manifest")
        assert built.find("a:assemblyIdentity", ns).attrib["version"] == ASSEMBLY_VERSION

    # 与post_helper实际缓存键使用相同输入；只改版本常量必须使缓存失效。
    sources = [ROOT / "keil_headers" / name for name in ("KeilHeaders.cs", "ProjectBinding.cs")]
    output = ROOT / "build/foxmolika_PostGenerate.exe"
    compiler_args = csharp_arguments(output, sources, references=XML_REFERENCES, defines=("RCS_POSTGEN",))
    suffix = "\n".join(map(str, compiler_args)).encode("utf-8")
    prefix = b"".join(path.read_bytes() for path in sources)
    current_digest = hashlib.sha256(prefix + version_source.read_bytes() + suffix).hexdigest()
    assert (ROOT / "build/post-source.sha256").read_text().strip() == current_digest
    changed_digest = hashlib.sha256(prefix + version_source.read_bytes().replace(
        VERSION.encode("ascii"), b"9.8.7") + suffix).hexdigest()
    assert current_digest != changed_digest

    # 旧版字符串只允许存在于有意兼容0.8.0助手的白名单行及说明注释。
    current_sources = [*list((ROOT / "keil_headers").glob("*.cs")),
                       *list((ROOT / "map_editor/src").glob("*.cs")),
                       ROOT / "map_editor/installer.cs", *list((ROOT / "suite/src").glob("*.cs"))]
    for path in current_sources:
        for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if "0.8.0" not in line and "foxmolika_Tools_080" not in line:
                continue
            assert path.name == "ProjectSetup.cs" and (
                line.strip().startswith("//") or "bool from080=" in line), (path, number, line)

    alternate = alternate_version(area)
    report = {"passed": True, "version": VERSION, "assembly_version": ASSEMBLY_VERSION,
              "scope": "host metadata and real assembly versions; no native or installation claim",
              "suite_checked": args.include_suite, "binaries": binaries, "pack_sha256": sha(pack),
              "generated_constants": expected, "alternate_source": alternate,
              "post_cache_includes_generated_version_bytes": True, "manifest_as_invoker": True}
    (ROOT / "build/version_validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2),
                                                        encoding="utf-8")
    print("PASS version source/assemblies/pack metadata/manifest/cache contracts: " + VERSION +
          ("; suite assembly checked" if args.include_suite else "; suite not part of host scope"))


if __name__ == "__main__":
    main()
