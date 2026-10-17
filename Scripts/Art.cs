using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

/// <summary>Builds the original NES artwork from public CHR image data and declarative sprite recipes.</summary>
public sealed class Art : IDisposable
{
    private readonly byte[] chr = FileAccess.GetFileAsBytes("res://Assets/smb1.chr");
    private readonly Dictionary<string, Texture2D> textures = [];
    private readonly Dictionary<string, JsonDocument> documents = [];
    public readonly Texture2D Glyphs;
    public string Theme = "Overworld";
    public int GeneratedSheets { get; private set; }
    public void Dispose()
    {
        foreach (var texture in textures.Values) texture.Dispose(); textures.Clear(); Glyphs.Dispose();
        foreach (var document in documents.Values) document.Dispose(); documents.Clear();
    }

    public Art()
    {
        using var image = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);
        Color[] palette = [Colors.Transparent, Colors.White, Colors.White, Colors.White];
        for (int i = 0; i < 256; i++) BlitChr(image, 256 + i, new(i % 16 * 8, i / 16 * 8), palette);
        Glyphs = ImageTexture.CreateFromImage(image);
    }
    public JsonElement Json(string path)
    {
        if (!documents.TryGetValue(path, out var doc)) documents[path] = doc = JsonDocument.Parse(FileAccess.GetFileAsString(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return doc.RootElement;
    }
    public Texture2D Texture(string relativePath)
    {
        string path = relativePath.StartsWith("res://") ? relativePath : "res://Assets/Sprites/" + relativePath;
        if (textures.TryGetValue(path, out var result)) return result;
        using var image = Image.LoadFromFile(path);
        if (image == null || image.IsEmpty()) throw new InvalidDataException("Missing image " + path);
        image.Convert(Image.Format.Rgba8);
        string recipePath = path.Replace("res://Assets/Sprites/", "res://Assets/Recipe/Sprites/").Replace(".png", ".json");
        if (FileAccess.FileExists(recipePath))
        {
            var recipe = Json(recipePath);
            string paletteBase = recipe.GetProperty("palette_base").GetString()!;
            var tiles = GD.StrToVar(recipe.GetProperty("tiles").GetString()!).AsGodotDictionary();
            Variant palettes = GD.StrToVar(recipe.GetProperty("palettes").GetString()!);
            var groups = new Godot.Collections.Dictionary();
            if (palettes.VariantType == Variant.Type.Array) groups[paletteBase] = palettes;
            else groups = palettes.AsGodotDictionary();
            var size = GD.StrToVar(recipe.GetProperty("sheet_size").GetString()!).AsVector2I();
            int columns = int.Parse(recipe.GetProperty("columns").GetString()!);
            foreach (var group in groups)
            {
                string groupName = group.Key.AsString();
                var paletteDoc = Json($"res://Assets/Recipe/Palettes/Default/{groupName}.json").GetProperty("palettes");
                int column = 0, row = 0;
                foreach (Variant variant in group.Value.AsGodotArray())
                {
                    string name = variant.AsString();
                    Color[] palette = paletteDoc.GetProperty(name).EnumerateArray().Select(v => Color.FromString(v.GetString()!, Colors.Transparent)).ToArray();
                    foreach (var tile in tiles)
                    {
                        Vector2 position = tile.Key.AsVector2();
                        var values = tile.Value.AsGodotDictionary();
                        string tileGroup = values.TryGetValue("palette", out var p) ? p.AsString() : paletteBase;
                        if (tileGroup != groupName) continue;
                        int index = values["index"].AsInt32();
                        bool flipX = values.TryGetValue("flip_h", out var fx) && fx.AsBool();
                        bool flipY = values.TryGetValue("flip_v", out var fy) && fy.AsBool();
                        BlitChr(image, index, (Vector2I)position + new Vector2I(column * size.X, row * size.Y), palette, flipX, flipY, true);
                    }
                    if (++column >= columns) { column = 0; row++; }
                }
            }
            GeneratedSheets++;
        }
        result = ImageTexture.CreateFromImage(image);
        textures[path] = result;
        return result;
    }
    private void BlitChr(Image image, int index, Vector2I at, Color[] palette, bool flipX = false, bool flipY = false, bool greenOnly = false)
    {
        if (index < 0 || index * 16 + 15 >= chr.Length) throw new InvalidDataException("CHR tile out of range");
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int px = at.X + x, py = at.Y + y;
                if (px < 0 || py < 0 || px >= image.GetWidth() || py >= image.GetHeight()) continue;
                Color old = image.GetPixel(px, py);
                if (greenOnly && !(old.G > .98f && old.R < .02f && old.B < .02f)) continue;
                int bit = flipX ? x : 7 - x, row = flipY ? 7 - y : y;
                int value = ((chr[index * 16 + row] >> bit) & 1) | (((chr[index * 16 + row + 8] >> bit) & 1) << 1);
                image.SetPixel(px, py, palette[value]);
            }
    }
    public void Sprite(Game canvas, string path, Vector2 feet, string animation = "Idle", float time = 0, bool flip = false, Color? tint = null, int variant = 0, float? clipBottom = null, bool flipVertical = false)
    {
        string jsonPath = "res://Assets/Sprites/" + path.Replace(".png", ".json");
        string imagePath = "res://Assets/Sprites/" + path;
        if (!FileAccess.FileExists(imagePath) && FileAccess.FileExists(jsonPath) && Json(jsonPath).TryGetProperty("variations", out var all))
        {
            JsonElement v = all;
            for (int depth = 0; depth < 8; depth++)
            {
                if (v.TryGetProperty("source", out var file)) { imagePath = imagePath[..(imagePath.LastIndexOf('/') + 1)] + file.GetString(); break; }
                if (v.TryGetProperty("link", out var link) && all.TryGetProperty(link.GetString()!, out var linked)) { v = linked; continue; }
                if (v.TryGetProperty(Theme, out var theme)) { v = theme; continue; }
                if (v.TryGetProperty("SMB1", out var smb)) { v = smb; continue; }
                if (v.TryGetProperty("default", out var def)) { v = def; continue; }
                break;
            }
        }
        Texture2D texture = Texture(imagePath);
        Rect2 source;
        JsonElement animations = default;
        bool hasAnimation = FileAccess.FileExists(jsonPath) && Json(jsonPath).TryGetProperty("animations", out animations);
        if (hasAnimation)
        {
            if (!animations.TryGetProperty(animation, out var anim)) anim = animations.EnumerateObject().First().Value;
            if (anim.TryGetProperty("link", out var link)) anim = animations.GetProperty(link.GetString()!);
            var frames = anim.GetProperty("frames");
            float speed = anim.TryGetProperty("speed", out var sp) ? sp.GetSingle() : 6;
            int count = frames.GetArrayLength();
            int frameIndex = (int)(MathF.Max(0, time) * speed);
            frameIndex = anim.TryGetProperty("loop", out var loop) && !loop.GetBoolean() ? Math.Min(count - 1, frameIndex) : frameIndex % count;
            var frame = frames[frameIndex];
            source = new(frame[0].GetSingle(), frame[1].GetSingle(), frame[2].GetSingle(), frame[3].GetSingle());
        }
        else source = new(0, 0, texture.GetWidth(), texture.GetHeight());
        if (FileAccess.FileExists(jsonPath) && Json(jsonPath).TryGetProperty("variations", out var variations))
        {
            JsonElement v = variations;
            for (int depth = 0; depth < 6 && v.ValueKind == JsonValueKind.Object; depth++)
            {
                if (v.TryGetProperty("rect", out var rect))
                {
                    source.Position += new Vector2(rect[0].GetSingle(), rect[1].GetSingle());
                    if (!hasAnimation) source.Size = new Vector2(rect[2].GetSingle(), rect[3].GetSingle());
                    break;
                }
                if (v.TryGetProperty("link", out var linked) && variations.TryGetProperty(linked.GetString()!, out var linkedVariation)) { v = linkedVariation; continue; }
                if (v.TryGetProperty(Theme, out var themed)) { v = themed; continue; }
                if (v.TryGetProperty("SMB1", out var smb)) { v = smb; continue; }
                if (v.TryGetProperty("default", out var def)) { v = def; continue; }
                break;
            }
        }
        source.Position += new Vector2(0, variant * source.Size.Y);
        Vector2 at = new(MathF.Round(feet.X - source.Size.X / 2), MathF.Round(feet.Y - source.Size.Y));
        if (clipBottom.HasValue)
        {
            float visibleHeight = Math.Clamp(MathF.Round(clipBottom.Value) - at.Y, 0, source.Size.Y);
            if (visibleHeight <= 0) return;
            source.Size = new(source.Size.X, visibleHeight);
        }
        Rect2 dest = new(at, source.Size);
        // Godot treats a negative width as a UV flip and preserves this top-left.
        // Moving the origin by the frame width would displace the visible sprite.
        if (flip) dest.Size *= new Vector2(-1, 1);
        if (flipVertical) dest.Size *= new Vector2(1, -1);
        canvas.DrawTextureRectRegion(texture, dest, source, tint ?? Colors.White, false);
    }
    public void Text(Game canvas, string text, Vector2 at, Color? color = null, int scale = 1)
    {
        int x = 0, y = 0;
        foreach (char c in text.ToUpperInvariant())
        {
            if (c == '\n') { y += 10; x = 0; continue; }
            int glyph = c switch { >= '0' and <= '9' => c - '0', >= 'A' and <= 'Z' => c - 'A' + 10, '-' => 40, '!' => 43, '.' => 36, ':' => 44, '*' => 46, _ => -1 };
            if (glyph >= 0) canvas.DrawTextureRectRegion(Glyphs, new Rect2(at + new Vector2(x * scale, y * scale), new Vector2(8 * scale, 8 * scale)), new Rect2(glyph % 16 * 8, glyph / 16 * 8, 8, 8), color ?? Colors.White);
            x += 8;
        }
    }
    public void Tile(Game canvas, string path, Vector2 at, int ax, int ay, int size = 16)
    {
        var tex = Texture(path);
        if ((ax + 1) * size > tex.GetWidth() || (ay + 1) * size > tex.GetHeight()) return;
        canvas.DrawTextureRectRegion(tex, new Rect2(at.Round(), new Vector2(size, size)), new Rect2(ax * size, ay * size, size, size));
    }
}
