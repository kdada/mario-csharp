using Godot;

namespace MarioStudy;

public partial class Game
{
    private string? pipeSwimAnimation;
    private string PipePlayerAnimation => pipeSwimAnimation ?? (pipeVelocity.X != 0 ? "Move" : "Idle");
    private void ResetPipeAnimation()
    {
        pipeClock = 0; pipeVelocity = Vector2.Zero; afterPipe = null; pipeSwimAnimation = null;
    }
    private void InitializeArrivalCamera()
    {
        // Arrivals must already satisfy UpdateCamera's 80..160 deadzone, including
        // the first frame after the exit animation. Room edges use the same clamp.
        CameraX = Math.Clamp(Player.Position.X - 80, World.MinX, MathF.Max(World.MinX, World.MaxX - 256));
        ResetPipeAnimation();
    }
    private void TickTriggers(float dt, Controls input)
    {
        TickTrampolineAnimations(dt);
        foreach (var trigger in World.Triggers)
        {
            string kind = trigger.Kind;
            if (kind == "EndFlagpole" && MathF.Abs(Player.Position.X - trigger.X) < 10 && Player.Position.Y >= -168 && Player.Position.Y < 8)
            { StartClear(trigger, false); return; }
            else if (kind == "CastleBridge" && Player.Box.Intersects(new Rect2(CastleAxeFeet(trigger) - new Vector2(8, 16), new Vector2(16, 16))))
            { StartClear(trigger, true); return; }
            else if (kind == "Firebar")
            {
                float direction = trigger.Number("direction", 0) == 1 ? -1 : 1;
                float angle = Clock * 1.85f * direction;
                for (int n = 1; n <= 6; n++)
                {
                    Vector2 point = trigger.Position + Vector2.FromAngle(angle) * (n * 8);
                    if (Player.Box.Intersects(new Rect2(point - Vector2.One * 3, Vector2.One * 6))) Hurt();
                }
            }
            else if (kind == "Trampoline")
            {
                if (Player.Velocity.Y > 0 && Player.Position.X > trigger.X - 12 && Player.Position.X < trigger.X + 12 && Player.Position.Y >= trigger.Y - 24 && Player.Position.Y <= trigger.Y - 8)
                { StartTrampolineAnimation(trigger); Player.Position = new(Player.Position.X, trigger.Y - 24); Player.Velocity = new(Player.Velocity.X, input.Jump ? -430 : -350); Sound.Effect("Spring"); }
            }
            else if (kind == "PipeArea" && !trigger.Bool("exit_only") && pipeCooldown <= 0)
            {
                Vector2 direction = PipeEntryDirection(trigger, input);
                if (direction != Vector2.Zero) { EnterPipe(trigger, direction); return; }
            }
            else if (kind == "TeleportPipeArea" && pipeCooldown <= 0 && input.Down && MathF.Abs(Player.Position.X - trigger.X) < 7 && MathF.Abs(Player.Position.Y - trigger.Y) < 5)
            {
                string link = trigger.Text("connecting_pipe");
                string name = link.Contains("../") ? link.Split("../")[1].TrimEnd('"', ')') : "PipeArea";
                var target = World.Triggers.FirstOrDefault(t => t.Name == name);
                if (target != null) StartPipeAnimation(Vector2.Down, () => { Player.Position = target.Position; HoldPipePlants(target.Position); InitializeArrivalCamera(); pipeCooldown = 1; });
                return;
            }
            else if (kind == "PickAPathPoint")
            {
                string path = trigger.Text("_path", trigger.Name);
                if (Player.Box.Intersects(new Rect2(trigger.X - 8, trigger.Y - 24, 16, 48)))
                    if (World.MazePoints.Add(path)) Sound.Effect("Correct");
            }
            else if (kind == "PickAPathTeleport")
            {
                string group = trigger.Text("_maze_group", trigger.Name);
                var points = World.Triggers.Where(t => t.Kind == "PickAPathPoint" && t.Text("_maze_group") == group).ToArray();
                bool solved = points.Length > 0 && points.All(t => World.MazePoints.Contains(t.Text("_path", t.Name)));
                Vector2 scale = trigger.Vector("scale", Vector2.One);
                Rect2 gate = new(trigger.X - 8, trigger.Y - 24 * scale.Y, 16, 48 * scale.Y);
                if (!solved && gate.Intersects(Player.Box) && pipeCooldown <= 0)
                {
                    Vector2 reset = trigger.Vector("reset_pos", new(-248, 0));
                    foreach (var point in points) World.MazePoints.Remove(point.Text("_path", point.Name));
                    Player.Position = reset; Player.Velocity = Vector2.Zero;
                    InitializeArrivalCamera(); pipeCooldown = .5f;
                }
            }
            else if (kind == "WarpZone" && input.Down && pipeCooldown <= 0 && trigger.P.TryGetValue("pipe_destinations", out var destinations))
            {
                for (int i = 0; i < destinations.GetArrayLength(); i++)
                {
                    int world = destinations[i].GetInt32(); float x = trigger.X + (i - 1) * 64;
                    if (world > 0 && world <= 8 && MathF.Abs(Player.Position.X - x) < 8 && MathF.Abs(Player.Position.Y - (trigger.Y - 48)) < 4)
                    { StartPipeAnimation(Vector2.Down, () => BeginLevel((world - 1) * 4)); return; }
                }
            }
            else if (kind == "WaterCurrentArea" && World.IsWater && MathF.Abs(Player.Position.X - trigger.X) < 16 && Player.Position.Y > trigger.Y)
            { Player.Velocity += new Vector2(0, 100 * dt); }
        }
        // Lava is represented by the liquid atlas; water rooms use the same atlas safely.
        if (!World.IsWater && World.Theme == "Castle")
            foreach (var tile in World.Data.Tiles)
                if ((int)tile[2] == 2 && Player.Box.Intersects(new Rect2(tile[0] * 16, tile[1] * 16 + 3, 16, 16))) { Die(); break; }
        if (World.Data.Id.StartsWith("cloud-") && Player.Position.Y > 32)
        {
            Vector2 position = pendingPosition.X == 0 ? new Vector2(1800, -208) : pendingPosition;
            LoadRoom(MainId, true, position); pipeCooldown = 1;
        }
    }
    private Vector2 PipeEntryDirection(EntityData pipe, Controls input)
    {
        if (pipe.Kind != "PipeArea" || pipe.Bool("exit_only") || pipeCooldown > 0) return Vector2.Zero;
        int direction = (int)pipe.Number("enter_direction");
        if (direction == 0 && input.Down && MathF.Abs(Player.Position.X - VerticalPipeCenter(pipe)) < 7 && MathF.Abs(Player.Position.Y - pipe.Y) < 5) return Vector2.Down;
        if (direction == 3 && (World.IsWater || Player.Grounded) && input.Axis > 0 && MathF.Abs(Player.Box.End.X - HorizontalPipeMouth(pipe)) < .05f && Player.Box.Position.Y >= pipe.Y - 16 - .05f && Player.Position.Y <= pipe.Y + 16 + .05f) return Vector2.Right;
        return Vector2.Zero;
    }
    private void PreparePipeEntry(Controls input)
    {
        foreach (var pipe in World.Triggers)
            if (PipeEntryDirection(pipe, input) != Vector2.Zero) { HoldPipePlants(pipe.Position); return; }
    }
    private void HoldPipePlants(Vector2 mouth)
    {
        foreach (var plant in World.Enemies.Where(e => e.Kind == "PiranhaPlant" && !e.Dead && !e.Removed && MathF.Abs(e.Home.X - mouth.X) <= 8 && MathF.Abs(e.Home.Y - mouth.Y) <= 8))
        {
            plant.PipeSuppressed = true; ResetHiddenPlant(plant);
        }
    }
    private static Vector2 CastleAxeFeet(EntityData bridge) => bridge.Position + new Vector2(208, -24);
    private float HorizontalPipeMouth(EntityData pipe)
    {
        var opening = new Rect2(pipe.X - 24, pipe.Y - 16, 48, 32);
        var faces = World.Colliders(opening, false).Where(c => c.block == null && c.box.Intersects(opening)).Select(c => c.box.Position.X).ToArray();
        return faces.Length == 0 ? float.NaN : faces.Min();
    }
    private float VerticalPipeCenter(EntityData pipe)
    {
        int row = Mathf.FloorToInt(pipe.Y / 16);
        var left = World.Data.Tiles.Where(t => (int)t[2] == 0 && (int)t[3] == 8 && (int)t[1] == row && MathF.Abs((t[0] + 1) * 16 - pipe.X) <= 24)
            .OrderBy(t => MathF.Abs((t[0] + 1) * 16 - pipe.X)).FirstOrDefault();
        if (left == null || !World.Data.Tiles.Any(t => t[0] == left[0] + 1 && (int)t[1] == row && (int)t[2] == 0 && (int)t[3] == 9)) return pipe.X;
        return (left[0] + 1) * 16;
    }
    private void StartPipeAnimation(Vector2 direction, Action action, Vector2? mouth = null)
    {
        HoldPipePlants(mouth ?? Player.Position);
        float travel = direction.X == 0 ? (Power == 0 ? 16 : 32) + 4 : Player.Width + 12;
        // Capture the swimming pose before pipe motion clears the player's velocity.
        pipeSwimAnimation = direction.X != 0 && World.IsWater ? Player.Velocity.Y < 0 ? "SwimUp" : "SwimIdle" : null;
        Sound.Effect("Pipe"); Player.Velocity = Vector2.Zero; pipeVelocity = direction * (travel / .75f); pipeClock = .75f; afterPipe = action;
        if (direction.X != 0) FacingLeft = direction.X < 0;
    }
    private void EnterPipe(EntityData pipe, Vector2 direction)
    {
        string target = pipe.Text("target_level");
        if (target.StartsWith("uid://")) target = direction.X != 0 ? World.IsWater ? "UnderwaterExit" : "UndergroundExit" : MainId;
        if (target.Length == 0) target = MainId;
        int pipeId = (int)pipe.Number("pipe_id");
        if (direction.X == 0) Player.Position = new(VerticalPipeCenter(pipe), pipe.Y);
        string destination = target;
        StartPipeAnimation(direction, () =>
        {
            if (!Maps.Rooms.ContainsKey(destination)) { GD.PushWarning("Unresolved pipe target " + destination); pipeCooldown = 1; return; }
            var entrance = Maps.Rooms[destination].Entities.FirstOrDefault(e => e.Kind == "PipeArea" && e.Bool("exit_only") && (int)e.Number("pipe_id") == pipeId);
            Vector2? position = entrance?.Position;
            LoadRoom(destination, true, position); pipeCooldown = 1.1f; PlayThemeMusic();
            if (entrance != null)
            {
                Vector2 exit = new(VerticalPipeCenter(entrance), entrance.Y);
                Player.Position = exit + new Vector2(0, Player.Height + 4);
                InitializeArrivalCamera();
                StartPipeAnimation(Vector2.Up, () => { Player.Position = exit; Player.Velocity = Vector2.Zero; Player.Grounded = true; Crouching = false; }, entrance.Position);
            }
        }, pipe.Position);
    }
    private void EnterCloudRoom()
    {
        var marker = Maps.Rooms[MainId].Entities.FirstOrDefault(e => e.Kind == "CoinHeavenWarpPoint");
        string target = marker?.Text("heaven_scene") ?? (MainId == "4-2" ? "4-2b" : MainId switch { "2-1" => "cloud-1", "3-1" => "cloud-2", "5-2" => "cloud-3", _ => "cloud-4" });
        pendingPosition = marker?.Position ?? new Vector2(Player.Position.X + 320, -208);
        LoadRoom(target, true); PlayThemeMusic();
    }
}
