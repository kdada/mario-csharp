"""Audit the unsigned HAP, NativeAOT payload, and every directly-read game asset."""
from pathlib import Path
import argparse
import hashlib
import json
import struct
import zipfile


def pck_entries(data):
    magic, version, _, _, _, flags, base = struct.unpack_from('<6IQ', data)
    assert magic == 0x43504447 and version in (2, 3, 4) and not flags & 1
    cursor = struct.unpack_from('<Q', data, 32)[0] if version >= 3 else 96
    count, = struct.unpack_from('<I', data, cursor)
    cursor += 4
    entries = {}
    for _ in range(count):
        length, = struct.unpack_from('<I', data, cursor)
        cursor += 4
        name = data[cursor:cursor + length].rstrip(b'\0').decode().removeprefix('res://')
        cursor += length
        offset, size = struct.unpack_from('<QQ', data, cursor)
        cursor += 16
        digest = data[cursor:cursor + 16]
        cursor += 16
        entry_flags, = struct.unpack_from('<I', data, cursor)
        cursor += 4
        content = data[base + offset:base + offset + size]
        assert not entry_flags and name not in entries and len(content) == size, name
        assert hashlib.md5(content).digest() == digest, name
        entries[name] = content
    return entries


def elf_metadata(data):
    assert data[:6] == b'\x7fELF\x02\x01' and struct.unpack_from('<HH', data, 16) == (3, 183)
    offset, = struct.unpack_from('<Q', data, 40)
    size, count, names_index = struct.unpack_from('<HHH', data, 58)
    sections = [struct.unpack_from('<IIQQQQIIQQ', data, offset + i * size) for i in range(count)]
    header = sections[names_index]
    names = data[header[4]:header[4] + header[5]]
    signed = any(names[s[0]:].split(b'\0', 1)[0] == b'.codesign' and s[5] > 0 for s in sections)
    exports = set()
    for section in sections:
        if section[1] != 11:
            continue
        header = sections[section[6]]
        strings = data[header[4]:header[4] + header[5]]
        for position in range(section[4], section[4] + section[5], section[9]):
            name, info, _, defined, _, _ = struct.unpack_from('<IBBHQQ', data, position)
            if defined and info >> 4 in (1, 2):
                exports.add(strings[name:strings.index(b'\0', name)].decode())
    return signed, exports


def audit(hap, project):
    raw = hap.read_bytes()
    with zipfile.ZipFile(hap) as package:
        assert package.testzip() is None, 'HAP ZIP CRC failed'
        names = package.namelist()
        assert len(names) == len(set(names)), 'Duplicate HAP entries'
        assert not any(n.endswith(('.dll', '.dbg', '.p12', '.p7b', '.keystore')) or any(x in n for x in ('libcoreclr', 'libclrjit', 'libhostfxr')) for n in names)
        module = json.loads(package.read('module.json'))
        assert module['app']['bundleName'] == 'org.kdada.mariocsharp'
        permissions = [p['name'] for p in module['module'].get('requestPermissions', [])]
        assert not any(p.endswith(('LOAD_INDEPENDENT_LIBRARY', 'ALLOW_WRITABLE_CODE_MEMORY')) for p in permissions)
        template = json.loads(package.read('resources/rawfile/godot_template.json'))
        assert template['hostAbi'] == 4 and template['monoEnabled'] and template['nativeAotEnabled'] and template['target'] == 'template_release'
        libraries = {}
        required = ('libgodot.so', 'libentry.so', 'libgodot-csharp-game.so', 'libicudata.so.78', 'libicuuc.so.78', 'libicui18n.so.78', 'libcrypto.so.3', 'libssl.so.3', 'libc++_shared.so')
        for name in required:
            data = package.read('libs/arm64-v8a/' + name)
            signed, exports = elf_metadata(data)
            assert signed, 'Missing ELF platform code signature: ' + name
            if name == 'libgodot-csharp-game.so':
                assert 'godotsharp_game_main_init' in exports
            libraries[name] = {'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(), 'platformCodesign': signed}
        pack = package.read('resources/rawfile/template.pck')
        files = pck_entries(pack)
        mode = json.loads(files['.godot/mono/openharmony_aot.json'])
        assert mode == {'schemaVersion': 1, 'mode': 'native-aot', 'library': 'libgodot-csharp-game.so'}
        assert 'Scenes/Game.tscn' in files or 'Scenes/Game.tscn.remap' in files
        assert b'_dsh_godot_game_helper' not in files['project.binary']
        assert not any(n.startswith(('addons/', 'Tools/', 'Reports/', 'Deliverables/')) for n in files)
        assert not any(n.endswith(('.dll', '.dbg', '.deps.json', '.runtimeconfig.json')) for n in files)
        assert all(not content.strip() for n, content in files.items() if n.endswith('.cs'))
        matched = 0
        for folder in ('Assets', 'Data'):
            for source in (project / folder).rglob('*'):
                if not source.is_file() or source.name.startswith('.') or source.name.endswith('.import'):
                    continue
                name = source.relative_to(project).as_posix()
                assert name in files and files[name] == source.read_bytes(), 'Missing or changed raw game asset: ' + name
                matched += 1
        end = 0
        for entry in package.infolist():
            header = struct.unpack_from('<4s5H3I2H', raw, entry.header_offset)
            assert header[0] == b'PK\x03\x04'
            last = entry.header_offset + 30 + header[-2] + header[-1] + entry.compress_size
            if entry.flag_bits & 8:
                if raw[last:last + 4] == b'PK\x07\x08':
                    last += 4
                last += 20 if max(entry.file_size, entry.compress_size) >= 0xffffffff else 12
            end = max(end, last)
        footer = raw.rfind(b'PK\x05\x06')
        directory_size, = struct.unpack_from('<I', raw, footer + 12)
        signing_gap = package.start_dir - end
        footer_gap = footer - package.start_dir - directory_size
        assert signing_gap == 0 and footer_gap == 0, 'Unexpected HAP signature data'
    return {'file': hap.name, 'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest(), 'zipCrcValid': True, 'hapSigned': False, 'bundleName': module['app']['bundleName'], 'deviceTypes': module['module']['deviceTypes'], 'permissions': permissions, 'template': template, 'nativeAot': mode, 'nativeLibraries': libraries, 'pckBytes': len(pack), 'pckEntries': len(files), 'rawAssetsMatched': matched, 'editorHelperExcluded': True, 'externalDotnetRuntime': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('hap', type=Path)
    parser.add_argument('--project', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.hap.resolve(), args.project.resolve())
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(result, ensure_ascii=False, indent=2))
