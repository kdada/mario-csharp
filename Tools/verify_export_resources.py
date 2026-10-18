"""Check original game assets inside a HAP, PCK or exported DevEco project."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import struct
import zipfile

CHR_SHA256 = '6ae7ffd9795d670954b76c19706d4f355b53f49c38a1da14027faee2c9266f10'


def read_pck(artifact: Path) -> bytes:
    if artifact.is_dir():
        artifact = artifact / 'entry/src/main/resources/rawfile/template.pck'
    if artifact.suffix.lower() == '.hap':
        with zipfile.ZipFile(artifact) as package:
            return package.read('resources/rawfile/template.pck')
    return artifact.read_bytes()


def pck_files(data: bytes) -> dict[str, bytes]:
    if len(data) < 96:
        raise ValueError('PCK header is incomplete')
    magic, version, major, minor, patch, flags, base = struct.unpack_from('<6IQ', data)
    if magic != 0x43504447 or version not in (2, 3, 4) or flags & 1:
        raise ValueError(f'Unsupported/encrypted PCK: magic={magic:x}, version={version}, flags={flags}')
    cursor = struct.unpack_from('<Q', data, 32)[0] if version >= 3 else 96
    count = struct.unpack_from('<I', data, cursor)[0]
    cursor += 4
    files = {}
    for _ in range(count):
        size = struct.unpack_from('<I', data, cursor)[0]
        cursor += 4
        name = data[cursor:cursor + size].rstrip(b'\0').decode().removeprefix('res://')
        cursor += size
        offset, length = struct.unpack_from('<QQ', data, cursor)
        cursor += 32  # offset, length and MD5
        file_flags = struct.unpack_from('<I', data, cursor)[0]
        cursor += 4
        if file_flags & 1:
            raise ValueError('Encrypted game file: ' + name)
        start = base + offset
        if start < 0 or start + length > len(data):
            raise ValueError('Invalid packed range: ' + name)
        files[name] = data[start:start + length]
    return files


def raw_source_files(project: Path):
    def descend(directory: Path):
        with os.scandir(directory) as entries:
            for entry in entries:
                if entry.name.startswith('.') or entry.name.endswith('.import'):
                    continue
                if entry.is_dir():
                    yield from descend(Path(entry.path))
                elif entry.is_file():
                    yield Path(entry.path)
    for name in ('Assets', 'Data'):
        yield from descend(project / name)


def verify_export_resources(artifact: Path, project: Path) -> dict:
    packed = pck_files(read_pck(artifact))
    missing = []
    changed = []
    total = 0
    groups = {}
    expected_manifest = {}
    for path in raw_source_files(project):
        name = path.relative_to(project).as_posix()
        data = path.read_bytes()
        expected_manifest['res://' + name] = len(data)
        total += 1
        groups[name.split('/')[0]] = groups.get(name.split('/')[0], 0) + 1
        if name not in packed:
            missing.append(name)
        elif packed[name] != data:
            changed.append(name)
    if missing or changed:
        raise ValueError(f'Game resources missing={len(missing)}, changed={len(changed)}; '
                         f'missing examples={missing[:12]}, changed examples={changed[:12]}')
    chr_data = packed.get('Assets/smb1.chr', b'')
    digest = hashlib.sha256(chr_data).hexdigest()
    if len(chr_data) != 8192 or digest != CHR_SHA256:
        raise ValueError(f'Invalid CHR: bytes={len(chr_data)}, sha256={digest}')
    manifest = json.loads(packed.get('Data/raw-export-manifest.json', b'{}'))
    if manifest.get('raw_files') != expected_manifest:
        raise ValueError('Missing/incomplete raw export manifest; enable Mario raw media export plugin')
    return {'passed': True, 'artifact': str(artifact), 'pck_entries': len(packed),
            'raw_files': total, 'raw_groups': groups, 'chr_bytes': len(chr_data),
            'chr_sha256': digest, 'missing': missing, 'changed': changed,
            'raw_manifest_matches_source': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('artifact', type=Path)
    parser.add_argument('--project', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    report = verify_export_resources(args.artifact.resolve(), args.project.resolve())
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(report, ensure_ascii=False))


if __name__ == '__main__':
    main()
