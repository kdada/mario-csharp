using Godot;

namespace MarioStudy;

public partial class Game
{
    private const int TouchLeft = 0, TouchRight = 1, TouchDown = 2, TouchRun = 3, TouchJump = 4, TouchPause = 5, TouchUp = 6, TouchRunJump = 7;
    private string deviceType = "unknown";
    private bool touchJumpPressed, touchFirePressed;
    private static readonly Rect2[] TouchBounds =
    [
        new(8, 184, 28, 28), new(68, 184, 28, 28), new(38, 214, 28, 24),
        new(156, 196, 38, 38), new(204, 177, 44, 44), new(225, 38, 26, 26), new(38, 154, 28, 28)
    ];

    private void SetupTouchControls()
    {
        deviceType = DeviceControls.DetectDeviceType();
        showTouch = DeviceControls.IsPhone(deviceType);
        GetViewport().SizeChanged += ReleaseTouchControls;
    }
    private void ReleaseTouchControls()
    {
        touches.Clear(); jumpHeld = fireHeld = touchJumpPressed = touchFirePressed = false;
        QueueRedraw();
    }
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut || what == NotificationApplicationPaused)
            ReleaseTouchControls();
    }
    private int TouchZone(Vector2 at)
    {
        for (int zone = 0; zone < TouchBounds.Length; zone++)
        {
            var bounds = TouchBounds[zone];
            if (zone == TouchPause && State is not (Screen.Playing or Screen.Pause)) continue;
            if (!bounds.HasPoint(at)) continue;
            if (zone is TouchRun or TouchJump or TouchPause && at.DistanceTo(bounds.GetCenter()) > bounds.Size.X / 2) continue;
            return zone;
        }
        return -1;
    }
    private void SetTouchButton(int index, int zone)
    {
        bool jump = Touch(TouchJump), fire = Touch(TouchRun);
        touches[index] = zone;
        if (!jump && Touch(TouchJump)) touchJumpPressed = true;
        if (!fire && Touch(TouchRun)) touchFirePressed = true;
    }
    private bool HandleTouchInput(InputEvent ev)
    {
        if (ev is InputEventScreenTouch touch)
        {
            if (!touch.Pressed || touch.Canceled)
            {
                touches.TryGetValue(touch.Index, out int previous); touches.Remove(touch.Index);
                if (touch.Canceled && previous is TouchJump or TouchRunJump && !Touch(TouchJump)) touchJumpPressed = false;
                if (touch.Canceled && previous is TouchRun or TouchRunJump && !Touch(TouchRun)) touchFirePressed = false;
                QueueRedraw(); return true;
            }
            touches.Remove(touch.Index);
            if (!showTouch) return true;
            int zone = TouchZone(touch.Position);
            if (zone >= 0)
            {
                SetTouchButton(touch.Index, zone);
                TouchMenuAction(zone);
                GetViewport().SetInputAsHandled();
            }
            QueueRedraw(); return true;
        }
        if (ev is InputEventScreenDrag drag)
        {
            if (showTouch && touches.TryGetValue(drag.Index, out int previous))
            {
                int zone = TouchZone(drag.Position);
                // Roll the right thumb from RUN to JUMP while retaining RUN.
                // This lets two thumbs move and perform a running jump together.
                if (previous is TouchRun or TouchRunJump && State is Screen.Playing or Screen.Transform or Screen.Intro)
                {
                    if (zone == TouchJump) zone = TouchRunJump;
                    else if (zone == -1 && new Rect2(156, 177, 92, 57).HasPoint(drag.Position)) zone = TouchRun;
                }
                SetTouchButton(drag.Index, zone);
                // Dragging off a button releases it; returning presses it again.
                // Pause/confirm require a fresh touch to avoid accidental actions.
                if (zone != previous && zone is TouchLeft or TouchRight or TouchDown or TouchUp) TouchMenuAction(zone);
                GetViewport().SetInputAsHandled(); QueueRedraw();
            }
            return true;
        }
        return false;
    }
    private void TouchMenuAction(int zone)
    {
        if (State is Screen.Playing or Screen.Transform or Screen.Intro or Screen.Dying or Screen.Clear)
        {
            if (zone == TouchPause && State == Screen.Playing) { TogglePause(); ReleaseTouchControls(); }
            return;
        }
        if (State == Screen.Pause && zone == TouchPause) { TogglePause(); ReleaseTouchControls(); return; }
        Key key = zone switch
        {
            TouchLeft => Key.Left, TouchRight => Key.Right, TouchDown => Key.Down, TouchUp => Key.Up,
            TouchRun => Key.Escape, TouchJump => Key.Enter, _ => Key.None
        };
        if (key == Key.None) return;
        HandleMenuKey(key);
        if (zone is TouchRun or TouchJump) ReleaseTouchControls();
    }
    private void DrawTouchControls()
    {
        if (!showTouch) return;
        bool menu = State is Screen.Title or Screen.Select or Screen.Pause or Screen.Help or Screen.GameOver or Screen.Ending;
        for (int zone = 0; zone < TouchBounds.Length; zone++)
        {
            if (zone == TouchPause && State is not (Screen.Playing or Screen.Pause)) continue;
            var bounds = TouchBounds[zone]; var center = bounds.GetCenter();
            bool held = Touch(zone);
            Color fill = held ? new Color(.25f, .57f, 1, .8f) : new Color(.03f, .05f, .1f, .48f);
            Color outline = new(1, 1, 1, held ? .95f : .65f);
            if (zone is TouchRun or TouchJump or TouchPause)
            {
                float radius = bounds.Size.X / 2;
                DrawCircle(center, radius, fill); DrawArc(center, radius - .5f, 0, Mathf.Tau, 32, outline, 1);
                string label = zone switch { TouchRun => menu ? "BACK" : "RUN", TouchJump => menu ? "OK" : "JUMP", _ => State == Screen.Pause ? ">" : "II" };
                Art.Text(this, label, center - new Vector2(label.Length * 4, 4), Colors.White);
            }
            else
            {
                DrawRect(bounds, fill); DrawRect(bounds, outline, false, 1);
                Vector2 direction = zone switch { TouchLeft => Vector2.Left, TouchRight => Vector2.Right, TouchDown => Vector2.Down, _ => Vector2.Up };
                Vector2 side = new(-direction.Y, direction.X);
                Vector2[] arrow = [center + direction * 7, center - direction * 4 + side * 5, center - direction * 4 - side * 5];
                DrawColoredPolygon(arrow, Colors.White);
            }
        }
    }
}
