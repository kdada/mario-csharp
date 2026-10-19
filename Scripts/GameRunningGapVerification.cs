using Godot;

namespace MarioStudy;

public partial class Game
{
    // Pure mechanism checks. The Lead's --verify-logic runner calls this method.
    private void VerifyRunningGapFixes()
    {
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            foreach (int hz in new[] { 30, 60, 120 })
                foreach (int direction in new[] { -1, 1 })
                    foreach (int power in new[] { 0, 1, 2 })
                        foreach (float phase in new[] { 0f, .2f, .4f, .6f, .8f })
                        {
                            RunningGapWorld(direction, power: power);
                            float dt = 1f / hz;
                            Player.Position = new(direction > 0 ? -Player.Width / 2 - phase * 150 * dt : 16 + Player.Width / 2 + phase * 150 * dt, 0);
                            Player.Velocity = new(direction * 150, 0);
                            float startX = Player.Position.X;
                            bool level = true, grounded = true, unobstructed = true;
                            for (int frame = 0; frame < hz / 2; frame++)
                            {
                                TickPlayer(dt, new Controls(direction, false, false, true, false, false, false));
                                level &= MathF.Abs(Player.Position.Y) < .001f;
                                grounded &= Player.Grounded;
                                unobstructed &= !Player.HitWall && Player.Velocity.X == direction * 150;
                            }
                            string label = $"running gap {hz}Hz dir {direction} power {power} phase {phase}";
                            Check(label + " crosses without sinking or side-wall collision", level && grounded && unobstructed);
                            Check(label + " preserves one ordinary horizontal move per frame", MathF.Abs(Player.Position.X - (startX + direction * 75)) < .01f, Player.Position.ToString());
                        }
            foreach (int hz in new[] { 30, 60, 120 })
                foreach (int direction in new[] { -1, 1 })
                {
                    float dt = 1f / hz;
                    RunningGapWorld(direction);
                    PlaceAtRunningGapEdge(direction, 150);
                    TickPlayer(dt, new Controls(0, false, false, false, false, false, false));
                    Check($"running gap uses actual inertia without run button {hz}Hz dir {direction}", Player.Grounded && Player.Position.Y == 0 && MathF.Abs(Player.Velocity.X) > 100);

                    foreach (bool run in new[] { false, true })
                    {
                        RunningGapWorld(direction); PlaceAtRunningGapEdge(direction, run ? 0 : 90);
                        bool fell = false;
                        for (int frame = 0; frame < hz / 3; frame++)
                        {
                            TickPlayer(dt, new Controls(direction, false, false, run, false, false, false));
                            fell |= Player.Position.Y > .001f;
                        }
                        Check($"running gap rejects {(run ? "slow run startup" : "walking speed")} {hz}Hz dir {direction}", fell);
                    }
                    foreach (int width in new[] { 32, 48 })
                    {
                        RunningGapWorld(direction, width); PlaceAtRunningGapEdge(direction, 150, width);
                        TickPlayer(dt, new Controls(direction, false, false, true, false, false, false));
                        Check($"running gap rejects {width}px hole {hz}Hz dir {direction}", Player.Position.Y > .001f && !Player.Grounded);
                    }
                    foreach (int height in new[] { -16, 16 })
                    {
                        RunningGapWorld(direction, landingHeight: height); PlaceAtRunningGapEdge(direction, 150);
                        TickPlayer(dt, new Controls(direction, false, false, true, false, false, false));
                        Check($"running gap rejects bank height {height} {hz}Hz dir {direction}", Player.Position.Y > .001f && !Player.Grounded);
                    }
                    RunningGapWorld(direction, farBank: false); PlaceAtRunningGapEdge(direction, 150);
                    TickPlayer(dt, new Controls(direction, false, false, true, false, false, false));
                    Check($"running gap cannot extend a cliff {hz}Hz dir {direction}", Player.Position.Y > .001f && !Player.Grounded);

                    RunningGapWorld(direction); PlaceAtRunningGapEdge(direction, 150); World.IsWater = true;
                    TickPlayer(dt, new Controls(direction, false, false, true, false, false, false));
                    Check($"running gap cannot bridge underwater {hz}Hz dir {direction}", Player.Position.Y > .001f && !Player.Grounded);

                    RunningGapWorld(direction); PlaceAtRunningGapEdge(direction, 150); Player.Grounded = false;
                    Player.Position = new(Player.Position.X, -8);
                    TickPlayer(dt, new Controls(direction, false, false, true, false, false, false));
                    Check($"running gap cannot snap airborne Mario to floor {hz}Hz dir {direction}", Player.Position.Y < 0 && !Player.Grounded);

                    RunningGapWorld(direction); PlaceAtRunningGapEdge(direction, 150);
                    TickPlayer(dt, new Controls(direction, true, true, true, false, false, false));
                    Check($"running gap preserves intentional jumping {hz}Hz dir {direction}", Player.Position.Y < 0 && Player.Velocity.Y < 0 && !Player.Grounded);

                    RunningGapWorld(direction); PlaceAtRunningGapEdge(direction, 150);
                    Player.Support = new Platform { Position = Player.Position, Previous = Player.Position };
                    TickPlayer(dt, new Controls(direction, false, false, true, false, false, false));
                    Check($"running gap does not treat a platform as a static bank {hz}Hz dir {direction}", Player.Position.Y > .001f && !Player.Grounded);
                }
            VerifyRunningGapGeometry();
        }
        catch (Exception ex) { Check("running gap regression exception", false, ex.ToString()); }
        finally { SetPhysicsProcess(enabled); }
    }
    private void RunningGapWorld(int direction, int gapWidth = 16, int landingHeight = 0, bool farBank = true, int power = 0, bool blockBanks = false)
    {
        MechanicsWorld();
        var tile = Maps.Rooms["1-1"].Tiles.First(t => Maps.IsSolid((int)t[2], (int)t[3], (int)t[4]));
        var data = new RoomData { Id = "1-1", Width = 1024 };
        if (blockBanks) data.Tiles.Add([-16, 16, tile[2], tile[3], tile[4], 0]);
        void AddBank(float x, int height)
        {
            if (blockBanks)
                data.Entities.Add(new EntityData { Kind = "BrickBlock", X = x + 8, Y = height + 8 });
            else
                for (int y = height / 16; y < Math.Max(4, height / 16 + 4); y++)
                    data.Tiles.Add([x / 16, y, tile[2], tile[3], tile[4], 0]);
        }
        if (direction > 0 || farBank)
            for (int x = -16; x < 0; x++) AddBank(x * 16, direction > 0 ? 0 : landingHeight);
        if (direction < 0 || farBank)
            for (int x = 0; x < 32; x++) AddBank(gapWidth + x * 16, direction < 0 ? 0 : landingHeight);
        World = new World(data, Maps, MainId); Power = power; Player.Height = power == 0 ? 16 : 32;
        Player.Grounded = true; Player.Support = null; Player.Velocity = Vector2.Zero; Crouching = false;
    }
    private void PlaceAtRunningGapEdge(int direction, float speed, int gapWidth = 16)
    {
        // The trailing edge still overlaps the incoming bank by half a pixel.
        Player.Position = new(direction > 0 ? Player.Width / 2 - .5f : gapWidth - Player.Width / 2 + .5f, 0);
        Player.Velocity = new(direction * speed, 0);
    }
    private void VerifyRunningGapGeometry()
    {
        foreach (int direction in new[] { -1, 1 })
        {
            RunningGapWorld(direction, 8, blockBanks: true); PlaceAtRunningGapEdge(direction, 150, 8);
            for (int frame = 0; frame < 30; frame++) TickPlayer(1f / 60, new Controls(direction, false, false, true, false, false, false));
            Check("running gap accepts smaller exposed block gap dir " + direction, Player.Grounded && Player.Position.Y == 0 && MathF.Abs(Player.Velocity.X) == 150);

            RunningGapWorld(direction); Player.Position = new(8, 0); Player.Velocity = new(direction * 100, 1);
            MovePlayerWithRunningGap(1f / 120, true, false);
            Check("running gap rejects exact 100px speed threshold dir " + direction, Player.Position.Y > 0 && !Player.Grounded);

            RunningGapWorld(direction); Player.Position = new(8, 0); Player.Velocity = new(direction * 105, 0);
            TickPlayer(1f / 120, new());
            bool initiallySupported = Player.Grounded && Player.Position.Y == 0;
            TickPlayer(1f / 120, new());
            Check("running gap releases support when braking below threshold dir " + direction, initiallySupported && Player.Position.Y > 0 && !Player.Grounded);

            RunningGapWorld(direction);
            // A wall beyond the far bank must keep its original collision behavior.
            float wallX = direction > 0 ? 32 : -32;
            var tile = Maps.Rooms["1-1"].Tiles.First(t => Maps.IsSolid((int)t[2], (int)t[3], (int)t[4]));
            World.Data.Tiles.Add([wallX / 16, -1, tile[2], tile[3], tile[4], 0]);
            World = new World(World.Data, Maps, MainId); PlaceAtRunningGapEdge(direction, 150);
            for (int frame = 0; frame < 30; frame++) TickPlayer(1f / 60, new Controls(direction, false, false, true, false, false, false));
            float wallFace = direction > 0 ? wallX : wallX + 16;
            Check("running gap preserves solid wall beyond bank dir " + direction, Player.Grounded && Player.Position.Y == 0 && MathF.Abs(Player.Position.X - (wallFace - direction * Player.Width / 2)) < .001f && Player.HitWall);

            foreach (bool hidden in new[] { false, true })
            {
                RunningGapWorld(direction, blockBanks: true);
                foreach (var block in World.Blocks.Where(b => direction > 0 ? b.Box.Position.X >= 16 : b.Box.End.X <= 0))
                { block.Removed = !hidden; block.Hidden = hidden; }
                PlaceAtRunningGapEdge(direction, 150); TickPlayer(1f / 60, new Controls(direction, false, false, true, false, false, false));
                Check($"running gap ignores {(hidden ? "hidden" : "removed")} landing bank dir {direction}", Player.Position.Y > 0 && !Player.Grounded);
            }
            foreach (int height in new[] { -1, 1, -8, 8 })
            {
                RunningGapWorld(direction, landingHeight: height, blockBanks: true); PlaceAtRunningGapEdge(direction, 150);
                TickPlayer(1f / 120, new Controls(direction, false, false, true, false, false, false));
                Check($"running gap rejects non-tile bank height {height} dir {direction}", Player.Position.Y > 0 && !Player.Grounded);
            }
            RunningGapWorld(direction);
            World.Data.Tiles.RemoveAll(t => direction > 0 ? t[0] >= 2 : t[0] < -1);
            World = new World(World.Data, Maps, MainId); PlaceAtRunningGapEdge(direction, 150);
            bool reachedBank = false, fellAfterBank = false;
            for (int frame = 0; frame < 30; frame++)
            {
                TickPlayer(1f / 60, new Controls(direction, false, false, true, false, false, false));
                reachedBank |= Player.Position.Y == 0 && (direction > 0 ? Player.Position.X > 22 : Player.Position.X < -6);
                fellAfterBank |= reachedBank && Player.Position.Y > 0;
            }
            Check("running gap cannot extend far bank past its following cliff dir " + direction, reachedBank && fellAfterBank);
        }
    }
}
