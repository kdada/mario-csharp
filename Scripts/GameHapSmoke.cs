using Godot;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MarioStudy;

public partial class Game
{
    private void RunHapSmoke()
    {
        int count = 0;
        void Require(string name, bool condition)
        {
            if (!condition) throw new InvalidDataException("HAP smoke failed: " + name);
            count++; GD.Print("HAP_CHECK_PASS " + name);
        }
        try
        {
            Require("native-aot", !RuntimeFeature.IsDynamicCodeSupported && !RuntimeFeature.IsDynamicCodeCompiled);
            Require("main-scene-title", State == Screen.Title);
            Require("all-maps", Maps.MainLevels.Length == 32 && Maps.Rooms.Count == 57);
            Require("raw-chr", Godot.FileAccess.GetFileAsBytes("res://Assets/smb1.chr").Length == 8192);
            Require("small-sprite-and-recipe", Art.Texture("Players/Mario/Small.png").GetWidth() > 0);
            Require("fire-sprite-and-recipe", Art.Texture("Players/Mario/Fire.png").GetHeight() > 0 && Art.GeneratedSheets >= 2);
            var progress = new Profile { Unlocked = 7, HighScore = 12345 }; progress.Completed[2] = true;
            var roundTrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(progress, GameJsonContext.Default.Progress), GameJsonContext.Default.Progress)!;
            Require("progress-json", roundTrip.Unlocked == 7 && roundTrip.HighScore == 12345 && roundTrip.Completed[2]);
            MechanicsWorld(); float highest = 0;
            for (int frame = 0; frame < 60; frame++) { TickPlayer(1f / 60, new Controls(1, true, frame == 0, true, false, false, false)); highest = MathF.Max(highest, -Player.Position.Y); }
            Require("four-tile-jump", highest >= 64 && highest <= 65.1f && Player.Position.X > 64);
            MechanicsWorld(); var brick = new Block { Center = new(40, -32), Item = "Coin" }; World.Blocks.Add(brick); HitBlock(brick);
            var coin = World.Popups.Single(p => p.CoinArc); TickPopups(MathF.Sqrt(2 * 24f / 480));
            Require("block-coin-arc", Coins == 1 && MathF.Abs(brick.Box.Position.Y - (coin.Position.Y - 16) - 24) < .01f);
            MechanicsWorld(); Player.Velocity = new(60, -100); BeginPowerTransition(1); var position = Player.Position;
            AdvanceState(.1f, new()); Require("growth-freezes", State == Screen.Transform && Player.Position == position && Player.Velocity == new Vector2(60, -100));
            AdvanceState(1, new()); Require("growth-resumes", State == Screen.Playing && Power == 1 && Player.Velocity == new Vector2(60, -100));
            GD.Print("MARIO_HAP_SMOKE_PASS checks=" + count); GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }
}
