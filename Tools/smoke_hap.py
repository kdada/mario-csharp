"""Load exact HAP payload bytes in an isolated native CLI host without .NET SDK."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('hap', type=Path)
parser.add_argument('--launcher', type=Path, default=Path.home() / '.oheco/packages/godot-editor/4.7.2-ohos.7/bin/godot')
parser.add_argument('--evidence', type=Path, required=True)
args = parser.parse_args()
hap = args.hap.resolve()
evidence = args.evidence.resolve()
evidence.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix='mario-hap-smoke-', dir=os.environ['TMPDIR']) as temporary:
    root = Path(temporary).resolve()
    assert root.parent == Path(os.environ['TMPDIR']).resolve()
    with zipfile.ZipFile(hap) as package:
        for name in package.namelist():
            if name.startswith('libs/arm64-v8a/') and not name.endswith('/'):
                target = root / Path(name).name
                target.write_bytes(package.read(name))
                target.chmod(0o700)
        (root / 'godot.pck').write_bytes(package.read('resources/rawfile/template.pck'))
    executable = root / 'godot'
    shutil.copy2(args.launcher.resolve(), executable)
    executable.chmod(0o700)
    env = dict(os.environ)
    for key in ('LD_LIBRARY_PATH', 'LD_PRELOAD', 'GODOT_SHARP_ROOT', 'GODOT_NUGET_SOURCE', 'NUGET_PACKAGES', 'DOTNET_CLI_HOME', 'DOTNET_OHOS_TMPDIR', 'OHECO_ROOT', 'NODE_PATH', 'NODE_OPTIONS'):
        env.pop(key, None)
    env.update(DOTNET_ROOT='/no-external-dotnet-sdk', DOTNET_ROOT_ARM64='/no-external-dotnet-sdk', DOTNET_MULTILEVEL_LOOKUP='0', PATH='/system/bin', GODOT_OHOS_DATA_DIR=str(root / 'data'), GODOT_OHOS_CACHE_DIR=str(root / 'cache'))
    command = [str(executable), '--headless', '--max-fps', '60', '--', '--hap-smoke']
    logpath = evidence / 'hap-smoke.log'
    with logpath.open('w') as log:
        try:
            result = subprocess.run(command, cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=90)
            status = result.returncode
        except subprocess.TimeoutExpired:
            status = -1
    text = logpath.read_text(errors='replace')
    match = re.search(r'MARIO_HAP_SMOKE_PASS checks=(\d+)', text)
    passed = status == 0 and match is not None and 'ERROR:' not in text
    report = {'file': hap.name, 'sha256': hashlib.sha256(hap.read_bytes()).hexdigest(), 'passed': passed, 'exitCode': status, 'checks': int(match[1]) if match else 0, 'payloadModified': False, 'externalDotnetSdk': False, 'headless': True, 'hapInstalled': False, 'scope': 'Exact HAP PCK and native libraries loaded by CLI; this does not test app installation, Vulkan, audio output or device touch input.'}
    (evidence / 'hap-smoke.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(report, ensure_ascii=False, indent=2))
    if not passed:
        print(text[-12000:])
        raise SystemExit(1)
