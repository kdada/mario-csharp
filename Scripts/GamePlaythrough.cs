using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private sealed class RouteNode
    {
        public Body Body = new();
        public bool Jump;
        public float Axis = 1, FirstAxis;
        public bool FirstJump;
        public ulong Stomped;
        public float Score;
    }
    private readonly List<object> workflowEvents = [];
    private readonly List<object> workflowSamples = [];
    private readonly List<object> workflowInputs = [];
    private bool autoJump;
    private int autoFrames, autoDecision;
    private Controls autoAction;
    private string autoRoom = "";
    private Vector2 autoGoal;
    private string autoGoalKind = "";
    private EntityData[] routeFirebars = [];
    private Rect2[] routeLava = [];

    /// <summary>End-to-end controller: only emits controls. Production state transitions
    /// execute through the same AdvanceState as the engine. No placements, level skips,
    /// artificial protection, extra lives, completion flags, or room loading.</summary>
    public async void RunPlaythrough()
    {
        SetPhysicsProcess(false); verifying = true;
        int maxFrames = 300000;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--playthrough-frames=")) int.TryParse(arg.Split('=')[1], out maxFrames);
        string outcome = "frame budget exhausted";
        try
        {
            HandleMenuKey(Key.Enter);
            RecordWorkflow("new-game");
            int lastLevel = LevelIndex, stuck = 0;
            float bestX = Player.Position.X;
            string previousRoom = World.Data.Id;
            Screen previousState = State;
            for (autoFrames = 0; autoFrames < maxFrames; autoFrames++)
            {
                Controls controls = State == Screen.Playing ? WorkflowControl() : new();
                AdvanceState(1f / 60, controls);
                if (State != previousState)
                {
                    RecordWorkflow("state " + previousState + " -> " + State);
                    previousState = State;
                    if (State == Screen.Dying) SaveWorkflow("running");
                }
                if (LevelIndex != lastLevel)
                {
                    RecordWorkflow("next-level"); lastLevel = LevelIndex;
                    bestX = Player.Position.X; stuck = 0; autoDecision = 0; SaveWorkflow("running");
                }
                if (World.Data.Id != previousRoom)
                {
                    RecordWorkflow("room-transition"); previousRoom = World.Data.Id;
                    bestX = Player.Position.X; stuck = 0; autoDecision = 0;
                }
                if (autoFrames % 120 == 0)
                    workflowSamples.Add(new { frame = autoFrames, stage = MainId, room = World.Data.Id, state = State.ToString(), x = Player.Position.X, y = Player.Position.Y, vx = Player.Velocity.X, vy = Player.Velocity.Y, camera = CameraX, power = Power, lives = Lives, target = new[] { autoGoal.X, autoGoal.Y }, targetKind = autoGoalKind });
                if (State == Screen.Ending) { outcome = "ending reached"; break; }
                if (State == Screen.GameOver) { outcome = "game over"; break; }
                if (State == Screen.Playing && pipeClock <= 0)
                {
                    if (Player.Position.X > bestX + 8) { bestX = Player.Position.X; stuck = 0; }
                    else stuck++;
                    if (stuck > 1800) { outcome = "no forward progress for 30 game seconds"; break; }
                }
                if (autoFrames % 240 == 0)
                {
                    QueueRedraw();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
        }
        catch (Exception ex) { outcome = ex.ToString(); GD.PushError("Playthrough: " + outcome); }
        RecordWorkflow(outcome); SaveWorkflow(outcome);
        bool complete = State == Screen.Ending && profile.Completed.All(x => x);
        GD.Print($"PLAYTHROUGH {(complete ? "PASS" : "INCOMPLETE")}: {outcome}; stage={MainId}, room={World.Data.Id}, position={Player.Position}, frames={autoFrames}, lives={Lives}");
        GetTree().Quit(complete ? 0 : 2);
    }
    private void RecordWorkflow(string reason)
    {
        workflowEvents.Add(new { frame = autoFrames, reason, stage = MainId, room = World.Data.Id, state = State.ToString(), x = Player.Position.X, y = Player.Position.Y, score = Score, lives = Lives, power = Power });
        GD.Print($"WORKFLOW {autoFrames} {reason}: {MainId}/{World.Data.Id} {Player.Position} lives={Lives}");
    }
    private void SaveWorkflow(string outcome)
    {
        DirAccess.MakeDirRecursiveAbsolute("res://Reports/Playthrough");
        using var file = FileAccess.Open("res://Reports/Playthrough/latest.json", FileAccess.ModeFlags.Write);
        file.StoreString(JsonSerializer.Serialize(new { outcome, complete = State == Screen.Ending && profile.Completed.All(x => x), frames = autoFrames, stage = MainId, room = World.Data.Id, state = State.ToString(), position = new[] { Player.Position.X, Player.Position.Y }, lives = Lives, completed = profile.Completed, interventions = Array.Empty<string>(), events = workflowEvents, samples = workflowSamples, inputs = workflowInputs }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private Controls WorkflowControl()
    {
        SelectWorkflowGoal();
        bool down = autoGoalKind == "top-pipe" && MathF.Abs(Player.Position.X - autoGoal.X) < 6 && MathF.Abs(Player.Position.Y - autoGoal.Y) < 5;
        if (down) { autoJump = false; return new(0, false, false, false, false, true, false); }
        if (autoDecision-- <= 0 || autoRoom != World.Data.Id)
        {
            autoRoom = World.Data.Id; autoDecision = 5;
            autoAction = SearchWorkflowAction();
            workflowInputs.Add(new { frame = autoFrames, stage = MainId, room = World.Data.Id, axis = autoAction.Axis, jump = autoAction.Jump, down });
        }
        bool jump = autoAction.Jump;
        var input = new Controls(autoAction.Axis, jump, jump && !autoJump, true, Power == 2 && autoFrames % 12 == 0, down, false);
        autoJump = jump;
        return input;
    }
    private void SelectWorkflowGoal()
    {
        var maze = World.Triggers.Where(t => t.Kind == "PickAPathPoint" && !World.MazePoints.Contains(t.Text("_path", t.Name))).OrderBy(t => t.X).FirstOrDefault();
        if (maze != null) { autoGoal = new(maze.X, MathF.Min(0, maze.Y + 24)); autoGoalKind = "maze-point"; return; }
        var flag = World.Triggers.FirstOrDefault(t => t.Kind == "EndFlagpole");
        if (flag != null) { autoGoal = new(flag.X, -8); autoGoalKind = "flag"; return; }
        var bridge = World.Triggers.FirstOrDefault(t => t.Kind == "CastleBridge");
        if (bridge != null) { autoGoal = CastleAxeFeet(bridge); autoGoalKind = "axe"; return; }
        string desired = World.Data.Id switch { "8-4" => "8-4a", "8-4a" => "8-4b", "8-4b" => "8-4c", "8-4c" => "8-4d", _ => "" };
        var pipes = World.Triggers.Where(t => t.Kind == "PipeArea" && !t.Bool("exit_only"));
        var pipe = desired.Length > 0 ? pipes.FirstOrDefault(t => t.Text("target_level") == desired) : pipes.OrderByDescending(t => t.X).FirstOrDefault();
        if (pipe != null)
        {
            autoGoal = pipe.Position; autoGoalKind = pipe.Number("enter_direction") == 3 ? "side-pipe" : "top-pipe"; return;
        }
        autoGoal = new(World.MaxX - 40, 0); autoGoalKind = "right-edge";
    }
    private Controls SearchWorkflowAction()
    {
        var enemies = World.Enemies.Where(e => !e.Dead && !e.Removed && e.Body.Position.X > Player.Position.X - 80 && e.Body.Position.X < Player.Position.X + 240).Take(63).ToArray();
        var nodes = new List<RouteNode> { new() { Body = CloneRouteBody(Player), Jump = autoJump } };
        RouteNode? best = null;
        float sign = autoGoal.X >= Player.Position.X ? 1 : -1;
        float[] axes = [sign, 0, -sign];
        for (int depth = 0; depth < 8; depth++)
        {
            var candidates = new Dictionary<(int, int, int, int, bool), RouteNode>();
            foreach (var prior in nodes)
                foreach (float axis in axes)
                    foreach (bool jump in new[] { false, true })
                    {
                        var node = new RouteNode { Body = CloneRouteBody(prior.Body), Jump = prior.Jump, Stomped = prior.Stomped, FirstAxis = depth == 0 ? axis : prior.FirstAxis, FirstJump = depth == 0 ? jump : prior.FirstJump };
                        bool valid = true;
                        for (int step = 0; step < 6; step++)
                        {
                            PredictRoutePlayer(node.Body, axis, jump, jump && !node.Jump);
                            node.Jump = jump;
                            if (!RouteSafe(node, enemies, (depth * 6 + step + 1) / 60f)) { valid = false; break; }
                        }
                        if (!valid) continue;
                        float dx = MathF.Abs(node.Body.Position.X - autoGoal.X);
                        float yWeight = dx < 160 && autoGoalKind is "maze-point" or "top-pipe" or "side-pipe" ? 1.5f : .15f;
                        node.Score = -dx - MathF.Abs(node.Body.Position.Y - autoGoal.Y) * yWeight + node.Body.Velocity.X * sign * .02f;
                        var key = ((int)(node.Body.Position.X / 6), (int)(node.Body.Position.Y / 6), (int)(node.Body.Velocity.X / 30), (int)(node.Body.Velocity.Y / 50), jump);
                        if (!candidates.TryGetValue(key, out var previous) || node.Score > previous.Score) candidates[key] = node;
                    }
            if (candidates.Count == 0) break;
            nodes = candidates.Values.OrderByDescending(n => n.Score).Take(10).ToList();
            best = nodes[0];
        }
        return best == null ? new Controls(sign, true, false, true, false, false, false) : new Controls(best.FirstAxis, best.FirstJump, false, true, false, false, false);
    }
    private void PredictRoutePlayer(Body body, float axis, bool jump, bool pressed)
    {
        const float dt = 1f / 60;
        float acceleration = body.Grounded ? 560 : 420;
        float a = MathF.Sign(body.Velocity.X) != MathF.Sign(axis) && MathF.Abs(body.Velocity.X) > 12 ? acceleration * 1.5f : acceleration;
        if (axis != 0) body.Velocity = new(Mathf.MoveToward(body.Velocity.X, axis * (World.IsWater ? 72 : 150), a * dt), body.Velocity.Y);
        else if (body.Grounded) body.Velocity = new(Mathf.MoveToward(body.Velocity.X, 0, 500 * dt), body.Velocity.Y);
        if (World.IsWater)
        {
            body.Velocity = new(body.Velocity.X, MathF.Min(84, body.Velocity.Y + 240 * dt));
            if (pressed && body.Position.Y > -176) body.Velocity = new(body.Velocity.X, -115);
            if (body.Position.Y - body.Height < -184) { body.Position = new(body.Position.X, -184 + body.Height); body.Velocity = new(body.Velocity.X, MathF.Max(0, body.Velocity.Y)); }
        }
        else
        {
            if (pressed && body.Grounded) body.Velocity = new(body.Velocity.X, JumpLaunchSpeed(true, body.Velocity.X, dt));
            body.Velocity = new(body.Velocity.X, MathF.Min(270, body.Velocity.Y + (body.Velocity.Y < 0 && jump ? 570 : 1350) * dt));
        }
        World.Move(body, dt, null, true, false, cornerCorrection: true);
    }
    private bool RouteSafe(RouteNode node, Enemy[] enemies, float future)
    {
        Body body = node.Body;
        if (body.Position.Y > 40 || body.Position.X < World.MinX + body.Width / 2 || body.Position.X > World.MaxX - body.Width / 2) return false;
        if (!World.IsWater && World.Theme == "Castle")
            foreach (var tile in World.Data.Tiles)
                if ((int)tile[2] == 2 && body.Box.Intersects(new Rect2(tile[0] * 16, tile[1] * 16 + 3, 16, 16))) return false;
        if (Invulnerable > future || StarTime > future) return true;
        for (int i = 0; i < enemies.Length; i++)
        {
            if ((node.Stomped & (1UL << i)) != 0) continue;
            var e = enemies[i];
            Vector2 at = e.Body.Position;
            if (e.Kind is "Goomba" or "GreenKoopaTroopa" or "RedKoopaTroopa" or "BuzzyBeetle" or "Spiny") at.X += e.Direction * (e.Kicked ? 220 : e.Shell ? 0 : 30) * future;
            else if (e.Kind == "BulletBill") at.X += e.Direction * 120 * future;
            else if (e.Kind == "BowserFlame") at.X += e.Direction * 90 * future;
            else if (e.Kind == "Blooper") at += e.Body.Velocity * future;
            Rect2 box = new(at.X - e.Body.Width / 2, at.Y - e.Body.Height, e.Body.Width, e.Body.Height);
            if (!body.Box.Intersects(box)) continue;
            bool stompable = e.Kind is "Goomba" or "GreenKoopaTroopa" or "RedKoopaTroopa" or "BuzzyBeetle" or "HammerBro" or "Lakitu" or "BulletBill" or "FlyingFish" or "Blooper" or "GreenCheepCheep" or "RedCheepCheep";
            if (stompable && body.Velocity.Y > 0 && body.Position.Y <= box.Position.Y + MathF.Max(7, body.Velocity.Y / 60 + 3))
            {
                body.Position = new(body.Position.X, box.Position.Y); body.Velocity = new(body.Velocity.X, World.IsWater ? -115 : -185); node.Stomped |= 1UL << i;
            }
            else return false;
        }
        foreach (var t in World.Triggers.Where(t => t.Kind == "Firebar"))
        {
            float angle = (Clock + future) * 1.85f * (t.Number("direction") == 1 ? -1 : 1);
            for (int i = 1; i <= 6; i++) if (body.Box.Intersects(new Rect2(t.Position + Vector2.FromAngle(angle) * i * 8 - Vector2.One * 3, Vector2.One * 6))) return false;
        }
        foreach (var shot in World.Shots.Where(s => s.Enemy && !s.Removed))
            if (body.Box.Intersects(new Rect2(shot.Body.Position + shot.Body.Velocity * future - new Vector2(4, 8), new Vector2(8, 8)))) return false;
        return true;
    }
    private static Body CloneRouteBody(Body source) => new() { Position = source.Position, Velocity = source.Velocity, Width = source.Width, Height = source.Height, Grounded = source.Grounded, HitWall = source.HitWall, HitCeiling = source.HitCeiling, Support = source.Support };
}
