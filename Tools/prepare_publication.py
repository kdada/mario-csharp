"""Create a clean public source checkout, excluding local editor integration."""
from pathlib import Path
import argparse
import json
import shutil
from project_copy import game_project_text, copy_raw_export_plugin

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('destination', type=Path)
args = parser.parse_args()
source = Path(__file__).resolve().parents[1]
destination = args.destination.resolve()
if destination.exists():
    raise SystemExit('Choose a new destination; existing publication checkouts are preserved.')
if destination == source or destination.is_relative_to(source):
    raise SystemExit('Publication checkout must be outside the active Editor project.')
destination.mkdir(parents=True)
for folder in ('Scripts', 'Scenes', 'Assets', 'Data', 'Licenses', 'Tools'):
    shutil.copytree(source / folder, destination / folder, ignore=shutil.ignore_patterns('__pycache__', '*.import'))
for name in ('Mario.csproj', 'Mario.sln', 'export_presets.cfg'):
    shutil.copy2(source / name, destination / name)
(destination / 'project.godot').write_text(game_project_text((source / 'project.godot').read_text()))
copy_raw_export_plugin(source, destination)
(destination / 'global.json').write_text('{"sdk":{"version":"10.0.401","rollForward":"latestPatch"}}\n')
(destination / '.gitignore').write_text(''' .godot/
bin/
obj/
__pycache__/
*.pyc
*.user
*.userprefs
addons/dsh_godot/
dist/
Deliverables/
*.hap
*.app
*.p12
*.p7b
*.cer
*.keystore
.env
local.properties
'''.lstrip())
feed = destination / 'ThirdParty/GodotNuGet'
feed.mkdir(parents=True)
devkit = Path.home() / '.oheco/packages/godot-editor/4.7.2-ohos.8'
manifest = json.loads((devkit / 'godot-devkit.json').read_text())
for name in manifest['godotPackages']:
    shutil.copy2(devkit / 'share/godot/GodotSharp/Tools/nupkgs' / name, feed / name)
shutil.copy2(devkit / 'godot-devkit.json', feed / 'devkit.json')
license_path = Path.home() / 'dev/ohos/godot-editor/LICENSE.txt'
if not license_path.is_file():
    raise SystemExit('Godot license must be located before publishing bundled SDK packages.')
shutil.copy2(license_path, feed / 'LICENSE.txt')
(destination / 'NuGet.Config').write_text('''<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="Godot-OpenHarmony" value="ThirdParty/GodotNuGet" />
  </packageSources>
</configuration>
''')
reports = destination / 'Reports'
reports.mkdir()
for name in ('verification.json', 'gui-verification.json'):
    shutil.copy2(source / 'Reports' / name, reports / name)
origins = (source / 'SOURCES.md').read_text()
origins = origins.replace('本工程仅供本地研究，未上传或发布。', '本仓库公开独立实现的 C# 程序及其构建材料。')
origins = origins.replace('此工程未做公开发行或素材再分发准备。', '本仓库及构建包保留上述来源和权利归属；不将第三方媒体或商标表述为本项目原创，也不把第三方程序许可证解释为 Nintendo 素材的授权。')
(destination / 'SOURCES.md').write_text(origins)
print(json.dumps({'checkout': str(destination), 'runtimeSourceIdentical': all((destination / 'Scripts' / p.name).read_bytes() == p.read_bytes() for p in (source / 'Scripts').iterdir() if p.is_file()), 'debugIntegrationExcluded': 'dsh_godot' not in (destination / 'project.godot').read_text(), 'mediaFiles': sum(1 for p in (destination / 'Assets').rglob('*') if p.is_file())}, ensure_ascii=False))
