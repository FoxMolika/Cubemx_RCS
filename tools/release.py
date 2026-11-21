from version import VERSION, release_asset_names
'Package ONLY validated current artifacts; never rebuild or install here.'
from datetime import datetime, timezone
from pathlib import Path
from gate_diagnostics import GateError
from xml.etree import ElementTree as E
import hashlib, json, os, re, shutil, subprocess, sys, zipfile
from provenance import assert_current, create_source_archive, verify_source_archive
R = Path(__file__).resolve().parents[1]

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def read(name):
    return json.loads((R / name).read_text(encoding='utf-8'))

def _release_body():
    sys.stdout.reconfigure(encoding='utf-8')
    evidence = read('build/check-release.json')
    provenance = evidence['source_provenance']
    if provenance['tag_required'] is not True:
        raise GateError('missing_evidence', '正式打包必须绑定版本标签', hint='先打 tag 再跑 release')
    try:
        assert_current(provenance, R)
    except RuntimeError as error:
        raise GateError('source_changed', str(error), hint='确认 HEAD 与 tag 一致、工作区干净后再打包') from error
    from check import steps_for
    expected_steps = [list(s) for s in steps_for('release')[:-1]]
    if evidence['completed'] != expected_steps:
        raise GateError('missing_evidence', '必须先完成本标签全部host/native检查',
                        hint='重跑 python tools/check.py --level release',
                        detail={'expected': expected_steps, 'actual': evidence['completed']})
    provenance_tests = read('build/provenance_validation.json')
    assert provenance_tests['status'] == 'PASS' and provenance_tests['checks'] >= 24
    for name, digest in provenance_tests['source_sha256'].items():
        assert sha(R / 'tools' / name) == digest, name
    preflight_tests = read('build/release_preflight_validation.json')
    assert preflight_tests['passed'] and len(preflight_tests['checks']) >= 10
    for name, digest in preflight_tests['source_sha256'].items():
        assert sha(R / name) == digest, name
    from check_guides import main as guides
    guides()
    refactor = read('build/refactor_validation.json')
    assert refactor['passed'] and refactor['editor_sha256'] == sha(R / 'map_editor/dist/foxmolika_MapEditor.exe')
    export_transaction = read('build/export_transaction_validation.json')
    assert export_transaction['passed']
    for name, digest in export_transaction['source_sha256'].items():
        assert sha(R / name) == digest, name
    keil_options = read('build/keil_options_validation.json')
    assert keil_options['passed'] and keil_options['editor_sha256'] == refactor['editor_sha256']
    installer_io = read('build/installer_io_validation.json')
    assert installer_io['passed']
    for name, digest in installer_io['source_sha256'].items():
        assert sha(R / name) == digest, name
    atomic_file=read('build/atomic_file_validation.json');assert atomic_file['passed']
    for name,digest in atomic_file['source_sha256'].items():assert sha(R/name)==digest,name
    version_evidence = read('build/version_validation.json')
    assert version_evidence['passed'] and version_evidence['version'] == VERSION and version_evidence['suite_checked']
    prior_release = read('build/original_080_release_SHA256.json')
    for name, digest in prior_release['files'].items():
        assert sha(R.parent / 'foxmolika_CubeMX_0.8.0' / name) == digest, ('old080asset changed', name)
    setup = R / f'suite/dist/foxmolika_CubeMX_Setup_{VERSION}_win-x64.exe'
    from windows_integrity import medium, security
    assert security(setup)[0] == 8192
    assert version_evidence['binaries']['editor']['sha256']==sha(R/'map_editor/dist/foxmolika_MapEditor.exe')
    assert version_evidence['binaries']['suite']['sha256']==sha(setup)
    assert version_evidence['pack_sha256']==sha(R/('dist/RCS.foxmolika.'+VERSION+'.pack'))
    permissions = read('build/permissions_validation.json')
    assert permissions['passed'] and permissions['installer_sha256'] == sha(setup)
    terrain_drive = read('build/terrain_drive_validation.json')
    assert terrain_drive['passed'] and terrain_drive['task_adapter_report'].startswith('PASS ')
    terrain_native = read('build/terrain_project_integration_validation.json')
    assert terrain_native['terrain_mode'] and terrain_native['terrain_source_compiled'] and (terrain_native['registered_route_file_count'] == 8) and (terrain_native['keil'] == 'pass: ARM Compiler 6.22, 0 errors, 0 warnings')
    optional_project = read('build/terrain_project_validation.json')
    assert optional_project['passed']
    correction = read('build/correction_validation.json')
    assert correction['passed']
    continuous = read('build/continuous_validation.json')
    assert continuous['passed'] and continuous['editor_sha256'] == sha(R / 'map_editor/dist/foxmolika_MapEditor.exe')
    workspace = read('build/workspace_validation.json')
    assert workspace['passed'] and workspace['checks'] >= 41
    assert permissions['actual_install_token']['IntegrityRid'] == '8192' and permissions['actual_install_token']['Elevated'] == 'false' and (permissions['actual_install_token']['Administrator'] == 'false') and (permissions['actual_install_token']['AppContainer'] == 'false')
    editor = R / 'map_editor/dist/foxmolika_MapEditor.exe'
    pack = R / f'dist/RCS.foxmolika.{VERSION}.pack'
    assert workspace['editor_sha256'] == sha(editor) and workspace['unfinished_draft_round_trip'] and workspace['inserted_transition_and_links_pass_geometry']
    accepted = read('suite/build/acceptance.json')
    integration = read('build/project_integration_validation.json')
    assert accepted.get('custom_empty_repository') == 'pass' and accepted.get('native_package_uninstall') == 'pass'
    assert accepted.get('clean_uninstall') == 'pass' and accepted['installed_editor_version'] == VERSION
    assert accepted['installer_sha256'] == sha(setup)
    editor_checks = int(re.search('PASS (\\d+)', accepted['installed_editor_report']).group(1))
    assert editor_checks >= 49
    assert int(re.search('PASS (\\d+)', accepted['installed_workspace_report']).group(1)) == workspace['checks'] and permissions['installed_workspace_checks'] == workspace['checks']
    assert correction['editor_sha256'] == sha(editor) and accepted['installed_correction_report'] == permissions['installed_correction_report'] == correction['model_report']
    assert accepted['installed_continuous_report'] == permissions['installed_continuous_report'] == continuous['report']
    assert terrain_drive['editor_sha256'] == sha(editor) and terrain_native['installer_sha256'] == sha(setup)
    for key, file in [('installed_terrain_drive_report', 'terrain-drive-selftest.txt'), ('installed_shape_geometry_report', 'shape-geometry-selftest.txt'), ('installed_shape_ui_report', 'shape-ui-selftest.txt')]:
        assert accepted[key] == permissions[key] == terrain_drive['model_UI_geometry_reports'][file]
    assert integration['installer_sha256'] == sha(setup) and integration['installed_editor_sha256'] == sha(editor)
    assert integration['keil'] == 'pass: ARM Compiler 6.22, 0 errors, 0 warnings'
    assert integration['route_sources_compiled'] == 'pass: route/nav/actions'
    assert integration['route_user_code_unchanged'] and integration['main_unchanged']
    phases = read('map_editor/build/phased_validation.json')
    assert len(phases['actual_export_simulations']) == 3
    assert all((int(re.search('(\\d+) assertions', s).group(1)) >= 3000 for s in phases['actual_export_simulations']))
    phase_count = int(re.search('PASS (\\d+) phase assertions', phases['unit_fault_phase_checks']).group(1))
    assert phase_count >= 11448
    generated = Path(phases['area']) / 'layout0/rcs_nav.c'
    assert generated.read_text(encoding='utf-8').split('/* RCS GENERATED SHA256')[0].strip() == re.sub('(?m)^/\\* RCS TEMPLATE (?:BEGIN|END) [A-Z0-9_]+ \\*/\\n', '', (R / 'map_editor/firmware/rcs_nav.c').read_text(encoding='utf-8')).strip()
    crash = read('build/crash-validation/results.json')
    assert crash['passed'] and crash['checks'] == 54
    assert crash['editor_SHA256'] == sha(editor)
    migration = read('build/migration080.json')
    assert migration['passed'] and migration['legacy_map_unchanged']
    course = read('build/course_validation.json')
    assert course['passed'] and course['editor_sha256'] == sha(editor)
    assert course['same_generated_six_C_H_as_070'] and course['legacy_points_segments_preserved'] and course['signed_arc_pixel_oracle'] and course['intentional_correction_engine_upgrade']
    terrain = read('build/terrain_validation.json')
    assert terrain['passed'] and terrain['editor_sha256'] == sha(editor)
    assert terrain['checks'] >= 95 and terrain['actual_drag_checks'] >= 38 and terrain['unsupported_ramp_stop_export_refused_before_mutation'] and terrain['terrain_map_unchanged']
    assert int(re.search('PASS (\\d+)', accepted['installed_terrain_report']).group(1)) == terrain['checks']
    route = read('build/route_project_validation.json')
    assert route['passed'] and route['editor_sha256'] == sha(editor)
    assert route['post_helper_sha256'] == sha(R / 'build/foxmolika_PostGenerate.exe')
    revision = read('build/revision_validation.json')
    assert revision['passed'] and revision['editor_sha256'] == sha(editor)
    keil = read('build/keil_guard_validation.json')
    assert keil['passed'] and keil['installer_sha256'] == sha(setup)
    transactions = read('suite/build/transactions.json')
    assert transactions['passed']
    assert transactions['installer_sha256'] == sha(setup) and transactions['pack_sha256'] == sha(pack)
    core = (R / 'build/host_validation.txt').read_text(encoding='utf-8')
    assert 'PASS 1555 checks' in core
    snapshot = read('build/original_037_SHA256.json')
    # 固件基线取上一发布 tag，而非仓库外的旧快照目录：换机可用，且固件合法演进后
    # 只需改 _prev_tag 一处，不会因目录缺失导致 FileNotFoundError。
    import io as _io, tarfile as _tarfile
    _prev_tag = 'v0.8.7'
    old = R.parent / 'foxmolika_CubeMX_0.3.7'
    prev = R / 'build/prev_release'
    if prev.exists():
        shutil.rmtree(prev)
    prev.mkdir(parents=True)
    _archive = subprocess.run(['git', 'archive', '--format=tar', _prev_tag],
                              cwd=R, capture_output=True, check=True).stdout
    _tarfile.open(fileobj=_io.BytesIO(_archive)).extractall(prev)
    for name, digest in snapshot.items():
        assert sha(old / name) == digest, ('original changed', name)
    previous = read('build/original_040_SHA256.json')
    previous_root = R.parent / 'foxmolika_CubeMX_0.4.0'
    for name, digest in previous.items():
        assert sha(previous_root / name) == digest, ('040 changed', name)
    previous041 = read('build/original_041_SHA256.json')
    root041 = R.parent / 'foxmolika_CubeMX_0.4.1'
    for name, digest in previous041.items():
        assert sha(root041 / name) == digest, ('041 changed', name)
    previous050 = read('build/original_050_SHA256.json')
    root050 = R.parent / 'foxmolika_CubeMX_0.5.0'
    for name, digest in previous050.items():
        assert sha(root050 / name) == digest, ('050 changed', name)
    previous051 = read('build/original_051_SHA256.json')
    root051 = R.parent / 'foxmolika_CubeMX_0.5.1'
    for name, digest in previous051.items():
        assert sha(root051 / name) == digest, ('051 changed', name)
    previous052 = read('build/original_052_SHA256.json')
    root052 = R.parent / 'foxmolika_CubeMX_0.5.2'
    for name, digest in previous052.items():
        assert sha(root052 / name) == digest, ('052 changed', name)
    previous060 = read('build/original_060_SHA256.json')
    root060 = R.parent / 'foxmolika_CubeMX_0.6.0'
    for name, digest in previous060.items():
        assert sha(root060 / name) == digest, ('060 changed', name)
    previous061 = read('build/original_061_SHA256.json')
    root061 = R.parent / 'foxmolika_CubeMX_0.6.1'
    for name, digest in previous061.items():
        assert sha(root061 / name) == digest, ('061 changed', name)
    previous070 = read('build/original_070_SHA256.json')
    root070 = R.parent / 'foxmolika_CubeMX_0.7.0'
    for name, digest in previous070.items():
        assert sha(root070 / name) == digest, ('070 changed', name)
    from audit_payload import resources as audit_resources
    resources = audit_resources(setup)
    manifest = R / 'suite/build/manifest.xml'
    assert resources['payload/manifest.xml'] == sha(manifest)
    for f in E.parse(manifest).findall('./AppFiles/File'):
        assert resources['payload/' + f.attrib['Name']] == f.attrib['Sha256']
    assert resources['payload/foxmolika_MapEditor.exe'] == sha(editor)
    assert resources[f'payload/RCS.foxmolika.{VERSION}.pack'] == sha(pack)
    with zipfile.ZipFile(pack) as z:
        assert z.read('Documentation/使用说明.html') == (R / 'docs/用户手册.html').read_bytes()
        for source in (R / 'firmware').glob('*'):
            if source.suffix in ['.c', '.h']:
                assert z.read('Middlewares/Third_Party/RCS_foxmolika/' + source.name) == source.read_bytes()
                # Git的Windows检出会转换行尾；旧源码比较只规范CRLF，不忽略其他字节。
                assert source.read_bytes().replace(b'\r\n',b'\n') == (prev / 'firmware' / source.name).read_bytes().replace(b'\r\n',b'\n')
    dist = R / 'dist'
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
    report = {'version': VERSION, 'passed': True, 'firmware_legacy_comparison':'CRLF/LF normalized only; pack/source raw bytes exact', 'refactor_validation': refactor, 'export_transaction': export_transaction, 'keil_options': keil_options, 'installer_io': installer_io, 'atomic_file':atomic_file, 'version_evidence': version_evidence, 'preserved_080_assets': prior_release, 'installer_sha256': sha(setup), 'editor_sha256': sha(editor), 'pack_sha256': sha(pack), 'terrain_execution': terrain_drive, 'terrain_native': terrain_native, 'optional_project_files': optional_project, 'original_070_files_unchanged': len(previous070), 'correction_validation': correction, 'original_061_files_unchanged': len(previous061), 'continuous_surface_checks': continuous['checks'], 'original_060_files_unchanged': len(previous060), 'features': ['working terrain4C4H export with calibrated arclength/horizontal conversion and predictive speed limits', 'convex outline vertex/edge/rotation editor and polygon-aware validation', 'atomic owned6to8project upgrade and downgrade with all-file revision guards', 'optional hardware-backed correction templates; empty means no correction', 'validated independent observation freshness, quality and bounded displacement', 'v5 hardware profiles with explicit driver binding; legacy mode retained but not executed', 'continuous connected-surface traversal without forced task waypoints', 'unified main-window waypoint and terrain workspace', 'exposed platform/ramp/connection/layer controls', 'shared document, global undo/redo and recovery snapshots', 'insert transition waypoint after selected point', 'draft save/open independent of full route validation', 'derived height and editable surface assignment', 'verified Medium non-admin installation', 'dual arrival/departure action panes', 'straight-ramp firmware export; unsupported ramp turns/stops rejected', 'Keil stale-map prebuild guard'], 'tests': {'core_assertions': 1555, 'generated_layouts': 3, 'generated_assertions_per_layout': int(re.search('(\\d+) assertions', phases['actual_export_simulations'][0]).group(1)), 'phase_assertions': phase_count, 'editor_model_checks': editor_checks, 'crash_checks': 54, 'route_integration_scenarios': len(route['checks']), 'project_setup_scenarios': 13, 'revision_scenarios': len(revision['checks']), 'installer_transaction_scenarios': 8}, 'actual_CubeMX': '6.18.1; fresh isolated profiles, automatic import, alternate empty repository, native generate/regenerate, uninstall', 'actual_Keil': 'ARM Compiler 6.22; flat3C3H and terrain4C4H compiled; 0 errors, 0 warnings', 'actual_stale_map_blocks_Keil_build': True, 'route_user_code_and_main_preserved': True, 'original_037_files_unchanged': len(snapshot), 'original_040_files_unchanged': len(previous), 'original_041_files_unchanged': len(previous041), 'original_050_files_unchanged': len(previous050), 'original_051_files_unchanged': len(previous051), 'terrain_checks': terrain['checks'], 'actual_drag_checks': terrain['actual_drag_checks'], 'embedded_resources_verified': len(resources), 'ordinary_user_install': permissions, 'published_installer_integrity_rid': security(dist / setup.name)[0], 'original_052_files_unchanged': len(previous052), 'workspace_checks': workspace['checks'], 'hardware_test': False, 'flashed': False, 'second_physical_PC_test': False, 'ui_review': 'two-endpoint continuous route profile and 3D inspected; actual unified main window at 1440px and 1120px; top/profile/3D and shared point/property editors inspected; event-level main-window workflow tests passed', 'delegated_review': 'workspace-scoped version and deterministic I/O test tasks; root integrated and verified current binaries and native toolchain', 'limitations': ['slope execution is known-map straight-fallline model; no real pitch/slip measurement or ground validation', 'convex polygon outlines and affine straight slopes only; no concave/holey/curved-height surfaces or slope turns', '3D drawing shows upper surfaces; clearance includes configured underside thickness and envelope margin', 'drive-encoder odometry alone cannot enable correction; declared independent adapters require real hardware integration and calibration', 'existing hardware driver support unchanged', 'custom-action preview is a placeholder', 'hard termination/power loss cannot guarantee immediate crash ZIP']}
    report['source_provenance'] = provenance
    report['provenance_tests'] = provenance_tests
    report['release_preflight_tests'] = preflight_tests
    validation = R / f'build/validation/release-{VERSION}.json'
    validation.parent.mkdir(parents=True, exist_ok=True)
    validation.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    out = dist / f'foxmolika_CubeMX_Source_{VERSION}.zip'
    create_source_archive(out, provenance, R)
    verify_source_archive(out, provenance, R)
    report['source_zip_sha256'] = sha(out)
    (dist / f'发布核验_{VERSION}.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    shutil.copyfile(R / ('变更说明_' + VERSION + '.md'), dist / ('变更说明_' + VERSION + '.md'))
    shutil.copyfile(dist / ('发布核验_' + VERSION + '.json'), dist / ('foxmolika_Validation_' + VERSION + '.json'))
    shutil.copyfile(dist / ('变更说明_' + VERSION + '.md'), dist / ('foxmolika_Changes_' + VERSION + '.md'))
    (dist / ('foxmolika_Provenance_' + VERSION + '.json')).write_text(
        json.dumps(provenance, ensure_ascii=False, indent=2), encoding='utf-8')
    sums = []
    for name in release_asset_names():
        artifact = dist / name
        assert artifact.is_file(), name
        sums.append(sha(artifact) + '  ' + name + '\n')
    (dist / 'SHA256SUMS.txt').write_text(''.join(sums), encoding='utf-8')
    try:
        assert_current(provenance, R)
    except RuntimeError as error:
        raise GateError('source_changed', str(error), hint='确认 HEAD 与 tag 一致、工作区干净后再打包') from error
    print('PASS packaged verified single-file installer, exact-tag source ZIP and synchronized tutorials')
    print('Installer SHA256', sha(setup))
    print('Installer bytes', setup.stat().st_size)
def main():
    """发布入口：同一标签只允许成功打包一次；失败不占用标记，便于修复后重试。"""
    from gate_diagnostics import GateError
    marker = R / 'build/release.done.json'
    if marker.exists() and os.environ.get('RCS_GATE_FORCE') != '1':
        raise GateError(
            'duplicate_invocation',
            '本标签已成功打包过：' + str(marker),
            hint='确认产物无误后可删除该标记，或设置 RCS_GATE_FORCE=1 强制重跑',
            detail={'marker': str(marker), 'content': marker.read_text(encoding='utf-8', errors='replace')},
        )
    _release_body()
    marker.parent.mkdir(parents=True, exist_ok=True)
    marker.write_text(datetime.now(timezone.utc).isoformat() + '\n', encoding='utf-8')

if __name__ == '__main__':
    main()
