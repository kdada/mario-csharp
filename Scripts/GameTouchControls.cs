using Godot;

namespace MarioStudy;

public partial class Game
{
    private const int TouchLeft = 0, TouchRight = 1, TouchRunLeft = 2, TouchRunRight = 3,
        TouchJump = 4, TouchBigJump = 5, TouchDown = 6, TouchFire = 7, TouchPause = 8;
    private string deviceType = "unknown";
    private bool touchPresentationConfigured;
    // Keep an unconsumed press with its finger even after a quick lift; cancel only that finger's presses.
    private readonly HashSet<int> touchJumpPresses = [], touchBigJumpPresses = [], touchFirePresses = [];
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
    private bool TouchMenu => State is Screen.Title or Screen.Select or Screen.Pause or Screen.Help or Screen.GameOver or Screen.Ending;
    private bool TouchFireAvailable => Power == 2 && !TouchMenu;

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
        touches.Clear(); touchJumpPresses.Clear(); touchBigJumpPresses.Clear(); touchFirePresses.Clear();
        // Keyboard/gamepad edge tracking is independent: clearing a finger must not press J or K again.
        QueueRedraw();
    }
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut || what == NotificationApplicationPaused)
            ReleaseTouchControls();
    }
    private bool TouchButtonVisible(int zone) =>
        zone == TouchPause ? State is Screen.Playing or Screen.Pause : !TouchMenu || zone is not (TouchDown or TouchFire);
    private int TouchZone(Vector2 at)
    {
        Rect2[] bounds = TouchBounds;
        for (int zone = 0; zone < bounds.Length; zone++)
        {
            if (!TouchButtonVisible(zone) || zone == TouchFire && !TouchFireAvailable) continue;
            if (!bounds[zone].HasPoint(at)) continue;
            if (zone == TouchPause && at.DistanceTo(bounds[zone].GetCenter()) > bounds[zone].Size.X / 2) continue;
            return zone;
        }
        return -1;
    }
    private void SetTouchButton(int index, int zone)
    {
        int previous = touches.GetValueOrDefault(index, -1);
        touches[index] = zone;
        if (TouchMenu) return;
        bool jump = zone is TouchJump or TouchBigJump;
        if (jump && previous is not (TouchJump or TouchBigJump)) touchJumpPresses.Add(index);
        if (zone == TouchBigJump && touchJumpPresses.Contains(index)) touchBigJumpPresses.Add(index);
        if (zone == TouchFire && previous != TouchFire && TouchFireAvailable) touchFirePresses.Add(index);
    }
    private bool HandleTouchInput(InputEvent ev)
    {
        if (ev is InputEventScreenTouch or InputEventScreenDrag) SyncTouchWindowSize();
        if (ev is InputEventScreenTouch touch)
        {
            if (!touch.Pressed || touch.Canceled)
            {
                bool tracked = touches.Remove(touch.Index);
                if (touch.Canceled)
                {
                    touchJumpPresses.Remove(touch.Index); touchBigJumpPresses.Remove(touch.Index); touchFirePresses.Remove(touch.Index);
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
                // Keep one jump gesture through the small visible gap between JUMP and BIG JUMP.
                if (!TouchMenu && (previous is TouchJump or TouchBigJump) && zone == -1 && TouchLayout.JumpRow.HasPoint(drag.Position))
                    zone = previous;
                SetTouchButton(drag.Index, zone);
                if (zone != previous && (zone is TouchLeft or TouchRight or TouchRunLeft or TouchRunRight)) TouchMenuAction(zone);
                GetViewport().SetInputAsHandled(); QueueRedraw();
            }
            return true;
        }
        return false;
    }
    private void TouchMenuAction(int zone)
    {
        if (!TouchMenu)
        {
            if (zone == TouchPause && State == Screen.Playing) { TogglePause(); ReleaseTouchControls(); }
            return;
        }
        if (State == Screen.Pause && zone == TouchPause) { TogglePause(); ReleaseTouchControls(); return; }
        Key key = zone switch
        {
            TouchLeft => Key.Left, TouchRight => Key.Right, TouchRunLeft => Key.Up, TouchRunRight => Key.Down,
            TouchBigJump => Key.Escape, TouchJump => Key.Enter, _ => Key.None
        };
        if (key == Key.None) return;
        HandleMenuKey(key);
        if (zone is TouchJump or TouchBigJump) ReleaseTouchControls();
    }
    public async void RunControlsVerification()
    {
        verifying = true; SetPhysicsProcess(false);
        try
        {
            await VerifyTouchControls(false);
            GD.Print($"Input verification: {verificationChecks.Count - verificationFailures}/{verificationChecks.Count} checks passed.");
            GetTree().Quit(verificationFailures == 0 ? 0 : 1);
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }
    private void DrawPhoneBorders()
    {
        if (!showTouch) return;
        var layout = TouchLayout;
        Color panel = new("111827");
        DrawRect(layout.LeftPanel, panel); DrawRect(layout.RightPanel, panel);
        DrawRect(new Rect2(layout.Game.Position.X, 0, layout.Game.Size.X, layout.Game.Position.Y), panel);
        DrawRect(new Rect2(layout.Game.Position.X, layout.Game.End.Y, layout.Game.Size.X, layout.Viewport.Y - layout.Game.End.Y), panel);
    }
    private void DrawTouchControls()
    {
        if (!showTouch) return;
        var layout = TouchLayout;
        bool menu = TouchMenu;
        void Label(string text, Vector2 at, Color color)
        {
            DrawSetTransform(at, scale: Vector2.One * layout.Scale);
            Art.Text(this, text, Vector2.Zero, color);
            DrawSetTransform(Vector2.Zero);
        }
        for (int zone = 0; zone < layout.Buttons.Length; zone++)
        {
            if (!TouchButtonVisible(zone)) continue;
            var bounds = layout.Buttons[zone]; var center = bounds.GetCenter();
            bool enabled = zone != TouchFire || TouchFireAvailable, held = enabled && Touch(zone);
            bool running = (zone is TouchRunLeft or TouchRunRight) && !menu;
            Color fill = held ? new("527b9e") : running ? new("30445e") : new("253349");
            Color ink = enabled ? Colors.White : new("737e90");
            Color outline = held ? new("ffdf80") : enabled ? new("aebacb") : new("465163");
            float line = held ? 2 * layout.Scale : layout.Scale;
            if (zone == TouchPause)
            {
                DrawCircle(center, bounds.Size.X / 2, fill);
                if (!held) DrawArc(center, bounds.Size.X / 2, 0, Mathf.Tau, 32, outline, line, true);
                Label(State == Screen.Pause ? ">" : "II", center - new Vector2(State == Screen.Pause ? 4 : 8, 4) * layout.Scale, ink);
                continue;
            }
            DrawRect(bounds, enabled ? fill : new Color("1b2433"));
            if (!held) DrawRect(bounds, outline, false, line);
            if (zone <= TouchRunRight)
            {
                Vector2 direction = zone switch
                {
                    TouchLeft => Vector2.Left, TouchRight => Vector2.Right,
                    TouchRunLeft => menu ? Vector2.Up : Vector2.Left, _ => menu ? Vector2.Down : Vector2.Right
                };
                if (running) Label("RUN", new(center.X - 12 * layout.Scale, bounds.Position.Y + 6 * layout.Scale), ink);
                Vector2 arrowCenter = center + (running ? new Vector2(0, 7) * layout.Scale : Vector2.Zero);
                Vector2 side = new(-direction.Y, direction.X);
                Vector2[] arrow = [arrowCenter + direction * 10 * layout.Scale,
                    arrowCenter + (-direction * 6 + side * 7) * layout.Scale, arrowCenter + (-direction * 6 - side * 7) * layout.Scale];
                DrawColoredPolygon(arrow, ink);
            }
            else if (zone == TouchBigJump && !menu)
            {
                Label("BIG", center - new Vector2(12, 11) * layout.Scale, ink);
                Label("JUMP", center - new Vector2(16, -3) * layout.Scale, ink);
            }
            else
            {
                string label = zone switch { TouchJump => menu ? "OK" : "JUMP", TouchBigJump => "BACK", TouchDown => "DUCK", _ => "FIRE" };
                Label(label, center - new Vector2(label.Length * 4, 4) * layout.Scale, ink);
            }
        }
        // Adjacent movement cells share exact edges. Draw held outlines after every
        // fill and label so a later neighbour cannot paint over their shared border.
        for (int zone = 0; zone < layout.Buttons.Length; zone++)
        {
            if (!TouchButtonVisible(zone) || !Touch(zone) || zone == TouchFire && !TouchFireAvailable) continue;
            Rect2 bounds = layout.Buttons[zone];
            Color outline = new("ffdf80");
            if (zone == TouchPause) DrawArc(bounds.GetCenter(), bounds.Size.X / 2, 0, Mathf.Tau, 32, outline, 2 * layout.Scale, true);
            else DrawRect(bounds, outline, false, 2 * layout.Scale);
        }
    }
}
