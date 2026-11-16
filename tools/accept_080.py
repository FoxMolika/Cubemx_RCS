"""保留 0.8 发布入口；检查清单统一由 check.py 维护，历史证据仍严格核验。"""
import argparse
import sys
from check import run_checks


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--from", dest="start_from", help="从原有脚本名称继续验收")
    args = parser.parse_args()
    try:
        run_checks("release", args.start_from)
    except (RuntimeError, FileNotFoundError, ValueError) as error:
        parser.exit(2, str(error) + "\n")


if __name__ == "__main__":
    main()
