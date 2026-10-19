using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    private async Task VerifyTouchControls(bool includeGraphics = true)
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool processing = IsPhysicsProcessing(), previousTouch = showTouch, previousPractice = practice;
        SetPhysicsProcess(false);
        void Press(int index, int zone)
        { using var ev = new InputEventScreenTouch { Index = index, Position = TouchBounds[zone].GetCenter(), Pressed = true }; _Input(ev); }
        void Lift(int index, bool canceled = false)
        { using var ev = new InputEventScreenTouch { Index = index, Pressed = false, Canceled = canceled }; _Input(ev); }
        void Drag(int index, Vector2 at)
        { using var ev = new InputEventScreenDrag { Index = index, Position = at }; _Input(ev); }
        void Tap(int index, int zone)
        { Press(index, zone); Lift(index); }
        void TogglePreview()
        {
            using var down = new InputEventKey { PhysicalKeycode = Key.T, Pressed = true };
            using var up = new InputEventKey { PhysicalKeycode = Key.T, Pressed = false };
            Input.ParseInputEvent(down); Input.FlushBufferedEvents(); Input.ParseInputEvent(up); Input.FlushBufferedEvents();
        }
        void ResetWorld(int power = 0)
        { ReleaseTouchControls(); ReadControls(); MechanicsWorld(); Power = power; }
        bool Idle(Controls input) => input.Axis == 0 && !input.Jump && !input.JumpPressed && !input.Run && !input.HighJump && !input.FirePressed && !input.Down && !input.Up;
        (float x, float speed, float peak, int shots, float[] horizontal) JumpTrial(int move, int jump, bool extraRunFinger = false)
        {
            ResetWorld(2);
            if (move >= 0) Press(200, move);
            if (extraRunFinger) Press(202, TouchRunRight);
            Press(201, jump);
            float peak = 0;
            float[] horizontal = new float[60];
            for (int frame = 0; frame < 60; frame++)
            {
                TickPlayer(1f / 60, ReadControls()); peak = MathF.Max(peak, -Player.Position.Y);
                horizontal[frame] = Player.Velocity.X;
            }
            var result = (Player.Position.X, Player.Velocity.X, peak, World.Shots.Count, horizontal);
            ReleaseTouchControls(); ReadControls(); return result;
        }
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://Reports/Regression"));
            foreach (var sample in new[] { ("phone", true), (" PHONE ", true), ("2in1", false), ("pc", false), ("tablet", false), ("default", false), ("unknown", false), ("", false) })
                Check("phone-only device policy " + sample.Item1, DeviceControls.IsPhone(sample.Item1) == sample.Item2);
            Check("system device type query", deviceType.Length > 0, deviceType);
            Check("legacy Controls constructor leaves high jump disabled", !new Controls(0, false, false, false, false, false, false).HighJump);
            foreach (var size in new[] { new Vector2(512, 240), new(1280, 720), new(2340, 1080), new(1920, 1080), new(1024, 768), new(240, 512) })
            {
                var layout = PhoneControlLayout.Create(size); string label = $"{size.X}x{size.Y}";
                Check("phone game remains centered " + label, layout.Game.GetCenter().DistanceTo(size / 2) < 1.5f);
                Check("phone game preserves NES aspect " + label, MathF.Abs(layout.Game.Size.X / layout.Game.Size.Y - 256f / 240) < .0001f);
                Check("phone exposes nine controls " + label, layout.Buttons.Length == 9);
                Check("phone controls stay inside viewport " + label, layout.Buttons.All(b => b.Position.X >= 0 && b.Position.Y >= 0 && b.End.X <= size.X && b.End.Y <= size.Y));
                Check("phone controls never cover game " + label, layout.Buttons.All(b => b.End.X <= layout.Game.Position.X || b.Position.X >= layout.Game.End.X));
                int[] movement = [TouchLeft, TouchRight, TouchRunLeft, TouchRunRight];
                Check("walk and run use left sidebar " + label, movement.All(z => layout.Buttons[z].End.X <= layout.Game.Position.X));
                Check("actions and pause use right sidebar " + label, new[] { TouchJump, TouchBigJump, TouchDown, TouchFire, TouchPause }.All(z => layout.Buttons[z].Position.X >= layout.Game.End.X));
                var merged = layout.Buttons[TouchLeft].Merge(layout.Buttons[TouchRight]).Merge(layout.Buttons[TouchRunLeft]).Merge(layout.Buttons[TouchRunRight]);
                float cellArea = movement.Sum(z => layout.Buttons[z].Size.X * layout.Buttons[z].Size.Y);
                Check("movement pad exactly covers four seamless cells " + label,
                    merged.Position.DistanceTo(layout.MovementPad.Position) < .01f && merged.End.DistanceTo(layout.MovementPad.End) < .01f && MathF.Abs(cellArea - merged.Size.X * merged.Size.Y) < .01f);
            }
            ResetWorld(); SetTouchPresentation(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Press(0, TouchJump); Drag(0, TouchBounds[TouchRight].GetCenter());
            Check("desktop touch never enables phone controls", !showTouch && touches.Count == 0 && Idle(ReadControls()));
            SetTouchPresentation(true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Vector2 outside = TouchLayout.Game.Position + new Vector2(128, 80) * TouchLayout.Scale;
            using (var ev = new InputEventScreenTouch { Index = 1, Position = outside, Pressed = true }) _Input(ev);
            Check("gameplay taps and HUD remain outside controls", touches.Count == 0 && TouchZone(outside) == -1 && TouchZone(TouchLayout.Game.Position + new Vector2(80, 16) * TouchLayout.Scale) == -1);
            Power = 2;
            foreach (int zone in Enumerable.Range(0, 9))
                Check("drawn button center hits same control " + zone, TouchZone(TouchBounds[zone].GetCenter()) == zone);
            foreach (int zone in Enumerable.Range(0, 8))
                Check("rectangular control includes its corner " + zone, TouchZone(TouchBounds[zone].Position + Vector2.One * TouchLayout.Scale * .01f) == zone);
            Check("only pause has rounded hit boundary", TouchZone(TouchBounds[TouchPause].Position) == -1);

            ResetWorld(); Press(10, TouchLeft);
            Check("left walk is slow movement without fire", ReadControls() is { Axis: -1, Run: false, FirePressed: false });
            Drag(10, TouchBounds[TouchRight].GetCenter()); Check("continuous horizontal drag changes walk direction", ReadControls() is { Axis: 1, Run: false });
            Drag(10, TouchBounds[TouchRunRight].GetCenter()); Check("vertical drag changes right walk to run", ReadControls() is { Axis: 1, Run: true, FirePressed: false });
            Drag(10, TouchBounds[TouchRunLeft].GetCenter()); Check("continuous lower row changes run direction", ReadControls() is { Axis: -1, Run: true, FirePressed: false });
            Drag(10, TouchBounds[TouchLeft].GetCenter()); Check("vertical drag changes left run back to walk", ReadControls() is { Axis: -1, Run: false });
            foreach (var pair in new[] { (TouchLeft, TouchRight), (TouchRunLeft, TouchRunRight), (TouchLeft, TouchRunLeft), (TouchRight, TouchRunRight) })
            {
                Rect2 a = TouchBounds[pair.Item1], b = TouchBounds[pair.Item2];
                Vector2 boundary = (a.GetCenter() + b.GetCenter()) / 2;
                Vector2 epsilon = (b.GetCenter() - a.GetCenter()).Normalized() * TouchLayout.Scale * .01f;
                foreach (var at in new[] { boundary - epsilon, boundary, boundary + epsilon })
                {
                    Drag(10, at); var value = ReadControls();
                    Check($"movement seam {pair.Item1}-{pair.Item2} has no dead area at {at}", TouchLayout.MovementPad.HasPoint(at) && TouchZone(at) is >= 0 and <= 3 && MathF.Abs(value.Axis) == 1);
                }
            }
            Drag(10, outside); Check("drag off movement pad releases", Idle(ReadControls()));
            Drag(10, TouchBounds[TouchRight].GetCenter()); Check("tracked finger can return to movement pad", ReadControls().Axis == 1);
            Lift(10); Drag(10, TouchBounds[TouchRunRight].GetCenter()); Check("orphan drag cannot reacquire controls", touches.Count == 0 && Idle(ReadControls()));

            foreach (int power in new[] { 0, 1 })
            {
                ResetWorld(power); Press(20, TouchFire); var disabled = ReadControls(); TickPlayer(1f / 60, disabled);
                Check("fire control disabled without flower power " + power, TouchZone(TouchBounds[TouchFire].GetCenter()) == -1 && touches.Count == 0 && !disabled.FirePressed && !disabled.Run && World.Shots.Count == 0);
            }
            ResetWorld(2); Press(21, TouchFire); var input = ReadControls(); TickPlayer(1f / 60, input);
            Check("fire press launches exactly one shot without run", input.FirePressed && !input.Run && !input.Jump && World.Shots.Count == 1);
            bool repeatedFire = false;
            for (int frame = 0; frame < 60; frame++) { fireCooldown = MathF.Max(0, fireCooldown - 1f / 60); input = ReadControls(); repeatedFire |= input.FirePressed || input.Run; TickPlayer(1f / 60, input); }
            Check("held fire never auto repeats or accelerates", !repeatedFire && World.Shots.Count == 1);
            Lift(21); ReadControls(); Press(21, TouchFire); TickPlayer(1f / 60, ReadControls());
            Check("repressing fire launches the next shot", World.Shots.Count == 2);
            ResetWorld(2); Press(24, TouchDown); ReadControls(); Drag(24, TouchBounds[TouchFire].GetCenter()); input = ReadControls();
            Check("dragging into fire emits one fire edge without run", input.FirePressed && !input.Run && !input.Down);
            Check("staying on fire after drag has no repeat edge", !ReadControls().FirePressed);
            Drag(24, TouchBounds[TouchDown].GetCenter()); ReadControls(); Drag(24, TouchBounds[TouchFire].GetCenter());
            Check("leaving then reentering fire re-arms the edge", ReadControls().FirePressed); Lift(24); ReadControls();
            ResetWorld(2); Press(22, TouchRunRight); input = ReadControls(); TickPlayer(1f / 60, input);
            Check("running direction never fires", input.Run && !input.FirePressed && World.Shots.Count == 0);
            Press(23, TouchBigJump); input = ReadControls(); TickPlayer(1f / 60, input);
            Check("big jump adds height but never fires", input.Run && input.HighJump && input.Jump && input.JumpPressed && !input.FirePressed && World.Shots.Count == 0);
            Lift(22); input = ReadControls(); Check("releasing running direction keeps high jump without run", input.Axis == 0 && !input.Run && input.HighJump && input.Jump);
            Press(22, TouchRunRight); Lift(23); input = ReadControls(); Check("releasing big jump preserves running direction only", input.Axis == 1 && input.Run && !input.HighJump && !input.Jump);
            Press(25, TouchRunRight); ReadControls(); Lift(22); input = ReadControls();
            Check("releasing one running finger preserves another running source", input.Axis == 1 && input.Run && !input.HighJump);

            ResetWorld(); Press(30, TouchJump); Lift(30); input = ReadControls();
            Check("ordinary short tap retains one jump edge without high jump", input.JumpPressed && !input.Jump && !input.Run && !input.HighJump && !ReadControls().JumpPressed);
            TickPlayer(1f / 60, input); float ordinaryTapLaunch = Player.Velocity.Y;
            ResetWorld(); Press(31, TouchBigJump); Lift(31); input = ReadControls();
            Check("big jump short tap retains height and jump edge without run", !input.Run && input.HighJump && input.JumpPressed && !input.Jump && !input.FirePressed);
            Check("big jump short tap consumes pending height once", Idle(ReadControls()));
            TickPlayer(1f / 60, input);
            Check("quick high jump affects launch height without horizontal acceleration", Player.Velocity.Y < ordinaryTapLaunch && Player.Velocity.X == 0 && !input.Run);
            Press(32, TouchBigJump); Lift(32, true); Check("cancel removes pending big jump height", Idle(ReadControls()));
            Press(33, TouchJump); Lift(33); Press(34, TouchJump); Lift(34, true);
            Check("canceling a different tracked jump preserves valid short tap", ReadControls().JumpPressed);
            Press(35, TouchBigJump); Lift(35); Lift(99, true); input = ReadControls();
            Check("orphan cancel preserves another finger high jump without run", !input.Run && input.HighJump && input.JumpPressed);
            ResetWorld(2); Press(36, TouchFire); Lift(36); Press(37, TouchFire); Lift(37, true);
            Check("canceling one fire finger preserves other pending shot", ReadControls().FirePressed);
            Press(38, TouchFire); Lift(38, true); Check("cancel removes its own fire edge", !ReadControls().FirePressed);

            ResetWorld(); Press(40, TouchJump); Check("ordinary jump first edge", ReadControls() is { Jump: true, JumpPressed: true, Run: false, HighJump: false });
            Vector2 jumpGap = (TouchBounds[TouchJump].GetCenter() + TouchBounds[TouchBigJump].GetCenter()) / 2;
            Check("jump row includes its connection gap", TouchLayout.JumpRow.HasPoint(jumpGap) && TouchZone(jumpGap) == -1);
            Drag(40, jumpGap); input = ReadControls(); Check("drag through jump gap keeps hold without extra edge", input.Jump && !input.Run && !input.HighJump && !input.JumpPressed);
            Drag(40, TouchBounds[TouchBigJump].GetCenter()); input = ReadControls();
            Check("drag into big jump adds height without run or new edge", input.Jump && !input.Run && input.HighJump && !input.JumpPressed && !input.FirePressed);
            Drag(40, TouchBounds[TouchJump].GetCenter()); input = ReadControls();
            Check("drag back to normal jump removes height without new edge", input.Jump && !input.Run && !input.HighJump && !input.JumpPressed);
            Press(41, TouchBigJump); input = ReadControls(); Check("new high jump finger has its own press edge", input.Jump && !input.Run && input.HighJump && input.JumpPressed);
            Lift(40); input = ReadControls(); Check("one jump finger can lift while high jump remains held", input.Jump && !input.Run && input.HighJump && !input.JumpPressed);
            Press(42, TouchJump); ReadControls(); Lift(41); input = ReadControls();
            Check("lifting big jump keeps normal jump but releases height", input.Jump && !input.Run && !input.HighJump && !input.JumpPressed);
            Lift(42); Check("all jump fingers released", Idle(ReadControls()));
            using (var gapPress = new InputEventScreenTouch { Index = 46, Position = jumpGap, Pressed = true }) _Input(gapPress);
            Check("fresh touch in jump gap does not activate", touches.Count == 0 && Idle(ReadControls()));
            Press(47, TouchBigJump); ReadControls(); Drag(47, jumpGap); input = ReadControls();
            Check("big jump keeps height through connection gap without run", input.Jump && !input.Run && input.HighJump && !input.JumpPressed);
            Lift(47); ReadControls();

            ResetWorld(1); Press(43, TouchDown); input = ReadControls(); TickPlayer(1f / 60, input);
            Check("down supports crouching and pipe input", input.Down && !input.Up && !input.Run && Crouching); Lift(43); ReadControls();
            ResetWorld(); World.Triggers.Add(new EntityData { Kind = "Vine", X = Player.Position.X, Y = 0 });
            foreach (int zone in new[] { TouchJump, TouchBigJump })
            {
                Press(44, zone); input = ReadControls(); Check("jump variant climbs near vine without run " + zone, input.Jump && input.Up && !input.Down && !input.Run && input.HighJump == (zone == TouchBigJump)); Lift(44); ReadControls();
            }
            Player.Position += Vector2.Right * 20; Press(45, TouchJump); Check("jump away from vine does not add up", !ReadControls().Up); Lift(45); ReadControls();

            ResetWorld(2); Press(50, TouchRunRight); Press(51, TouchBigJump); Press(52, TouchFire); _Notification((int)NotificationApplicationFocusOut);
            Check("focus loss clears touch actions and pending edges", touches.Count == 0 && Idle(ReadControls()));
            Press(53, TouchFire); _Notification((int)NotificationApplicationPaused); Check("app pause clears pending fire", Idle(ReadControls()));
            Press(54, TouchBigJump); _Notification((int)NotificationWMWindowFocusOut); Check("window defocus clears big jump", Idle(ReadControls()));
            Press(55, TouchRunLeft); Press(56, TouchBigJump); OnTouchViewportChanged();
            Check("resize clears touch run and jump edges", touches.Count == 0 && Idle(ReadControls()));
            Drag(55, TouchBounds[TouchRunRight].GetCenter()); Check("old drag cannot reacquire after resize", touches.Count == 0);
            Press(57, TouchBigJump); TogglePreview(); Check("T disable clears touch without pending jump", !showTouch && touches.Count == 0 && Idle(ReadControls()));
            TogglePreview(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check("T enables expanded sidebar preview", showTouch && GetWindow().ContentScaleSize == new Vector2I(512, 240) && GetWindow().ContentScaleAspect == Window.ContentScaleAspectEnum.Expand);
            Press(58, TouchPause); Check("pause touch clears gameplay fingers", State == Screen.Pause && touches.Count == 0);
            Press(59, TouchPause); Check("pause touch resumes", State == Screen.Playing && touches.Count == 0);

            State = Screen.Title; titleChoice = 0; Press(60, TouchRunRight); Lift(60); Check("lower right movement cell navigates menu down", titleChoice == 1);
            Press(61, TouchRunLeft); Lift(61); Check("lower left movement cell navigates menu up", titleChoice == 0);
            Press(62, TouchDown); Press(63, TouchFire);
            Check("menu hides crouch and fire controls", State == Screen.Title && titleChoice == 0 && touches.Count == 0 && TouchZone(TouchBounds[TouchDown].GetCenter()) == -1 && TouchZone(TouchBounds[TouchFire].GetCenter()) == -1);
            titleChoice = 2; Press(64, TouchJump); Check("jump is menu OK", State == Screen.Select && touches.Count == 0);
            Press(65, TouchRight); Lift(65); Press(66, TouchRunRight); Lift(66); Check("menu movement pad maps two axes", selectChoice == 5);
            Press(67, TouchLeft); Lift(67); Press(68, TouchRunLeft); Lift(68); Check("menu movement reverses both axes", selectChoice == 0);
            Press(69, TouchBigJump); Check("big jump is menu BACK", State == Screen.Title && touches.Count == 0);
            State = Screen.Help; Press(70, TouchJump); Check("OK leaves help", State == Screen.Title);

            ResetWorld(); Player.Position = new(80, 0); Player.Velocity = new(43, 0); TimeLeft = 173.5f;
            var practiceWorld = World; Vector2 practicePosition = Player.Position;
            float practiceSpeed = Player.Velocity.X, practiceTime = TimeLeft;
            Tap(300, TouchPause);
            for (int step = 1; step <= 6; step++)
            {
                Tap(301, TouchRunRight);
                Check("practice pause cycles through six options down " + step, State == Screen.Pause && pauseChoice == step % 6);
            }
            Tap(302, TouchRunLeft); Check("practice pause up wraps to FIRE MARIO", State == Screen.Pause && pauseChoice == 5);
            Tap(302, TouchRunLeft); Check("practice pause BIG MARIO follows original four items", pauseChoice == 4);
            Tap(303, TouchJump);
            Check("practice OK selects BIG MARIO and resumes", State == Screen.Playing && Power == 1);
            Check("BIG MARIO selection preserves room position speed and time", World == practiceWorld && Player.Position == practicePosition && Player.Velocity.X == practiceSpeed && TimeLeft == practiceTime);
            Tap(304, TouchPause);
            for (int step = 0; step < 5; step++) Tap(305, TouchRunRight);
            Check("practice FIRE MARIO is sixth pause item", State == Screen.Pause && pauseChoice == 5);
            Tap(306, TouchJump);
            Check("practice OK selects FIRE MARIO and resumes", State == Screen.Playing && Power == 2);
            Check("FIRE MARIO selection preserves room position speed and time", World == practiceWorld && Player.Position == practicePosition && Player.Velocity.X == practiceSpeed && TimeLeft == practiceTime);
            Check("practice FIRE MARIO enables fire touch control", TouchZone(TouchBounds[TouchFire].GetCenter()) == TouchFire);
            ReleaseTouchControls(); ReadControls(); MechanicsWorld((0, -2)); Player.Position = new(8, 0);
            Tap(307, TouchPause);
            for (int step = 0; step < 4; step++) Tap(308, TouchRunRight);
            Tap(309, TouchJump);
            Check("practice BIG MARIO under a low roof stays crouched", State == Screen.Playing && Power == 1 && Crouching && Player.Height == 16 && Player.Position == new Vector2(8, 0));
            ResetWorld(); practice = false; Tap(310, TouchPause);
            for (int step = 1; step <= 4; step++)
            {
                Tap(311, TouchRunRight);
                Check("ordinary pause retains four options " + step, State == Screen.Pause && pauseChoice == step % 4);
            }
            Tap(312, TouchRunLeft); Check("ordinary pause up wraps to TITLE", pauseChoice == 3);
            Tap(313, TouchBigJump); Check("ordinary pause BACK resumes without a power shortcut or pending jump", State == Screen.Playing && Power == 0 && Idle(ReadControls()));

            var walkJump = JumpTrial(TouchRight, TouchJump);
            var walkHigh = JumpTrial(TouchRight, TouchBigJump);
            var runJump = JumpTrial(TouchRunRight, TouchJump);
            var runHigh = JumpTrial(TouchRunRight, TouchBigJump);
            var extraRunHigh = JumpTrial(TouchRunRight, TouchBigJump, extraRunFinger: true);
            var stationary = JumpTrial(-1, TouchBigJump);
            Check("walking with either jump stays at walk speed 90", MathF.Abs(walkJump.speed - 90) < .001f && MathF.Abs(walkHigh.speed - 90) < .001f, $"normal={walkJump.speed}, high={walkHigh.speed}");
            Check("high jump leaves walking horizontal acceleration unchanged", walkJump.horizontal.AsSpan().SequenceEqual(walkHigh.horizontal) && MathF.Abs(walkJump.x - walkHigh.x) < .001f);
            Check("walking big jump raises height without enabling run", walkHigh.peak > walkJump.peak && walkHigh.peak >= 64 && walkHigh.peak <= 65.1f, $"normal={walkJump.peak:F2}, high={walkHigh.peak:F2}");
            Check("running with either jump stays at run speed 150", MathF.Abs(runJump.speed - 150) < .001f && MathF.Abs(runHigh.speed - 150) < .001f, $"normal={runJump.speed}, high={runHigh.speed}");
            Check("high jump leaves running horizontal acceleration unchanged", runJump.horizontal.AsSpan().SequenceEqual(runHigh.horizontal) && MathF.Abs(runJump.x - runHigh.x) < .001f);
            Check("run and high jump do not stack jump height", MathF.Abs(runJump.peak - runHigh.peak) < .001f && MathF.Abs(walkHigh.peak - runHigh.peak) < .001f);
            Check("multiple running fingers plus high jump never stack acceleration or height", runHigh.horizontal.AsSpan().SequenceEqual(extraRunHigh.horizontal) && MathF.Abs(runHigh.x - extraRunHigh.x) < .001f && MathF.Abs(runHigh.peak - extraRunHigh.peak) < .001f);
            Check("stationary high jump adds no horizontal movement", MathF.Abs(stationary.speed) < .001f && MathF.Abs(stationary.x) < .001f && MathF.Abs(walkHigh.peak - stationary.peak) < .001f);
            Check("walk run and high jump trials never emit fireballs", walkJump.shots + walkHigh.shots + runJump.shots + runHigh.shots + extraRunHigh.shots + stationary.shots == 0);
            ResetWorld(2); Player.Velocity = new(60, 0); Press(210, TouchJump); TickPlayer(1f / 60, ReadControls());
            float ordinaryInertia = Player.Velocity.X;
            ResetWorld(2); Player.Velocity = new(60, 0); Press(210, TouchBigJump); input = ReadControls(); TickPlayer(1f / 60, input);
            Check("directionless high jump preserves ordinary horizontal inertia", input.Axis == 0 && !input.Run && input.HighJump && MathF.Abs(Player.Velocity.X - ordinaryInertia) < .001f);
            ReleaseTouchControls(); ReadControls();
            VerifyPhysicalControlMappings();

            if (includeGraphics && DisplayServer.GetName() != "headless")
            {
                ResetWorld(2); Clock = 0; SetTouchPresentation(false);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                using var desktop = await RegressionFrame(); SetTouchPresentation(true);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                using var phone = await RegressionFrame("phone-virtual-controls");
                using var game = phone.GetRegion(new Rect2I((Vector2I)TouchLayout.Game.Position, new Vector2I(256, 240)));
                Check("GPU phone sidebars leave complete game frame unchanged", TouchLayout.Scale == 1 && SamePixels(desktop, game));
                var sample = TouchBounds[TouchLeft].Position + Vector2.One * 4 * TouchLayout.Scale;
                Check("GPU movement pad is visible in sidebar", phone.GetPixel((int)sample.X, (int)sample.Y).ToRgba32() != new Color("111827").ToRgba32());
                Press(80, TouchBigJump); using var held = await RegressionFrame("phone-jump-held");
                using var feedback = DifferenceImage(phone, held); Check("GPU held rectangle gives feedback", OpaqueBounds(feedback).HasArea()); Lift(80); ReadControls();
                Power = 0; using var gray = await RegressionFrame("phone-fire-disabled");
                Power = 2; using var active = await RegressionFrame("phone-fire-enabled");
                var fireRect = new Rect2I((Vector2I)TouchBounds[TouchFire].Position, (Vector2I)TouchBounds[TouchFire].Size);
                using var grayFire = gray.GetRegion(fireRect); using var activeFire = active.GetRegion(fireRect);
                Check("GPU fire availability changes its button appearance", !SamePixels(grayFire, activeFire));
                State = Screen.Title; using var title = await RegressionFrame("phone-title");
                State = Screen.Select; using var select = await RegressionFrame("phone-level-select");
                State = Screen.Help; using var help = await RegressionFrame("phone-controls-help");
                Check("GPU menus and action labels render", lastRenderError.Length == 0, lastRenderError);
            }
        }
        catch (Exception ex) { Check("touch controls exception", false, ex.ToString()); }
        finally
        {
            using var releaseT = new InputEventKey { PhysicalKeycode = Key.T, Pressed = false };
            Input.ParseInputEvent(releaseT); Input.FlushBufferedEvents();
            SetTouchPresentation(previousTouch); ReadControls(); practice = previousPractice; SetPhysicsProcess(processing);
            using var report = FileAccess.Open("res://Reports/Regression/phone-touch-controls.json", FileAccess.ModeFlags.Write);
            report.StoreString(JsonSerializer.Serialize(new { deviceType, display = DisplayServer.GetName(), graphicsIncluded = includeGraphics && DisplayServer.GetName() != "headless", failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
