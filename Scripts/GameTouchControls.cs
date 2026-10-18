using Godot;

namespace MarioStudy;

public partial class Game
{
    private const int TouchLeft = 0, TouchRight = 1, TouchDown = 2, TouchRun = 3, TouchJump = 4, TouchPause = 5, TouchUp = 6, TouchRunJump = 7;
    private string deviceType = "unknown";
    private bool touchJumpPressed, touchFirePressed, touchPresentationConfigured;
    private Vector2I desktopContentSize, touchWindowSize;
    private Window.ContentScaleAspectEnum desktopContentAspect;
    private PhoneControlLayout touchLayout = PhoneControlLayout.Create(new(256, 240));
    private Vector2 touchLayoutSize = new(-1, -1);
    private PhoneControlLayout TouchLayout
    {
        get
        {
            Vector2 size = GetViewportRect().Size;
            if (size != touchLayoutSize) { touchLayoutSize = size; touchLayout = PhoneControlLayout.Create(size); }
            return touchLayout;
        }
    }
    private Rect2[] TouchBounds => TouchLayout.Buttons;
    private Vector2 GameScreenOrigin => showTouch ? TouchLayout.Game.Position : Vector2.Zero;
    private float GameScreenScale => showTouch ? TouchLayout.Scale : 1;

