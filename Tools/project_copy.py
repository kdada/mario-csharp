"""Strip local editor-control wiring from portable game copies and archives."""
import json
import re
import shutil
from pathlib import Path


RAW_EXPORT_PLUGIN = 'res://addons/mario_raw_export/plugin.cfg'


def copy_raw_export_plugin(source: Path, destination: Path) -> None:
    """Keep the game's required export plugin in every portable project copy."""
    relative = Path('addons/mario_raw_export')
    shutil.copytree(source / relative, destination / relative, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns('*.uid', '*.import', '__pycache__'))


def enable_raw_export_plugin(text: str) -> str:
    if RAW_EXPORT_PLUGIN in text:
        return text
    match = re.search(r'(?ms)^\[editor_plugins\]\n(.*?)(?=^\[|\Z)', text)
    if match:
        section = match.group(0)
        enabled = re.search(r'(?m)^enabled=PackedStringArray\((.*?)\)$', section)
        if enabled:
            paths = re.findall(r'"([^"\n]+)"', enabled.group(1)) + [RAW_EXPORT_PLUGIN]
            replacement = 'enabled=PackedStringArray(' + ', '.join(json.dumps(p) for p in paths) + ')'
            section = section[:enabled.start()] + replacement + section[enabled.end():]
        else:
            section += '\nenabled=PackedStringArray(' + json.dumps(RAW_EXPORT_PLUGIN) + ')\n'
        return text[:match.start()] + section + text[match.end():]
    return text.rstrip() + '\n\n[editor_plugins]\nenabled=PackedStringArray(' + json.dumps(RAW_EXPORT_PLUGIN) + ')\n'


def game_project_text(text: str) -> str:
    section = ''
    output = []
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith('[') and stripped.endswith(']'):
            section = stripped[1:-1]
        if section == 'autoload' and stripped.startswith('_dsh_godot_game_helper='):
            continue
        if section == 'editor_plugins' and stripped.startswith('enabled='):
            paths = re.findall(r'"([^"\n]+)"', line)
            paths = [p for p in paths if p != 'res://addons/dsh_godot/plugin.cfg']
            if not paths:
                continue
            line = 'enabled=PackedStringArray(' + ', '.join(json.dumps(p) for p in paths) + ')'
        output.append(line)
    result = '\n'.join(output).rstrip() + '\n'
    result = re.sub(r'\[(?:autoload|editor_plugins)\]\s*(?=\[|\Z)', '', result)
    assert 'res://addons/dsh_godot/' not in result
    return result
