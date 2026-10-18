using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private async Task VerifyUserRegressions()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            StartGame(0, true); State = Screen.Playing; Player.Grounded = true;
            TestKey(Key.A, true); Check("A moves left", ReadControls().Axis == -1); TestKey(Key.A, false);
            TestKey(Key.D, true); Check("D moves right", ReadControls().Axis == 1); TestKey(Key.D, false);
            jumpHeld = false; TestKey(Key.J, true); var jump = ReadControls(); TickPlayer(1f / 60, jump);
            Check("J starts jump", jump.Jump && jump.JumpPressed && Player.Velocity.Y < 0);
            Check("held J does not repeat jump press", !ReadControls().JumpPressed); TestKey(Key.J, false); ReadControls();
            StartGame(0, true); State = Screen.Playing; Power = 2; Player.Height = 32; Player.Grounded = true;
            fireHeld = false; TestKey(Key.K, true); var fire = ReadControls(); TickPlayer(1f / 60, fire);
            Check("K fires and enables run", fire.FirePressed && fire.Run && World.Shots.Count == 1);
            var run = ReadControls();
            Check("held K runs without repeating fire press", run.Run && !run.FirePressed);
            TestKey(Key.D, true); run = ReadControls();
            for (int i = 0; i < 45; i++) TickPlayer(1f / 60, run);
            Check("D plus K reaches running speed", Player.Velocity.X == 150);
            TestKey(Key.K, false); var walk = ReadControls();
            for (int i = 0; i < 30; i++) TickPlayer(1f / 60, walk);
            Check("release K returns to walking speed", Player.Velocity.X == 90); TestKey(Key.D, false);

            StartGame(0, true); State = Screen.Playing; Power = 2; Player.Height = 32; Invulnerable = 0;
            Vector2 feetBeforeDamage = Player.Position; Hurt();
            Check("fire damage downgrades to big Mario", Power == 1 && Player.Height == 32 && Player.Position == feetBeforeDamage && State == Screen.Playing);
            Hurt(); Check("damage protection prevents repeated downgrade", Power == 1 && Invulnerable > 0);
            Invulnerable = 0; Hurt(); Check("second damage downgrades big to small", Power == 0 && Player.Height == 16 && State == Screen.Playing);
            Invulnerable = 0; Hurt(); Check("small Mario dies on next damage", State == Screen.Dying);
            StartGame(0, true); State = Screen.Playing;
            var block = World.Blocks.First(b => b.Item == "" && !b.Hidden);
            var mushroom = new Item { Age = 1 }; mushroom.Body.Position = new(block.Center.X, block.Box.Position.Y); mushroom.Body.Velocity = new(40, 0); mushroom.Body.Grounded = true; World.Items.Add(mushroom);
            HitBlock(block);
            Check("brick bump reverses and launches mushroom", mushroom.Body.Velocity.X == -40 && mushroom.Body.Velocity.Y < 0 && !mushroom.Body.Grounded);
            TickItems(.1f);
            Check("bumped mushroom rises and moves opposite", mushroom.Body.Position.Y < block.Box.Position.Y && mushroom.Body.Position.X < block.Center.X);
            World.Items.Clear(); World.Coins.Clear(); World.CollectedCoins.Clear(); block.Bump = 0;
            World.Coins.Add(new(block.Center.X, block.Box.Position.Y - 8)); World.Coins.Add(new(block.Center.X + 16, block.Box.Position.Y - 8));
            int coins = Coins, score = Score; HitBlock(block);
            Check("bump collects coin directly above brick", World.CollectedCoins.Contains(0) && Coins == coins + 1 && Score == score + 200);
            Check("bump leaves adjacent coin intact", !World.CollectedCoins.Contains(1));
            block.Bump = 0; HitBlock(block); Check("bumped coin counted once", Coins == coins + 1);
            block.Bump = 0; Power = 1; World.Coins.Add(new(block.Center.X, block.Box.Position.Y - 8)); HitBlock(block);
            Check("breaking brick also collects coin above", block.Removed && World.CollectedCoins.Contains(2) && Coins == coins + 2);

            foreach (int power in new[] { 0, 1 })
            {
                StartGame(0, true); Power = power; LoadRoom("1-2", false); State = Screen.Playing; pipeCooldown = 0;
                var pipe = World.Triggers.Where(t => t.Kind == "PipeArea" && !t.Bool("exit_only") && t.Number("enter_direction") == 3).OrderByDescending(t => t.X).First();
                Player.Position = new(HorizontalPipeMouth(pipe) - Player.Width / 2, pipe.Y + 16); float feet = Player.Position.Y; Crouching = true;
                TickTriggers(1f / 60, new Controls(1, false, false, false, false, false, false));
                Check("side pipe preserves feet on entry " + power, pipeClock > 0 && Player.Position.Y == feet);
                TickGame(.375f, new()); Check("side pipe keeps horizontal trajectory " + power, Player.Position.Y == feet);
                TickGame(.375f, new()); Check("destination clears crouch for exit " + power, World.Data.Id == "UndergroundExit" && !Crouching && Player.Height == (power == 0 ? 16 : 32));
                TickGame(.75f, new()); Check("finished exit is standing on rim " + power, Player.Grounded && !Crouching && Player.Velocity == Vector2.Zero);
            }
            foreach (var scenario in new[] { (0, 11, 1), (2, 13, 3), (0, 16, 6), (0, 12, 0) })
            {
                StartGame(scenario.Item1, true); State = Screen.Playing; Clock = animationTime = 0; showTouch = false;
                var flag = World.Triggers.First(t => t.Kind == "EndFlagpole");
                var castle = World.Triggers.First(t => t.Kind is "EndSmallCastle" or "EndFinalCastle");
                Player.Position = new(flag.X, scenario.Item3 == 1 ? -150 : -16); CameraX = flag.X - 160; TimeLeft = scenario.Item2;
                StartClear(flag, false); int scored = Score; bool framing = false, captured = false; var bursts = new HashSet<Popup>();
                for (int frame = 0; frame < 1000 && State == Screen.Clear; frame++)
                {
                    TickClear(1f / 60);
                    if (!framing && StateClock >= 3.8f)
                    {
                        Check("castle centered during clear " + scenario, MathF.Abs(castle.X - CameraX - 128) < .01f && clearInsideCastle && MathF.Abs(Player.Position.X - castle.X) < .01f);
                        framing = true;
                    }
                    foreach (var popup in World.Popups.Where(p => p.Sprite == "Particles/Firework.png")) bursts.Add(popup);
                    if (!captured && scenario.Item3 == 6 && fireworkMade > 0 && DisplayServer.GetName() != "headless")
                    {
                        using var image = await RegressionFrame("castle-centered-firework"); captured = true;
                        Check("GPU firework is inside castle view", World.Popups.Where(p => p.Sprite == "Particles/Firework.png").All(p => p.Position.X - CameraX > 16 && p.Position.X - CameraX < 240 && p.Position.Y + 208 > 18));
                    }
                }
                Check("fireworks follow time last digit " + scenario, bursts.Count == scenario.Item3 && fireworkMade == scenario.Item3);
                Check("fireworks award 500 points each " + scenario, Score == scored + scenario.Item2 * 50 + scenario.Item3 * 500);
            }
            if (DisplayServer.GetName() != "headless")
            {
                foreach (int power in new[] { 0, 1, 2 })
                {
                    StartGame(0, true); State = Screen.Playing; Power = power; Player.Height = power == 0 ? 16 : 32;
                    Player.Position = new(40, -96); Player.Grounded = true; Player.Velocity = Vector2.Zero; Crouching = false; CameraX = 0; Clock = animationTime = 0; Invulnerable = 0; showTouch = false;
                    using var standing = await RegressionFrame();
                    Crouching = true; Player.Grounded = false; StartPipeAnimation(Vector2.Up, () => { });
                    using var pipePose = await RegressionFrame("pipe-standing-" + power);
                    using var difference = DifferenceImage(standing, pipePose);
                    Check("GPU pipe uses standing pose " + power, !OpaqueBounds(difference).HasArea());
                }
                pipeClock = 0; Crouching = false; Player.Grounded = true; showTouch = false;
                using var hidden = await RegressionFrame();
                using var touch = new InputEventScreenTouch { Index = 90, Position = new(226, 199), Pressed = true };
                _Input(touch);
                using var touched = await RegressionFrame("no-button-overlay");
                using var release = new InputEventScreenTouch { Index = 90, Position = touch.Position, Pressed = false };
                _Input(release);
                using var overlay = DifferenceImage(hidden, touched);
                Check("GPU desktop touch does not enable phone overlay", !showTouch && !OpaqueBounds(overlay).HasArea());
            }
        }
        catch (Exception ex) { Check("user regression exception", false, ex.ToString()); }
        finally
        {
            foreach (Key key in new[] { Key.A, Key.D, Key.J, Key.K, Key.L }) TestKey(key, false);
            jumpHeld = fireHeld = false; SetPhysicsProcess(enabled);
            using var file = FileAccess.Open("res://Reports/Regression/user-controls-blocks-pipes-clear.json", FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(new { display = DisplayServer.GetName(), failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private static void TestKey(Key key, bool pressed)
    {
        using var input = new InputEventKey { PhysicalKeycode = key, Pressed = pressed };
        Input.ParseInputEvent(input);
        Input.FlushBufferedEvents();
    }
}
