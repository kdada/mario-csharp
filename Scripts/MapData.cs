using Godot;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public sealed class RoomData
{
    public string Id { get; set; } = "1-1";
    public string Theme { get; set; } = "Overworld";
    public int Time { get; set; } = 400;
    public float Width { get; set; }
    public float[] Spawn { get; set; } = [-208, 0];
    public List<float[]> Tiles { get; set; } = [];
    public List<float[]> Deco { get; set; } = [];
    public List<EntityData> Entities { get; set; } = [];
    [JsonIgnore] public Vector2 Start => new(Spawn[0], Spawn[1]);
}

public sealed class EntityData
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public Dictionary<string, JsonElement> P { get; set; } = [];
    [JsonIgnore] public Vector2 Position => new(X, Y);
    public float Number(string key, float fallback = 0) => P.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
    public bool Bool(string key, bool fallback = false) => P.TryGetValue(key, out var v) && (v.ValueKind is JsonValueKind.True or JsonValueKind.False) ? v.GetBoolean() : fallback;
    public string Text(string key, string fallback = "") => P.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    public Vector2 Vector(string key, Vector2 fallback = default) => P.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 2 ? new(v[0].GetSingle(), v[1].GetSingle()) : fallback;
}

public sealed class AtlasData
{
    public string Type { get; set; } = "atlas";
    public string Texture { get; set; } = "";
    public Dictionary<string, AtlasCell> Cells { get; set; } = [];
}
public sealed class AtlasCell { public bool Solid { get; set; } }

public sealed class MapCatalog
{
    public readonly Dictionary<string, RoomData> Rooms;
    public readonly Dictionary<int, AtlasData> Atlases;
    public readonly string[] MainLevels = Enumerable.Range(1, 8).SelectMany(w => Enumerable.Range(1, 4).Select(l => $"{w}-{l}")).ToArray();
    public MapCatalog()
    {
        Rooms = JsonSerializer.Deserialize(FileAccess.GetFileAsString("res://Data/rooms.json"), GameJsonContext.Default.Rooms)!.ToDictionary(r => r.Id);
        Atlases = JsonSerializer.Deserialize(FileAccess.GetFileAsString("res://Data/atlas.json"), GameJsonContext.Default.Atlases)!;
        foreach (string id in MainLevels)
            if (!Rooms.TryGetValue(id, out var room) || room.Tiles.Count == 0) throw new InvalidDataException($"Main level {id} has no layout");
    }
    public bool IsSolid(int source, int ax, int ay) => Atlases.TryGetValue(source, out var a) && a.Cells.TryGetValue($"{ax},{ay}", out var cell) && cell.Solid;
    public string ClassicTheme(string id, string mainId)
    {
        if (id == "8-4c") return "CastleWater";
        string original = Rooms[id].Theme;
        if (original is "Underground" or "Underwater" or "Castle" or "CastleWater" or "Bonus") return original;
        if (mainId.EndsWith("-4")) return "Castle";
        return "Overworld";
    }
    public bool IsNight(string id, string mainId) => ClassicTheme(id, mainId) == "Overworld" && (mainId[0] == '3' || mainId[0] == '6');
}
