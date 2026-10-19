using Godot;

namespace MarioStudy;

public partial class Game
{
    /// <summary>Exercises Godot's actual key/joypad polling state, including touch cleanup while keys stay held.</summary>
    private void VerifyPhysicalControlMappings()
    {
        bool previousTouch = showTouch;
        HashSet<Key> keys = [];
        HashSet<JoyButton> buttons = [];
        HashSet<JoyAxis> axes = [];
        void KeyEvent(Key key, bool pressed)
        {
            keys.Add(key);
            using var ev = new InputEventKey { PhysicalKeycode = key, Pressed = pressed };
            Input.ParseInputEvent(ev); Input.FlushBufferedEvents();
        }
        void PadEvent(JoyButton button, bool pressed)
        {
            buttons.Add(button);
            using var ev = new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = pressed };
            Input.ParseInputEvent(ev); Input.FlushBufferedEvents();
        }
        void AxisEvent(JoyAxis axis, float value)
        {
            axes.Add(axis);
            using var ev = new InputEventJoypadMotion { Device = 0, Axis = axis, AxisValue = value };
            Input.ParseInputEvent(ev); Input.FlushBufferedEvents();
        }
        void ReleaseInjected()
        {
            List<string> errors = [];
            foreach (Key key in keys.ToArray()) { try { KeyEvent(key, false); } catch (Exception ex) { errors.Add(key + ": " + ex.Message); } }
            foreach (JoyButton button in buttons.ToArray()) { try { PadEvent(button, false); } catch (Exception ex) { errors.Add(button + ": " + ex.Message); } }
            foreach (JoyAxis axis in axes.ToArray()) { try { AxisEvent(axis, 0); } catch (Exception ex) { errors.Add(axis + ": " + ex.Message); } }
            if (errors.Count > 0) Check("all injected physical inputs release", false, string.Join("; ", errors));
        }
        void Reset()
        {
            ReleaseInjected(); ReleaseTouchControls(); ReadControls(); MechanicsWorld(); Power = 2;
        }
        void TouchEvent(int index, int zone, bool pressed)
        {
            using var ev = new InputEventScreenTouch { Index = index, Position = TouchBounds[zone].GetCenter(), Pressed = pressed };
            _Input(ev);
        }
        void PreviewKey()
        { KeyEvent(Key.T, true); KeyEvent(Key.T, false); }
        bool NoEdges(Controls input) => !input.JumpPressed && !input.FirePressed && !input.HighJump;
        try
        {
            Reset(); SetTouchPresentation(true);
            foreach (var pair in new[] { (Key.A, -1f), (Key.Left, -1f), (Key.D, 1f), (Key.Right, 1f) })
            {
                KeyEvent(pair.Item1, true); var input = ReadControls();
                Check("physical movement mapping " + pair.Item1, input.Axis == pair.Item2 && !input.Run && !input.HighJump && !input.FirePressed);
                KeyEvent(pair.Item1, false); ReadControls();
            }
            foreach (Key key in new[] { Key.S, Key.Down })
            {
                KeyEvent(key, true); Check("physical down mapping " + key, ReadControls().Down);
                KeyEvent(key, false); ReadControls();
            }
            foreach (Key key in new[] { Key.W, Key.Up })
            {
                KeyEvent(key, true); Check("physical up mapping " + key, ReadControls().Up);
                KeyEvent(key, false); ReadControls();
            }
            World.Triggers.Add(new EntityData { Kind = "Vine", X = Player.Position.X, Y = 0 });
            KeyEvent(Key.J, true); var value = ReadControls();
            Check("physical J remains jump only near vine", value.Jump && value.JumpPressed && !value.Up && !value.Run && !value.HighJump && !value.FirePressed);
            Check("held physical J never repeats jump edge", ReadControls() is { Jump: true, JumpPressed: false, HighJump: false });
            KeyEvent(Key.J, false); Check("physical J release clears hold", !ReadControls().Jump);
            KeyEvent(Key.K, true); value = ReadControls();
            Check("physical K retains run and fire mapping", value.Run && value.FirePressed && !value.Jump && !value.HighJump);
            Check("held physical K never repeats fire edge", ReadControls() is { Run: true, FirePressed: false, HighJump: false });
            KeyEvent(Key.K, false); ReadControls();
            Power = 0; KeyEvent(Key.K, true); value = ReadControls(); TickPlayer(1f / 60, value);
            Check("keyboard K still runs without flower while shot stays gated", value.Run && value.FirePressed && !value.HighJump && World.Shots.Count == 0);
            KeyEvent(Key.K, false); ReadControls();

            Reset();
            foreach (var pair in new[] { (JoyButton.DpadLeft, -1f), (JoyButton.DpadRight, 1f) })
            {
                PadEvent(pair.Item1, true); Check("real joypad direction " + pair.Item1, ReadControls().Axis == pair.Item2);
                PadEvent(pair.Item1, false); ReadControls();
            }
            PadEvent(JoyButton.DpadDown, true); Check("real joypad down", ReadControls().Down); PadEvent(JoyButton.DpadDown, false); ReadControls();
            PadEvent(JoyButton.DpadUp, true); Check("real joypad up", ReadControls().Up); PadEvent(JoyButton.DpadUp, false); ReadControls();
            AxisEvent(JoyAxis.LeftX, .75f); Check("real joypad stick right", ReadControls().Axis == 1);
            AxisEvent(JoyAxis.LeftX, -.75f); Check("real joypad stick left", ReadControls().Axis == -1);
            PadEvent(JoyButton.DpadRight, true); Check("joypad direction retains priority over opposite stick", ReadControls().Axis == 1);
            PadEvent(JoyButton.DpadRight, false); AxisEvent(JoyAxis.LeftX, .1f); Check("joypad stick deadzone", ReadControls().Axis == 0); AxisEvent(JoyAxis.LeftX, 0);
            World.Triggers.Add(new EntityData { Kind = "Vine", X = Player.Position.X, Y = 0 });
            PadEvent(JoyButton.A, true); value = ReadControls();
            Check("real joypad A remains jump without touch climb", value.Jump && value.JumpPressed && !value.Run && !value.Up && !value.HighJump && !value.FirePressed);
            Check("held joypad A has one jump edge", !ReadControls().JumpPressed);
            PadEvent(JoyButton.A, false); ReadControls();
            PadEvent(JoyButton.X, true); value = ReadControls(); Check("real joypad X retains run and fire", value.Run && value.FirePressed && !value.Jump && !value.HighJump);
            Check("held joypad X has one fire edge", !ReadControls().FirePressed);
            PadEvent(JoyButton.X, false); ReadControls();
            PadEvent(JoyButton.RightShoulder, true); value = ReadControls(); Check("joypad shoulder runs without firing or high jump", value.Run && !value.HighJump && !value.FirePressed && !value.Jump);
            TouchEvent(500, TouchBigJump, true); value = ReadControls();
            Check("shoulder run and touch high jump remain independent", value.Run && value.HighJump && value.Jump && value.JumpPressed && !value.FirePressed);
            TouchEvent(500, TouchBigJump, false); value = ReadControls();
            Check("releasing touch height preserves shoulder run only", value.Run && !value.HighJump && !value.Jump && !value.FirePressed);
            PadEvent(JoyButton.RightShoulder, false); ReadControls();
            PadEvent(JoyButton.Start, true); Check("joypad Start pauses", State == Screen.Pause);
            PadEvent(JoyButton.Start, false); PadEvent(JoyButton.Start, true); Check("joypad Start resumes", State == Screen.Playing);
            PadEvent(JoyButton.Start, false);
            State = Screen.Title; titleChoice = 2; PadEvent(JoyButton.A, true);
            Check("joypad A still confirms menus", State == Screen.Select); PadEvent(JoyButton.A, false);
            PadEvent(JoyButton.DpadRight, true); PadEvent(JoyButton.DpadRight, false);
            PadEvent(JoyButton.DpadDown, true); PadEvent(JoyButton.DpadDown, false);
            Check("joypad directions still navigate menu grid", selectChoice == 5);
            PadEvent(JoyButton.B, true); Check("joypad B still returns from menus", State == Screen.Title); PadEvent(JoyButton.B, false);

            Reset(); PadEvent(JoyButton.RightShoulder, true); TouchEvent(510, TouchRunRight, true); TouchEvent(511, TouchBigJump, true);
            float combinedPeak = 0;
            for (int frame = 0; frame < 60; frame++)
            {
                TickPlayer(1f / 60, ReadControls()); combinedPeak = MathF.Max(combinedPeak, -Player.Position.Y);
            }
            Check("touch run plus shoulder plus high jump obeys run speed and height caps", MathF.Abs(Player.Velocity.X - 150) < .001f && combinedPeak >= 64 && combinedPeak <= 65.1f && World.Shots.Count == 0);
            PadEvent(JoyButton.RightShoulder, false); value = ReadControls();
            Check("releasing shoulder preserves touch run and separate height", value.Run && value.HighJump && value.Jump);
            TouchEvent(510, TouchRunRight, false); value = ReadControls();
            Check("releasing last run source leaves height without run", !value.Run && value.HighJump && value.Jump);
            TouchEvent(511, TouchBigJump, false); value = ReadControls();
            Check("releasing final high jump clears both modifiers", !value.Run && !value.HighJump && !value.Jump && NoEdges(value));

            Reset(); KeyEvent(Key.J, true); KeyEvent(Key.K, true); value = ReadControls();
            Check("physical jump and fire first edges are present", value.Jump && value.Run && value.JumpPressed && value.FirePressed && !value.HighJump);
            ReleaseTouchControls(); value = ReadControls();
            Check("touch cleanup preserves held keyboard state without new edges", value.Jump && value.Run && NoEdges(value));
            PreviewKey(); value = ReadControls(); Check("T disable does not retrigger held keyboard", !showTouch && value.Jump && value.Run && NoEdges(value));
            PreviewKey(); value = ReadControls(); Check("T enable does not retrigger held keyboard", showTouch && value.Jump && value.Run && NoEdges(value));
            TouchEvent(501, TouchBigJump, true); ReadControls(); TouchEvent(501, TouchBigJump, false); value = ReadControls();
            Check("lifting touch jump does not retrigger held keyboard", value.Jump && value.Run && NoEdges(value));
            OnTouchViewportChanged(); value = ReadControls(); Check("resize touch cleanup does not retrigger held keyboard", value.Jump && value.Run && NoEdges(value));
            _Notification((int)NotificationApplicationFocusOut); value = ReadControls();
            Check("touch focus cleanup leaves injected physical edge history intact", value.Jump && value.Run && NoEdges(value));
            KeyEvent(Key.J, false); KeyEvent(Key.K, false); value = ReadControls();
            Check("physical release remains observable after touch cleanup", !value.Jump && !value.Run && NoEdges(value));
            KeyEvent(Key.J, true); KeyEvent(Key.K, true); value = ReadControls();
            Check("fresh physical press re-arms both edges after cleanup", value.JumpPressed && value.FirePressed && !value.HighJump);

            Reset(); PadEvent(JoyButton.A, true); PadEvent(JoyButton.X, true); value = ReadControls();
            Check("joypad jump and fire first edges are present", value.JumpPressed && value.FirePressed && !value.HighJump && value.Run);
            ReleaseTouchControls(); value = ReadControls(); Check("touch cleanup does not retrigger held joypad", value.Jump && value.Run && NoEdges(value));
            PreviewKey(); value = ReadControls(); Check("T toggle does not retrigger held joypad", value.Jump && value.Run && NoEdges(value));
            PreviewKey(); ReadControls();
            OnTouchViewportChanged(); value = ReadControls(); Check("resize cleanup does not retrigger held joypad", value.Jump && value.Run && NoEdges(value));
            PadEvent(JoyButton.A, false); PadEvent(JoyButton.X, false); value = ReadControls(); Check("joypad release clears jump and run", !value.Jump && !value.Run && NoEdges(value));
        }
        catch (Exception ex) { Check("physical input regression exception", false, ex.ToString()); }
        finally
        {
            // Release every key, button and axis injected by this group even if a native input call fails.
            try { ReleaseInjected(); }
            finally
            {
                ReleaseTouchControls();
                try { ReadControls(); }
                finally { SetTouchPresentation(previousTouch); }
            }
        }
    }
}
