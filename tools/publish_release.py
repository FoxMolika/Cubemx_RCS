"""显式发布：逐阶段复核干净标签源码、远端标签和资产；既有公开版本不覆盖。"""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess
import urllib.error
import urllib.parse
import urllib.request

from audit_release import local_preflight
from provenance import assert_current
from version import VERSION, release_asset_names

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = "FoxMolika/Cubemx_RCS"
PRESERVED_TAGS = ("v0.8.0", "v0.8.1")


def remote_tag(api, tag):
    """解析轻量/附注标签到真实 commit；target_commitish 只是提示，不是来源证据。"""
    reference = api("/git/ref/tags/" + urllib.parse.quote(tag, safe=""))
    item = reference["object"]
    reference_sha = item["sha"]
    seen = set()
    while item["type"] == "tag":
        if item["sha"] in seen or len(seen) >= 16:
            raise RuntimeError("远端标签链无效：" + tag)
        seen.add(item["sha"])
        item = api("/git/tags/" + item["sha"])["object"]
    if item["type"] != "commit":
        raise RuntimeError("远端版本标签未指向提交：" + tag)
    return {"reference_sha": reference_sha, "commit": item["sha"]}


def paginated(api, path):
    result = []
    page = 1
    while True:
        batch = api(path + ("&" if "?" in path else "?") + "per_page=100&page=" + str(page))
        result.extend(batch)
        if len(batch) < 100:
            return result
        page += 1


def asset_identity(asset):
    return {key: asset[key] for key in ("id", "name", "size", "digest")}


def preserved_releases(api):
    """保存旧版 tag SHA 与完整资产身份；本程序不调用任何旧版修改接口。"""
    result = {}
    for tag in PRESERVED_TAGS:
        release = api("/releases/tags/" + tag)
        if release["draft"] or release["tag_name"] != tag:
            raise RuntimeError("历史公开版本状态异常：" + tag)
        assets = paginated(api, "/releases/" + str(release["id"]) + "/assets")
        result[tag] = {"tag": remote_tag(api, tag), "release_id": release["id"],
                       "assets": sorted((asset_identity(a) for a in assets), key=lambda a: a["id"])}
    return result


def verify_remote_tag(api, record):
    found = remote_tag(api, record["tag"])
    if found["commit"] != record["commit"]:
        raise RuntimeError("远端版本标签与验收提交不一致；请先推送正确标签。")
    if api("/git/commits/" + found["commit"])["tree"]["sha"] != record["tree"]:
        raise RuntimeError("远端源码树与本次验收不一致。")
    return found


def verify_asset(asset, name, digest, path):
    if asset["name"] != name or asset["size"] != path.stat().st_size or asset["digest"] != "sha256:" + digest:
        raise RuntimeError("远端资产名称、大小或摘要不一致：" + name)


