using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    /// <summary>Actual map/collider and flag-clear motion checks; never renders or captures a frame.</summary>
    private void VerifyFlagBaseFixes()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool processing = IsPhysicsProcessing(), previousVerifying = verifying, previousPractice = practice;
        verifying = true; SetPhysicsProcess(false); ReleaseTouchControls();
        EntityData Prepare(RoomData room, int power)
        {
            int index = Array.IndexOf(Maps.MainLevels, room.Id);
            if (index < 0) index = room.Id == "UnderwaterExit" ? 5 : 1;
            StartGame(index, true);
            if (World.Data.Id != room.Id) LoadRoom(room.Id, false);
            State = Screen.Playing; Power = power; Crouching = FacingLeft = false;
            Player.Width = 12; Player.Height = power == 0 ? 16 : 32;
            Player.Velocity = Vector2.Zero; Player.Support = null; Player.Grounded = false;
            TimeLeft = 400;
            return World.Triggers.Single(t => t.Kind == "EndFlagpole");
        }
        bool Penetrates(Body body) => World.Colliders(body.Box, false).Any(c => body.Box.Intersects(c.box));
        void GravityStep(Body body, float vx, float dt)
        {
            body.Velocity = new(vx, MathF.Min(270, body.Velocity.Y + 570 * dt));
            World.Move(body, dt);
        }
        try
        {
            var rooms = Maps.Rooms.Values.Where(r => r.Entities.Any(e => e.Kind == "EndFlagpole")).ToArray();
            Check("flag-base coverage includes all 22 authored pole rooms", rooms.Length == 22, string.Join(", ", rooms.Select(r => r.Id)));
            // Underground and underwater exits are shared by four main courses; cover their graph too.
            foreach (string main in Maps.MainLevels.Where(id => !id.EndsWith("-4")))
            {
                HashSet<string> seen = [];
                Queue<string> pending = new(); pending.Enqueue(main);
                bool reachesPole = false;
                while (pending.TryDequeue(out var id))
                {
                    if (!seen.Add(id) || !Maps.Rooms.TryGetValue(id, out var room)) continue;
                    reachesPole |= room.Entities.Any(e => e.Kind == "EndFlagpole");
                    foreach (var pipe in room.Entities.Where(e => e.Kind == "PipeArea"))
                    {
                        string target = pipe.Text("target_level");
                        if (Maps.Rooms.ContainsKey(target) && !seen.Contains(target)) pending.Enqueue(target);
                    }
                }
                Check("main course reaches an authored flag base " + main, reachesPole);
            }
            foreach (var room in rooms)
            {
                foreach (int power in new[] { 0, 1, 2 })
                {
                    var flag = Prepare(room, power); string label = $"{room.Id} power={power}";
                    var tile = room.Tiles.SingleOrDefault(t => t[0] * 16 <= flag.X && (t[0] + 1) * 16 > flag.X &&
                        t[1] * 16 <= flag.Y - 8 && (t[1] + 1) * 16 > flag.Y - 8);
                    Rect2? actual = World.FlagBase(flag);
                    Check("flag base comes from a solid authored tile " + label, tile != null && Maps.IsSolid((int)tile[2], (int)tile[3], (int)tile[4]));
                    Check("flag base has a real terrain collider " + label, actual.HasValue);
                    if (tile == null || !actual.HasValue) continue;
                    Rect2 baseBox = new(tile[0] * 16, tile[1] * 16, 16, 16);
                    Check("flag base query matches map tile and collider " + label, actual.Value == baseBox &&
                        World.Colliders(baseBox, false).Any(c => c.box == baseBox && c.block == null) &&
                        World.Colliders(baseBox, true).Any(c => c.box == baseBox && c.block == null) && World.SolidAt(baseBox.GetCenter()));
                    float height = Player.Height;
                    Body falling = new() { Position = new(flag.X, baseBox.Position.Y - 24), Height = height };
                    bool fallThrough = false;
                    for (int frame = 0; frame < 120; frame++)
                    {
                        GravityStep(falling, 0, 1f / 60); fallThrough |= Penetrates(falling);
                        if (falling.Grounded) break;
                    }
                    Check("flag base supports a falling player " + label, !fallThrough && falling.Grounded && MathF.Abs(falling.Position.Y - baseBox.Position.Y) < .001f);
                    bool stable = true;
                    for (int frame = 0; frame < 30; frame++)
                    {
                        GravityStep(falling, 0, 1f / 60);
                        stable &= falling.Grounded && MathF.Abs(falling.Position.Y - baseBox.Position.Y) < .001f && !Penetrates(falling);
                    }
                    Check("flag base remains solid while standing " + label, stable);
                    foreach (int direction in new[] { 1, -1 })
                    {
                        float startX = direction > 0 ? baseBox.Position.X - 10 : baseBox.End.X + 10;
                        Body side = new() { Position = new(startX, baseBox.End.Y), Height = height };
                        bool inside = false, wall = false;
                        for (int frame = 0; frame < 30; frame++)
                        {
                            GravityStep(side, direction * 90, 1f / 60); inside |= Penetrates(side); wall |= side.HitWall;
                        }
                        float stoppedX = direction > 0 ? baseBox.Position.X - side.Width / 2 : baseBox.End.X + side.Width / 2;
                        Check($"flag base blocks {(direction > 0 ? "rightward" : "leftward")} ground approach {label}", wall && !inside && side.Grounded && MathF.Abs(side.Position.X - stoppedX) < .001f);
                    }
                    bool off = false, walkInside = false;
                    for (int frame = 0; frame < 120; frame++)
                    {
                        GravityStep(falling, 52, 1f / 60); walkInside |= Penetrates(falling);
                        if (falling.Box.Position.X > baseBox.End.X + 8 && falling.Grounded && MathF.Abs(falling.Position.Y - baseBox.End.Y) < .001f) { off = true; break; }
                    }
                    Check("walking right off flag base lands on normal ground " + label, off && !walkInside);
                    bool returnedWall = false;
                    for (int frame = 0; frame < 60; frame++)
                    {
                        GravityStep(falling, -90, 1f / 60); returnedWall |= falling.HitWall; walkInside |= Penetrates(falling);
                    }
                    Check("walking back cannot pass through flag base " + label, returnedWall && !walkInside && falling.Grounded && MathF.Abs(falling.Position.X - baseBox.End.X - falling.Width / 2) < .001f);

                    // The old clear animation teleported the feet to -8, halfway into the -16..0 base.
                    Player.Position = new(flag.X - 8, baseBox.End.Y); StartClear(flag, false);
                    Check("flag clear immediately clamps low grabs above stone " + label, State == Screen.Clear && Player.Position.Y <= baseBox.Position.Y && !Penetrates(Player));
                    foreach (int hz in new[] { 30, 60, 120 })
                    {
                        flag = Prepare(room, power); baseBox = World.FlagBase(flag)!.Value;
                        Player.Position = new(flag.X - 8, baseBox.Position.Y - 120);
                        StartClear(flag, false);
                        bool neverInside = !Penetrates(Player), slideAbove = true, monotonic = true;
                        bool stoodOnBase = false, walkedOnBase = false, leftBase = false, landedAfterBase = false;
                        float lastX = Player.Position.X, lastY = Player.Position.Y;
                        for (int frame = 0; frame < hz * 5 && State == Screen.Clear && !clearInsideCastle; frame++)
                        {
                            TickClear(1f / hz); neverInside &= !Penetrates(Player);
                            monotonic &= Player.Position.X >= lastX - .001f;
                            if (StateClock < 1.3f)
                                slideAbove &= Player.Position.Y <= baseBox.Position.Y + .001f && Player.Position.Y >= lastY - .001f && MathF.Abs(Player.Position.X - flag.X + 8) < .001f;
                            bool overlapsBaseX = Player.Box.End.X > baseBox.Position.X && Player.Box.Position.X < baseBox.End.X;
                            bool onBase = overlapsBaseX && Player.Grounded && MathF.Abs(Player.Position.Y - baseBox.Position.Y) < .001f;
                            stoodOnBase |= onBase;
                            walkedOnBase |= onBase && Player.Position.X > flag.X - 8 + .01f;
                            leftBase |= Player.Box.Position.X >= baseBox.End.X;
                            landedAfterBase |= stoodOnBase && leftBase && Player.Grounded && MathF.Abs(Player.Position.Y - baseBox.End.Y) < .001f;
                            lastX = Player.Position.X; lastY = Player.Position.Y;
                        }
                        string trace = $"{label} {hz}Hz";
                        Check("flag slide keeps feet above base and moves down smoothly " + trace, slideAbove);
                        Check("flag clear never penetrates real map colliders " + trace, neverInside);
                        Check("flag clear stands on base before walking off " + trace, stoodOnBase && walkedOnBase);
                        Check("flag clear walks right and lands on normal ground " + trace, monotonic && leftBase && landedAfterBase);
                        Check("flag clear reaches castle on the ground " + trace, State == Screen.Clear && clearInsideCastle && Player.Grounded && MathF.Abs(Player.Position.X - clearCastleX) < .01f && MathF.Abs(Player.Position.Y - clearCastleY) < .01f,
                            $"feet={Player.Position}, castle=({clearCastleX},{clearCastleY}), grounded={Player.Grounded}");
                    }
                }
            }
        }
        catch (Exception ex) { Check("flag base verification exception", false, ex.ToString()); }
        finally
        {
            ReleaseTouchControls(); practice = previousPractice; verifying = previousVerifying; SetPhysicsProcess(processing);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://Reports/Regression"));
            using var report = FileAccess.Open("res://Reports/Regression/flag-base-collision-clear.json", FileAccess.ModeFlags.Write);
            report.StoreString(JsonSerializer.Serialize(new { graphicsIncluded = false, failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
