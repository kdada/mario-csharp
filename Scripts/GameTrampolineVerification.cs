using Godot;
using System.Text.Json;

namespace MarioStudy;

public partial class Game
{
    // State/metadata only: this entry never draws, loads textures, or captures pixels.
    private void VerifyTrampolineSpriteFixes()
    {
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            VerifyTrampolineRegions();
            foreach (int hz in new[] { 30, 60, 120 })
                foreach (int power in new[] { 0, 1, 2 })
                    foreach (bool jump in new[] { false, true })
                    {
                        var (spring, neighbor) = TrampolineRegressionWorld();
                        Power = power; Player.Height = power == 0 ? 16 : 32; Clock = 12345;
                        Check($"trampoline idle uses frame zero {hz}Hz power {power} jump {jump}", TrampolineFrame(spring) == 0);
                        TouchRegressionTrampoline(spring, jump);
                        string label = $"trampoline {hz}Hz power {power} jump {jump}";
                        Check(label + " contact starts only the touched animation", TrampolineFrame(spring) == 1 && TrampolineFrame(neighbor) == 0);
                        Check(label + " preserves original launch and horizontal velocity", Player.Position == new Vector2(spring.X, spring.Y - 24) && Player.Velocity == new Vector2(37, jump ? -430 : -350) && Power == power && State == Screen.Playing);
                        float elapsed = 0;
                        foreach (var sample in new[] { (time: .03f, frame: 1), (time: .07f, frame: 2), (time: .12f, frame: 1), (time: .2f, frame: 0) })
                        {
                            while (elapsed + .00001f < sample.time)
                            {
                                TickTriggers(1f / hz, new()); elapsed += 1f / hz;
                            }
                            Check(label + $" frame at {sample.time:F2}s", TrampolineFrame(spring) == sample.frame && TrampolineFrame(neighbor) == 0);
                        }
                        Clock += 10000; TickTriggers(0, new());
                        Check(label + " returns to idle without looping", TrampolineFrame(spring) == 0 && trampolineAnimations.GetOrCreateValue(World).Count == 0);
                    }
            foreach (var miss in new[] { (x: 13f, y: -16f, speed: 60f), (x: 0f, y: -32f, speed: 60f), (x: 0f, y: -4f, speed: 60f), (x: 0f, y: -16f, speed: -60f), (x: 0f, y: -16f, speed: 0f) })
            {
                var (spring, _) = TrampolineRegressionWorld();
                Player.Position = spring.Position + new Vector2(miss.x, miss.y); Player.Velocity = new(37, miss.speed);
                Vector2 position = Player.Position, velocity = Player.Velocity;
                TickTriggers(1f / 60, new());
                Check($"trampoline non-contact remains idle x {miss.x} y {miss.y} vy {miss.speed}", TrampolineFrame(spring) == 0 && Player.Position == position && Player.Velocity == velocity);
            }
            VerifyIndependentTrampolines();
            VerifyFrozenTrampolines();
            VerifyTrampolineWorldLifetime();
        }
        catch (Exception ex) { Check("trampoline regression exception", false, ex.ToString()); }
        finally { SetPhysicsProcess(enabled); }
    }
    private (EntityData first, EntityData second) TrampolineRegressionWorld()
    {
        MechanicsWorld();
        var first = new EntityData { Kind = "Trampoline", Name = "spring", X = 40, Y = 0 };
        var second = new EntityData { Kind = "Trampoline", Name = "spring", X = 100, Y = 0 };
        World.Triggers.Add(first); World.Triggers.Add(second);
        Player.Position = new(-100, -80); Player.Velocity = Vector2.Zero; Player.Grounded = false;
        return (first, second);
    }
    private void TouchRegressionTrampoline(EntityData spring, bool jump = false)
    {
        Player.Position = spring.Position + new Vector2(0, -16); Player.Velocity = new(37, 60);
        TickTriggers(0, new Controls(0, jump, false, false, false, false, false));
    }
    private void VerifyIndependentTrampolines()
    {
        var (first, second) = TrampolineRegressionWorld();
        TouchRegressionTrampoline(first); TickTrampolineAnimations(.08f);
        TouchRegressionTrampoline(second);
        Check("trampolines with identical names keep separate phases", TrampolineFrame(first) == 2 && TrampolineFrame(second) == 1);
        TickTrampolineAnimations(.09f);
        Check("one trampoline can finish while another remains compressed", TrampolineFrame(first) == 0 && TrampolineFrame(second) == 2);
        TouchRegressionTrampoline(first);
        Check("repeated real contact restarts only that trampoline", TrampolineFrame(first) == 1 && TrampolineFrame(second) == 2);
        float elapsed = trampolineAnimations.GetOrCreateValue(World)[first];
        for (int i = 0; i < 20; i++) _ = TrampolineFrame(first);
        Check("trampoline frame queries cannot advance simulation", trampolineAnimations.GetOrCreateValue(World)[first] == elapsed);
    }
    private void VerifyFrozenTrampolines()
    {
        var (spring, _) = TrampolineRegressionWorld(); TouchRegressionTrampoline(spring); TickTrampolineAnimations(.03f);
        var animations = trampolineAnimations.GetOrCreateValue(World); float elapsed = animations[spring];
        State = Screen.Pause; AdvanceState(1, new()); TickTrampolineAnimations(10);
        Check("pause freezes local trampoline timeline", animations[spring] == elapsed && TrampolineFrame(spring) == 1);
        State = Screen.Transform; TickTrampolineAnimations(10);
        Check("transformation freezes local trampoline timeline", animations[spring] == elapsed && TrampolineFrame(spring) == 1);
        State = Screen.Playing; StartPipeAnimation(Vector2.Down, () => { });
        AdvanceState(.1f, new()); TickTrampolineAnimations(10);
        Check("pipe animation freezes local trampoline timeline", animations[spring] == elapsed && TrampolineFrame(spring) == 1);
        ResetPipeAnimation(); Clock += 100000;
        Check("global clock cannot advance trampoline phase", animations[spring] == elapsed && TrampolineFrame(spring) == 1);
        TickTrampolineAnimations(.04f);
        Check("trampoline resumes without paused-time catch-up", MathF.Abs(animations[spring] - .07f) < .0001f && TrampolineFrame(spring) == 2);
    }
    private void VerifyTrampolineWorldLifetime()
    {
        var (spring, _) = TrampolineRegressionWorld(); TouchRegressionTrampoline(spring); TickTrampolineAnimations(.03f);
        var original = World; roomCache["1-1"] = original;
        LoadRoom("1-2", false); State = Screen.Playing; World.Triggers.Clear(); World.Triggers.Add(spring);
        Check("different world cannot inherit shared map entity animation", TrampolineFrame(spring) == 0);
        TouchRegressionTrampoline(spring); TickTrampolineAnimations(.08f);
        Check("different world owns its own trampoline animation", TrampolineFrame(spring) == 2);
        LoadRoom("1-1", true);
        Check("cached room resumes its own frozen trampoline phase", ReferenceEquals(World, original) && TrampolineFrame(spring) == 1 && trampolineAnimations.GetOrCreateValue(World)[spring] == .03f);
        BeginLevel(0); World.Triggers.Add(spring);
        Check("restarting level resets trampoline to idle", !ReferenceEquals(World, original) && TrampolineFrame(spring) == 0);
    }
    private void VerifyTrampolineRegions()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Recipe/Sprites/Blocks/Trampoline.json"));
        var recipe = document.RootElement;
        Vector2I size = GD.StrToVar(recipe.GetProperty("sheet_size").GetString()!).AsVector2I();
        Check("trampoline metadata describes three 16x32 frames", size == new Vector2I(48, 32) && recipe.GetProperty("source_path").GetString() == "res://Assets/Sprites/Blocks/Trampoline.png");
        var tiles = GD.StrToVar(recipe.GetProperty("tiles").GetString()!).AsGodotDictionary();
        var positions = new List<Vector2>();
        foreach (Variant key in tiles.Keys) positions.Add(key.AsVector2());
        var spring = new EntityData { Kind = "Trampoline", X = 40, Y = -16 };
        Rect2 destination = TrampolineDestination(spring);
        Check("trampoline destination has one-frame width and correct feet offset", destination.Position == spring.Position + new Vector2(-8, -24) && destination.Size == new Vector2(16, 32) && destination.End.Y == spring.Y + 8);
        foreach (int frame in new[] { 0, 1, 2 })
        {
            Rect2 source = TrampolineSource(frame);
            var content = positions.Where(p => p.X >= source.Position.X && p.X < source.End.X).ToArray();
            Check($"trampoline frame {frame} crops one bounded sheet region", source == new Rect2(frame * 16, 0, 16, 32) && source.End.X <= size.X && source.End.Y <= size.Y && content.Length > 0 && content.All(p => p.Y >= 0 && p.Y + 8 <= source.End.Y && p.X + 8 <= source.End.X));
            Check($"trampoline frame {frame} shares base while compressing", content.Min(p => p.Y) == 1 + frame * 8 && content.Max(p => p.Y) + 7 == 24 && destination.Position.Y + content.Max(p => p.Y) + 7 == spring.Y);
        }
    }
}
