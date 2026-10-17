@tool
extends EditorPlugin

# Build tooling only. Gameplay remains C#. The game reads original PNG, MP3,
# WAV, CHR and JSON bytes directly, so these must bypass resource conversion.
class RawMediaExport extends EditorExportPlugin:
	func _get_name() -> String:
		return "MarioRawMedia"

	func _export_begin(_features: PackedStringArray, _debug: bool, _path: String, _flags: int) -> void:
		_add_folder("res://Assets")
		_add_folder("res://Data")

	func _add_folder(path: String) -> void:
		for filename in DirAccess.get_files_at(path):
			if filename.begins_with(".") or filename.ends_with(".import"):
				continue
			var file := path.path_join(filename)
			add_file(file, FileAccess.get_file_as_bytes(file), false)
		for folder in DirAccess.get_directories_at(path):
			if not folder.begins_with("."):
				_add_folder(path.path_join(folder))

var exporter: RawMediaExport

func _enter_tree() -> void:
	exporter = RawMediaExport.new()
	add_export_plugin(exporter)

func _exit_tree() -> void:
	remove_export_plugin(exporter)
