using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private async Task VerifyFollowupRegressions()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            foreach (int hz in new[] { 30, 60, 120 })
            {
                MechanicsWorld(); var block = new Block { Center = new(40, -32), Item = "Coin" }; World.Blocks.Add(block); HitBlock(block);
                var coin = World.Popups.Single(p => p.CoinArc); float maximum = 0;
                for (int f = 0; f < hz / 2; f++)
                {
                    TickPopups(1f / hz); TickBlockTimers(1f / hz);
                    maximum = MathF.Max(maximum, block.Box.Position.Y - (coin.Position.Y - 16));
                }
                Check($"block coin rises one and half tiles at {hz}Hz", maximum >= 23.7f && maximum <= 24.01f, maximum.ToString("F3"));
            }
            foreach (int power in new[] { 0, 1, 2 })
            {
                StartGame(0, true); Power = power; LoadRoom("1-1", false); State = Screen.Playing; pipeCooldown = 0;
                var pipe = World.Triggers.First(t => t.Kind == "PipeArea" && !t.Bool("exit_only") && t.Number("enter_direction") == 0);
                Player.Position = new(VerticalPipeCenter(pipe), pipe.Y); Player.Grounded = true;
                TickPlayer(1f / 60, new Controls(0, false, false, false, false, true, false));
                TickTriggers(1f / 60, new Controls(0, false, false, false, false, true, false));
                TickGame(.74f, new());
                Check($"ducked vertical pipe hides whole standing sprite power {power}", pipeClock > 0 && Player.Position.Y - pipe.Y >= (power == 0 ? 16 : 32), (Player.Position.Y - pipe.Y).ToString("F3"));
                pipeClock = 0; afterPipe = null;
            }
            foreach (string id in new[] { "1-1a", "1-2", "8-4c" })
            {
                StartGame(id == "8-4c" ? 31 : 0, true); LoadRoom(id, false); State = Screen.Playing; pipeCooldown = 0; Invulnerable = 10;
                var pipe = World.Triggers.FirstOrDefault(t => t.Kind == "PipeArea" && !t.Bool("exit_only") && t.Number("enter_direction") == 3);
                if (pipe == null) continue;
                float mouth = HorizontalPipeMouth(pipe); Player.Height = 16; Player.Position = new(mouth - Player.Width / 2 - 4, pipe.Y + 16); Player.Velocity = Vector2.Zero; Player.Grounded = true;
                TickTriggers(1f / 60, new Controls(1, false, false, true, false, false, false));
                Check($"{id} side pipe rejects four pixel gap", pipeClock == 0);
                Player.Position = new(mouth - Player.Width / 2, pipe.Y + 16);
                TickTriggers(1f / 60, new Controls(-1, false, false, true, false, false, false));
                Check($"{id} touching pipe rejects opposite direction", pipeClock == 0);
                TickTriggers(1f / 60, new()); Check($"{id} touching pipe requires inward input", pipeClock == 0);
                Player.Position -= new Vector2(0, 20); TickTriggers(1f / 60, new Controls(1, false, false, true, false, false, false));
                Check($"{id} side pipe rejects incorrect height", pipeClock == 0);
                Player.Position = new(mouth - Player.Width / 2, pipe.Y + 16); float feet = Player.Position.Y;
                TickTriggers(1f / 60, new Controls(1, false, false, true, false, false, false));
                Check($"{id} side pipe accepts exact contact and inward input", pipeClock > 0 && Player.Position.Y == feet);
                pipeClock = 0; afterPipe = null; Player.Position = new(mouth - Player.Width / 2 - 4, pipe.Y + 16); Player.Velocity = Vector2.Zero; Player.Grounded = true;
                for (int f = 0; f < 30 && pipeClock == 0; f++) { var inward = new Controls(1, false, false, false, false, false, false); TickPlayer(1f / 60, inward); TickTriggers(1f / 60, inward); }
                Check($"{id} walking enters only after physical pipe contact", pipeClock > 0 && MathF.Abs(Player.Box.End.X - mouth) < .05f, $"right={Player.Box.End.X:F3}, mouth={mouth:F3}");
                pipeClock = 0; afterPipe = null;
            }
            MechanicsWorld((0, -2), (1, -2)); Power = 2; Player.Position = new(16, 0); Player.Height = 16; Crouching = true; Hurt();
            Check("fire damage inside low tunnel preserves sixteen pixel crouch", Power == 1 && Player.Height == 16 && Crouching && !World.Colliders(Player.Box, false).Any(c => c.box.Intersects(Player.Box)));
            foreach (var change in new[] { (before: 0, after: 1, item: "SuperMushroom"), (before: 1, after: 2, item: "FireFlower") })
            {
                MechanicsWorld(); Power = change.before; Player.Height = Power == 0 ? 16 : 32; Player.Velocity = new(75, -100); Player.Grounded = false;
                var enemy = MechanicsEnemy("Goomba", 100, 0, -1);
                var platform = new Platform { Position = new(160, -32), Previous = new(160, -32), Home = new(160, -32) }; World.Platforms.Add(platform);
                var shot = new FireShot(); shot.Body.Position = new(80, -64); shot.Body.Velocity = new(100, -50); World.Shots.Add(shot);
                var block = new Block { Center = new(240, -32), Item = "Coin", CoinsLeft = 9, CoinWindowStarted = true, CoinWindowRemaining = 3 }; World.Blocks.Add(block);
                PlayThemeMusic();
                var item = new Item { Kind = change.item, Age = 1 }; item.Body.Position = Player.Position; World.Items.Add(item); TickItems(0);
                Check($"{change.item} starts frozen power transition", item.Removed && Power == change.after && State == Screen.Transform && VisiblePower == change.before);
                Vector2 playerPos = Player.Position, velocity = Player.Velocity, enemyPos = enemy.Body.Position, platformPos = platform.Position, shotPos = shot.Body.Position; float time = TimeLeft, star = StarTime, window = block.CoinWindowRemaining;
                AdvanceState(.1f, new Controls(1, true, true, true, true, false, false)); Check($"{change.item} shows target form during transition", VisiblePower == change.after);
                AdvanceState(.1f, new()); Check($"{change.item} alternates source form during transition", VisiblePower == change.before);
                Check($"{change.item} freezes player enemies platforms shots TIME and blocks", Player.Position == playerPos && Player.Velocity == velocity && enemy.Body.Position == enemyPos && platform.Position == platformPos && shot.Body.Position == shotPos && TimeLeft == time && StarTime == star && block.CoinWindowRemaining == window && (DisplayServer.GetName() == "headless" || Sound.IsMusicPaused));
                AdvanceState(.6f, new()); Check($"{change.item} resumes with target form and preserved velocity", State == Screen.Playing && Power == change.after && Player.Velocity == velocity && !Sound.IsMusicPaused);
                AdvanceState(1f / 60, new()); Check($"{change.item} world resumes after transition", enemy.Body.Position != enemyPos && Player.Position != playerPos && TimeLeft < time);
            }
            MechanicsWorld((0, -2), (1, -2)); Player.Position = new(16, 0); var mushroom = new Item { Age = 1 }; mushroom.Body.Position = Player.Position; World.Items.Add(mushroom); TickItems(0); AdvanceState(1, new());
            Check("growth under low ceiling ends safely crouched", Power == 1 && Player.Height == 16 && Crouching && State == Screen.Playing && !World.Colliders(Player.Box, false).Any(c => c.box.Intersects(Player.Box)));
            if (DisplayServer.GetName() != "headless")
            {
                foreach (int before in new[] { 0, 1 })
                {
                    MechanicsWorld(); Power = before; Player.Height = before == 0 ? 16 : 32; Player.Position = new(40, -96); Player.Grounded = true; Clock = animationTime = 0;
                    using var source = await CaptureMechanicsFrame(); BeginPowerTransition(before + 1); Invulnerable = .6f;
                    using var firstFrame = await CaptureMechanicsFrame(); AdvanceState(.1f, new()); using var changed = await CaptureMechanicsFrame();
                    AdvanceState(1, new()); Invulnerable = 0; using var finalFrame = await CaptureMechanicsFrame();
                    Check($"GPU power transition source and target frames {before}", SamePixels(source, firstFrame) && SamePixels(changed, finalFrame) && !SamePixels(firstFrame, changed));
                    changed.SavePng($"res://Reports/Regression/power-transition-{before}.png");
                }
                MechanicsWorld(); Player.Position = new(-100, 0); var block = new Block { Center = new(40, -64), Item = "Coin" }; World.Blocks.Add(block); HitBlock(block);
                using var hidden = await CaptureMechanicsFrame(); TickPopups(MathF.Sqrt(2 * 24f / 480)); using var peak = await CaptureMechanicsFrame(); using var difference = DifferenceImage(hidden, peak); var bounds = OpaqueBounds(difference);
                Check("GPU coin pop reaches one and half tiles above block", bounds.HasArea() && bounds.Size.Y <= 16 && bounds.Position.Y >= 208 + block.Box.Position.Y - 24 && bounds.Position.Y < 208 + block.Box.Position.Y - 16 && bounds.End.Y <= 208 + block.Box.Position.Y);
                peak.SavePng("res://Reports/Regression/block-coin-one-and-half-tiles.png");
            }
        }
        catch (Exception ex) { Check("followup regression exception", false, ex.ToString()); }
        finally
        {
            SetPhysicsProcess(enabled);
            using var file = FileAccess.Open("res://Reports/Regression/height-pipe-contact-transform-time.json", FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(new { display = DisplayServer.GetName(), failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
