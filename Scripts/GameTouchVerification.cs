using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private async Task VerifyTouchControls()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool processing = IsPhysicsProcessing(), previousTouch = showTouch;
        SetPhysicsProcess(false);
        void Press(int index, int zone)
        { using var ev = new InputEventScreenTouch { Index = index, Position = TouchBounds[zone].GetCenter(), Pressed = true }; _Input(ev); }
        void Lift(int index, bool canceled = false)
        { using var ev = new InputEventScreenTouch { Index = index, Pressed = false, Canceled = canceled }; _Input(ev); }
        void Drag(int index, Vector2 position)
        { using var ev = new InputEventScreenDrag { Index = index, Position = position }; _Input(ev); }
        void TogglePreview()
        { using var ev = new InputEventKey { PhysicalKeycode = Key.T, Pressed = true }; _Input(ev); }
        Vector2 GamePoint(Vector2 at) => TouchLayout.Game.Position + at * TouchLayout.Scale;
        try
        {
            foreach (var sample in new[] { ("phone", true), (" PHONE ", true), ("2in1", false), ("pc", false), ("tablet", false), ("default", false), ("unknown", false), ("", false) })
                Check("phone-only device policy " + sample.Item1, DeviceControls.IsPhone(sample.Item1) == sample.Item2);
            Check("system device type query", deviceType.Length > 0, deviceType);
            foreach (var size in new[] { new Vector2(512, 240), new(1280, 720), new(2340, 1080), new(1920, 1080), new(1024, 768), new(240, 512) })
            {
                var layout = PhoneControlLayout.Create(size);
                string label = $"{size.X}x{size.Y}";
                Check("phone game remains centered " + label, layout.Game.GetCenter().DistanceTo(size / 2) < 1.5f);
                Check("phone game preserves NES aspect " + label, MathF.Abs(layout.Game.Size.X / layout.Game.Size.Y - 256f / 240) < .0001f);
                Check("phone controls stay inside viewport " + label, layout.Buttons.All(b => b.Position.X >= 0 && b.Position.Y >= 0 && b.End.X <= size.X && b.End.Y <= size.Y));
                Check("phone controls never cover game " + label, layout.Buttons.All(b => b.End.X <= layout.Game.Position.X || b.Position.X >= layout.Game.End.X));
                Check("directions use left sidebar " + label, new[] { TouchLeft, TouchRight, TouchDown, TouchUp }.All(z => layout.Buttons[z].End.X <= layout.Game.Position.X));
                Check("actions and pause use right sidebar " + label, new[] { TouchRun, TouchJump, TouchPause }.All(z => layout.Buttons[z].Position.X >= layout.Game.End.X));
                Check("running jump bridge stays outside game " + label, layout.RunJumpBridge.Position.X >= layout.Game.End.X);
            }
            MechanicsWorld(); SetTouchPresentation(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Press(0, TouchJump); Drag(0, TouchBounds[TouchRight].GetCenter());
            Check("desktop touchscreen stays without virtual controls", !showTouch && touches.Count == 0 && !ReadControls().Jump);
            SetTouchPresentation(true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Vector2 outside = GamePoint(new(128, 80));
            using (var ev = new InputEventScreenTouch { Index = 1, Position = outside, Pressed = true }) _Input(ev);
            Check("gameplay tap outside buttons has no action", touches.Count == 0 && State == Screen.Playing && TouchZone(outside) == -1);
            Check("HUD tap does not pause", TouchZone(GamePoint(new(80, 16))) == -1);
            Check("round button corners do not activate", TouchZone(TouchBounds[TouchJump].Position) == -1);
            foreach (int zone in new[] { TouchLeft, TouchRight, TouchDown, TouchRun, TouchJump, TouchPause, TouchUp })
                Check("drawn button center hits same button " + zone, TouchZone(TouchBounds[zone].GetCenter()) == zone);
            Press(2, TouchRight); Press(3, TouchRun); Press(4, TouchJump); var input = ReadControls();
            Check("three fingers move run and jump together", input.Axis == 1 && input.Run && input.Jump && input.JumpPressed && input.FirePressed);
            input = ReadControls(); Check("held buttons do not repeat press edges", input.Run && input.Jump && !input.JumpPressed && !input.FirePressed);
            Lift(4); input = ReadControls(); Check("jump release preserves movement and run", !input.Jump && input.Axis == 1 && input.Run);
            Press(4, TouchJump); ReadControls(); Press(5, TouchJump); input = ReadControls();
            Check("second finger on jump does not retrigger", input.Jump && !input.JumpPressed);
            Lift(4); Check("one jump finger can lift while another holds", ReadControls().Jump);
            Lift(5); Lift(3); Lift(2); input = ReadControls();
            Check("all lifted buttons release", !input.Jump && !input.Run && input.Axis == 0);
            Press(6, TouchJump); Lift(6); input = ReadControls();
            Check("short tap survives between physics frames", input.JumpPressed && !input.Jump && !ReadControls().JumpPressed);
            Press(7, TouchRun); Lift(7); input = ReadControls();
            Check("short fire tap survives between physics frames", input.FirePressed && !input.Run && !ReadControls().FirePressed);
            Press(8, TouchJump); Lift(8, true); Check("canceled jump does not fire", !ReadControls().JumpPressed);
            Press(8, TouchJump); Lift(8); Lift(99, true); Check("untracked cancel preserves another finger short tap", ReadControls().JumpPressed);
            Press(9, TouchRight); Drag(9, outside); Check("sliding off releases direction", ReadControls().Axis == 0);
            Drag(9, TouchBounds[TouchLeft].GetCenter()); Check("sliding back changes direction", ReadControls().Axis == -1);
            Lift(9); Drag(9, TouchBounds[TouchRight].GetCenter()); Check("orphan drag cannot stick a button", touches.Count == 0 && ReadControls().Axis == 0);
            Press(10, TouchDown); Check("down supports crouching and pipes", ReadControls().Down); Lift(10);
            Press(11, TouchUp); Check("up supports vine climbing", ReadControls().Up); Lift(11);
            Press(12, TouchRight); Press(13, TouchJump); _Notification((int)NotificationApplicationFocusOut);
            input = ReadControls(); Check("focus loss releases all touch state", touches.Count == 0 && input.Axis == 0 && !input.Jump && !input.JumpPressed);
            Press(14, TouchRun); _Notification((int)NotificationApplicationPaused); Check("application pause releases fire", !ReadControls().FirePressed && !ReadControls().Run);
            Press(15, TouchJump); _Notification((int)NotificationWMWindowFocusOut); Check("window defocus releases jump", !ReadControls().Jump && !ReadControls().JumpPressed);
            Press(15, TouchRun); Press(16, TouchJump); OnTouchViewportChanged(); input = ReadControls();
            Check("viewport resize clears held buttons and pending edges", touches.Count == 0 && !input.Run && !input.Jump && !input.FirePressed && !input.JumpPressed);
            Drag(15, TouchBounds[TouchRun].GetCenter()); Check("old drag after resize cannot reacquire a button", touches.Count == 0);
            Press(15, TouchJump); TogglePreview(); input = ReadControls();
            Check("T preview disable releases all buttons", !showTouch && touches.Count == 0 && !input.Jump && !input.JumpPressed);
            TogglePreview(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check("T preview enables sidebar presentation", showTouch && GetWindow().ContentScaleSize == new Vector2I(512, 240) && GetWindow().ContentScaleAspect == Window.ContentScaleAspectEnum.Expand);
            Press(16, TouchPause); Check("pause button pauses and clears gameplay fingers", State == Screen.Pause && touches.Count == 0);
            Press(17, TouchPause); Check("pause button resumes", State == Screen.Playing && touches.Count == 0);
            State = Screen.Title; titleChoice = 0; Press(18, TouchDown); Lift(18); Check("phone title navigates down", titleChoice == 1);
            Press(19, TouchUp); Lift(19); Check("phone title navigates up", titleChoice == 0);
            titleChoice = 2; Press(20, TouchJump); Check("phone OK opens level select", State == Screen.Select && touches.Count == 0);
            Press(21, TouchRight); Lift(21); Press(22, TouchDown); Lift(22); Check("phone level select uses both axes", selectChoice == 5);
            Press(23, TouchRun); Check("phone BACK returns to title", State == Screen.Title && touches.Count == 0);
            State = Screen.Help; Press(24, TouchJump); Check("phone OK leaves help", State == Screen.Title);
            MechanicsWorld(); ReleaseTouchControls(); Press(25, TouchRight); Press(26, TouchRun);
            Vector2 bridge = (TouchBounds[TouchRun].GetCenter() + TouchBounds[TouchJump].GetCenter()) / 2;
            Drag(26, bridge); Check("right thumb keeps run across button gap", ReadControls().Run);
            Drag(26, TouchBounds[TouchJump].GetCenter()); input = ReadControls();
            Check("two thumbs combine move run and jump", input.Axis == 1 && input.Run && input.Jump && input.JumpPressed);
            Lift(26, true); input = ReadControls();
            Check("canceling running jump releases both actions", !input.Run && !input.Jump && !input.JumpPressed && !input.FirePressed);
            ReleaseTouchControls(); Press(25, TouchRight); Press(26, TouchRun); Drag(26, TouchBounds[TouchJump].GetCenter());
            float peak = 0;
            for (int frame = 0; frame < 60; frame++) { TickPlayer(1f / 60, ReadControls()); peak = MathF.Max(peak, -Player.Position.Y); }
            Check("virtual buttons drive actual running jump", Player.Position.X > 64 && peak >= 64 && peak <= 65.1f, $"x={Player.Position.X:F1}, height={peak:F1}");
            ReleaseTouchControls();
            if (DisplayServer.GetName() != "headless")
            {
                StartGame(0, true); State = Screen.Playing; Clock = 0; SetTouchPresentation(false);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                using var desktop = await RegressionFrame(); SetTouchPresentation(true);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                using var phone = await RegressionFrame("phone-virtual-controls");
                var gameRect = new Rect2I((Vector2I)TouchLayout.Game.Position, new Vector2I(256, 240));
                using var game = phone.GetRegion(gameRect);
                Check("GPU phone sidebars leave complete game frame unchanged", TouchLayout.Scale == 1 && SamePixels(desktop, game));
                Rect2 left = TouchBounds[TouchLeft];
                Check("GPU left sidebar contains visible direction control", phone.GetPixel((int)left.GetCenter().X, (int)left.GetCenter().Y).ToRgba32() != new Color("111827").ToRgba32());
                Press(30, TouchJump); using var held = await RegressionFrame("phone-jump-held");
                using var feedback = DifferenceImage(phone, held); Check("GPU held button gives feedback", OpaqueBounds(feedback).HasArea()); Lift(30);
                State = Screen.Title; using var title = await RegressionFrame("phone-title");
                State = Screen.Select; using var select = await RegressionFrame("phone-level-select");
                State = Screen.Help; using var help = await RegressionFrame("phone-controls-help");
                Check("GPU phone menus render", lastRenderError.Length == 0, lastRenderError);
            }
        }
        catch (Exception ex) { Check("touch controls exception", false, ex.ToString()); }
        finally
        {
            SetTouchPresentation(previousTouch); SetPhysicsProcess(processing);
            using var report = FileAccess.Open("res://Reports/Regression/phone-touch-controls.json", FileAccess.ModeFlags.Write);
            report.StoreString(JsonSerializer.Serialize(new { deviceType, display = DisplayServer.GetName(), failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
