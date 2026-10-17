using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private async Task VerifyItemCameraRegressions()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        DirAccess.MakeDirRecursiveAbsolute("res://Reports/Regression");
        try
        {
            var fixture = MushroomFixture();
            Block block = fixture.block; Item mushroom = fixture.item;
            Check("mushroom begins centered inside source block", mushroom.Body.Position.X == block.Center.X && mushroom.Body.Position.Y == block.Box.End.Y && mushroom.Body.Velocity == Vector2.Zero);
            if (DisplayServer.GetName() != "headless") await CheckEmergencePixels(mushroom, block, "hidden", 0);
            TickItems(.35f);
            Check("mushroom rises vertically without horizontal drift", mushroom.Emerging && mushroom.Body.Position.X == block.Center.X && MathF.Abs(mushroom.Body.Position.Y - (block.Box.Position.Y + 8)) < .001f);
            if (DisplayServer.GetName() != "headless") await CheckEmergencePixels(mushroom, block, "half", 8);
            TickItems(.35f);
            Check("mushroom completes on block top facing right", !mushroom.Emerging && mushroom.Body.Position == mushroom.Home && mushroom.Body.Velocity.X == 40);
            if (DisplayServer.GetName() != "headless") await CheckEmergencePixels(mushroom, block, "full", 16);
            for (int frame = 0; frame < 60; frame++) TickItems(1f / 60);
            Check("mushroom moves right after emergence", mushroom.Body.Position.X > block.Center.X + 35 && mushroom.Body.Velocity.X > 0, $"position={mushroom.Body.Position}, velocity={mushroom.Body.Velocity}");
            fixture = MushroomFixture(); TickItems(.8f);
            Check("emergence crossing step does not collide with source side", !fixture.item.Emerging && fixture.item.Body.Velocity.X > 0 && fixture.item.Body.Position.X >= fixture.block.Center.X);

            StartGame(0, true); State = Screen.Playing; Invulnerable = 100;
            for (int frame = 0; frame < 210; frame++) TickGame(1f / 60, new Controls(1, false, false, false, false, false, false));
            float advancedX = Player.Position.X, advancedCamera = CameraX;
            for (int frame = 0; frame < 210; frame++) TickGame(1f / 60, new Controls(-1, false, false, false, false, false, false));
            Check("walk back across former screen left edge", State == Screen.Playing && Player.Position.X < advancedCamera && Player.Position.X < advancedX - 200, $"advancedX={advancedX}, oldCamera={advancedCamera}, returnX={Player.Position.X}");
            Check("camera follows return to earlier terrain", CameraX < advancedCamera - 100 && Player.Position.X >= CameraX, $"oldCamera={advancedCamera}, returnCamera={CameraX}");
            Player.Position = new(World.MinX - 100, 0); UpdateCamera();
            Check("backtracking respects world left boundary", CameraX == World.MinX && Player.Position.X == World.MinX + Player.Width / 2);
            Player.Position = new(World.MaxX + 100, 0); UpdateCamera();
            Check("camera respects world right boundary", CameraX == MathF.Max(World.MinX, World.MaxX - 256) && Player.Position.X == World.MaxX - Player.Width / 2);
            StartGame(0, true); State = Screen.Playing;
            var enemy = World.Enemies.First(e => e.Kind == "Goomba"); enemy.Active = true;
            Vector2 enemyPosition = enemy.Body.Position; CameraX = enemy.Body.Position.X + 200;
            TickEnemies(1f / 60);
            Check("living enemy retained behind camera for return", !enemy.Removed && enemy.Body.Position == enemyPosition);
        }
        catch (Exception ex) { Check("item camera regression exception", false, ex.ToString()); }
        finally
        {
            SetPhysicsProcess(enabled);
            using var file = FileAccess.Open("res://Reports/Regression/item-camera.json", FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(new { display = DisplayServer.GetName(), failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private (Block block, Item item) MushroomFixture()
    {
        StartGame(0, true); State = Screen.Playing; CameraX = -40; Clock = 0; showTouch = false;
        Invulnerable = 1; Player.Position = new(-208, 0);
        var block = World.Blocks.Where(b => b.Kind.Contains("PowerUp")).OrderBy(b => b.Center.X).First();
        block.Bump = 0; HitBlock(block); block.Bump = 0;
        return (block, World.Items.Single(i => i.Kind == "SuperMushroom"));
    }
    private async Task CheckEmergencePixels(Item item, Block block, string stage, int expectedHeight, string prefix = "mushroom")
    {
        item.Removed = true; using var background = await RegressionFrame(); item.Removed = false;
        using var frame = await RegressionFrame(prefix + "-" + stage);
        using var difference = DifferenceImage(frame, background);
        Rect2I visible = OpaqueBounds(difference);
        int top = (int)MathF.Round(block.DrawPosition.Y + 208);
        bool valid = expectedHeight == 0 ? !visible.HasArea() : visible.HasArea() && visible.End.Y == top && visible.Size.Y == expectedHeight && MathF.Abs(visible.GetCenter().X - (block.Center.X - MathF.Round(CameraX))) <= 1;
        Check("GPU " + item.Kind + " emerges through block top " + stage, valid, $"visible={visible}, blockTop={top}, expectedHeight={expectedHeight}");
    }
}
