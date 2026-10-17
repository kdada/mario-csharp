"""Verify a fresh source copy, without reusing this project's .godot build cache."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import tempfile
from project_copy import game_project_text

root = Path(__file__).resolve().parents[1]
reports = root / 'Reports'
reports.mkdir(exist_ok=True)
temp_base = Path(os.environ['TMPDIR']).resolve()
godot = os.environ.get('GODOT_BIN') or shutil.which('godot@4.7.2-ohos.6') or shutil.which('godot')
if not godot:
    raise SystemExit('Godot .NET executable not found.')
with tempfile.TemporaryDirectory(prefix='mario-clean-', dir=temp_base) as directory:
    project = Path(directory).resolve()
    assert project.parent == temp_base and project.name.startswith('mario-clean-')
    (project / 'project.godot').write_text(game_project_text((root / 'project.godot').read_text()))
    for name in ['Mario.csproj', 'NuGet.Config']:
        shutil.copyfile(root / name, project / name)
    for name in ['Scripts', 'Scenes']:
        shutil.copytree(root / name, project / name)
    # Media/layouts are immutable inputs; source and build outputs are isolated.
    for name in ['Assets', 'Data']:
        (project / name).symlink_to(root / name, target_is_directory=True)
    with (reports / 'fresh-build.log').open('w') as log:
        for command in [
            ['dotnet', 'restore', '--configfile', str(project / 'NuGet.Config')],
            ['dotnet', 'build', '--no-restore', '-v', 'minimal'],
            [godot, '--headless', '--path', str(project), '--', '--verify'],
        ]:
            result = subprocess.run(command, cwd=project, stdout=log, stderr=subprocess.STDOUT)
            if result.returncode:
                raise SystemExit(f'Fresh verification failed ({result.returncode}); see Reports/fresh-build.log')
    report = json.loads((project / 'Reports/verification.json').read_text())
    result = {'passed': report['failures'] == 0, 'checks': len(report['checks']), 'failures': report['failures'], 'engine': report['engine'], 'platform': report['platform'], 'timestamp': report['timestamp'], 'build_cache_reused': False, 'inputs': 'fresh source copy, shared immutable media and layouts'}
    (reports / 'fresh-build.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(result, ensure_ascii=False))
    # TemporaryDirectory removes only this verified temporary project, never its input links.
