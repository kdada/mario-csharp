using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    // State-only checks: no renderer, frame capture, or editor is needed.
    private void VerifyPipeArrivalFixes()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            foreach (var room in Maps.Rooms.Values)
            {
                LoadPipeRegressionRoom(room.Id, 0);
                CheckStablePipeCamera(room.Id + " room load");
            }
            foreach (var room in Maps.Rooms.Values)
                foreach (var entry in room.Entities.Where(e => e.Kind == "PipeArea" && !e.Bool("exit_only")))
                    foreach (int power in new[] { 0, 1, 2 })
                    {
                        LoadPipeRegressionRoom(room.Id, power);
                        Vector2 direction = entry.Number("enter_direction") == 3 ? Vector2.Right : Vector2.Down;
                        string destination = entry.Text("target_level");
                        if (destination.StartsWith("uid://")) destination = direction.X != 0 ? World.IsWater ? "UnderwaterExit" : "UndergroundExit" : MainId;
                        if (destination.Length == 0) destination = MainId;
                        string label = $"{room.Id}/{entry.Name} to {destination} power {power}";
                        Check(label + " target resolves", Maps.Rooms.ContainsKey(destination));
                        if (!Maps.Rooms.ContainsKey(destination)) continue;
                        Player.Position = direction.X != 0 ? new(HorizontalPipeMouth(entry) - Player.Width / 2, entry.Y + 16) : new(VerticalPipeCenter(entry), entry.Y);
                        Player.Grounded = !World.IsWater; Player.Velocity = new(40, World.IsWater ? -60 : 0);
                        EnterPipe(entry, direction); TickGame(.75f, new());
                        Check(label + " reaches destination", World.Data.Id == destination);
                        CheckStablePipeCamera(label + " arrival");
                        if (pipeClock > 0)
                        {
                            Check(label + " vertical exit clears swimming pose", PipePlayerAnimation == "Idle" && pipeSwimAnimation == null);
                            float camera = CameraX;
                            TickGame(.375f, new()); TickGame(.375f, new());
                            Check(label + " exit keeps camera fixed", CameraX == camera && pipeClock == 0 && pipeSwimAnimation == null);
                        }
                        CheckStablePipeCamera(label + " after exit");
                        CheckFirstPipeGameplayFrame(label);
                    }
            VerifySameRoomPipeArrivals();
            VerifyHorizontalPipeRules();
            VerifyPipePoseLifetime();
            VerifyArrivalCameraBacktracking();
            VerifyHighJumpWalkingSpeed();
        }
        catch (Exception ex) { Check("pipe arrival regression exception", false, ex.ToString()); }
        finally
        {
            SetPhysicsProcess(enabled);
            DirAccess.MakeDirRecursiveAbsolute("res://Reports/Regression");
            using var file = FileAccess.Open("res://Reports/Regression/pipe-arrival-grounding-swim.json", FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(new { failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private void LoadPipeRegressionRoom(string id, int power)
    {
        int index = id.Length >= 3 ? Array.IndexOf(Maps.MainLevels, id[..3]) : -1;
        StartGame(Math.Max(0, index), true); Power = power; LoadRoom(id, false);
        State = Screen.Playing; TimeLeft = 400; Invulnerable = 10; pipeCooldown = 0;
    }
    private void CheckStablePipeCamera(string label)
    {
        float camera = CameraX;
        UpdateCamera();
        Check(label + " has no first-frame camera correction", MathF.Abs(CameraX - camera) < .001f, $"before={camera:F3}, after={CameraX:F3}, player={Player.Position.X:F3}");
        Check(label + " stays within room camera bounds", CameraX >= World.MinX && CameraX <= MathF.Max(World.MinX, World.MaxX - 256));
    }
    private void CheckFirstPipeGameplayFrame(string label)
    {
        // Isolate the arrival from unrelated hazards while exercising the complete tick.
        World.Enemies.Clear(); World.Items.Clear(); World.Shots.Clear(); World.Triggers.Clear(); World.Coins.Clear();
        float camera = CameraX;
        TickGame(1f / 60, new());
        Check(label + " first gameplay tick keeps camera fixed", MathF.Abs(CameraX - camera) < .001f, $"before={camera:F3}, after={CameraX:F3}");
    }
    private void VerifySameRoomPipeArrivals()
    {
        foreach (var room in Maps.Rooms.Values)
            foreach (var teleport in room.Entities.Where(e => e.Kind == "TeleportPipeArea"))
            {
                LoadPipeRegressionRoom(room.Id, 0);
                string link = teleport.Text("connecting_pipe");
                string name = link.Contains("../") ? link.Split("../")[1].TrimEnd('"', ')') : "PipeArea";
                var target = World.Triggers.FirstOrDefault(t => t.Name == name);
                string label = room.Id + "/" + teleport.Name + " same-room pipe";
                Check(label + " target resolves", target != null);
                if (target == null) continue;
                World.Triggers.RemoveAll(t => t != teleport && t != target);
                Player.Position = teleport.Position;
                TickTriggers(1f / 60, new Controls(0, false, false, false, false, true, false));
                Check(label + " starts transition", pipeClock > 0);
                TickGame(.75f, new());
                Check(label + " reaches linked pipe", Player.Position == target.Position && pipeClock == 0);
                CheckStablePipeCamera(label); CheckFirstPipeGameplayFrame(label);
            }
        foreach (var room in Maps.Rooms.Values)
            foreach (var gate in room.Entities.Where(e => e.Kind == "PickAPathTeleport"))
            {
                LoadPipeRegressionRoom(room.Id, 0);
                World.Triggers.RemoveAll(t => t != gate); World.MazePoints.Clear(); Player.Position = gate.Position;
                TickTriggers(1f / 60, new());
                string label = room.Id + "/" + gate.Name + " unsolved maze";
                Check(label + " returns to reset position", Player.Position == gate.Vector("reset_pos", new(-248, 0)) && Player.Velocity == Vector2.Zero);
                CheckStablePipeCamera(label); CheckFirstPipeGameplayFrame(label);
            }
        foreach (var room in Maps.Rooms.Values)
            foreach (var marker in room.Entities.Where(e => e.Kind == "CoinHeavenWarpPoint" && e.Text("heaven_scene").StartsWith("cloud-")))
            {
                LoadPipeRegressionRoom(room.Id, 0); EnterCloudRoom();
                string label = room.Id + " cloud return";
                Check(label + " enters cloud room", World.Data.Id == marker.Text("heaven_scene"));
                CheckStablePipeCamera(label + " cloud arrival");
                World.Triggers.Clear(); Player.Position = new(Player.Position.X, 33);
                TickTriggers(1f / 60, new());
                Check(label + " restores return point", World.Data.Id == room.Id && Player.Position == marker.Position);
                CheckStablePipeCamera(label); CheckFirstPipeGameplayFrame(label);
            }
    }
    private void VerifyHorizontalPipeRules()
    {
        var inward = new Controls(1, false, false, false, false, false, false);
        foreach (var room in Maps.Rooms.Values)
            foreach (var pipe in room.Entities.Where(e => e.Kind == "PipeArea" && !e.Bool("exit_only") && e.Number("enter_direction") == 3))
                foreach (int power in new[] { 0, 1, 2 })
                    foreach (float verticalSpeed in new[] { -60f, 60f })
                    {
                        LoadPipeRegressionRoom(room.Id, power); World.Triggers.RemoveAll(t => t != pipe);
                        float mouth = HorizontalPipeMouth(pipe);
                        string label = $"{room.Id} horizontal power {power} vy {verticalSpeed}";
                        Check(label + " mouth exists", float.IsFinite(mouth));
                        if (!float.IsFinite(mouth)) continue;
                        Vector2 contact = new(mouth - Player.Width / 2, pipe.Y + 16);
                        Player.Position = contact; Player.Grounded = false; Player.Velocity = new(40, verticalSpeed);
                        if (!World.IsWater)
                        {
                            TickTriggers(1f / 60, inward);
                            Check(label + " airborne contact cannot enter or snap", pipeClock == 0 && Player.Position == contact);
                            Player.Grounded = true;
                        }
                        Player.Position = contact - new Vector2(4, 0); TickTriggers(1f / 60, inward);
                        Check(label + " gap cannot enter or snap", pipeClock == 0 && Player.Position == contact - new Vector2(4, 0));
                        Player.Position = contact; TickTriggers(1f / 60, new Controls(-1, false, false, false, false, false, false));
                        Check(label + " opposite input cannot enter", pipeClock == 0);
                        TickTriggers(1f / 60, new()); Check(label + " neutral input cannot enter", pipeClock == 0);
                        Player.Position = new(contact.X, pipe.Y - 17); TickTriggers(1f / 60, inward);
                        Check(label + " wrong height cannot enter", pipeClock == 0);
                        Player.Position = contact; TickTriggers(1f / 60, inward);
                        string pose = World.IsWater ? verticalSpeed < 0 ? "SwimUp" : "SwimIdle" : "Move";
                        Check(label + " valid contact enters without snapping", pipeClock > 0 && Player.Position == contact);
                        Check(label + " captures entry pose before clearing velocity", PipePlayerAnimation == pose && Player.Velocity == Vector2.Zero && (World.IsWater ? !Player.Grounded : Player.Grounded));
                        float clock = Clock, animation = animationTime, time = TimeLeft, camera = CameraX;
                        var entryWorld = World;
                        AdvanceState(.25f, inward);
                        Check(label + " transition freezes pose and world clocks", PipePlayerAnimation == pose && Clock == clock && animationTime == animation && TimeLeft == time && CameraX == camera && Player.Position.Y == contact.Y);
                        Check(label + " entry progresses inside the source room before switching", ReferenceEquals(World, entryWorld) && pipeClock > .49f && Player.Position.X > contact.X && Player.Position.X < contact.X + Player.Width + 12 && PlayerAnimation == pose);
                        // A room load must discard a captured swimming pose and its callback.
                        LoadRoom("1-1", false);
                        Check(label + " room change clears transition", pipeClock == 0 && pipeVelocity == Vector2.Zero && afterPipe == null && pipeSwimAnimation == null);
                    }
    }
    private void VerifyPipePoseLifetime()
    {
        LoadPipeRegressionRoom("8-4c", 0); Player.Velocity = new(0, -60);
        bool completed = false;
        StartPipeAnimation(Vector2.Right, () => completed = true);
        Vector2 start = Player.Position, velocity = pipeVelocity;
        TickGame(1, new());
        Check("completed swimming pipe clears pose and callback", completed && pipeClock == 0 && afterPipe == null && pipeVelocity == Vector2.Zero && pipeSwimAnimation == null);
        Check("oversized pipe step stops at transition endpoint", Player.Position.DistanceTo(start + velocity * .75f) < .001f);
        LoadPipeRegressionRoom("8-4c", 0); Player.Velocity = new(0, -60);
        StartPipeAnimation(Vector2.Down, () => { });
        Check("vertical water pipe retains idle pose", PipePlayerAnimation == "Idle" && pipeSwimAnimation == null);
        ResetPipeAnimation(); Player.Velocity = new(0, -60); completed = false;
        StartPipeAnimation(Vector2.Right, () => completed = true); Die();
        Check("death cancels captured swimming transition", State == Screen.Dying && pipeClock == 0 && afterPipe == null && pipeSwimAnimation == null && !completed);
        LoadPipeRegressionRoom("8-4c", 0); Player.Velocity = new(0, -60); completed = false;
        StartPipeAnimation(Vector2.Right, () => completed = true); LoadRoom("1-1", false);
        Check("room change cancels swimming callback", pipeClock == 0 && afterPipe == null && pipeSwimAnimation == null && !completed);
        StartPipeAnimation(Vector2.Right, () => { });
        Check("ground pipe cannot inherit swimming pose", PipePlayerAnimation == "Move" && pipeSwimAnimation == null);
        ResetPipeAnimation();
        LoadPipeRegressionRoom("8-4c", 0); Player.Velocity = new(0, -60); completed = false;
        StartPipeAnimation(Vector2.Right, () => completed = true); TogglePause();
        float remaining = pipeClock, clock = Clock, animation = animationTime;
        AdvanceState(.25f, new());
        Check("pause preserves frozen swimming transition", State == Screen.Pause && pipeClock == remaining && Clock == clock && animationTime == animation && PipePlayerAnimation == "SwimUp");
        pauseChoice = 2; HandleMenuKey(Key.J);
        Check("selecting another level cancels swimming transition", State == Screen.Select && pipeClock == 0 && afterPipe == null && pipeSwimAnimation == null && !completed);
    }
    private void VerifyArrivalCameraBacktracking()
    {
        MechanicsWorld(); Player.Position = new(256, 0); InitializeArrivalCamera();
        float camera = CameraX;
        Player.Position = new(camera + 120, 0); UpdateCamera();
        Check("arrival camera preserves normal deadzone", CameraX == camera);
        Player.Position = new(camera + 180, 0); UpdateCamera();
        Check("arrival camera still follows rightward movement", CameraX == camera + 20);
        camera = CameraX; Player.Position = new(camera + 60, 0); UpdateCamera();
        Check("arrival camera still follows leftward backtracking", CameraX == camera - 20);
        foreach (float edge in new[] { World.MinX + Player.Width / 2, World.MaxX - Player.Width / 2 })
        {
            Player.Position = new(edge, 0); InitializeArrivalCamera(); CheckStablePipeCamera("room edge " + edge);
        }
    }
    private void VerifyHighJumpWalkingSpeed()
    {
        foreach (int hz in new[] { 30, 60, 120 })
        {
            MechanicsWorld(); float highest = 0, fastest = 0;
            for (int frame = 0; frame < hz; frame++)
            {
                TickPlayer(1f / hz, new Controls(1, true, frame == 0, false, false, false, false, HighJump: true));
                highest = MathF.Max(highest, -Player.Position.Y); fastest = MathF.Max(fastest, MathF.Abs(Player.Velocity.X));
            }
            Check($"dedicated high jump reaches four tiles while walking {hz}Hz", highest >= 64 && highest <= 65.1f, highest.ToString("F3"));
            Check($"dedicated high jump preserves walking speed {hz}Hz", fastest == 90, fastest.ToString("F3"));
        }
    }
}
