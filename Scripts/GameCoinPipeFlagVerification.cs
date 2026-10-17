using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private async Task VerifyCoinPipeFlagRegressions()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            foreach (int hz in new[] { 30, 60, 120 }) foreach (int power in new[] { 0, 1 })
            {
                MechanicsWorld(); Power = power; Player.Height = power == 0 ? 16 : 32;
                var brick = new Block { Center = new(40, -32) }; World.Blocks.Add(brick);
                Vector2 coinPosition = new(brick.Center.X, brick.Box.Position.Y - 8); World.Coins.Add(coinPosition);
                HitBlock(brick); var arc = World.Popups.Single(p => p.CoinArc); float start = arc.Position.Y, highest = 0;
                Check($"coin above {(power == 0 ? "bumped" : "broken")} brick is counted once {hz}Hz", Coins == 1 && Score == (power == 0 ? 200 : 250) && World.CollectedCoins.Contains(0) && arc.SourceBlock == null && arc.Position == coinPosition + new Vector2(0, 8));
                for (int frame = 0; frame < hz / 2; frame++) { TickPopups(1f / hz); TickBlockTimers(1f / hz); highest = MathF.Max(highest, start - arc.Position.Y); }
                Check($"coin above brick rises half tile power {power} {hz}Hz", highest >= 7.8f && highest <= 8.01f, highest.ToString("F3"));
                HitBlock(brick); Check($"above coin disappears and cannot be awarded twice power {power} {hz}Hz", Coins == 1 && !World.Popups.Any(p => p.CoinArc || p.Text == "200"));
            }
            StartGame(1, true); State = Screen.Playing; CameraX = 1280; pipeCooldown = Invulnerable = 0;
            var entry = World.Triggers.First(t => t.Kind == "PipeArea" && !t.Bool("exit_only") && World.Enemies.Any(e => e.Kind == "PiranhaPlant" && e.Home == t.Position));
            var plant = World.Enemies.First(e => e.Kind == "PiranhaPlant" && e.Home == entry.Position);
            var neighbor = World.Enemies.First(e => e.Kind == "PiranhaPlant" && e.Home.X != plant.Home.X);
            World.Enemies.RemoveAll(e => e.Kind != "PiranhaPlant");
            plant.Active = neighbor.Active = true; plant.Body.Position = plant.Home; plant.Body.Height = 24; plant.Clock = Mathf.PosMod(1.5f - plant.Phase, 4);
            Player.Position = new(VerticalPipeCenter(entry), entry.Y); Player.Grounded = true;
            TickGame(1f / 60, new Controls(0, false, false, false, false, true, false));
            Check("entry hides exposed piranha before enemy contact", pipeClock > 0 && plant.PipeSuppressed && plant.Body.Position == plant.Home + new Vector2(0, 24) && State == Screen.Playing && Invulnerable == 0);
            Check("pipe transition leaves unrelated piranhas unsuppressed", !neighbor.PipeSuppressed);
            if (DisplayServer.GetName() != "headless") await CheckSuppressedPlantPixels(plant, "entry");
            pipeClock = 0; afterPipe = null;
            for (int frame = 0; frame < 120; frame++) TickEnemies(1f / 60);
            Check("piranha stays hidden while Mario remains at mouth", plant.PipeSuppressed && plant.Body.Position == plant.Home + new Vector2(0, 24));
            Player.Position = plant.Body.Position; TouchEnemy(plant);
            Check("suppressed piranha cannot damage Mario", State == Screen.Playing && Invulnerable == 0);
            Player.Position = plant.Home + new Vector2(64, 0);
            for (int frame = 0; frame < 100; frame++) TickEnemies(1f / 60);
            Check("piranha resumes normal cycle after Mario leaves pipe", !plant.PipeSuppressed && plant.Body.Position.Y < plant.Home.Y + 22);

            StartGame(1, true); LoadRoom("UndergroundExit", false); State = Screen.Playing;
            var exit = World.Triggers.First(t => t.Kind == "PipeArea" && t.Bool("exit_only"));
            var exitPlant = MechanicsEnemy("PiranhaPlant", exit.X, exit.Y, -1); exitPlant.Body.Height = 24; exitPlant.Clock = 1.5f;
            LoadRoom("1-2", false); State = Screen.Playing; Invulnerable = 0;
            var side = World.Triggers.Where(t => t.Kind == "PipeArea" && !t.Bool("exit_only") && t.Number("enter_direction") == 3).OrderByDescending(t => t.X).First();
            Player.Position = new(HorizontalPipeMouth(side) - Player.Width / 2, side.Y + 16); EnterPipe(side, Vector2.Right); TickGame(.75f, new());
            Check("cached destination hides exposed piranha before exit frame", World.Data.Id == "UndergroundExit" && World.Enemies.Contains(exitPlant) && pipeClock > 0 && exitPlant.PipeSuppressed && exitPlant.Body.Position == exitPlant.Home + new Vector2(0, 24));
            TickGame(.375f, new()); if (DisplayServer.GetName() != "headless") await CheckSuppressedPlantPixels(exitPlant, "exit-half");
            TickGame(.375f, new()); TickGame(1f / 60, new());
            Check("exit finishes safely with piranha still hidden", pipeClock == 0 && State == Screen.Playing && Invulnerable == 0 && exitPlant.PipeSuppressed && Player.Position == new Vector2(VerticalPipeCenter(exit), exit.Y));
            if (DisplayServer.GetName() != "headless")
            {
                await CheckSuppressedPlantPixels(exitPlant, "exit-finished");
                foreach (string id in new[] { "1-1", "1-3", "3-1" })
                {
                    LevelIndex = Array.IndexOf(Maps.MainLevels, id); LoadRoom(id, false); State = Screen.Playing; Clock = 0; pipeClock = 0; Invulnerable = 0;
                    var flag = World.Triggers.First(t => t.Kind == "EndFlagpole"); CameraX = flag.X - 128; Player.Position = new(-10000, 0); World.Enemies.Clear(); World.Coins.Clear();
                    using var visible = await CaptureMechanicsFrame(); World.Triggers.Remove(flag); using var absent = await CaptureMechanicsFrame();
                    var block = World.Colliders(new Rect2(flag.X - 1, -16, 2, 16), false).First(c => c.box.HasPoint(new Vector2(flag.X, -12))).box;
                    Vector2 top = View(block.Position); bool covered = true, poleVisible = false;
                    for (int y = (int)top.Y; y < top.Y + block.Size.Y; y++) for (int x = (int)top.X; x < top.X + block.Size.X; x++) if (visible.GetPixel(x, y) != absent.GetPixel(x, y)) covered = false;
                    for (int y = (int)top.Y - 24; y < top.Y; y++) for (int x = 126; x <= 130; x++) if (visible.GetPixel(x, y) != absent.GetPixel(x, y)) poleVisible = true;
                    Check($"GPU {id} brick covers flagpole base but upper pole remains", covered && poleVisible);
                    visible.SavePng($"res://Reports/Regression/flagpole-base-covered-{id}.png");
                }
                MechanicsWorld(); Player.Position = new(-10000, 0); var brick = new Block { Center = new(40, -64) }; World.Blocks.Add(brick); World.Coins.Add(new(40, brick.Box.Position.Y - 8));
                HitBlock(brick); var arc = World.Popups.Single(p => p.CoinArc); float height = arc.Position.Y; World.Popups.Remove(arc); using var empty = await CaptureMechanicsFrame(); World.Popups.Add(arc);
                TickPopups(MathF.Sqrt(2 * 8f / 480)); using var peak = await CaptureMechanicsFrame(); using var difference = DifferenceImage(peak, empty); var bounds = OpaqueBounds(difference);
                Check("GPU coin above brick reaches half tile above original position", bounds.HasArea() && bounds.Position.Y >= 208 + height - 16 - 8 && bounds.End.Y <= 208 + height - 8 && bounds.Size.Y <= 16);
                peak.SavePng("res://Reports/Regression/above-brick-coin-half-tile.png");
            }
        }
        catch (Exception ex) { Check("coin pipe flag regression exception", false, ex.ToString()); }
        finally
        {
            SetPhysicsProcess(enabled);
            using var file = FileAccess.Open("res://Reports/Regression/coin-heights-pipe-plants-flag-base.json", FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(new { display = DisplayServer.GetName(), failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private async Task CheckSuppressedPlantPixels(Enemy plant, string phase)
    {
        Vector2 position = plant.Body.Position; plant.Body.Position = plant.Home;
        using var suppressed = await CaptureMechanicsFrame(); plant.Removed = true; using var absent = await CaptureMechanicsFrame(); plant.Removed = false; plant.Body.Position = position;
        Check("GPU pipe piranha completely absent " + phase, SamePixels(suppressed, absent));
        suppressed.SavePng("res://Reports/Regression/pipe-piranha-hidden-" + phase + ".png");
    }
}
