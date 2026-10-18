@tool
extends EditorPlugin

# Build tooling only: all gameplay is C#. Assets is deliberately outside Godot's
# importer because the game reads original CHR, PNG, MP3, WAV and JSON bytes.
class RawMediaExport extends EditorExportPlugin:
	func _get_name() -> String:
		return "MarioRawMedia"

	func _export_begin(_features: PackedStringArray, _debug: bool, path: String, _flags: int) -> void:
		_ignore_generated_output(path)
		var files: Dictionary = {}
		_add_folder("res://Assets", files)
		_add_folder("res://Data", files)
		if files.get("res://Assets/smb1.chr", -1) != 8192:
			get_export_platform().add_message(EditorExportPlatform.EXPORT_MESSAGE_ERROR, "Mario assets", "Required Assets/smb1.chr must contain 8192 bytes.")
		var manifest := {"format": 1, "raw_files": files}
		add_file("res://Data/raw-export-manifest.json", JSON.stringify(manifest, "\t").to_utf8_buffer(), false)

	func _add_folder(path: String, files: Dictionary) -> void:
		if not DirAccess.dir_exists_absolute(path):
			get_export_platform().add_message(EditorExportPlatform.EXPORT_MESSAGE_ERROR, "Mario assets", "Missing raw resource directory: " + path)
			return
		for filename in DirAccess.get_files_at(path):
			if filename.begins_with(".") or filename.ends_with(".import"):
				continue
			var file := path.path_join(filename)
			var bytes := FileAccess.get_file_as_bytes(file)
			var error := FileAccess.get_open_error()
			if error != OK:
				get_export_platform().add_message(EditorExportPlatform.EXPORT_MESSAGE_ERROR, "Mario assets", "Cannot read " + file + ": " + error_string(error))
				continue
			add_file(file, bytes, false)
			files[file] = bytes.size()
		for folder in DirAccess.get_directories_at(path):
			if not folder.begins_with("."):
				_add_folder(path.path_join(folder), files)

	func _ignore_generated_output(path: String) -> void:
		# A generated DevEco project contains PNGs too. Keep the editor from adding
		# *.png.import files there, which HarmonyOS would treat as invalid names.
		var root := ProjectSettings.globalize_path("res://").simplify_path().trim_suffix("/")
		var parent := ProjectSettings.globalize_path(path).get_base_dir().simplify_path()
		if parent != root and parent.begins_with(root + "/"):
			DirAccess.make_dir_recursive_absolute(parent)
			var ignore := parent.path_join(".gdignore")
			if not FileAccess.file_exists(ignore):
				var file := FileAccess.open(ignore, FileAccess.WRITE)
				if file:
					file.close()

var exporter: RawMediaExport

func _enter_tree() -> void:
	exporter = RawMediaExport.new()
	add_export_plugin(exporter)

func _exit_tree() -> void:
	remove_export_plugin(exporter)
