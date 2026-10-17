using Godot;

namespace MarioStudy;

/// <summary>Optional CPU capture of the same pixel draw commands, for headless study reports.</summary>
public sealed class StudyRaster : IDisposable
{
    private readonly byte[] pixels = new byte[256 * 240 * 4];
    private readonly Dictionary<Texture2D, (int w, int h, byte[] pixels)> sources = [];
    public Vector2 Offset;
    private void Pixel(int x, int y, Color color)
    {
        if (x < 0 || x >= 256 || y < 0 || y >= 240 || color.A <= 0) return;
        int i = (y * 256 + x) * 4; float a = Math.Clamp(color.A, 0, 1), inv = 1 - a;
        pixels[i] = (byte)Math.Clamp((int)(color.R * 255 * a + pixels[i] * inv), 0, 255);
        pixels[i + 1] = (byte)Math.Clamp((int)(color.G * 255 * a + pixels[i + 1] * inv), 0, 255);
        pixels[i + 2] = (byte)Math.Clamp((int)(color.B * 255 * a + pixels[i + 2] * inv), 0, 255); pixels[i + 3] = 255;
    }
    public void Rect(Rect2 rect, Color color, bool filled = true, float width = 1)
    {
        Vector2 at = rect.Position + Offset; int x0 = (int)MathF.Round(at.X), y0 = (int)MathF.Round(at.Y), x1 = x0 + (int)rect.Size.X, y1 = y0 + (int)rect.Size.Y;
        int stroke = Math.Max(1, (int)width);
        for (int y = Math.Max(0, y0); y < Math.Min(240, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(256, x1); x++)
                if (filled || x < x0 + stroke || x >= x1 - stroke || y < y0 + stroke || y >= y1 - stroke) Pixel(x, y, color);
    }
    public void Texture(Texture2D texture, Rect2 destination, Rect2 source, Color tint)
    {
        if (!sources.TryGetValue(texture, out var s))
        {
            using var image = texture.GetImage(); image.Convert(Image.Format.Rgba8);
            sources[texture] = s = (image.GetWidth(), image.GetHeight(), image.GetData());
        }
        Vector2 at = destination.Position + Offset;
        bool flip = (destination.Size.X < 0) != (source.Size.X < 0);
        bool flipY = (destination.Size.Y < 0) != (source.Size.Y < 0);
        int width = (int)MathF.Abs(destination.Size.X), height = (int)MathF.Abs(destination.Size.Y);
        // Match CanvasItem: negative sizes flip UVs without moving the origin.
        int x0 = (int)MathF.Round(at.X), y0 = (int)MathF.Round(at.Y);
        if (width == 0 || height == 0) return;
        for (int y = Math.Max(0, -y0); y < Math.Min(height, 240 - y0); y++)
            for (int x = Math.Max(0, -x0); x < Math.Min(width, 256 - x0); x++)
            {
                int sx = (int)source.Position.X + (int)((flip ? width - 1 - x : x) * MathF.Abs(source.Size.X) / width);
                int sy = (int)source.Position.Y + (int)((flipY ? height - 1 - y : y) * MathF.Abs(source.Size.Y) / height);
                if (sx < 0 || sx >= s.w || sy < 0 || sy >= s.h) continue;
                int i = (sy * s.w + sx) * 4;
                if (s.pixels[i + 3] == 0) continue;
                Pixel(x0 + x, y0 + y, new Color(s.pixels[i] / 255f * tint.R, s.pixels[i + 1] / 255f * tint.G, s.pixels[i + 2] / 255f * tint.B, s.pixels[i + 3] / 255f * tint.A));
            }
    }
    public void Line(Vector2 from, Vector2 to, Color color, float width)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(from.DistanceTo(to)));
        for (int i = 0; i <= steps; i++) Rect(new Rect2(from.Lerp(to, (float)i / steps), Vector2.One * MathF.Max(1, width)), color);
    }
    public void Circle(Vector2 position, float radius, Color color)
    {
        for (int y = -(int)radius; y <= radius; y++)
            for (int x = -(int)radius; x <= radius; x++)
                if (x * x + y * y <= radius * radius) Pixel((int)(position.X + Offset.X + x), (int)(position.Y + Offset.Y + y), color);
    }
    public void Save(string path)
    {
        using var image = Image.CreateFromData(256, 240, false, Image.Format.Rgba8, pixels);
        image.SavePng(path);
    }
    public void Dispose() => sources.Clear();
}

public partial class Game
{
    private StudyRaster? studyRaster;
    private string studyFramePath = "";
    public new void DrawSetTransform(Vector2 offset, float rotation = 0, Vector2? scale = null)
    { if (studyRaster != null) studyRaster.Offset = offset; base.DrawSetTransform(offset, rotation, scale); }
    public new void DrawRect(Rect2 rect, Color color, bool filled = true, float width = -1, bool antialiased = false)
    { studyRaster?.Rect(rect, color, filled, width); base.DrawRect(rect, color, filled, width, antialiased); }
    public new void DrawTextureRectRegion(Texture2D texture, Rect2 rect, Rect2 source, Color? modulate = null, bool transpose = false, bool clipUv = true)
    { studyRaster?.Texture(texture, rect, source, modulate ?? Colors.White); base.DrawTextureRectRegion(texture, rect, source, modulate, transpose, clipUv); }
    public new void DrawTexture(Texture2D texture, Vector2 position, Color? modulate = null)
    { studyRaster?.Texture(texture, new Rect2(position, texture.GetSize()), new Rect2(Vector2.Zero, texture.GetSize()), modulate ?? Colors.White); base.DrawTexture(texture, position, modulate); }
    public new void DrawLine(Vector2 from, Vector2 to, Color color, float width = -1, bool antialiased = false)
    { studyRaster?.Line(from, to, color, width); base.DrawLine(from, to, color, width, antialiased); }
    public new void DrawCircle(Vector2 position, float radius, Color color, bool filled = true, float width = -1, bool antialiased = false)
    { studyRaster?.Circle(position, radius, color); base.DrawCircle(position, radius, color, filled, width, antialiased); }
    private void FinishStudyFrame()
    {
        if (studyRaster == null) return;
        studyRaster.Save(studyFramePath); studyRaster.Dispose(); studyRaster = null; studyFramePath = "";
    }
}
