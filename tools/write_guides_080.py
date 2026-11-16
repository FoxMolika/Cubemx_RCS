"""旧命令兼容入口；版本与教程统一由sync_versions生成。"""
from pathlib import Path
from sync_versions import main as sync_versions

ROOT = Path(__file__).resolve().parents[1]


def main():
    sync_versions()
    source = (ROOT / "docs/接入说明.md").read_bytes()
    for folder in ("docs", "suite/docs", "map_editor/docs"):
        target = ROOT / folder / "底盘接入说明.md"
        if not target.exists() or target.read_bytes() != source:
            target.write_bytes(source)


if __name__ == "__main__":
    main()
