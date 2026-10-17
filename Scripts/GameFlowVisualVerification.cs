using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private async Task VerifyFlowVisuals()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            StartGame(0, true); Clock = 0; showTouch = false;
            if (DisplayServer.GetName() != "headless")
            {
                using var intro = await RegressionFrame("intro-full-black");
                Check("GPU intro upper background is black", SamePixel(intro.GetPixel(0, 0), Colors.Black) && SamePixel(intro.GetPixel(128, 36), Colors.Black) && SamePixel(intro.GetPixel(255, 39), Colors.Black));
            }
            foreach (int power in new[] { 0, 1 })
            {
                StartGame(0, true); Power = power; Player.Height = power == 0 ? 16 : 32;
                LoadRoom("1-2", false); State = Screen.Playing; Clock = 0; Invulnerable = 0; showTouch = false;
                var pipe = World.Triggers.Where(t => t.Kind == "PipeArea" && !t.Bool("exit_only") && t.Number("enter_direction") == 3).OrderByDescending(t => t.X).First();
                Player.Position = new(pipe.X - Player.Width / 2, pipe.Y); EnterPipe(pipe, Vector2.Right);
                TickGame(.75f, new());
                var exit = World.Triggers.First(t => t.Kind == "PipeArea" && t.Bool("exit_only"));
                Check("pipe exit starts inside actual destination " + power, World.Data.Id == "UndergroundExit" && pipeClock > 0 && Player.Position == exit.Position + new Vector2(0, Player.Height + 4));
                if (DisplayServer.GetName() != "headless") await CheckPipePixels(exit, "hidden-" + power, true);
                TickGame(.375f, new());
                if (DisplayServer.GetName() != "headless") await CheckPipePixels(exit, "half-" + power, false);
                TickGame(.375f, new());
                Check("pipe exit ends exactly on pipe rim " + power, pipeClock <= 0 && Player.Position == exit.Position && Player.Velocity == Vector2.Zero);
            }
            if (DisplayServer.GetName() != "headless")
            {
                StartGame(0, true); State = Screen.Playing; CameraX = -128; Clock = 0; Player.Position = new(-208, 0); showTouch = false;
                World.Coins.Clear(); World.CollectedCoins.Clear(); World.Triggers.Clear();
                using var background = await RegressionFrame();
                World.Coins.Add(new(0, -80));
                using var coin = await RegressionFrame("coin-single-frame");
                using var coinDiff = DifferenceImage(coin, background);
                Rect2I coinBounds = OpaqueBounds(coinDiff);
                Check("GPU coin uses one 16px frame", coinBounds.HasArea() && coinBounds.Size.X <= 16 && coinBounds.Size.Y <= 16, coinBounds.ToString());
                World.Coins.Clear(); World.Triggers.Add(new EntityData { Kind = "EndFlagpole", X = 0 });
                using var flag = await RegressionFrame("flag-single-frame");
                using var flagDiff = DifferenceImage(flag, background);
                for (int y = 0; y < flagDiff.GetHeight(); y++)
                    for (int x = 124; x <= 132; x++) flagDiff.SetPixel(x, y, Colors.Transparent);
                Rect2I flagBounds = OpaqueBounds(flagDiff);
                Check("GPU flag variation crops to one 16px flag", flagBounds.HasArea() && flagBounds.Position.X >= 112 && flagBounds.End.X <= 128 && flagBounds.Position.Y >= 48 && flagBounds.End.Y <= 64, flagBounds.ToString());
            }
        }
        catch (Exception ex) { Check("flow visual exception", false, ex.ToString()); }
        finally
        {
            SetPhysicsProcess(enabled);
            using var file = FileAccess.Open("res://Reports/Regression/flow-visual.json", FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(new { display = DisplayServer.GetName(), failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private async Task CheckPipePixels(EntityData exit, string phase, bool hidden)
    {
        Vector2 position = Player.Position; Player.Position = new(-10000, 0);
        using var background = await RegressionFrame(); Player.Position = position;
        using var frame = await RegressionFrame("pipe-exit-" + phase);
        using var difference = DifferenceImage(frame, background);
        Rect2I bounds = OpaqueBounds(difference);
        int rim = (int)MathF.Round(exit.Y + 208);
        Check("GPU pipe masks player " + phase, hidden ? !bounds.HasArea() : bounds.HasArea() && bounds.End.Y <= rim && bounds.Size.Y < Player.Height, $"visible={bounds}, rim={rim}");
    }
}
