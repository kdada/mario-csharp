"""Build and run the original C# game's native, headless verification suite."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
godot = os.environ.get('GODOT_BIN') or shutil.which('godot@4.7.2-ohos.6') or shutil.which('godot')
if not godot:
    raise SystemExit('Godot .NET is required. Set GODOT_BIN to its executable path.')
subprocess.run(['dotnet', 'restore', '--configfile', str(root / 'NuGet.Config')], cwd=root, check=True)
subprocess.run(['dotnet', 'build', '--no-restore', '-v', 'minimal'], cwd=root, check=True)
reports = root / 'Reports'
reports.mkdir(exist_ok=True)
with (reports / 'run-verify.log').open('w') as log:
    result = subprocess.run([godot, '--headless', '--path', str(root), '--', '--verify'], cwd=root, stdout=log, stderr=subprocess.STDOUT)
if result.returncode:
    print((reports / 'run-verify.log').read_text())
    raise SystemExit(result.returncode)
report = json.loads((reports / 'verification.json').read_text())
print(f"{len(report['checks']) - report['failures']}/{len(report['checks'])} checks passed on {report['platform']}.")
print(f"Report: {reports / 'verification.json'}")
print(f"CPU pixel preview (not a GPU/window capture): {reports / 'gameplay-preview.png'}")
raise SystemExit(1 if report['failures'] else 0)
