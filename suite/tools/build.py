"""构建一体安装器：内嵌已经生成的包、编辑器和共享接入示例。"""
from pathlib import Path
from xml.etree import ElementTree as E
import hashlib
import os
import shutil
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT.parent
sys.path.insert(0, str(PROJECT / "tools"))
from build_integration import post_helper
from build_support import XML_REFERENCES, compile_csharp, common_editor_payload, run
from version import VERSION, ASSEMBLY_VERSION, sync_csharp_version
from windows_integrity import medium


def sha(data):
    return hashlib.sha256(data).hexdigest()


def check_version():
    """阻断套件、软件包、程序集和安装清单之间的版本漂移。"""
    assert (ROOT / "docs/使用说明.html").read_bytes() == (
        PROJECT / "docs/使用说明.html"
    ).read_bytes(), "Suite and pack tutorials must stay synchronized"
    sync_csharp_version()
    source = (ROOT / "src/PackService.cs").read_text(encoding="utf-8")
    assert "BuildVersion.Version" in source, "PackService must use generated version"
    for path in (ROOT / "src/Setup.cs", PROJECT / "keil_headers/KeilHeaders.cs"):
        text = path.read_text(encoding="utf-8")
        assert "AssemblyVersion(BuildVersion.AssemblyVersion)" in text, path
        assert "AssemblyFileVersion(BuildVersion.AssemblyVersion)" in text, path


def setup_manifest(build):
    """保留权限清单模板，版本注入到build后校验；不改写仓内源文件。"""
    template = (ROOT / "setup.manifest").read_text(encoding="utf-8")
    assert template.count("@ASSEMBLY_VERSION@") == 1, "Setup manifest must use the version token"
    path = build / "setup.manifest"
    path.write_text(template.replace("@ASSEMBLY_VERSION@", ASSEMBLY_VERSION), encoding="utf-8")
    identity = E.parse(path).find("{urn:schemas-microsoft-com:asm.v1}assemblyIdentity")
    assert identity is not None and identity.attrib["version"] == ASSEMBLY_VERSION
    return path



def build_helpers(build):
    """原生验收的 Java 配置隔离器和两个工程工具；不修改 CubeMX 安装目录。"""
    cube = Path(os.environ.get(
        "STM32CubeMX_PATH", str(Path.home() / "AppData/Local/Programs/STM32CubeMX")
    ))
    run([
        cube / "jre/bin/javac.exe", "--release", "8", "-d", build / "java",
        PROJECT / "tools/WorkspacePreferencesFactory.java",
    ])
    keil = build / "foxmolika_KeilHeaders.exe"
    compile_csharp(keil, [
        PROJECT / "keil_headers/KeilHeaders.cs",
        PROJECT / "keil_headers/ProjectBinding.cs",
    ], references=XML_REFERENCES)
    post = build / "foxmolika_PostGenerate.exe"
    shutil.copyfile(post_helper(), post)
    setup = build / "foxmolika_ProjectSetup.exe"
    compile_csharp(setup, [
        PROJECT / "keil_headers/ProjectSetup.cs",
        PROJECT / "keil_headers/ProjectBinding.cs",
    ], target="winexe", references=XML_REFERENCES + ("System.Windows.Forms.dll",),
        resources={"payload/foxmolika_PostGenerate.exe": post})
    return keil, setup


def write_manifest(build, payload, pack, packdata):
    """清单哈希针对最终要嵌入的字节，安装与卸载据此识别归属。"""
    manifest = E.Element("Suite", Version=VERSION, PackSha256=sha(pack.read_bytes()))
    app = E.SubElement(manifest, "AppFiles")
    files = E.SubElement(manifest, "PackFiles")
    for name, path in payload.items():
        E.SubElement(app, "File", Name=name, Sha256=sha(path.read_bytes()))
    for name, data in sorted(packdata.items()):
        E.SubElement(files, "File", Name=name, Sha256=sha(data))
    E.indent(manifest)
    path = build / "manifest.xml"
    E.ElementTree(manifest).write(path, encoding="utf-8", xml_declaration=True)
    return path


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    build, dist = ROOT / "build", ROOT / "dist"
    build.mkdir(exist_ok=True)
    dist.mkdir(exist_ok=True)
    check_version()
    editor = PROJECT / "map_editor/dist/foxmolika_MapEditor.exe"
    if not editor.is_file():
        raise RuntimeError("先运行 python map_editor/tools/build.py")
    # 内嵌 tools/build_pack.py 的原始产物，禁止在此重新生成另一份包。
    pack = PROJECT / "dist" / ("RCS.foxmolika." + VERSION + ".pack")
    if not pack.is_file():
        raise RuntimeError("先运行 python tools/build_pack.py；缺少 " + str(pack))
    with zipfile.ZipFile(pack) as archive:
        packdata = {name: archive.read(name) for name in archive.namelist()}
    keil, project_setup = build_helpers(build)
    payload = common_editor_payload()
    payload.update({
        "foxmolika_ProjectSetup.exe": project_setup,
        "foxmolika_MapEditor.exe": editor,
        "RCS.foxmolika." + VERSION + ".pack": pack,
        "foxmolika_KeilHeaders.exe": keil,
        "使用说明.html": ROOT / "docs/使用说明.html",
        "地图编辑器说明.html": PROJECT / "map_editor/dist/使用说明.html",
        "底盘接入说明.md": PROJECT / "docs/接入说明.md",
        "LICENSE.txt": PROJECT / "LICENSE.txt",
        "坡道与形状编辑.md": PROJECT / "docs/坡道与形状编辑.md",
        "定位硬件接入.md": PROJECT / "docs/定位硬件接入.md",
        "validation/WorkspacePreferencesFactory.class": build / "java/WorkspacePreferencesFactory.class",
        "validation/WorkspacePreferencesFactory$MemoryPreferences.class": build / "java/WorkspacePreferencesFactory$MemoryPreferences.class",
    })
    payload["manifest.xml"] = write_manifest(build, payload, pack, packdata)
    setup = dist / ("foxmolika_CubeMX_Setup_" + VERSION + "_win-x64.exe")
    log = compile_csharp(
        setup, sorted((ROOT / "src").glob("*.cs")), target="winexe",
        manifest=setup_manifest(build),
        references=("System.Windows.Forms.dll", "System.Drawing.dll", "System.Xml.Linq.dll", "System.Core.dll"),
        resources={"payload/" + name: path for name, path in payload.items()},
    )
    (build / "compile.log").write_text(log or "Build succeeded without warnings.\n", encoding="utf-8")
    medium(setup)
    shutil.copyfile(ROOT / "docs/使用说明.html", dist / "使用说明.html")
    (dist / "SHA256SUMS.txt").write_text(sha(setup.read_bytes()) + "  " + setup.name + "\n", encoding="utf-8")
    print(setup)
    print("bytes:", setup.stat().st_size)


if __name__ == "__main__":
    main()