    private void SetupTouchControls()
    {
        deviceType = DeviceControls.DetectDeviceType();
        var window = GetWindow();
        desktopContentSize = window.ContentScaleSize;
        desktopContentAspect = window.ContentScaleAspect;
        touchPresentationConfigured = true;
        GetViewport().SizeChanged += OnTouchViewportChanged;
        if (GetViewport() != window) window.SizeChanged += OnTouchViewportChanged;
        window.VisibilityChanged += ReleaseTouchControls;
        SetTouchPresentation(DeviceControls.IsPhone(deviceType));
    }
    private void TeardownTouchControls()
    {
        if (!touchPresentationConfigured) return;
        var window = GetWindow();
        GetViewport().SizeChanged -= OnTouchViewportChanged;
        if (GetViewport() != window) window.SizeChanged -= OnTouchViewportChanged;
        window.VisibilityChanged -= ReleaseTouchControls;
        window.ContentScaleSize = desktopContentSize;
        window.ContentScaleAspect = desktopContentAspect;
        touchPresentationConfigured = false;
        ReleaseTouchControls();
    }
    private void SetTouchPresentation(bool enabled)
    {
        ReleaseTouchControls();
        showTouch = enabled;
        if (touchPresentationConfigured)
        {
            var window = GetWindow();
            // Expand the actual root viewport; drawing in the old 256x240 viewport cannot use its letterbox bars.
            window.ContentScaleSize = enabled ? new(PhoneControlLayout.BaseWidth, PhoneControlLayout.BaseHeight) : desktopContentSize;
            window.ContentScaleAspect = enabled ? Window.ContentScaleAspectEnum.Expand : desktopContentAspect;
        }
        OnTouchViewportChanged();
    }
    private void OnTouchViewportChanged()
    {
        if (touchPresentationConfigured) touchWindowSize = GetWindow().Size;
        touchLayoutSize = new(-1, -1);
        ReleaseTouchControls();
    }
    private void SyncTouchWindowSize()
    {
        // A proportional physical resize can leave a stretched viewport's logical size unchanged.
        if (showTouch && touchPresentationConfigured && GetWindow().Size != touchWindowSize) OnTouchViewportChanged();
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
        Rect2[] bounds = TouchBounds;
        for (int zone = 0; zone < bounds.Length; zone++)
        {
            if (zone == TouchPause && State is not (Screen.Playing or Screen.Pause)) continue;
            if (!bounds[zone].HasPoint(at)) continue;
            if ((zone is TouchRun or TouchJump or TouchPause) && at.DistanceTo(bounds[zone].GetCenter()) > bounds[zone].Size.X / 2) continue;
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
        if (ev is InputEventScreenTouch or InputEventScreenDrag) SyncTouchWindowSize();
        if (ev is InputEventScreenTouch touch)
        {
            if (!touch.Pressed || touch.Canceled)
            {
                bool tracked = touches.TryGetValue(touch.Index, out int previous);
                touches.Remove(touch.Index);
                if (touch.Canceled && tracked)
                {
                    if ((previous is TouchJump or TouchRunJump) && !Touch(TouchJump)) touchJumpPressed = false;
                    if ((previous is TouchRun or TouchRunJump) && !Touch(TouchRun)) touchFirePressed = false;
                }
                if (tracked) GetViewport().SetInputAsHandled();
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
                // Two thumbs can move and roll RUN -> JUMP while preserving RUN.
                // The bridge comes from the same button rectangles used to draw and hit test.
                if ((previous is TouchRun or TouchRunJump) && (State is Screen.Playing or Screen.Transform or Screen.Intro))
                {
                    if (zone == TouchJump) zone = TouchRunJump;
                    else if (zone == -1 && TouchLayout.RunJumpBridge.HasPoint(drag.Position)) zone = TouchRun;
                }
                SetTouchButton(drag.Index, zone);
                if (zone != previous && (zone is TouchLeft or TouchRight or TouchDown or TouchUp)) TouchMenuAction(zone);
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
    private void DrawPhoneBorders()
    {
        if (!showTouch) return;
        var layout = TouchLayout;
        Color panel = new("111827");
        // Cover world drawing beyond its original viewport, keeping every side button clear of gameplay.
        DrawRect(layout.LeftPanel, panel); DrawRect(layout.RightPanel, panel);
        DrawRect(new Rect2(layout.Game.Position.X, 0, layout.Game.Size.X, layout.Game.Position.Y), panel);
        DrawRect(new Rect2(layout.Game.Position.X, layout.Game.End.Y, layout.Game.Size.X, layout.Viewport.Y - layout.Game.End.Y), panel);
    }
    private void DrawTouchControls()
    {
        if (!showTouch) return;
        var layout = TouchLayout;
        bool menu = State is Screen.Title or Screen.Select or Screen.Pause or Screen.Help or Screen.GameOver or Screen.Ending;
        for (int zone = 0; zone < layout.Buttons.Length; zone++)
        {
            if (zone == TouchPause && State is not (Screen.Playing or Screen.Pause)) continue;
            var bounds = layout.Buttons[zone]; var center = bounds.GetCenter();
            bool held = Touch(zone);
            Color fill = held ? new Color(.25f, .57f, 1, .9f) : new Color(.18f, .23f, .32f, 1);
            Color outline = new(1, 1, 1, held ? .95f : .65f);
            float line = MathF.Max(.5f, layout.Scale);
            if (zone is TouchRun or TouchJump or TouchPause)
            {
                float radius = bounds.Size.X / 2;
                DrawCircle(center, radius, fill); DrawArc(center, radius - line / 2, 0, Mathf.Tau, 32, outline, line);
                string label = zone switch { TouchRun => menu ? "BACK" : "RUN", TouchJump => menu ? "OK" : "JUMP", _ => State == Screen.Pause ? ">" : "II" };
                DrawSetTransform(center - new Vector2(label.Length * 4, 4) * layout.Scale, scale: Vector2.One * layout.Scale);
                Art.Text(this, label, Vector2.Zero, Colors.White);
                DrawSetTransform(Vector2.Zero);
            }
            else
            {
                DrawRect(bounds, fill); DrawRect(bounds, outline, false, line);
                Vector2 direction = zone switch { TouchLeft => Vector2.Left, TouchRight => Vector2.Right, TouchDown => Vector2.Down, _ => Vector2.Up };
                Vector2 side = new(-direction.Y, direction.X);
                Vector2[] arrow = [center + direction * 7 * layout.Scale, center + (-direction * 4 + side * 5) * layout.Scale, center + (-direction * 4 - side * 5) * layout.Scale];
                DrawColoredPolygon(arrow, Colors.White);
            }
        }
    }
}
