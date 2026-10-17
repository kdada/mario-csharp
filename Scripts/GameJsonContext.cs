using System.Text.Json.Serialization;

namespace MarioStudy;

// The standalone OpenHarmony game uses NativeAOT, which cannot discover JSON
// metadata through reflection. Keep gameplay data and saved progress explicit.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<RoomData>), TypeInfoPropertyName = "Rooms")]
[JsonSerializable(typeof(Dictionary<int, AtlasData>), TypeInfoPropertyName = "Atlases")]
[JsonSerializable(typeof(Profile), TypeInfoPropertyName = "Progress")]
internal partial class GameJsonContext : JsonSerializerContext
{
}
