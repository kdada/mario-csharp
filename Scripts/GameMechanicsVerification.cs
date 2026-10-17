using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private void MechanicsWorld(params (int x, int y)[] cells)
    {
        StartGame(0, true); State = Screen.Playing;
        var tile = Maps.Rooms["1-1"].Tiles.First(t => Maps.IsSolid((int)t[2], (int)t[3], (int)t[4]));
        var data = new RoomData { Id = "1-1", Width = 1024 };
        for (int x = -16; x < 64; x++) data.Tiles.Add([x, 0, tile[2], tile[3], tile[4], 0]);
        foreach (var cell in cells) data.Tiles.Add([cell.x, cell.y, tile[2], tile[3], tile[4], 0]);
        World = new World(data, Maps, MainId); CameraX = 0;
        Player.Position = Vector2.Zero; Player.Velocity = Vector2.Zero; Player.Height = 16; Player.Grounded = true; Player.Support = null;
        Power = 0; Crouching = FacingLeft = false; StarTime = Invulnerable = pipeClock = fireCooldown = pipeCooldown = 0; afterPipe = null;
    }
    private Enemy MechanicsEnemy(string kind, float x, float y, int direction)
    {
        var e = NewEnemy(kind, new(x, y)); e.Active = true; e.Direction = direction; e.Body.Grounded = true; e.Body.Height = kind.Contains("Koopa") ? 24 : 16;
        World.Enemies.Add(e); return e;
    }
    private async Task VerifyMechanicsRegressions()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            foreach (int hz in new[] { 30, 60, 120 })
                foreach (int power in new[] { 0, 1, 2 })
                {
                    MechanicsWorld(); Power = power; Player.Height = power == 0 ? 16 : 32;
                    float highest = 0;
                    for (int f = 0; f < hz * 2; f++)
                    {
                        TickPlayer(1f / hz, new Controls(0, true, f == 0, true, false, false, false));
                        highest = MathF.Max(highest, -Player.Position.Y);
                        if (f > 1 && Player.Grounded) break;
                    }
                    Check($"K long jump clears four tiles at {hz}Hz power {power}", highest >= 64 && highest <= 65.1f, highest.ToString("F3"));
                }
            MechanicsWorld(); float shortHeight = 0;
            for (int f = 0; f < 90; f++) { TickPlayer(1f / 60, new Controls(0, f < 3, f == 0, true, false, false, false)); shortHeight = MathF.Max(shortHeight, -Player.Position.Y); }
            Check("releasing J retains short jump control", shortHeight < 40, shortHeight.ToString("F3"));
            var pit = Enumerable.Range(-4, 4).SelectMany(y => new[] { (3, y), (8, y) }).ToArray();
            MechanicsWorld(pit); Player.Position = new(88, 0); float pitHeight = 0; bool solid = false;
            for (int f = 0; f < 80; f++)
            {
                TickPlayer(1f / 60, new Controls(1, true, f == 0, true, false, false, false));
                pitHeight = MathF.Max(pitHeight, -Player.Position.Y); solid |= World.Colliders(Player.Box, false).Any(c => Player.Box.Intersects(c.box));
            }
            Check("K jump escapes a four-tile pit without initial run-up", Player.Position.X > 150 && pitHeight >= 64 && !solid, Player.Position.ToString());
            foreach (int power in new[] { 0, 1, 2 })
            {
                MechanicsWorld((0, -4), (0, -7)); Power = power; Player.Height = power == 0 ? 16 : 32; Player.Position = new(8, -64);
                for (int f = 0; f < 30 && Player.Position.X < 19.5f; f++) TickPlayer(1f / 60, new Controls(1, false, false, true, false, false, false));
                bool returned = false, landed = false; solid = false;
                for (int f = 0; f < 90; f++)
                {
                    if (f > 2 && Player.Velocity.X < 0 && Player.Position.X <= 24) returned = true;
                    float axis = f < 2 || returned ? 1 : -1;
                    TickPlayer(1f / 60, new Controls(axis, true, f == 0, true, false, false, false));
                    solid |= World.Colliders(Player.Box, false).Any(c => Player.Box.Intersects(c.box));
                    if (Player.Grounded && MathF.Abs(Player.Position.Y + 112) < .01f) { landed = true; break; }
                }
                Check($"curved jump lands on vertically stacked upper block power {power}", landed && !solid, Player.Position.ToString());
            }
            MechanicsWorld((0, -4), (0, -7)); Player.Position = new(8, -64); bool passed = false;
            for (int f = 0; f < 60; f++) { TickPlayer(1f / 60, new Controls(0, true, f == 0, true, false, false, false)); passed |= Player.Position.Y < -80.01f; }
            Check("straight jump cannot pass through upper block", !passed);

            var roof = Enumerable.Range(0, 6).Select(x => (x, -2)).ToArray();
            foreach (int power in new[] { 1, 2 })
            {
                MechanicsWorld(roof); Power = power; Player.Height = 32; Player.Position = new(-12, 0); Player.Velocity = new(150, 0);
                for (int f = 0; f < 24; f++) TickPlayer(1f / 60, new Controls(1, false, false, true, false, true, false));
                Check($"running duck slides into one-tile tunnel power {power}", Player.Position.X > 0 && Player.Height == 16);
                float inside = Player.Position.X;
                for (int f = 0; f < 45; f++) TickPlayer(1f / 60, new Controls(1, false, false, true, false, false, false));
                Check($"low ceiling prevents standing and ordinary walking power {power}", Crouching && Player.Height == 16 && MathF.Abs(Player.Position.X - inside) < .01f);
                solid = false;
                for (int f = 0; f < 70; f++) { TickPlayer(1f / 60, new Controls(1, true, f == 0, true, false, false, false)); solid |= World.Colliders(Player.Box, false).Any(c => Player.Box.Intersects(c.box)); }
                Check($"duck jump traverses tunnel and restores standing outside power {power}", !solid && Player.Position.X > 110 && !Crouching && Player.Height == 32, Player.Position.ToString());
            }
            MechanicsWorld(); Power = 2; Player.Height = 32;
            TickPlayer(1f / 60, new Controls(0, false, false, true, true, false, false)); float standingShot = World.Shots.Single().Body.Position.Y;
            World.Shots.Clear(); fireCooldown = 0;
            TickPlayer(1f / 60, new Controls(0, false, false, true, true, true, false));
            Check("crouched fireball originates twelve pixels lower", World.Shots.Single().Body.Position.Y == standingShot + 12);
            MechanicsWorld(roof); Power = 2; Player.Height = 16; Player.Position = new(16, 0);
            TickPlayer(1f / 60, new Controls(0, false, false, true, true, false, false));
            var lowShot = World.Shots.Single(); for (int i = 0; i < 2; i++) TickShots(1f / 60);
            Check("crouched fireball travels inside one-tile tunnel", !lowShot.Removed && lowShot.Body.Position.X > 30 && !World.Colliders(lowShot.Body.Box, false).Any(c => lowShot.Body.Box.Intersects(c.box)), lowShot.Body.Position.ToString());

            MechanicsWorld(); var multi = new Block { Center = new(40, -32), Item = "Coin", Kind = "CoinBrickBlock", CoinsLeft = 10 }; World.Blocks.Add(multi);
            HitBlock(multi); Check("multi-coin window starts at first hit", multi.CoinWindowStarted && multi.CoinWindowRemaining == Block.CoinWindowDuration && Coins == 1);
            TickBlockTimers(1); HitBlock(multi); Check("multi-coin block accepts another hit inside window", Coins == 2 && !multi.Used);
            float timer = multi.CoinWindowRemaining; State = Screen.Pause; AdvanceState(.5f, new()); State = Screen.Playing;
            Check("pause freezes multi-coin window", multi.CoinWindowRemaining == timer);
            TickBlockTimers(3); Check("expired coin window leaves exactly one final coin", multi.CoinsLeft == 1 && !multi.Used);
            HitBlock(multi); TickBlockTimers(.2f); HitBlock(multi);
            Check("expired block pays final coin once then becomes used", Coins == 3 && multi.Used);
            Check("coin collection preserves score without 200 popup", Score == 600 && !World.Popups.Any(p => p.Text == "200"));
            Check("each successful block hit emits one spinning coin", World.Popups.Count(p => p.CoinArc) == 3);
            var arc = World.Popups.First(p => p.CoinArc); float arcStart = arc.Position.Y; TickPopups(.15f);
            Check("block coin rises above source block", arc.Position.Y < arcStart && arc.Position.Y - 16 < multi.Box.Position.Y && arc.Position.Y - 16 >= multi.Box.Position.Y - 24.1f && arc.Sprite == "Items/SpinningCoin.png");
            TickPopups(.6f); Check("block coin vanishes after animation", !World.Popups.Any(p => p.CoinArc));
            AddCoin(new(80, -16)); Check("ordinary coin pickup has no score label", Coins == 4 && Score == 800 && World.Popups.Count == 0);
            MechanicsWorld(); multi = new Block { Center = new(40, -32), Item = "Coin", CoinsLeft = 10 }; World.Blocks.Add(multi);
            for (int i = 0; i < 11; i++) { HitBlock(multi); TickBlockTimers(.2f); }
            Check("rapid multi-coin hits respect maximum count", Coins == 10 && multi.Used);

            foreach (int power in new[] { 0, 1, 2 })
            {
                StartGame(0, true); State = Screen.Playing; Power = power; Player.Height = power == 0 ? 16 : 32;
                var entry = World.Triggers.First(t => t.Kind == "PipeArea" && !t.Bool("exit_only") && t.Number("enter_direction") == 0);
                int row = Mathf.FloorToInt(entry.Y / 16);
                var cap = World.Data.Tiles.Where(t => t[2] == 0 && t[3] == 8 && (int)t[1] == row).OrderBy(t => MathF.Abs((t[0] + 1) * 16 - entry.X)).First();
                float middle = (cap[0] + 1) * 16; Player.Position = new(middle + 3, entry.Y); EnterPipe(entry, Vector2.Down); TickGame(.2f, new());
                Check($"vertical entry remains centered between pipe cap tiles power {power}", MathF.Abs(Player.Position.X - middle) < .01f); pipeClock = 0; afterPipe = null;
            }
            VerifyEnemyPairs();
            MechanicsWorld(); Player.Position = new(-100, 0); var burned = MechanicsEnemy("Goomba", 40, -96, -1);
            var fireball = new FireShot(); fireball.Body.Position = new(36, -104); fireball.Body.Velocity = new(240, 0); World.Shots.Add(fireball); TickShots(1f / 60);
            Check("fireball hit flips and launches defeated enemy", burned.Dead && burned.Flipped && !burned.Squashed && burned.Body.Velocity.Y < 0);
            for (int f = 0; f < 100; f++) TickEnemies(1f / 60);
            Check("flipped defeated enemy falls out of scene", burned.Removed && burned.Body.Position.Y > 80);
            foreach (bool bonus in new[] { false, true })
            {
                StartGame(0, true); State = Screen.Playing; Lives = 3; Score = 1234; Coins = 7;
                if (bonus) LoadRoom("1-1a", true); else { Player.Position = new(1800, 0); TickTriggers(1f / 60, new()); }
                Die(); FinishDeath();
                Check($"death returns to main stage start from {(bonus ? "bonus" : "middle")}", World.Data.Id == "1-1" && Player.Position == Maps.Rooms["1-1"].Start && Lives == 2 && Score == 1234 && Coins == 7 && State == Screen.Intro, Player.Position.ToString());
            }
            foreach (var room in Maps.Rooms.Values.Where(r => r.Entities.Any(e => e.Kind == "CastleBridge")))
            {
                LevelIndex = Array.IndexOf(Maps.MainLevels, room.Id[..3]); LoadRoom(room.Id, false); State = Screen.Playing; Power = 0; TimeLeft = 400;
                var bridge = World.Triggers.First(t => t.Kind == "CastleBridge"); Vector2 axe = CastleAxeFeet(bridge);
                var axeBox = new Rect2(axe - new Vector2(8, 16), new Vector2(16, 16));
                Check($"{room.Id} axe sits above wall", !World.Colliders(axeBox, false).Any(c => axeBox.Intersects(c.box)));
                Player.Position = axe; Player.Height = 16; Player.Grounded = true; TickTriggers(1f / 60, new());
                Check($"{room.Id} touching visible axe clears bridge", State == Screen.Clear && World.BridgeGone);
                solid = false;
                for (int f = 0; f < 360 && State == Screen.Clear; f++) { TickClear(1f / 60); solid |= World.Colliders(Player.Box, false).Any(c => Player.Box.Intersects(c.box)); }
                Check($"{room.Id} castle clear walks and falls along terrain", !solid && Player.Grounded && MathF.Abs(Player.Position.Y - clearCastleY) < 1 && Player.Position.X > axe.X + 80, Player.Position.ToString());
            }
            var startRoom = Maps.Rooms["2-1"]; var castle = startRoom.Entities.First(e => e.Kind == "LargeStartCastle");
            float castleBottom = startRoom.Deco.Where(t => t[2] == -1 && t[0] * 16 >= castle.X - 40 && t[0] * 16 <= castle.X + 104).Max(t => (t[1] + 1) * 16);
            Check("2-1 large start castle reaches ground", MathF.Abs(castleBottom) < .01f, castleBottom.ToString());
            var layered = startRoom.Deco.Where(t => t[2] == -1).GroupBy(t => (t[0], t[1])).Where(g => g.Any(t => t[3] == 14 && t[4] == 8) && g.Any(t => t[3] != 14 || t[4] != 8)).ToArray();
            Check("castle background layers precede overlapping masonry", layered.Length > 0 && layered.All(g => g.Last()[3] != 14 || g.Last()[4] != 8));
            MechanicsWorld(); for (int f = 0; f < 45; f++) TickPlayer(1f / 60, new Controls(1, false, false, true, false, false, false)); float normalSpeed = Player.Velocity.X;
            Player.Position = Vector2.Zero; Player.Velocity = Vector2.Zero; StarTime = 10;
            for (int f = 0; f < 45; f++) TickPlayer(1f / 60, new Controls(1, false, false, true, false, false, false));
            Check("NES star preserves normal K running speed", normalSpeed == 150 && Player.Velocity.X == normalSpeed);
            MechanicsWorld(); TimeLeft = 400; for (int f = 0; f < 120; f++) TickGame(1f / 60, new());
            Check("TIME counts at 1.5 units per second", MathF.Abs(TimeLeft - 397f) < .01f, TimeLeft.ToString("F3"));
            if (DisplayServer.GetName() != "headless") await VerifyMechanicsFrames();
        }
        catch (Exception ex) { Check("mechanics regression exception", false, ex.ToString()); }
        finally
        {
            SetPhysicsProcess(enabled);
            using var file = FileAccess.Open("res://Reports/Regression/mechanics-pipes-jump-enemies-coins-castles.json", FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(new { display = DisplayServer.GetName(), failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private void VerifyEnemyPairs()
    {
        foreach (bool reversed in new[] { false, true })
        {
            MechanicsWorld(); Player.Position = new(-100, 0);
            var left = MechanicsEnemy("Goomba", 20, 0, 1); var right = MechanicsEnemy("GreenKoopaTroopa", 32.5f, 0, -1);
            if (reversed) World.Enemies.Reverse(); TickEnemies(1f / 60);
            Check($"head-on enemies both reverse list reversed {reversed}", left.Direction == -1 && right.Direction == 1 && !left.Body.Box.Intersects(right.Body.Box));
            for (int i = 0; i < 20; i++) TickEnemies(1f / 60);
            Check($"enemy contact does not repeatedly reverse {reversed}", left.Direction == -1 && right.Direction == 1);
        }
        foreach (int direction in new[] { -1, 1 })
        {
            MechanicsWorld(); Player.Position = new(-100, 0); var left = MechanicsEnemy("Goomba", 20, 0, direction); var right = MechanicsEnemy("Goomba", 31, 0, direction);
            TickEnemies(1f / 60);
            Check($"same-direction contact turns trailing enemy direction {direction}", left.Direction == -1 && right.Direction == 1 && !left.Body.Box.Intersects(right.Body.Box));
        }
        MechanicsWorld(); Player.Position = new(-100, 0);
        var high = MechanicsEnemy("Goomba", 30, -16.2f, 1); var low = MechanicsEnemy("Goomba", 30, 0, -1);
        high.PreviousBox = high.Body.Box; low.PreviousBox = low.Body.Box; high.Body.Position += new Vector2(0, .5f); high.Simulated = low.Simulated = true; ResolveEnemyCollisions();
        Check("vertical enemy contact does not cause side reversal", high.Direction == 1 && low.Direction == -1);
        MechanicsWorld(); Player.Position = new(-100, 0); var resting = MechanicsEnemy("GreenKoopaTroopa", 33, 0, -1); resting.Shell = true; resting.Timer = 5; resting.Body.Height = 14;
        var walker = MechanicsEnemy("Goomba", 20, 0, 1); for (int f = 0; f < 4; f++) TickEnemies(1f / 60);
        Check("walker reverses against stationary shell", walker.Direction == -1 && resting.Body.Position.X == 33 && !resting.Dead);
        foreach (float step in new[] { 1f / 60, .1f })
        {
            MechanicsWorld(); Player.Position = new(-100, 0); var shell = MechanicsEnemy("GreenKoopaTroopa", 20, 0, 1); shell.Shell = shell.Kicked = true; shell.Body.Height = 14;
            var target = MechanicsEnemy("Goomba", 33, 0, -1); TickEnemies(step);
            Check($"moving shell defeats enemy without tunneling dt {step:F3}", target.Dead && target.Flipped && !shell.Dead && shell.Kicked);
        }
        foreach (int order in new[] { 0, 1, 2 })
        {
            MechanicsWorld(); Player.Position = new(-100, 0);
            var first = MechanicsEnemy("GreenKoopaTroopa", 0, -64, 1); first.Shell = first.Kicked = true; first.Body.Height = 14; first.Body.Grounded = false;
            var second = MechanicsEnemy("GreenKoopaTroopa", 14, -57, -1); second.Shell = second.Kicked = true; second.Body.Height = 14; second.Body.Grounded = false;
            var victim = MechanicsEnemy("Goomba", 13, -71, -1); victim.Body.Grounded = false;
            if (order == 1) World.Enemies.Reverse(); else if (order == 2) { World.Enemies.Remove(victim); World.Enemies.Insert(1, victim); }
            TickEnemies(1f / 60);
            Check($"shell impacts resolve earliest contact before shell-shell collision order {order}", first.Dead && second.Dead && victim.Dead && first.Combo == 2 && second.Combo == 1, $"score={Score}, combo={first.Combo}/{second.Combo}");
        }
        MechanicsWorld((0, -1), (0, -2)); Player.Position = new(-100, 0); var atWall = MechanicsEnemy("Goomba", 22, 0, -1); var behind = MechanicsEnemy("Goomba", 32, 0, -1); TickEnemies(1f / 60);
        Check("enemy separation does not push through wall", !World.Colliders(atWall.Body.Box, false).Any(c => c.box.Intersects(atWall.Body.Box)) && !atWall.Body.Box.Intersects(behind.Body.Box));
        MechanicsWorld(); Player.Position = new(-100, 0); var asleep = MechanicsEnemy("Goomba", 400, 0, 1); var neighbor = MechanicsEnemy("Goomba", 410, 0, -1); TickEnemies(1f / 60);
        Check("offscreen frozen enemies do not collide or turn", !asleep.Simulated && !neighbor.Simulated && asleep.Direction == 1 && neighbor.Direction == -1);
        foreach (bool reversed in new[] { false, true })
        {
            MechanicsWorld(); Player.Position = new(30, -23); Player.Velocity = new(0, 120); Player.Grounded = false;
            var a = MechanicsEnemy("GreenKoopaTroopa", 24, 0, 1); var b = MechanicsEnemy("GreenKoopaTroopa", 36, -.5f, -1); b.Winged = true; b.Body.Grounded = false; b.Body.Velocity = new(0, 0);
            if (reversed) World.Enemies.Reverse(); Power = 1; Player.Height = 32; TickEnemies(1f / 60);
            Check($"adjacent enemy stomp avoids order-dependent damage {reversed}", Power == 1 && State == Screen.Playing && Invulnerable == 0 && Player.Velocity.Y < 0);
        }
    }
    private async Task VerifyMechanicsFrames()
    {
        MechanicsWorld(); Player.Position = new(40, -96); Player.Grounded = true; animationTime = .15f;
        foreach (int power in new[] { 0, 1, 2 })
        {
            Power = power; Player.Height = power == 0 ? 16 : 32; Player.Velocity = new(32, 0); pipeClock = 0;
            using var walk = await CaptureMechanicsFrame();
            StartPipeAnimation(Vector2.Right, () => { });
            using var pipe = await CaptureMechanicsFrame();
            Check($"horizontal pipe uses moving sprite power {power}", SamePixels(walk, pipe)); pipeClock = 0; afterPipe = null;
        }
        MechanicsWorld(); Player.Position = new(-100, -96); var e = MechanicsEnemy("Goomba", 40, -96, -1); e.Removed = true;
        using var empty = await CaptureMechanicsFrame(); e.Removed = false; using var upright = await CaptureMechanicsFrame();
        var fireball = new FireShot(); fireball.Body.Position = new(36, -104); fireball.Body.Velocity = new(240, 0); World.Shots.Add(fireball); TickShots(1f / 60);
        World.Shots.Clear(); World.Popups.Clear(); using var upside = await CaptureMechanicsFrame();
        bool mirror = true, changed = false;
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        {
            Color normal = upright.GetPixel(32 + x, 96 + 15 - y), flipped = upside.GetPixel(32 + x, 96 + y);
            Color back = empty.GetPixel(32 + x, 96 + y);
            if (normal != flipped) mirror = false; if (flipped != back) changed = true;
        }
        Check("GPU defeated enemy is upside down at same position", mirror && changed && e.Flipped);
        upside.SavePng("res://Reports/Regression/fireball-enemy-upside-down.png");
        MechanicsWorld(); Player.Position = new(-100, 0); var block = new Block { Center = new(40, -64), Item = "Coin", CoinsLeft = 1 }; World.Blocks.Add(block);
        HitBlock(block); using var hidden = await CaptureMechanicsFrame(); TickPopups(.15f); using var raised = await CaptureMechanicsFrame();
        Check("GPU block coin visibly rises above brick", !SamePixels(hidden, raised));
        raised.SavePng("res://Reports/Regression/block-coin-pop.png");
        LevelIndex = 3; LoadRoom("1-4", false); State = Screen.Playing; CameraX = 1888; Player.Position = CastleAxeFeet(World.Triggers.First(t => t.Kind == "CastleBridge")) - new Vector2(24, 0); Player.Grounded = true;
        using var axe = await CaptureMechanicsFrame(); axe.SavePng("res://Reports/Regression/1-4-axe-position.png");
        LevelIndex = 4; LoadRoom("2-1", false); State = Screen.Playing; CameraX = World.MinX;
        using var castle = await CaptureMechanicsFrame(); castle.SavePng("res://Reports/Regression/2-1-start-castle-ground.png");
    }
    private static bool SamePixels(Image a, Image b) => a.GetWidth() == b.GetWidth() && a.GetHeight() == b.GetHeight() && a.GetData().AsSpan().SequenceEqual(b.GetData());
    private async Task<Image> CaptureMechanicsFrame()
    {
        QueueRedraw(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var frame = GetViewport().GetTexture().GetImage(); frame.Convert(Image.Format.Rgba8); return frame;
    }
}
