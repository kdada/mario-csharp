"""Exercise normal Editor PCK exports with and without the required asset plugin."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import tempfile
from project_copy import copy_raw_export_plugin
from verify_export_resources import verify_export_resources

root = Path(__file__).resolve().parents[1]
devkit = Path(os.environ.get('GODOT_DEVKIT', str(Path.home() / '.oheco/packages/godot-editor/4.7.2-ohos.8')))
env = dict(os.environ)
env.update(DSH_GODOT_ALLOW_HEADLESS='0', GODOT_OHOS_DEVKIT_ROOT=str(devkit),
           GODOT_SHARP_ROOT=str(devkit / 'share/godot/GodotSharp'),
           DOTNET_CLI_TELEMETRY_OPTOUT='1')
reports = root / 'Reports/Regression'
reports.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix='mario-raw-export-', dir=os.environ['TMPDIR']) as temporary:
    base = Path(temporary).resolve()
    assert base.parent == Path(os.environ['TMPDIR']).resolve()
    project = base / 'project'
    project.mkdir()
    for folder in ('Assets', 'Data'):
        (project / folder).symlink_to(root / folder, target_is_directory=True)
    copy_raw_export_plugin(root, project)
    (project / 'Game.tscn').write_text('[gd_scene format=3]\n\n[node name="ExportProbe" type="Node"]\n')
    config = 'config_version=5\n\n[application]\nconfig/name="Mario resource export regression"\nrun/main_scene="res://Game.tscn"\n\n'
    enabled = '[editor_plugins]\nenabled=PackedStringArray("res://addons/mario_raw_export/plugin.cfg")\n'
    (project / 'project.godot').write_text(config + enabled)
    (project / 'export_presets.cfg').write_text((root / 'export_presets.cfg').read_text())
    def run(arguments, name):
        logfile = reports / name
        with logfile.open('w') as log:
            result = subprocess.run([str(devkit / 'bin/godot'), '--headless', '--path', str(project)] + arguments,
                                    env=env, cwd=project, stdout=log, stderr=subprocess.STDOUT)
        if result.returncode:
            raise RuntimeError(f'Editor export failed: exit={result.returncode}, log={logfile}')
    positive = base / 'with-assets.pck'
    run(['--export-pack', 'OpenHarmony HAP (Unsigned)', str(positive)], 'raw-export-enabled.log')
    report = verify_export_resources(positive, root)
    # Reproduce the reported bug using the same normal exporter without the plugin.
    (project / 'project.godot').write_text(config)
    negative = base / 'without-plugin.pck'
    run(['--export-pack', 'OpenHarmony HAP (Unsigned)', str(negative)], 'raw-export-disabled.log')
    try:
        verify_export_resources(negative, root)
    except ValueError as error:
        report['missing_plugin_export_rejected'] = True
        report['missing_plugin_error'] = str(error)
    else:
        raise RuntimeError('Regression guard accepted an export with the raw plugin disabled')
    report['route'] = 'ordinary Editor export-pack with checked-in plugin; no export_hap injection'
    report.pop('artifact', None)
    (reports / 'raw-export-resources.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(report, ensure_ascii=False), flush=True)
