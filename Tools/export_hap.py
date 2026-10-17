"""Export an unsigned OpenHarmony ARM64 NativeAOT HAP from a clean build copy."""
from pathlib import Path
import argparse
import datetime
import json
import os
import shutil
import subprocess
import tempfile
from project_copy import game_project_text


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    packages = Path.home() / '.oheco/packages'
    parser.add_argument('--godot-devkit', type=Path, default=packages / 'godot-editor/4.7.2-ohos.7')
    parser.add_argument('--dotnet-sdk', type=Path, default=packages / 'dotnet-sdk/10.0.401-ohos.2')
    parser.add_argument('--command-line-tools', type=Path, default=packages / 'command-line-tools/26.0.0.201')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--cache', type=Path)
    args = parser.parse_args()
    project = Path(__file__).resolve().parents[1]
    output = args.output.resolve()
    if output.exists() or output.with_suffix('').exists():
        raise SystemExit('Choose a new output path; existing HAPs and generated projects are preserved.')
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    cache = (args.cache or Path(os.environ['XDG_CACHE_HOME']) / 'mario-csharp' / ('hap-' + stamp)).resolve()
    cache.mkdir(parents=True, exist_ok=False)
    work = cache / 'project'
    work.mkdir()
    for folder in ('Scripts', 'Scenes', 'Assets', 'Data'):
        shutil.copytree(project / folder, work / folder, ignore=shutil.ignore_patterns('__pycache__', '*.import'))
    for name in ('Mario.csproj', 'Mario.sln'):
        shutil.copy2(project / name, work / name)
    devkit, sdk, tools = args.godot_devkit.resolve(), args.dotnet_sdk.resolve(), args.command_line_tools.resolve()
    for required in (devkit / 'bin/godot', sdk / 'bin/dotnet', tools / 'sdk', tools / 'node/bin/node'):
        if not required.exists():
            raise SystemExit('Missing export input: ' + str(required))
    feed = devkit / 'share/godot/GodotSharp/Tools/nupkgs'
    (work / 'NuGet.Config').write_text('<?xml version="1.0"?><configuration><packageSources><clear/><add key="Godot-OpenHarmony" value="' + str(feed) + '"/></packageSources></configuration>\n')
    # Original assets are intentionally ignored by Godot's import pipeline.
    # Inject their raw bytes into the PCK with a build-only editor plugin.
    plugin = work / 'addons/mario_raw_export'
    plugin.mkdir(parents=True)
    shutil.copy2(project / 'Tools/export_assets.gd', plugin / 'plugin.gd')
    (plugin / 'plugin.cfg').write_text('[plugin]\nname="Mario raw media export"\ndescription="Pack directly-read game media"\nauthor="kdada"\nversion="1.0"\nscript="plugin.gd"\n')
    text = game_project_text((project / 'project.godot').read_text())
    text += '\n[editor_plugins]\nenabled=PackedStringArray("res://addons/mario_raw_export/plugin.cfg")\n'
    (work / 'project.godot').write_text(text)
    preset = (project / 'export_presets.cfg').read_text()
    for mode in ('debug', 'release'):
        template = devkit / f'share/godot/templates/openharmony_{mode}_arm64-v8a.zip'
        if not template.is_file():
            raise SystemExit('Missing template: ' + str(template))
        preset = preset.replace(f'custom_template/{mode}=""', f'custom_template/{mode}=' + json.dumps(str(template)))
    (work / 'export_presets.cfg').write_text(preset)
    output.parent.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ)
    for key in ('LD_PRELOAD', 'LD_LIBRARY_PATH', 'NODE_OPTIONS', 'NODE_PATH', 'JAVA_HOME'):
        env.pop(key, None)
    env.update(DOTNET_ROOT=str(sdk), DOTNET_ROOT_ARM64=str(sdk),
               GODOT_OHOS_DEVKIT_ROOT=str(devkit), GODOT_SHARP_ROOT=str(devkit / 'share/godot/GodotSharp'),
               GODOT_NUGET_SOURCE=str(feed), GODOT_OHOS_COMMAND_LINE_TOOLS_ROOT=str(tools),
               GODOT_OHOS_SDK_ROOT=str(tools / 'sdk'),
               GODOT_OHOS_DATA_DIR=str(cache / 'editor-data'), GODOT_OHOS_CACHE_DIR=str(cache / 'editor-cache'),
               DOTNET_CLI_HOME=str(cache / 'dotnet-home'), HVIGOR_USER_HOME=str(Path.home() / '.hvigor'),
               UseSharedCompilation='false', NuGetAudit='false', DSH_GODOT_ALLOW_HEADLESS='0',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1',
               DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true')
    env['PATH'] = str(sdk / 'bin') + os.pathsep + str(tools / 'node/bin') + os.pathsep + env.get('PATH', '')
    commands = [
        [str(sdk / 'bin/dotnet'), 'restore', '--configfile', 'NuGet.Config'],
        [str(sdk / 'bin/dotnet'), 'build', '--no-restore'],
        [str(devkit / 'bin/godot'), '--headless', '--editor', '--path', str(work), '--import', '--quit'],
        [str(devkit / 'bin/godot'), '--headless', '--path', str(work), '--export-release', 'OpenHarmony HAP (Unsigned)', str(output)],
    ]
    print('EXPORT_CACHE ' + str(cache), flush=True)
    with tempfile.TemporaryDirectory(prefix='mario-hap-', dir=os.environ['TMPDIR']) as temporary:
        env.update(TMPDIR=temporary, DOTNET_OHOS_TMPDIR=temporary)
        for index, command in enumerate(commands):
            logfile = cache / f'{index:02d}-export.log'
            print('RUN ' + ' '.join(command) + '\nLOG ' + str(logfile), flush=True)
            with logfile.open('w') as log:
                result = subprocess.run(command, cwd=work, env=env, stdout=log, stderr=subprocess.STDOUT)
            if result.returncode:
                print(logfile.read_text(errors='replace')[-14000:], flush=True)
                raise SystemExit(result.returncode)
    if not output.is_file():
        raise SystemExit('Exporter returned success but did not create the requested HAP.')
    print('UNSIGNED_HAP ' + str(output), flush=True)
    print('HAP_BYTES ' + str(output.stat().st_size), flush=True)


if __name__ == '__main__':
    main()
