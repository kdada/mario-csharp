"""Drive the original C# game from its title through normal inputs and transitions.
This is distinct from isolated mechanism checks; incomplete attempts exit nonzero.
"""
from pathlib import Path
import argparse
import json
import os
import shutil
import subprocess

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--no-build', action='store_true')
parser.add_argument('--frames', type=int, default=300000)
options = parser.parse_args()
if options.frames < 1:
    parser.error('--frames must be positive')
engine = os.environ.get('GODOT_BIN') or shutil.which('godot@4.7.2-ohos.6') or shutil.which('godot')
if not engine:
    raise SystemExit('Godot .NET is required.')
if not options.no_build:
    subprocess.run(['dotnet', 'restore', '--configfile', str(root / 'NuGet.Config')], cwd=root, check=True)
    subprocess.run(['dotnet', 'build', '--no-restore', '-v', 'minimal'], cwd=root, check=True)
reports = root / 'Reports' / 'Playthrough'
reports.mkdir(parents=True, exist_ok=True)
with (reports / 'latest.log').open('w') as log:
    result = subprocess.run([engine, '--headless', '--path', str(root), '--', '--playthrough', f'--playthrough-frames={options.frames}'], cwd=root, stdout=log, stderr=subprocess.STDOUT)
if (reports / 'latest.json').exists():
    report = json.loads((reports / 'latest.json').read_text())
    print(json.dumps({key: report[key] for key in ['outcome', 'complete', 'frames', 'stage', 'room', 'position', 'lives']}, ensure_ascii=False))
print('Input-driven workflow log:', reports / 'latest.log')
raise SystemExit(result.returncode)
