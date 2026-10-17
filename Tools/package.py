"""Create a local source/media study archive; never upload or publish it."""
from pathlib import Path
import hashlib
import json
import zipfile
import argparse
import io
import os
from project_copy import game_project_text

root = Path(__file__).resolve().parents[1]
output = root / 'Deliverables'
output.mkdir(exist_ok=True)
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--name', default='Mario-CSharp-Study.zip')
options = parser.parse_args()
if Path(options.name).name != options.name or not options.name.endswith('.zip'):
    raise SystemExit('--name must be a plain .zip filename')
archive = output / options.name
if archive.exists():
    raise SystemExit('Archive already exists; preserve it and choose a new filename before repackaging.')
inputs = [root / name for name in ['project.godot', 'Mario.csproj', 'NuGet.Config', '.gitignore', 'README.md', 'SOURCES.md', 'VERIFICATION.md']]
def source_files(directory):
    # DirEntry caches type information; avoid resolving every ancestor on hmdfs.
    with os.scandir(directory) as entries:
        for entry in entries:
            if entry.is_symlink():
                raise SystemExit('Refuse symbolic link in archive inputs: ' + entry.path)
            if entry.is_dir(follow_symlinks=False):
                if entry.name != '__pycache__':
                    yield from source_files(Path(entry.path))
            elif entry.is_file(follow_symlinks=False) and not entry.name.endswith('.import'):
                yield Path(entry.path)
for folder in ['Scripts', 'Scenes', 'Data', 'Assets', 'Tools', 'Reports', 'Licenses']:
    inputs.extend(source_files(root / folder))
inputs.sort(key=lambda p: str(p.relative_to(root)))
assert all(p.is_relative_to(root) for p in inputs)
# All recursive inputs came from non-symlink directory entries below root.
assert not any(p.is_symlink() for p in inputs if p.parent == root)
assert not any(p.suffix in ['.gd', '.dll'] or '.godot' in p.parts or 'addons' in p.parts for p in inputs)
buffer = io.BytesIO()
with zipfile.ZipFile(buffer, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for path in inputs:
        name = 'Mario-CSharp-Study/' + path.relative_to(root).as_posix()
        if path.name == 'project.godot':
            z.writestr(name, game_project_text(path.read_text()))
        else:
            z.write(path, name)
archive.write_bytes(buffer.getbuffer())
persisted = archive.read_bytes()
with zipfile.ZipFile(io.BytesIO(persisted)) as z:
    bad = z.testzip()
    if bad:
        raise SystemExit('Archive CRC check failed: ' + bad)
digest = hashlib.sha256(persisted).hexdigest()
(archive.with_suffix('.zip.sha256')).write_text(digest + '  ' + archive.name + '\n')
print(json.dumps({'archive': str(archive), 'files': len(inputs), 'bytes': archive.stat().st_size, 'sha256': digest, 'crc_ok': True}, ensure_ascii=False))
