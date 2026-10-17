"""Strip local editor-control wiring from portable game copies and archives."""
import json
import re


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
