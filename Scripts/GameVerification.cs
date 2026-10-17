using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private readonly List<object> verificationChecks = [];
    private int verificationFailures;
    private string lastRenderError = "";
    private void Check(string name, bool passed, string detail = "")
    {
        verificationChecks.Add(new { name, passed, detail });
        GD.Print($"{(passed ? "PASS" : "FAIL")} {name} {detail}");
        if (!passed) verificationFailures++;
    }
    public async void RunVerification()
    {
        try
        {
            foreach (var pair in Maps.Rooms)
            {
                LevelIndex = 0; State = Screen.Playing; LoadRoom(pair.Key, false);
                foreach (var sampleEnemy in World.Enemies) sampleEnemy.Active = true;
                foreach (float fraction in new[] { 0f, .5f, 1f })
                {
                    CameraX = Mathf.Lerp(World.MinX, MathF.Max(World.MinX, World.MaxX - 256), fraction);
                    lastRenderError = ""; QueueRedraw();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Check($"render {pair.Key} {fraction:F1}", lastRenderError.Length == 0, lastRenderError);
                }
            }
            Check("32 main levels", Maps.MainLevels.Length == 32);
            foreach (string id in Maps.MainLevels)
            {
                LevelIndex = Array.IndexOf(Maps.MainLevels, id); LoadRoom(id, false);
                var testBody = new Body { Position = World.Data.Start, Height = 16 };
                for (int i = 0; i < 180; i++) { testBody.Velocity += new Vector2(0, 900f / 60); World.Move(testBody, 1f / 60); }
                bool goal = World.Triggers.Any(t => t.Kind is "EndFlagpole" or "CastleBridge") || World.Triggers.Any(t => t.Kind == "PipeArea" && !t.Bool("exit_only"));
                Check("level " + id, testBody.Position.Y < 40 && World.Data.Tiles.Count > 100 && goal, $"tiles={World.Data.Tiles.Count}, entities={World.Data.Entities.Count}, spawnFloor={testBody.Position.Y:F1}");
            }
            foreach (var pair in Maps.Rooms)
            {
                var room = new World(pair.Value, Maps, MainId);
                Check("room data " + pair.Key, pair.Value.Tiles.Count > 0 && pair.Value.Entities.All(e => float.IsFinite(e.X) && float.IsFinite(e.Y)));
            }
            string[] sprites = ["Players/Mario/Small.png", "Players/Mario/Big.png", "Players/Mario/Fire.png", "Enemies/Goomba.png", "Enemies/KoopaTroopa.png", "Enemies/Bowser.png", "Enemies/Blooper.png", "Enemies/CheepCheep.png", "Enemies/HammerBro.png", "Enemies/Lakitu.png", "Enemies/PiranhaPlant.png", "Enemies/BuzzyBeetle.png", "Enemies/Spiny.png", "Enemies/BulletBill.png", "Items/StaticCoin.png", "Items/FireFlower.png", "Items/SuperMushroom.png", "Items/1UP.png", "Items/SuperStar.png", "Blocks/QuestionBlock.png", "Tilesets/Terrain/Overworld.png", "Tilesets/Terrain/Underground.png", "Tilesets/Terrain/Castle.png", "Tilesets/Terrain/Underwater.png"];
            DirAccess.MakeDirRecursiveAbsolute("res://Reports");
            foreach (string sprite in sprites)
            {
                var texture = Art.Texture(sprite);
                Check("original art " + sprite, texture.GetWidth() > 0 && texture.GetHeight() > 0);
            }
            Art.Texture("Players/Mario/Small.png").GetImage().SavePng("res://Reports/mario-sheet.png");
            Art.Texture("Tilesets/Terrain/Overworld.png").GetImage().SavePng("res://Reports/terrain-sheet.png");
            Art.Glyphs.GetImage().SavePng("res://Reports/glyph-sheet.png");
            StartGame(0, true); State = Screen.Playing; TimeLeft = 400;
            for (int i = 0; i < 4; i++) TickPlayer(1f / 60, new());
            float initial = Player.Position.Y, top = initial;
            for (int i = 0; i < 65; i++) { TickPlayer(1f / 60, new Controls(0, true, i == 0, false, false, false, false)); top = MathF.Min(top, Player.Position.Y); }
            float highJump = initial - top;
            Check("variable jump long", highJump > 40 && highJump < 80, highJump.ToString("F2"));
            Player.Position = World.Data.Start; Player.Velocity = Vector2.Zero; Player.Grounded = true; top = Player.Position.Y;
            for (int i = 0; i < 65; i++) { TickPlayer(1f / 60, new Controls(0, i < 4, i == 0, false, false, false, false)); top = MathF.Min(top, Player.Position.Y); }
            float shortJump = initial - top;
            Check("variable jump release", shortJump < highJump - 10 && shortJump > 10, shortJump.ToString("F2"));
            float pausedClock = Clock; State = Screen.Pause;
            for (int i = 0; i < 30; i++) _PhysicsProcess(1d / 60);
            Check("pause freezes hazard animation clock", Clock == pausedClock);
            State = Screen.Playing;
            Player.Position = new(-220, 0); Player.Velocity = Vector2.Zero;
            for (int i = 0; i < 90; i++) TickPlayer(1f / 60, new Controls(1, false, false, true, false, false, false));
            Check("run accelerates", Player.Position.X > -100 && Player.Velocity.X >= 100, Player.Position.ToString());
            var question = World.Blocks.First(b => b.Kind.Contains("PowerUp") || b.Item == "FireFlower");
            HitBlock(question); Check("question emits mushroom", question.Used && World.Items.Any(i => i.Kind == "SuperMushroom"));
            var brick = World.Blocks.First(b => b.Item.Length == 0); Power = 1; HitBlock(brick); Check("big Mario breaks brick", brick.Removed);
            Power = 2; Player.Position = new(-208, 0); Player.Grounded = true; fireCooldown = 0;
            TickPlayer(1f / 60, new Controls(0, false, false, false, true, false, false)); Check("fire flower shoots", World.Shots.Count(s => !s.Enemy) == 1);
            Power = 1; Invulnerable = 0; Hurt(); Check("damage shrinks and protects", Power == 0 && Invulnerable > 0 && State == Screen.Playing);
            var enemy = World.Enemies.First(e => e.Kind == "Goomba"); Invulnerable = 0; StarTime = 5; TouchEnemy(enemy); Check("star defeats enemy", enemy.Dead);
            StarTime = 0; LoadRoom("2-2", false); Player.Position = new(0, -80); Player.Velocity = Vector2.Zero;
            TickPlayer(1f / 60, new Controls(0, true, true, false, false, false, false)); Check("underwater swim", Player.Velocity.Y < 0 && World.IsWater);
            LevelIndex = 0; LoadRoom("1-1", false); var pipe = World.Triggers.First(t => t.Kind == "PipeArea" && !t.Bool("exit_only"));
            EnterPipe(pipe, Vector2.Down); TickGame(.8f, new()); Check("pipe enters bonus room", World.Data.Id == "1-1a", World.Data.Id);
            var exit = World.Triggers.First(t => t.Kind == "PipeArea"); EnterPipe(exit, Vector2.Right); TickGame(.8f, new()); Check("pipe returns at matching exit", World.Data.Id == "1-1" && Player.Position.X > 2300, Player.Position.ToString());
            foreach (var roomData in Maps.Rooms.Values)
                foreach (var sourcePipe in roomData.Entities.Where(e => e.Kind == "PipeArea" && !e.Bool("exit_only")))
                {
                    int main = roomData.Id.Length >= 3 ? Array.IndexOf(Maps.MainLevels, roomData.Id[..3]) : -1;
                    if (main < 0) continue;
                    LevelIndex = main; LoadRoom(roomData.Id, false); State = Screen.Playing;
                    int direction = (int)sourcePipe.Number("enter_direction"); string expected = sourcePipe.Text("target_level");
                    if (expected.StartsWith("uid://")) expected = direction == 3 ? World.IsWater ? "UnderwaterExit" : "UndergroundExit" : MainId;
                    EnterPipe(sourcePipe, direction == 3 ? Vector2.Right : Vector2.Down); TickGame(.8f, new());
                    Check("pipe link " + roomData.Id + "/" + sourcePipe.Name, World.Data.Id == expected, World.Data.Id);
                }
            foreach (string vineLevel in new[] { "2-1", "3-1", "4-2", "5-2", "6-2" })
            {
                LevelIndex = Array.IndexOf(Maps.MainLevels, vineLevel); LoadRoom(vineLevel, false); State = Screen.Playing;
                var marker = World.Triggers.First(t => t.Kind == "CoinHeavenWarpPoint"); EnterCloudRoom();
                Check("vine destination " + vineLevel, World.Data.Id == marker.Text("heaven_scene"), World.Data.Id);
                if (World.Data.Id.StartsWith("cloud-"))
                {
                    Player.Position = new(500, 40); TickTriggers(0, new());
                    Check("cloud return " + vineLevel, World.Data.Id == vineLevel && Player.Position.DistanceTo(marker.Position) < 1, Player.Position.ToString());
                }
            }
            foreach (string maze in new[] { "4-4", "7-4" })
            {
                LevelIndex = Array.IndexOf(Maps.MainLevels, maze); LoadRoom(maze, false); State = Screen.Playing; StarTime = 100;
                var gate = World.Triggers.First(t => t.Kind == "PickAPathTeleport"); string group = gate.Text("_maze_group");
                Player.Position = gate.Position + new Vector2(0, 8); pipeCooldown = 0; TickTriggers(0, new());
                Check("wrong castle route " + maze, Player.Position.DistanceTo(gate.Vector("reset_pos", new(-248, 0))) < 1);
                foreach (var point in World.Triggers.Where(t => t.Kind == "PickAPathPoint" && t.Text("_maze_group") == group))
                { Player.Position = point.Position + new Vector2(0, 8); pipeCooldown = 0; TickTriggers(0, new()); }
                Player.Position = gate.Position + new Vector2(0, 8); pipeCooldown = 0; TickTriggers(0, new());
                Check("correct castle route " + maze, MathF.Abs(Player.Position.X - gate.X) < 1);
            }
            LevelIndex = 8; LoadRoom("3-3", false);
            var ropeA = World.Platforms.First(p => p.Kind.Contains("Rope"));
            var ropeB = World.Platforms.First(p => p.Data.Name == ropeA.Data.Text("linked_platform").Replace("NodePath(\"../", "").Replace("\")", ""));
            float ropeSum = ropeA.Position.Y + ropeB.Position.Y;
            for (int i = 0; i < 60; i++) { ropeA.Touched = true; World.UpdatePlatforms(1f / 60); }
            Check("paired rope conserves length", MathF.Abs(ropeSum - ropeA.Position.Y - ropeB.Position.Y) < .05f && ropeA.Position.Y > ropeA.Home.Y && ropeB.Position.Y < ropeB.Home.Y);
            LevelIndex = 2; LoadRoom("1-3", false);
            var moving = World.Platforms.First(p => p.Kind == "SidewaysPlatform"); var rider = new Body { Position = moving.Position, Support = moving };
            for (int i = 0; i < 120; i++) { World.UpdatePlatforms(1f / 60); rider.Velocity = new(0, 15); World.Move(rider, 1f / 60); }
            Check("platform carries standing rider", rider.Grounded && MathF.Abs(rider.Position.X - moving.Position.X) < 1, rider.Position.ToString());
            LevelIndex = 0; LoadRoom("1-2", false); State = Screen.Playing; StarTime = 0;
            var zone = World.Triggers.First(t => t.Kind == "WarpZone");
            Player.Position = zone.Position + new Vector2(-64, -48); pipeCooldown = 0;
            TickTriggers(0, new Controls(0, false, false, false, false, true, false)); TickGame(.8f, new());
            Check("warp zone selects world 4", LevelIndex == 12, MainId);
            LevelIndex = 31; LoadRoom("8-4d", false); State = Screen.Playing;
            var bowser = World.Enemies.First(e => e.Kind.Contains("Bowser")); World.Shots.Clear();
            for (int i = 0; i < 5; i++)
            {
                var shot = new FireShot(); shot.Body.Position = bowser.Body.Position - new Vector2(0, 8); World.Shots.Add(shot); TickShots(0);
            }
            Check("Bowser takes five fireballs", bowser.Dead);
            var bridge = World.Triggers.First(t => t.Kind == "CastleBridge");
            Player.Position = CastleAxeFeet(bridge); TickTriggers(0, new());
            Check("axe collapses bridge", World.BridgeGone && State == Screen.Clear);
            StartGame(0, true); State = Screen.Playing; TimeLeft = 23;
            var flag = World.Triggers.First(t => t.Kind == "EndFlagpole"); StartClear(flag, false); int scoreAtFlag = Score;
            for (int i = 0; i < 800 && State == Screen.Clear; i++) TickClear(1f / 60);
            Check("timer bonus and three fireworks", fireworkMade == 3 && Score - scoreAtFlag == 23 * 50 + 3 * 500 && LevelIndex == 1, $"fireworks={fireworkMade}, bonus={Score - scoreAtFlag}");
            for (int i = 0; i < 32; i++)
            {
                BeginLevel(i); State = Screen.Clear; message = i % 4 == 3 ? "CASTLE" : "FLAG"; TimeLeft = 0; StateClock = 8; clearBonusDoneAt = 0; fireworkGoal = fireworkMade = 0;
                TickClear(1f / 60); Check("progression " + Maps.MainLevels[i], i == 31 ? State == Screen.Ending : LevelIndex == i + 1);
            }
            Check("full campaign completion", profile.Completed.All(c => c));
            await VerifyVisualRegressions();
            await VerifyItemCameraRegressions();
            await VerifyBacktrackExtended();
            await VerifyFlowVisuals();
            await VerifyUserRegressions();
            await VerifyMechanicsRegressions();
            await VerifyFollowupRegressions();
            await VerifyCoinPipeFlagRegressions();
            await CaptureStudyPreviews();
            WriteVerificationReport();
        }
        catch (Exception ex)
        {
            Check("verification exception", false, ex.ToString()); WriteVerificationReport();
        }
        GetTree().Quit(verificationFailures == 0 ? 0 : 1);
    }
    private async Task CaptureStudyPreviews()
    {
        using var sheet = Image.CreateEmpty(512, 480, false, Image.Format.Rgba8);
        using var nativeSheet = Image.CreateEmpty(512, 480, false, Image.Format.Rgba8);
        bool native = DisplayServer.GetName() != "headless";
        var shots = new (string id, float camera, Vector2 player)[]
        { ("1-1", 0, new(40, 0)), ("2-2", 256, new(328, -72)), ("4-4", 1456, new(1512, -8)), ("8-4d", 384, new(424, -48)) };
        for (int i = 0; i < shots.Length; i++)
        {
            var shot = shots[i]; LevelIndex = Array.IndexOf(Maps.MainLevels, shot.id.Length == 3 ? shot.id : shot.id[..3]);
            Power = i == 3 ? 2 : i == 2 ? 1 : 0; LoadRoom(shot.id, false); State = Screen.Playing;
            Player.Position = shot.player; Player.Grounded = i != 1; CameraX = shot.camera; TimeLeft = 400; StarTime = Invulnerable = 0; showTouch = false;
            foreach (var e in World.Enemies) e.Active = true;
            studyFramePath = "res://Reports/frame-" + shot.id + ".png";
            QueueRedraw();
            if (native) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            else { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            if (native)
            {
                using var gpu = GetViewport().GetTexture().GetImage();
                gpu.Convert(Image.Format.Rgba8);
                var colors = new HashSet<uint>();
                for (int y = 0; y < gpu.GetHeight(); y += 2)
                    for (int x = 0; x < gpu.GetWidth(); x += 2) colors.Add(gpu.GetPixel(x, y).ToRgba32());
                Check("native framebuffer " + shot.id, gpu.GetWidth() == 256 && gpu.GetHeight() == 240 && colors.Count > 6, $"{gpu.GetWidth()}x{gpu.GetHeight()}, colors={colors.Count}");
                gpu.SavePng("res://Reports/gpu-" + shot.id + ".png");
                nativeSheet.BlitRect(gpu, new Rect2I(0, 0, 256, 240), new Vector2I(i % 2 * 256, i / 2 * 240));
            }
            string path = "res://Reports/frame-" + shot.id + ".png";
            Check("pixel preview " + shot.id, FileAccess.FileExists(path));
            if (FileAccess.FileExists(path))
            {
                using var frame = new Image();
                frame.LoadPngFromBuffer(FileAccess.GetFileAsBytes(path));
                sheet.BlitRect(frame, new Rect2I(0, 0, 256, 240), new Vector2I(i % 2 * 256, i / 2 * 240));
            }
        }
        sheet.Resize(1024, 960, Image.Interpolation.Nearest); sheet.SavePng("res://Reports/gameplay-preview.png");
        if (native) { nativeSheet.Resize(1024, 960, Image.Interpolation.Nearest); nativeSheet.SavePng("res://Reports/gpu-gameplay.png"); }
        LevelIndex = 0; Power = 0; LoadRoom("1-1", false); State = Screen.Title;
        studyFramePath = "res://Reports/title-preview.png"; QueueRedraw();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void WriteVerificationReport()
    {
        string path = DisplayServer.GetName() == "headless" ? "res://Reports/verification.json" : "res://Reports/gui-verification.json";
        using var report = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        report.StoreString(JsonSerializer.Serialize(new { engine = Engine.GetVersionInfo()["string"].AsString(), platform = OS.GetName(), display = DisplayServer.GetName(), renderingDriver = RenderingServer.GetCurrentRenderingDriverName(), timestamp = DateTimeOffset.Now, failures = verificationFailures, checks = verificationChecks }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"Verification: {verificationChecks.Count - verificationFailures}/{verificationChecks.Count} passed; report {path}");
    }
}
