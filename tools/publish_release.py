"""显式发布命令：先核对门禁/远端提交，草稿上传校验后才公开；不覆盖既有附件。"""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess
import urllib.error
import urllib.parse
import urllib.request

from version import VERSION, release_asset_names

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = "FoxMolika/Cubemx_RCS"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", action="store_true", help="明确要求创建并公开当前版本Release")
    args = parser.parse_args()
    dist = ROOT / "dist"
    report = json.loads((dist / ("发布核验_" + VERSION + ".json")).read_text(encoding="utf-8"))
    assert report["passed"] and report["version"] == VERSION
    assets = list(release_asset_names()) + ["SHA256SUMS.txt"]
    checksums = {}
    for line in (dist / "SHA256SUMS.txt").read_text(encoding="utf-8").splitlines():
        digest, name = line.split("  ", 1)
        assert name in release_asset_names() and sha(dist / name) == digest, name
        checksums[name] = digest
    assert set(checksums) == set(release_asset_names())
    assert report["installer_sha256"] == checksums[release_asset_names()[0]]
    native = json.loads((ROOT / "build/check-release.json").read_text(encoding="utf-8"))
    from check import steps_for
    assert native["passed"] and native["completed"] == [list(step) for step in steps_for("release")], "完整release门禁尚未通过"
    assert native["continued_from"] is None or native.get("verified_prior_prefix"), "缺少续跑前序证据"
    if not args.publish:
        print("PASS local release preflight; use --publish only after publication is authorized")
        return

    # 复用用户已有Git凭据，只存内存；不写令牌、不打印凭据或请求头。
    result = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n",
                            text=True, capture_output=True, check=True)
    credential = dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)
    token = credential["password"]
    base = "https://api.github.com/repos/" + REPOSITORY

    def api(path, method="GET", data=None, binary=False):
        url = path if path.startswith("https://") else base + path
        headers = {"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
                   "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "foxmolika-release"}
        if data is not None:
            data = data if binary else json.dumps(data, ensure_ascii=False).encode("utf-8")
            headers["Content-Type"] = "application/octet-stream" if binary else "application/json"
        request = urllib.request.Request(url, data=data, headers=headers, method=method)
        with urllib.request.urlopen(request, timeout=120) as response:
            return json.load(response)

    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, capture_output=True,
                          text=True, check=True).stdout.strip()
    assert api("/git/ref/heads/main")["object"]["sha"] == head, "先推送并核对main提交"
    old = api("/releases/tags/v0.8.0")
    old_assets = {(a["id"], a["name"], a["size"], a["digest"]) for a in old["assets"]}
    tag = "v" + VERSION
    notes = (ROOT / ("变更说明_" + VERSION + ".md")).read_text(encoding="utf-8")
    notes += ("\n验证：主机回归、确定性文件故障注入、Medium非管理员安装、"
              "CubeMX6.18.1原生生成/再生成、Keil ARM6.22编译均通过（0错误0警告）。"
              "完整版本哈希见附件SHA256SUMS和发布核验JSON。未烧录或实车验证。\n")
    try:
        release = api("/releases/tags/" + tag)
        assert release["draft"], "此版本已发布，拒绝覆盖；请检查远端资产"
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
        # 尚未公开的草稿按tag可能查不到，先枚举并复用本版本草稿。
        candidates = [r for r in api("/releases") if r["tag_name"] == tag]
        assert len(candidates) <= 1
        release = candidates[0] if candidates else api("/releases", "POST", {
            "tag_name": tag, "target_commitish": head, "name": "foxmolika " + VERSION,
            "body": notes, "draft": True, "prerelease": False})
        assert release["draft"]
    assert release["target_commitish"] == head, "草稿并非本次受验收的源码提交"
    current = {a["name"]: a for a in api("/releases/" + str(release["id"]) + "/assets")}
    uploaded = []
    for name in assets:
        digest = sha(dist / name)
        asset = current.get(name)
        if asset is None:
            endpoint = release["upload_url"].split("{", 1)[0] + "?name=" + urllib.parse.quote(name)
            asset = api(endpoint, "POST", (dist / name).read_bytes(), binary=True)
        assert asset["name"] == name and asset["size"] == (dist / name).stat().st_size and asset["digest"] == "sha256:" + digest, name
        uploaded.append({"name": name, "sha256": digest, "size": asset["size"]})
        print("PASS uploaded/verified " + name, flush=True)
    assert old_assets == {(a["id"], a["name"], a["size"], a["digest"])
                          for a in api("/releases/" + str(old["id"]))["assets"]}, "旧版附件发生变化"
    published = api("/releases/" + str(release["id"]), "PATCH", {"draft": False})
    assert not published["draft"] and published["tag_name"] == tag
    receipt = {"version": VERSION, "commit": head, "url": published["html_url"],
               "assets": uploaded, "old_v080_assets_unchanged": True}
    (ROOT / "build/github-publication.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2),
                                                       encoding="utf-8")
    print("PASS published " + published["html_url"])


if __name__ == "__main__":
    main()
