using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private async Task VerifyBacktrackExtended()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            foreach (string kind in new[] { "SuperMushroom", "FireFlower", "OneUp", "SuperStar" })
                foreach (bool left in new[] { false, true })
                {
                    StartGame(0, true); State = Screen.Playing; Power = kind == "FireFlower" ? 1 : 0;
                    CameraX = -40; Clock = 0; showTouch = false; Invulnerable = 1;
                    var block = World.Blocks.Where(b => b.Kind.Contains("PowerUp")).OrderBy(b => b.Center.X).First();
                    block.Item = kind; FacingLeft = left; HitBlock(block); block.Bump = 0;
                    var item = World.Items.Single();
                    Player.Position = item.Body.Position;
                    TickItems(.35f);
                    Check($"emerging {kind} cannot be collected left={left}", !item.Removed && item.Emerging && item.Body.Position.X == block.Center.X);
                    Player.Position = new(-208, 0);
                    if (!left && kind != "SuperMushroom" && DisplayServer.GetName() != "headless")
                        await CheckEmergencePixels(item, block, "half", 8, "item-" + kind);
                    TickItems(.35f);
                    Check($"emerged {kind} launch direction left={left}", !item.Emerging && item.Body.Position == item.Home && item.Body.Velocity.X == (kind == "FireFlower" ? 0 : 40));
                    int lives = Lives, score = Score;
                    Player.Position = item.Body.Position; TickItems(1f / 60);
                    bool effect = kind == "OneUp" ? Lives == lives + 1 : kind == "SuperStar" ? StarTime > 0 : Power == (kind == "FireFlower" ? 2 : 1);
                    Check($"collect {kind} applies effect left={left}", item.Removed && effect);
                    int collectedLives = Lives, collectedScore = Score;
                    TickItems(1f / 60);
                    Check($"collect {kind} only once left={left}", Lives == collectedLives && Score == collectedScore && (kind == "OneUp" || Score == score + 1000));
                }
            foreach (float step in new[] { 1f / 30, 1f / 60, 1f / 120 })
            {
                var fixture = MushroomFixture();
                for (int i = 0; i < (int)MathF.Round(1 / step); i++) TickItems(step);
                Check($"mushroom emergence stable at step {step:F5}", !fixture.item.Emerging && fixture.item.Body.Velocity.X > 0 && fixture.item.Body.Position.X > fixture.block.Center.X + 10);
            }
            var solid = Maps.Rooms["1-1"].Tiles.First(t => Maps.IsSolid((int)t[2], (int)t[3], (int)t[4]));
            var room = new RoomData { Id = "1-1", Width = 512, Spawn = [16, 0] };
            for (int x = 0; x < 32; x++) room.Tiles.Add([x, 0, solid[2], solid[3], solid[4]]);
            for (int y = -3; y < 0; y++) room.Tiles.Add([8, y, solid[2], solid[3], solid[4]]);
            World = new World(room, Maps, "1-1"); Player.Position = new(16, 0);
            var moving = new Item { Age = 1 }; moving.Body.Position = new(116, 0); moving.Body.Velocity = new(40, 0); World.Items.Add(moving);
            for (int i = 0; i < 60; i++) TickItems(1f / 60);
            Check("mushroom reverses at real wall", !moving.Removed && moving.Body.Velocity.X < 0 && moving.Body.Position.X < 116, moving.Body.Position.ToString());

            foreach (var data in Maps.Rooms.Values)
            {
                LoadRoom(data.Id, false); State = Screen.Playing;
                Player.Position = new(World.MaxX - 16, -80); UpdateCamera(); float right = CameraX;
                Player.Position = new(World.MinX + 16, -80); UpdateCamera();
                float maxCamera = MathF.Max(World.MinX, World.MaxX - 256);
                Check("bidirectional camera bounds " + data.Id, CameraX == World.MinX && right <= maxCamera && right >= CameraX && Player.Position.X == World.MinX + 16);
            }
            foreach (string id in new[] { "1-1", "1-2", "2-2", "1-4" })
            {
                StartGame(Array.IndexOf(Maps.MainLevels, id), true); State = Screen.Playing;
                for (int i = 0; i < 30; i++) TickPlayer(1f / 60, new());
                float startX = Player.Position.X;
                for (int i = 0; i < 90; i++) { TickPlayer(1f / 60, new Controls(1, false, false, false, false, false, false)); UpdateCamera(); }
                float rightX = Player.Position.X, rightCamera = CameraX;
                for (int i = 0; i < 90; i++) { TickPlayer(1f / 60, new Controls(-1, false, false, false, false, false, false)); UpdateCamera(); }
                Check("physical forward and return movement " + id, rightX > startX + 24 && Player.Position.X < rightX - 24, $"start={startX}, right={rightX}, return={Player.Position.X}");
                Check("physical return camera stays bounded " + id, CameraX <= rightCamera && CameraX >= World.MinX && CameraX <= MathF.Max(World.MinX, World.MaxX - 256));
            }
            StartGame(0, true); State = Screen.Playing; CameraX = 512;
            Player.Position = new(600, 0); UpdateCamera(); float stable = CameraX;
            Player.Position = new(660, 0); UpdateCamera();
            Check("camera dead zone avoids small reversal jitter", stable == 512 && CameraX == stable);
            World.Enemies.Clear();
            var winged = new Enemy { Kind = "RedKoopaTroopa", Winged = true, Active = true, Home = new(80, -80) };
            winged.Body.Position = winged.Home; World.Enemies.Add(winged); CameraX = 0; Player.Position = new(-208, 0);
            TickEnemies(1f / 60); Vector2 frozenPosition = winged.Body.Position; float frozenClock = winged.Clock;
            CameraX = 500; TickEnemies(1);
            Check("offscreen winged enemy phase freezes", winged.Clock == frozenClock && winged.Body.Position == frozenPosition && !winged.Removed);
            CameraX = 0; TickEnemies(1f / 60);
            Check("returning winged enemy does not jump phase", winged.Body.Position.DistanceTo(frozenPosition) < 3, $"before={frozenPosition}, returned={winged.Body.Position}");
            var cache = MushroomFixture(); var saved = World;
            cache.item.Age = 1; cache.item.Body.Velocity = new(-40, 0); World.CollectedCoins.Add(0);
            LoadRoom("1-1a", false); LoadRoom("1-1", true);
            Check("return visit preserves used block item and collected coin", ReferenceEquals(World, saved) && cache.block.Used && World.CollectedCoins.Contains(0) && World.Items.Single().Body.Velocity.X == -40);
        }
        catch (Exception ex) { Check("extended backtracking exception", false, ex.ToString()); }
        finally
        {
            SetPhysicsProcess(enabled);
            using var file = FileAccess.Open("res://Reports/Regression/backtrack-extended.json", FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(new { display = DisplayServer.GetName(), failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
