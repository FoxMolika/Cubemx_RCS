from version import VERSION, release_asset_names
'Package ONLY validated current artifacts; never rebuild or install here.'
from datetime import datetime, timezone
from pathlib import Path
from gate_diagnostics import GateError, file_sha, read_json, require_file, write_report, now
from xml.etree import ElementTree as E
import json, re, shutil, subprocess, zipfile
from provenance import assert_current, create_source_archive, verify_source_archive
from release_baselines import previous_tag, verify as verify_baselines
from release_evidence import BUILD_RECORDS, CH_ZIP, POST, POST_RECORD, verify_prefix

R = Path(__file__).resolve().parents[1]

def sha(p):
    return file_sha(require_file(p, "artifact_missing"))


def read(name):
    return read_json(R / name)


def require(condition, name):
    if not condition:
        raise GateError("stale_evidence", "发布验收条件未满足：" + str(name),
                        "核对本轮报告与对应的验证步骤", {"check": name})


def _release_body(evidence, dist):
    provenance = evidence["source_provenance"]
    changelog = require_file(R / ("变更说明_" + VERSION + ".md"))
    assert_current(provenance, R)
    history_counts = verify_baselines(R)
    prev_tag = previous_tag(R)
    require(evidence.get("installed_editor_sha256") == evidence["evidence"]["map_editor/dist/foxmolika_MapEditor.exe"],
            "卸载前安装编辑器摘要")
    for record in (*BUILD_RECORDS, POST_RECORD):
        build = read(record)
        output = R / POST if record == POST_RECORD else build["output"]
        require(sha(output) == build["output_sha256"], record)
        # 构建输入中包含已生成的版本、模板、资源与工具。内容变化要求完整重跑。
        for path, digest in build["inputs"].items():
            require(sha(path) == digest, (record, path))

    provenance_tests = read('build/provenance_validation.json')
    require(provenance_tests['status'] == 'PASS' and provenance_tests['checks'] >= 24, "provenance_tests['status'] == 'PASS' and provenance_tests['checks'] >= 24")
    for name, digest in provenance_tests['source_sha256'].items():
        require(sha(R / 'tools' / name) == digest, name)
    preflight_tests = read('build/release_preflight_validation.json')
    require(preflight_tests['passed'] and len(preflight_tests['checks']) >= 10, "preflight_tests['passed'] and len(preflight_tests['checks']) >= 10")
    for name, digest in preflight_tests['source_sha256'].items():
        require(sha(R / name) == digest, name)
    from check_guides import main as guides
    guides()
    refactor = read('build/refactor_validation.json')
    require(refactor['passed'] and refactor['editor_sha256'] == sha(R / 'map_editor/dist/foxmolika_MapEditor.exe'), "refactor['passed'] and refactor['editor_sha256'] == sha(R / 'map_editor/dist/foxmolika_MapEditor.exe')")
    export_transaction = read('build/export_transaction_validation.json')
    require(export_transaction['passed'], "export_transaction['passed']")
    for name, digest in export_transaction['source_sha256'].items():
        require(sha(R / name) == digest, name)
    keil_options = read('build/keil_options_validation.json')
    require(keil_options['passed'] and keil_options['editor_sha256'] == refactor['editor_sha256'], "keil_options['passed'] and keil_options['editor_sha256'] == refactor['editor_sha256']")
    installer_io = read('build/installer_io_validation.json')
    require(installer_io['passed'], "installer_io['passed']")
    for name, digest in installer_io['source_sha256'].items():
        require(sha(R / name) == digest, name)
    atomic_file=read('build/atomic_file_validation.json');require(atomic_file['passed'], "atomic_file['passed']")
    for name,digest in atomic_file['source_sha256'].items():require(sha(R/name)==digest, name)
    version_evidence = read('build/version_validation.json')
    require(version_evidence['passed'] and version_evidence['version'] == VERSION and version_evidence['suite_checked'], "version_evidence['passed'] and version_evidence['version'] == VERSION and version_evidence['suite_checked']")
    from release_baselines import baseline_paths
    prior_release = read_json(baseline_paths(R, "0.8.0")[1])
    setup = R / f'suite/dist/foxmolika_CubeMX_Setup_{VERSION}_win-x64.exe'
    from windows_integrity import medium, security
    require(security(setup)[0] == 8192, 'security(setup)[0] == 8192')
    require(version_evidence['binaries']['editor']['sha256']==sha(R/'map_editor/dist/foxmolika_MapEditor.exe'), "version_evidence['binaries']['editor']['sha256']==sha(R/'map_editor/dist/foxmolika_MapEditor.exe')")
    require(version_evidence['binaries']['suite']['sha256']==sha(setup), "version_evidence['binaries']['suite']['sha256']==sha(setup)")
    require(version_evidence['pack_sha256']==sha(R/('dist/RCS.foxmolika.'+VERSION+'.pack')), "version_evidence['pack_sha256']==sha(R/('dist/RCS.foxmolika.'+VERSION+'.pack'))")
    permissions = read('build/permissions_validation.json')
    require(permissions['passed'] and permissions['installer_sha256'] == sha(setup), "permissions['passed'] and permissions['installer_sha256'] == sha(setup)")
    terrain_drive = read('build/terrain_drive_validation.json')
    require(terrain_drive['passed'] and terrain_drive['task_adapter_report'].startswith('PASS '), "terrain_drive['passed'] and terrain_drive['task_adapter_report'].startswith('PASS ')")
    terrain_native = read('build/terrain_project_integration_validation.json')
    require(terrain_native['terrain_mode'] and terrain_native['terrain_source_compiled'] and (terrain_native['registered_route_file_count'] == 8) and (terrain_native['keil'] == 'pass: ARM Compiler 6.22, 0 errors, 0 warnings'), "terrain_native['terrain_mode'] and terrain_native['terrain_source_compiled'] and (terrain_native['registered_route_file_count'] == 8) and (terrain_native['keil'] == 'pass: ARM Compiler 6.22, 0 errors, 0 warnings')")
    optional_project = read('build/terrain_project_validation.json')
    require(optional_project['passed'], "optional_project['passed']")
    correction = read('build/correction_validation.json')
    require(correction['passed'], "correction['passed']")
    continuous = read('build/continuous_validation.json')
    require(continuous['passed'] and continuous['editor_sha256'] == sha(R / 'map_editor/dist/foxmolika_MapEditor.exe'), "continuous['passed'] and continuous['editor_sha256'] == sha(R / 'map_editor/dist/foxmolika_MapEditor.exe')")
    workspace = read('build/workspace_validation.json')
    require(workspace['passed'] and workspace['checks'] >= 41, "workspace['passed'] and workspace['checks'] >= 41")
    require(permissions['actual_install_token']['IntegrityRid'] == '8192' and permissions['actual_install_token']['Elevated'] == 'false' and (permissions['actual_install_token']['Administrator'] == 'false') and (permissions['actual_install_token']['AppContainer'] == 'false'), "permissions['actual_install_token']['IntegrityRid'] == '8192' and permissions['actual_install_token']['Elevated'] == 'false' and (permissions['actual_install_token']['Administrator'] == 'false') and (permissions['actual_install_token']['AppContainer'] == 'false')")
    editor = R / 'map_editor/dist/foxmolika_MapEditor.exe'
    pack = R / f'dist/RCS.foxmolika.{VERSION}.pack'
    require(workspace['editor_sha256'] == sha(editor) and workspace['unfinished_draft_round_trip'] and workspace['inserted_transition_and_links_pass_geometry'], "workspace['editor_sha256'] == sha(editor) and workspace['unfinished_draft_round_trip'] and workspace['inserted_transition_and_links_pass_geometry']")
    accepted = read('suite/build/acceptance.json')
    integration = read('build/project_integration_validation.json')
    require(accepted.get('custom_empty_repository') == 'pass' and accepted.get('native_package_uninstall') == 'pass', "accepted.get('custom_empty_repository') == 'pass' and accepted.get('native_package_uninstall') == 'pass'")
    require(accepted.get('clean_uninstall') == 'pass' and accepted['installed_editor_version'] == VERSION, "accepted.get('clean_uninstall') == 'pass' and accepted['installed_editor_version'] == VERSION")
    require(accepted['installer_sha256'] == sha(setup), "accepted['installer_sha256'] == sha(setup)")
    editor_checks = int(re.search('PASS (\\d+)', accepted['installed_editor_report']).group(1))
    require(editor_checks >= 49, 'editor_checks >= 49')
    require(int(re.search('PASS (\\d+)', accepted['installed_workspace_report']).group(1)) == workspace['checks'] and permissions['installed_workspace_checks'] == workspace['checks'], "int(re.search('PASS (\\\\d+)', accepted['installed_workspace_report']).group(1)) == workspace['checks'] and permissions['installed_workspace_checks'] == workspace['checks']")
    require(correction['editor_sha256'] == sha(editor) and accepted['installed_correction_report'] == permissions['installed_correction_report'] == correction['model_report'], "correction['editor_sha256'] == sha(editor) and accepted['installed_correction_report'] == permissions['installed_correction_report'] == correction['model_report']")
    require(accepted['installed_continuous_report'] == permissions['installed_continuous_report'] == continuous['report'], "accepted['installed_continuous_report'] == permissions['installed_continuous_report'] == continuous['report']")
    require(terrain_drive['editor_sha256'] == sha(editor) and terrain_native['installer_sha256'] == sha(setup), "terrain_drive['editor_sha256'] == sha(editor) and terrain_native['installer_sha256'] == sha(setup)")
    for key, file in [('installed_terrain_drive_report', 'terrain-drive-selftest.txt'), ('installed_shape_geometry_report', 'shape-geometry-selftest.txt'), ('installed_shape_ui_report', 'shape-ui-selftest.txt')]:
        require(accepted[key] == permissions[key] == terrain_drive['model_UI_geometry_reports'][file], "accepted[key] == permissions[key] == terrain_drive['model_UI_geometry_reports'][file]")
    require(integration['installer_sha256'] == sha(setup) and integration['installed_editor_sha256'] == sha(editor), "integration['installer_sha256'] == sha(setup) and integration['installed_editor_sha256'] == sha(editor)")
    require(integration['keil'] == 'pass: ARM Compiler 6.22, 0 errors, 0 warnings', "integration['keil'] == 'pass: ARM Compiler 6.22, 0 errors, 0 warnings'")
    require(integration['route_sources_compiled'] == 'pass: route/nav/actions', "integration['route_sources_compiled'] == 'pass: route/nav/actions'")
    require(integration['route_user_code_unchanged'] and integration['main_unchanged'], "integration['route_user_code_unchanged'] and integration['main_unchanged']")
    phases = read('map_editor/build/phased_validation.json')
    require(len(phases['actual_export_simulations']) == 3, "len(phases['actual_export_simulations']) == 3")
    require(all((int(re.search('(\\d+) assertions', s).group(1)) >= 3000 for s in phases['actual_export_simulations'])), "all((int(re.search('(\\\\d+) assertions', s).group(1)) >= 3000 for s in phases['actual_export_simulations']))")
    phase_count = int(re.search('PASS (\\d+) phase assertions', phases['unit_fault_phase_checks']).group(1))
    require(phase_count >= 11448, 'phase_count >= 11448')
    generated = Path(phases['area']) / 'layout0/rcs_nav.c'
    require(generated.read_text(encoding='utf-8').split('/* RCS GENERATED SHA256')[0].strip() == re.sub('(?m)^/\\* RCS TEMPLATE (?:BEGIN|END) [A-Z0-9_]+ \\*/\\n', '', (R / 'map_editor/firmware/rcs_nav.c').read_text(encoding='utf-8')).strip(), "generated.read_text(encoding='utf-8').split('/* RCS GENERATED SHA256')[0].strip() == re.sub('(?m)^/\\\\* RCS TEMPLATE (?:BEGIN|END) [A-Z0-9_]+ \\\\*/\\\\n', '', (R / 'map_editor/firmware/rcs_nav.c').read_text(encoding='utf-8')).strip()")
    crash = read('build/crash-validation/results.json')
    require(crash['passed'] and crash['checks'] == 54, "crash['passed'] and crash['checks'] == 54")
    require(crash['editor_SHA256'] == sha(editor), "crash['editor_SHA256'] == sha(editor)")
    migration = read('build/migration080.json')
    require(migration['passed'] and migration['legacy_map_unchanged'], "migration['passed'] and migration['legacy_map_unchanged']")
    course = read('build/course_validation.json')
    require(course['passed'] and course['editor_sha256'] == sha(editor), "course['passed'] and course['editor_sha256'] == sha(editor)")
    require(course['same_generated_six_C_H_as_070'] and course['legacy_points_segments_preserved'] and course['signed_arc_pixel_oracle'] and course['intentional_correction_engine_upgrade'], "course['same_generated_six_C_H_as_070'] and course['legacy_points_segments_preserved'] and course['signed_arc_pixel_oracle'] and course['intentional_correction_engine_upgrade']")
    terrain = read('build/terrain_validation.json')
    require(terrain['passed'] and terrain['editor_sha256'] == sha(editor), "terrain['passed'] and terrain['editor_sha256'] == sha(editor)")
    require(terrain['checks'] >= 95 and terrain['actual_drag_checks'] >= 38 and terrain['unsupported_ramp_stop_export_refused_before_mutation'] and terrain['terrain_map_unchanged'], "terrain['checks'] >= 95 and terrain['actual_drag_checks'] >= 38 and terrain['unsupported_ramp_stop_export_refused_before_mutation'] and terrain['terrain_map_unchanged']")
    require(int(re.search('PASS (\\d+)', accepted['installed_terrain_report']).group(1)) == terrain['checks'], "int(re.search('PASS (\\\\d+)', accepted['installed_terrain_report']).group(1)) == terrain['checks']")
    route = read('build/route_project_validation.json')
    require(route['passed'] and route['editor_sha256'] == sha(editor), "route['passed'] and route['editor_sha256'] == sha(editor)")
    require(route['post_helper_sha256'] == sha(R / 'build/foxmolika_PostGenerate.exe'), "route['post_helper_sha256'] == sha(R / 'build/foxmolika_PostGenerate.exe')")
    revision = read('build/revision_validation.json')
    require(revision['passed'] and revision['editor_sha256'] == sha(editor), "revision['passed'] and revision['editor_sha256'] == sha(editor)")
    keil = read('build/keil_guard_validation.json')
    require(keil['passed'] and keil['installer_sha256'] == sha(setup), "keil['passed'] and keil['installer_sha256'] == sha(setup)")
    transactions = read('suite/build/transactions.json')
    require(transactions['passed'], "transactions['passed']")
    require(transactions['installer_sha256'] == sha(setup) and transactions['pack_sha256'] == sha(pack), "transactions['installer_sha256'] == sha(setup) and transactions['pack_sha256'] == sha(pack)")
    core = (R / 'build/host_validation.txt').read_text(encoding='utf-8')
    require('PASS 1555 checks' in core, "'PASS 1555 checks' in core")
    # 比较上一标签的 Git blob，不解包到共享 build 路径。
    tag_sources = subprocess.run(
        ["git", "ls-tree", "--name-only", prev_tag + ":firmware"],
        cwd=R, capture_output=True, text=True, check=True,
    ).stdout.splitlines()
    previous_sources = {name for name in tag_sources if Path(name).suffix in (".c", ".h")}
    current_sources = {p.name for p in (R / "firmware").iterdir() if p.suffix in (".c", ".h")}
    require(previous_sources == current_sources, "上一版固件文件集合 " + prev_tag)
    from audit_payload import resources as audit_resources
    embedded = audit_resources(editor)
    editor_record = read(BUILD_RECORDS[0])
    for resource, path in editor_record["resources"].items():
        require(embedded.get(resource) == editor_record["inputs"][path],
                "编辑器嵌入资源 " + resource)
    resources = audit_resources(setup)
    manifest = R / 'suite/build/manifest.xml'
    require(resources['payload/manifest.xml'] == sha(manifest), "resources['payload/manifest.xml'] == sha(manifest)")
    for f in E.parse(manifest).findall('./AppFiles/File'):
        require(resources['payload/' + f.attrib['Name']] == f.attrib['Sha256'], "resources['payload/' + f.attrib['Name']] == f.attrib['Sha256']")
    require(resources['payload/foxmolika_MapEditor.exe'] == sha(editor), "resources['payload/foxmolika_MapEditor.exe'] == sha(editor)")
    require(resources[f'payload/RCS.foxmolika.{VERSION}.pack'] == sha(pack), "resources[f'payload/RCS.foxmolika.{VERSION}.pack'] == sha(pack)")
    with zipfile.ZipFile(pack) as z:
        require(z.read('Documentation/使用说明.html') == (R / 'docs/用户手册.html').read_bytes(), "z.read('Documentation/使用说明.html') == (R / 'docs/用户手册.html').read_bytes()")
        for source in (R / 'firmware').glob('*'):
            if source.suffix in ['.c', '.h']:
                require(z.read('Middlewares/Third_Party/RCS_foxmolika/' + source.name) == source.read_bytes(), "z.read('Middlewares/Third_Party/RCS_foxmolika/' + source.name) == source.read_bytes()")
                # Git的Windows检出会转换行尾；旧源码比较只规范CRLF，不忽略其他字节。
                previous_bytes = subprocess.run(
                    ["git", "show", f"{prev_tag}:firmware/{source.name}"],
                    cwd=R, capture_output=True, check=True,
                ).stdout
                require(source.read_bytes().replace(b"\r\n", b"\n") == previous_bytes.replace(b"\r\n", b"\n"),
                        "上一版固件内容 " + prev_tag + "/" + source.name)
    shutil.copyfile(pack, dist / pack.name)
    shutil.copyfile(R / CH_ZIP, dist / Path(CH_ZIP).name)
    shutil.copyfile(setup, dist / setup.name)
    medium(dist / setup.name)
    with zipfile.ZipFile(dist / f'foxmolika_CubeMX_Installer_{VERSION}.zip', 'w', zipfile.ZIP_DEFLATED) as z:
        z.write(setup, setup.name)
        z.writestr('使用提示.txt', '请在普通用户目录解压后双击EXE，默认不需管理员权限。不要在被标记为Low完整性的目录直接执行；若出现Low级别提示，请换到正常用户目录解压。安装器内嵌.pack并自动导入，不需要手动导入。')
    shutil.copyfile(R / 'build/ui/continuous1.png', dist / '连续跨平台侧视图.png')
    shutil.copyfile(R / 'build/ui/continuous2.png', dist / '连续跨平台三维图.png')
    shutil.copyfile(R / 'map_editor/build/examples/连续通行示例.rcsmap.json', dist / '连续通行示例.rcsmap.json')
    shutil.copyfile(R / 'build/ui/correction-templates.png', dist / '纠错模板界面.png')
    shutil.copyfile(R / 'build/ui/hardware-profile.png', dist / '定位硬件界面.png')
    shutil.copyfile(R / 'docs/定位硬件接入.md', dist / '定位硬件接入.md')
    shutil.copyfile(R / 'map_editor/examples/定位硬件档案示例.json', dist / '定位硬件档案示例.json')
    shutil.copyfile(R / 'build/ui/shape-editing.png', dist / '形状编辑界面.png')
    shutil.copyfile(R / 'docs/坡道与形状编辑.md', dist / '坡道与形状编辑.md')
    shutil.copyfile(R / 'map_editor/build/examples/坡道执行示例.rcsmap.json', dist / '坡道执行示例.rcsmap.json')
    shutil.copyfile(R / 'build/ui/course_angles.png', dist / '夹角显示预览.png')
    for name, source in [('航点双动作框.png', 'phases.png'), ('立体地图预览.png', 'terrain2.png'), ('路线高度侧视图.png', 'terrain1.png')]:
        shutil.copyfile(R / 'build/ui' / source, dist / name)
    shutil.copyfile(R / 'build/ui/terrain0.png', dist / '地形拖动界面.png')
    shutil.copyfile(R / 'build/ui/terrain0.png', dist / '主窗口一体编辑.png')
    shutil.copyfile(Path(workspace['area']) / 'workspace-minimum.png', dist / '未完成草稿编辑.png')
    for name, source in [('使用说明.html', 'docs/使用说明.html'), ('地图编辑器说明.html', 'map_editor/dist/使用说明.html'), ('底盘接入说明.md', 'docs/接入说明.md')]:
        shutil.copyfile(R / source, dist / name)
    report = {'version': VERSION, 'passed': True, 'firmware_legacy_comparison':'CRLF/LF normalized only; pack/source raw bytes exact', 'refactor_validation': refactor, 'export_transaction': export_transaction, 'keil_options': keil_options, 'installer_io': installer_io, 'atomic_file':atomic_file, 'version_evidence': version_evidence, 'preserved_080_assets': prior_release, 'installer_sha256': sha(setup), 'editor_sha256': sha(editor), 'pack_sha256': sha(pack), 'terrain_execution': terrain_drive, 'terrain_native': terrain_native, 'optional_project_files': optional_project, 'original_070_files_unchanged': history_counts['0.7.0'], 'correction_validation': correction, 'original_061_files_unchanged': history_counts['0.6.1'], 'continuous_surface_checks': continuous['checks'], 'original_060_files_unchanged': history_counts['0.6.0'], 'features': ['working terrain4C4H export with calibrated arclength/horizontal conversion and predictive speed limits', 'convex outline vertex/edge/rotation editor and polygon-aware validation', 'atomic owned6to8project upgrade and downgrade with all-file revision guards', 'optional hardware-backed correction templates; empty means no correction', 'validated independent observation freshness, quality and bounded displacement', 'v5 hardware profiles with explicit driver binding; legacy mode retained but not executed', 'continuous connected-surface traversal without forced task waypoints', 'unified main-window waypoint and terrain workspace', 'exposed platform/ramp/connection/layer controls', 'shared document, global undo/redo and recovery snapshots', 'insert transition waypoint after selected point', 'draft save/open independent of full route validation', 'derived height and editable surface assignment', 'verified Medium non-admin installation', 'dual arrival/departure action panes', 'straight-ramp firmware export; unsupported ramp turns/stops rejected', 'Keil stale-map prebuild guard'], 'tests': {'core_assertions': 1555, 'generated_layouts': 3, 'generated_assertions_per_layout': int(re.search('(\\d+) assertions', phases['actual_export_simulations'][0]).group(1)), 'phase_assertions': phase_count, 'editor_model_checks': editor_checks, 'crash_checks': 54, 'route_integration_scenarios': len(route['checks']), 'project_setup_scenarios': 13, 'revision_scenarios': len(revision['checks']), 'installer_transaction_scenarios': 8}, 'actual_CubeMX': '6.18.1; fresh isolated profiles, automatic import, alternate empty repository, native generate/regenerate, uninstall', 'actual_Keil': 'ARM Compiler 6.22; flat3C3H and terrain4C4H compiled; 0 errors, 0 warnings', 'actual_stale_map_blocks_Keil_build': True, 'route_user_code_and_main_preserved': True, 'original_037_files_unchanged': history_counts['0.3.7'], 'original_040_files_unchanged': history_counts['0.4.0'], 'original_041_files_unchanged': history_counts['0.4.1'], 'original_050_files_unchanged': history_counts['0.5.0'], 'original_051_files_unchanged': history_counts['0.5.1'], 'terrain_checks': terrain['checks'], 'actual_drag_checks': terrain['actual_drag_checks'], 'embedded_resources_verified': len(resources), 'ordinary_user_install': permissions, 'published_installer_integrity_rid': security(dist / setup.name)[0], 'original_052_files_unchanged': history_counts['0.5.2'], 'workspace_checks': workspace['checks'], 'hardware_test': False, 'flashed': False, 'second_physical_PC_test': False, 'ui_review': 'two-endpoint continuous route profile and 3D inspected; actual unified main window at 1440px and 1120px; top/profile/3D and shared point/property editors inspected; event-level main-window workflow tests passed', 'delegated_review': 'workspace-scoped version and deterministic I/O test tasks; root integrated and verified current binaries and native toolchain', 'limitations': ['slope execution is known-map straight-fallline model; no real pitch/slip measurement or ground validation', 'convex polygon outlines and affine straight slopes only; no concave/holey/curved-height surfaces or slope turns', '3D drawing shows upper surfaces; clearance includes configured underside thickness and envelope margin', 'drive-encoder odometry alone cannot enable correction; declared independent adapters require real hardware integration and calibration', 'existing hardware driver support unchanged', 'custom-action preview is a placeholder', 'hard termination/power loss cannot guarantee immediate crash ZIP']}
    report['source_provenance'] = provenance
    report['gate_run_id'] = evidence['run_id']
    report['previous_firmware_tag'] = prev_tag
    report['historical_asset_counts'] = history_counts
    report['provenance_tests'] = provenance_tests
    report['release_preflight_tests'] = preflight_tests
    out = dist / f'foxmolika_CubeMX_Source_{VERSION}.zip'
    create_source_archive(out, provenance, R)
    verify_source_archive(out, provenance, R)
    report['source_zip_sha256'] = sha(out)
    (dist / f'发布核验_{VERSION}.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    shutil.copyfile(changelog, dist / ('变更说明_' + VERSION + '.md'))
    shutil.copyfile(dist / ('发布核验_' + VERSION + '.json'), dist / ('foxmolika_Validation_' + VERSION + '.json'))
    shutil.copyfile(dist / ('变更说明_' + VERSION + '.md'), dist / ('foxmolika_Changes_' + VERSION + '.md'))
    (dist / ('foxmolika_Provenance_' + VERSION + '.json')).write_text(
        json.dumps(provenance, ensure_ascii=False, indent=2), encoding='utf-8')
    sums = []
    for name in release_asset_names():
        artifact = dist / name
        require(artifact.is_file(), name)
        sums.append(sha(artifact) + '  ' + name + '\n')
    (dist / 'SHA256SUMS.txt').write_text(''.join(sums), encoding='utf-8')
    try:
        assert_current(provenance, R)
    except RuntimeError as error:
        raise GateError('source_changed', str(error), hint='确认 HEAD 与 tag 一致、工作区干净后再打包') from error
def freeze_candidate(stage, destination, gate, commit_guard):
    """将完整候选及通过记录一并提交。失败留在本轮目录。"""
    if destination.exists():
        raise GateError("duplicate_invocation", "冻结候选已存在：" + str(destination))
    write_report(stage / "gate.json", gate)
    commit_guard()
    destination.parent.mkdir(parents=True, exist_ok=True)
    stage.rename(destination)


def package(evidence, directory, destination):
    from check import steps_for
    from audit_release import verify_candidate
    expected = [list(step) for step in steps_for("release")]
    provenance = evidence["source_provenance"]
    require(evidence["completed"] == expected[:-1] and provenance.get("tag_required") is True,
            "正式标签与全部前序步骤")
    verify_prefix(R, evidence)
    stage = directory / "candidate"
    stage.mkdir()
    _release_body(evidence, stage)
    frozen_gate = dict(evidence, completed=expected, passed=True, status="passed", finished_at=now())
    frozen_gate.pop("active_step", None)
    frozen_gate.pop("active_step_directory", None)
    verify_candidate(R, stage, frozen_gate)
    def current():
        verify_prefix(R, evidence)
        assert_current(provenance, R)

    freeze_candidate(stage, destination, frozen_gate, current)
    print("冻结发布候选 " + str(destination), flush=True)


def main():
    from check import main as check
    check(["--level", "release", "--from", "tools/release.py"])


if __name__ == '__main__':
    main()
