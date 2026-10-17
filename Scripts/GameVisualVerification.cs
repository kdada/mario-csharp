using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    // These assertions inspect native pixels against collider geometry and independent
    // atlas references. Successful draw calls or a CPU replay alone are insufficient.
    public async void RunVisualVerification(string label)
    {
        verifying = true;
        await VerifyVisualRegressions(label == "before" ? "before" : "after");
    }

    private async Task VerifyVisualRegressions(string label = "latest")
    {
        int first = verificationChecks.Count, failuresBefore = verificationFailures;
        bool native = DisplayServer.GetName() != "headless";
        bool physicsEnabled = IsPhysicsProcessing();
        SetPhysicsProcess(false);
        DirAccess.MakeDirRecursiveAbsolute("res://Reports/Regression");
        try
        {
            for (int power = 0; power <= 2; power++)
                foreach (bool left in new[] { false, true })
                {
                    Block question = HeadHitFixture(power, left);
                    bool hit = false;
                    for (int frame = 0; frame < 60 && !hit; frame++)
                    {
                        TickPlayer(1f / 60, new Controls(0, true, frame == 0, false, false, false, false));
                        hit = Player.HitCeiling;
                    }
                    Check($"head contact power={power} left={left}", hit && question.Used && MathF.Abs(Player.Box.Position.Y - question.Box.End.Y) < .001f,
                        $"head={Player.Box.Position}, block={question.Box}, used={question.Used}");
                    if (native)
                    {
                        Invulnerable = 1;
                        using var background = await RegressionFrame();
                        Invulnerable = 0;
                        using var impact = await RegressionFrame($"{label}-impact-{power}-{left}");
                        using var player = DifferenceImage(impact, background);
                        Rect2I visible = OpaqueBounds(player);
                        float expectedHead = question.Box.End.Y + 208;
                        float expectedCenter = Player.Position.X - MathF.Round(CameraX);
                        Check($"visible head meets hit block power={power} left={left}",
                            visible.HasArea() && MathF.Abs(visible.Position.Y - expectedHead) <= 1 && MathF.Abs(visible.GetCenter().X - expectedCenter) <= 2,
                            $"visible={visible}, expected headY={expectedHead}, centerX={expectedCenter}");
                        using var annotated = (Image)impact.Duplicate();
                        Outline(annotated, ScreenRect(Player.Box), new Color("00ff00"));
                        Outline(annotated, ScreenRect(question.Box), new Color("00ffff"));
                        annotated.SavePng($"res://Reports/Regression/{label}-contact-{power}-{left}.png");
                    }
                }
            foreach (bool left in new[] { false, true })
                foreach (bool outsideLeft in new[] { false, true })
                {
                    Block question = HeadHitFixture(0, left);
                    Player.Position = new(outsideLeft ? question.Box.Position.X - Player.Width / 2 - .25f : question.Box.End.X + Player.Width / 2 + .25f, 0);
                    for (int frame = 0; frame < 45; frame++) TickPlayer(1f / 60, new Controls(0, true, frame == 0, false, false, false, false));
                    Check($"outside edge misses question left={left} edge={outsideLeft}", !question.Used);
                }
            if (native)
            {
                for (int power = 0; power <= 2; power++)
                {
                    StartGame(0, true); Power = power; LoadRoom("1-1", false, new Vector2(40, 0));
                    State = Screen.Playing; Player.Grounded = true; CameraX = 0; Clock = animationTime = 0; showTouch = false;
                    Invulnerable = 1;
                    using var background = await RegressionFrame();
                    Invulnerable = 0; FacingLeft = false;
                    using var right = await RegressionFrame($"{label}-pose-{power}-right");
                    FacingLeft = true;
                    string stem = $"{label}-pose-{power}-left";
                    using var left = await RegressionFrame(stem);
                    using var rightPlayer = DifferenceImage(right, background);
                    using var leftPlayer = DifferenceImage(left, background);
                    Rect2I rightBounds = OpaqueBounds(rightPlayer), leftBounds = OpaqueBounds(leftPlayer);
                    Check($"right sprite anchored power={power}", rightBounds.HasArea() && MathF.Abs(rightBounds.GetCenter().X - 40) <= 1 && rightBounds.End.Y == 208, rightBounds.ToString());
                    Check($"left sprite anchored power={power}", leftBounds.HasArea() && MathF.Abs(leftBounds.GetCenter().X - 40) <= 1 && leftBounds.End.Y == 208, leftBounds.ToString());
                    int mirrorMismatch = 0;
                    for (int y = 176; y < 208; y++)
                        for (int x = 0; x < 256; x++)
                        {
                            int reflected = 79 - x;
                            Color expected = reflected >= 0 && reflected < 256 ? rightPlayer.GetPixel(reflected, y) : Colors.Transparent;
                            if (!SamePixel(leftPlayer.GetPixel(x, y), expected)) mirrorMismatch++;
                        }
                    Check($"turning mirrors around same feet power={power}", mirrorMismatch == 0, $"different pixels={mirrorMismatch}");
                    using var cpu = new Image(); cpu.LoadPngFromBuffer(FileAccess.GetFileAsBytes($"res://Reports/Regression/{stem}.cpu.png"));
                    int cpuMismatch = 0;
                    for (int y = 176; y < 208; y++)
                        for (int x = 0; x < 112; x++) if (!SamePixel(cpu.GetPixel(x, y), left.GetPixel(x, y))) cpuMismatch++;
                    Check($"CPU and GPU flipped sprite agree power={power}", cpuMismatch == 0, $"different pixels={cpuMismatch}");
                }
                foreach (var fixture in new[] { ("1-1", 0), ("1-2", 1), ("2-2", 5), ("1-4", 3) })
                {
                    StartGame(fixture.Item2, true); LoadRoom(fixture.Item1, false); State = Screen.Playing; CameraX = 0; Clock = 0; showTouch = false;
                    using var background = await RegressionFrame();
                    var block = new Block { Center = new(128, -80), Kind = "CoinQuestionBlock", Item = "Coin" };
                    World.Blocks.Add(block); HitBlock(block); block.Bump = 0;
                    using var used = await RegressionFrame($"{label}-used-{fixture.Item1}");
                    var variations = Art.Json("res://Assets/Sprites/Tilesets/Terrain/EmptyBlock.json").GetProperty("variations");
                    var reference = (variations.TryGetProperty(World.Theme, out var themed) ? themed : variations.GetProperty("default")).GetProperty("SMB1");
                    var referenceRect = reference.GetProperty("rect");
                    int referenceX = referenceRect[0].GetInt32(), referenceY = referenceRect[1].GetInt32();
                    using var atlas = Art.Texture("Tilesets/Terrain/" + reference.GetProperty("source").GetString()).GetImage();
                    Rect2I target = ScreenRect(block.Box);
                    int mismatch = 0;
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                        {
                            Color expected = atlas.GetPixel(referenceX + x, referenceY + y);
                            // Original CHR corner pixels are transparent: compare their
                            // actual scene background, not transparent RGB against opaque RGB.
                            if (expected.A == 0) expected = background.GetPixel(target.Position.X + x, target.Position.Y + y);
                            if (!SamePixel(used.GetPixel(target.Position.X + x, target.Position.Y + y), expected)) mismatch++;
                        }
                    Check("used block matches empty CHR tile " + fixture.Item1, block.Used && mismatch == 0, $"different pixels={mismatch}");
                    Clock = 2.25f;
                    using var later = await RegressionFrame();
                    int changed = 0;
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 16; x++) if (!SamePixel(used.GetPixel(target.Position.X + x, target.Position.Y + y), later.GetPixel(target.Position.X + x, target.Position.Y + y))) changed++;
                    Check("used block remains static " + fixture.Item1, changed == 0, $"changed pixels={changed}");
                }
            }
        }
        catch (Exception ex)
        {
            Check("visual regression exception", false, ex.ToString());
        }
        finally
        {
            SetPhysicsProcess(physicsEnabled);
            using var report = FileAccess.Open($"res://Reports/Regression/{label}-checks.json", FileAccess.ModeFlags.Write);
            report.StoreString(JsonSerializer.Serialize(new { display = DisplayServer.GetName(), nativePixels = native, failures = verificationFailures - failuresBefore, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private Block HeadHitFixture(int power, bool left)
    {
        StartGame(0, true); Power = power; LoadRoom("1-1", false); State = Screen.Playing;
        Block question = World.Blocks.Where(b => b.Kind.Contains("Question") && b.Item.Contains("Coin")).OrderBy(b => b.Center.X).First();
        Player.Position = new(question.Center.X, 0); Player.Grounded = true; Player.Velocity = Vector2.Zero;
        FacingLeft = left; CameraX = question.Center.X - 128; Clock = animationTime = 0; showTouch = false;
        return question;
    }
    private async Task<Image> RegressionFrame(string stem = "")
    {
        string cpuPath = "res://Reports/Regression/" + (stem.Length > 0 ? stem : "scratch") + ".cpu.png";
        studyFramePath = cpuPath; QueueRedraw();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage(); image.Convert(Image.Format.Rgba8);
        if (stem.Length > 0) image.SavePng("res://Reports/Regression/" + stem + ".png");
        return image;
    }
    private static Image DifferenceImage(Image image, Image background)
    {
        var result = Image.CreateEmpty(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8);
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
                if (!SamePixel(image.GetPixel(x, y), background.GetPixel(x, y))) result.SetPixel(x, y, image.GetPixel(x, y));
        return result;
    }
    private static Rect2I OpaqueBounds(Image image)
    {
        int x0 = image.GetWidth(), y0 = image.GetHeight(), x1 = -1, y1 = -1;
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
                if (image.GetPixel(x, y).A > 0) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        return x1 < x0 ? new Rect2I() : new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }
    private static bool SamePixel(Color a, Color b) => (a.A == 0 && b.A == 0) || a.ToRgba32() == b.ToRgba32();
    private Rect2I ScreenRect(Rect2 world) => new((int)MathF.Round(world.Position.X - MathF.Round(CameraX)), (int)MathF.Round(world.Position.Y + 208), (int)world.Size.X, (int)world.Size.Y);
    private static void Outline(Image image, Rect2I rect, Color color)
    {
        for (int x = rect.Position.X; x < rect.End.X; x++)
            foreach (int y in new[] { rect.Position.Y, rect.End.Y - 1 }) if (x >= 0 && x < image.GetWidth() && y >= 0 && y < image.GetHeight()) image.SetPixel(x, y, color);
        for (int y = rect.Position.Y; y < rect.End.Y; y++)
            foreach (int x in new[] { rect.Position.X, rect.End.X - 1 }) if (x >= 0 && x < image.GetWidth() && y >= 0 && y < image.GetHeight()) image.SetPixel(x, y, color);
    }
}