def verify_remote_assets(api, release, checksums, dist):
    assets = paginated(api, "/releases/" + str(release["id"]) + "/assets")
    if len(assets) != len(checksums) or {a["name"] for a in assets} != set(checksums):
        raise RuntimeError("远端资产集合与规范清单不一致，未删除或覆盖任何附件。")
    for asset in assets:
        verify_asset(asset, asset["name"], checksums[asset["name"]], dist / asset["name"])
    return assets


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", action="store_true", help="明确要求创建并公开当前版本 Release")
    args = parser.parse_args()
    record, checksums = local_preflight()
    if not args.publish:
        print("PASS local release preflight; --publish additionally verifies remote tags and assets")
        return
    dist = ROOT / "dist"

    # 复用用户已有 Git 凭据，只存内存；不写令牌、不打印凭据或请求头。
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

    verify_remote_tag(api, record)
    old = preserved_releases(api)
    tag = record["tag"]
    notes = (ROOT / ("变更说明_" + VERSION + ".md")).read_text(encoding="utf-8")
    notes += ("\n验证：主机回归、确定性文件故障注入、Medium 非管理员安装、"
              "CubeMX 原生生成/再生成、Keil ARM 编译均通过（0 错误 0 警告）。"
              "来源提交及完整资产哈希见 Provenance、Validation 和 SHA256SUMS 附件。未烧录或实车验证。\n")
    try:
        release = api("/releases/tags/" + tag)
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
        candidates = [r for r in paginated(api, "/releases") if r["tag_name"] == tag]
        if len(candidates) > 1:
            raise RuntimeError("同一标签存在多个发布记录，拒绝自动选择。")
        assert_current(record, root=ROOT, version=VERSION, assets=release_asset_names())
        verify_remote_tag(api, record)
        release = candidates[0] if candidates else api("/releases", "POST", {
            "tag_name": tag, "target_commitish": record["commit"], "name": "foxmolika " + VERSION,
            "body": notes, "draft": True, "prerelease": False})
    if not release["draft"] or release["tag_name"] != tag:
        raise RuntimeError("此版本已公开或标签不一致，拒绝覆盖；请发布新版本。")
    current_assets = paginated(api, "/releases/" + str(release["id"]) + "/assets")
    current = {a["name"]: a for a in current_assets}
    if len(current) != len(current_assets) or not set(current).issubset(checksums):
        raise RuntimeError("草稿含重复或清单外附件，拒绝删除或覆盖。")
    for name, digest in checksums.items():
        # 每个附件上传前重取干净标签来源、门禁及全部本地摘要。
        assert_current(record, root=ROOT, version=VERSION, assets=release_asset_names())
        if local_preflight() != (record, checksums):
            raise RuntimeError("上传期间发布来源或资产发生变化。")
        verify_remote_tag(api, record)
        latest = api("/releases/" + str(release["id"]))
        if not latest["draft"] or latest["tag_name"] != tag:
            raise RuntimeError("上传期间草稿被公开或更换标签，已停止。")
        asset = current.get(name)
        if asset is None:
            payload = (dist / name).read_bytes()
            if hashlib.sha256(payload).hexdigest() != digest:
                raise RuntimeError("上传读取期间资产发生变化：" + name)
            endpoint = release["upload_url"].split("{", 1)[0] + "?name=" + urllib.parse.quote(name)
            asset = api(endpoint, "POST", payload, binary=True)
        verify_asset(asset, name, digest, dist / name)
        print("PASS uploaded/verified " + name, flush=True)
    assert_current(record, root=ROOT, version=VERSION, assets=release_asset_names())
    if local_preflight() != (record, checksums):
        raise RuntimeError("公开前发布来源或资产发生变化。")
    verify_remote_tag(api, record)
    verify_remote_assets(api, release, checksums, dist)
    if preserved_releases(api) != old:
        raise RuntimeError("历史标签或附件发生变化，未公开当前草稿。")
    latest = api("/releases/" + str(release["id"]))
    if not latest["draft"] or latest["tag_name"] != tag:
        raise RuntimeError("公开前草稿状态或标签发生变化，未修改该版本。")
    # 紧邻公开请求再次检查，避免远端检查期间出现本地源码变更。
    assert_current(record, root=ROOT, version=VERSION, assets=release_asset_names())
    published = api("/releases/" + str(release["id"]), "PATCH", {"draft": False})
    if published["draft"] or published["tag_name"] != tag:
        raise RuntimeError("发布返回的状态或标签不一致。")
    verify_remote_tag(api, record)
    uploaded = verify_remote_assets(api, published, checksums, dist)
    after = preserved_releases(api)
    if after != old:
        raise RuntimeError("发布后历史标签或附件核对失败。")
    receipt = {"version": VERSION, "commit": record["commit"], "source_provenance": record,
               "url": published["html_url"], "assets": [asset_identity(a) for a in uploaded],
               "old_releases_unchanged": True, "preserved_releases_before": old,
               "preserved_releases_after": after}
    (ROOT / "build/github-publication.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PASS published " + published["html_url"])


if __name__ == "__main__":
    main()
