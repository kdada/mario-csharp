using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public enum Screen { Title, Select, Playing, Pause, Intro, Dying, Clear, GameOver, Ending, Help, Transform }
public readonly record struct Controls(float Axis, bool Jump, bool JumpPressed, bool Run, bool FirePressed, bool Down, bool Up);
public sealed class Profile
{
    public int Unlocked { get; set; }
    public int HighScore { get; set; }
    public bool[] Completed { get; set; } = new bool[32];
}

/// <summary>All gameplay is original C# code. External inputs are media and declarative map data only.</summary>
public partial class Game : Node2D
{
    public MapCatalog Maps = null!;
    public Art Art = null!;
    public Sound Sound = null!;
    public World World = null!;
    public readonly Body Player = new();
    public Screen State = Screen.Title;
    public int LevelIndex, Score, Coins, Lives = 3, Power;
    public float CameraX, Clock, StateClock, TimeLeft, Invulnerable, StarTime;
    public string MainId => Maps.MainLevels[LevelIndex];
    public bool FacingLeft, Crouching;
    private bool jumpHeld, fireHeld, practice, hurry, verifying;
    private float fireCooldown, pipeCooldown, animationTime, deathY, flagX, clearStartY;
    private Vector2 pendingPosition;
    private string pendingRoom = "", message = "";
    private Action? afterPipe;
    private float pipeClock;
    private Vector2 pipeVelocity;
    private int titleChoice, selectChoice, pauseChoice, stompCombo;
    private Profile profile = new();
    private readonly Dictionary<string, World> roomCache = [];
    private readonly Dictionary<int, int> touches = [];
    private bool showTouch;
    private readonly Random random = new(1985);
    private readonly HashSet<string> firedTriggers = [];

    public override void _Ready()
    {
        try
        {
            Maps = new MapCatalog(); Art = new Art(); Sound = new Sound(this);
            bool playthrough = OS.GetCmdlineUserArgs().Contains("--playthrough");
            bool hapSmoke = OS.GetCmdlineUserArgs().Contains("--hap-smoke");
            verifying = OS.GetCmdlineUserArgs().Contains("--verify") || playthrough || hapSmoke;
            if (!verifying) LoadProfile();
            SetupInputs();
            showTouch = DisplayServer.IsTouchscreenAvailable();
            if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--level=")) is string arg)
            {
                int idx = Array.IndexOf(Maps.MainLevels, arg[8..]); StartGame(Math.Max(0, idx), idx > 0);
            }
            else
            {
                LoadRoom("1-1", false); State = Screen.Title; Sound.StopMusic();
            }
            if (hapSmoke) CallDeferred(nameof(RunHapSmoke));
            else if (playthrough) CallDeferred(nameof(RunPlaythrough));
            else if (verifying) CallDeferred(MethodName.RunVerification);
            GD.Print($"Mario C# study ready: {Maps.MainLevels.Length} main levels, {Maps.Rooms.Count} total rooms.");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }
    public override void _ExitTree()
    {
        if (Maps != null) SaveProfile();
        Sound?.Dispose(); Art?.Dispose(); studyRaster?.Dispose();
    }
    private void SetupInputs()
    {
        // Physical key polling deliberately avoids locale-dependent character mappings.
        Player.Position = new(-208, 0);
    }
    private bool KeyDown(Key key) => Input.IsPhysicalKeyPressed(key);
    private bool Joy(JoyButton button) => Input.IsJoyButtonPressed(0, button);
    private bool Touch(int zone) => touches.Values.Contains(zone);
    private Controls ReadControls()
    {
        float axis = (KeyDown(Key.Right) || KeyDown(Key.D) || Touch(1) || Joy(JoyButton.DpadRight) ? 1 : 0) - (KeyDown(Key.Left) || KeyDown(Key.A) || Touch(0) || Joy(JoyButton.DpadLeft) ? 1 : 0);
        float stick = Input.GetJoyAxis(0, JoyAxis.LeftX);
        if (axis == 0 && MathF.Abs(stick) > .25f) axis = MathF.Sign(stick);
        bool jump = KeyDown(Key.J) || Touch(4) || Joy(JoyButton.A);
        bool fire = KeyDown(Key.K) || Touch(3) || Joy(JoyButton.X);
        bool run = fire || Joy(JoyButton.RightShoulder);
        bool down = KeyDown(Key.Down) || KeyDown(Key.S) || Touch(2) || Joy(JoyButton.DpadDown);
        bool up = KeyDown(Key.Up) || KeyDown(Key.W) || Joy(JoyButton.DpadUp);
        var result = new Controls(axis, jump, jump && !jumpHeld, run, fire && !fireHeld, down, up);
        jumpHeld = jump; fireHeld = fire;
        return result;
    }
    public override void _Input(InputEvent ev)
    {
        if (ev is InputEventScreenTouch touch)
        {
            showTouch = true;
            if (touch.Pressed)
            {
                int zone = TouchZone(touch.Position);
                touches[touch.Index] = zone;
                if (State != Screen.Playing) HandleMenuKey(zone == 0 ? Key.Left : zone == 1 ? Key.Right : zone == 2 ? Key.Down : Key.Enter);
                else if (zone == 5) TogglePause();
            }
            else touches.Remove(touch.Index);
        }
        if (ev is InputEventScreenDrag drag) touches[drag.Index] = TouchZone(drag.Position);
        if (ev is InputEventJoypadButton pad && pad.Pressed)
        {
            Key key = pad.ButtonIndex switch { JoyButton.Start => Key.Enter, JoyButton.A => Key.J, JoyButton.B => Key.Escape, JoyButton.DpadUp => Key.Up, JoyButton.DpadDown => Key.Down, JoyButton.DpadLeft => Key.Left, JoyButton.DpadRight => Key.Right, _ => Key.None };
            if (key != Key.None) HandleMenuKey(key);
        }
        if (ev is InputEventKey k && k.Pressed && !k.Echo)
        {
            Key key = k.PhysicalKeycode;
            if (key == Key.None) key = k.Keycode;
            if (key == Key.M) { Sound.ToggleMute(); return; }
            if (key == Key.T) { showTouch = !showTouch; return; }
            if (key == Key.F11)
            {
                var mode = DisplayServer.WindowGetMode();
                DisplayServer.WindowSetMode(mode == DisplayServer.WindowMode.Fullscreen ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen); return;
            }
            if (key == Key.F12) { CaptureScreenshot(); return; }
            HandleMenuKey(key);
        }
    }
    private int TouchZone(Vector2 at) => at.Y < 55 ? 5 : at.X < 42 ? 0 : at.X < 83 ? 1 : at.X < 120 ? 2 : at.X < 199 ? 3 : 4;
    private void HandleMenuKey(Key key)
    {
        bool accept = key is Key.J or Key.Enter;
        bool back = key is Key.Escape or Key.K;
        if (State == Screen.Title)
        {
            if (key == Key.Down) titleChoice = (titleChoice + 1) % 4;
            if (key == Key.Up) titleChoice = (titleChoice + 3) % 4;
            if (accept)
                switch (titleChoice)
                {
                    case 0: StartGame(0, false); break;
                    case 1: StartGame(Math.Clamp(profile.Unlocked, 0, 31), false); break;
                    case 2: selectChoice = 0; State = Screen.Select; break;
                    case 3: State = Screen.Help; break;
                }
        }
        else if (State == Screen.Select)
        {
            if (key == Key.Left) selectChoice = (selectChoice + 31) % 32;
            if (key == Key.Right) selectChoice = (selectChoice + 1) % 32;
            if (key == Key.Up) selectChoice = (selectChoice + 28) % 32;
            if (key == Key.Down) selectChoice = (selectChoice + 4) % 32;
            if (accept) StartGame(selectChoice, true);
            if (back) ToTitle();
        }
        else if (State == Screen.Playing && key is Key.Enter or Key.Escape or Key.P) TogglePause();
        else if (State == Screen.Pause)
        {
            if (key == Key.Up) pauseChoice = (pauseChoice + 3) % 4;
            if (key == Key.Down) pauseChoice = (pauseChoice + 1) % 4;
            if (back || key == Key.P) TogglePause();
            else if (accept)
                switch (pauseChoice)
                {
                    case 0: TogglePause(); break;
                    case 1: Sound.Pause(false); BeginLevel(LevelIndex); break;
                    case 2: Sound.Pause(false); State = Screen.Select; selectChoice = LevelIndex; break;
                    case 3: ToTitle(); break;
                }
        }
        else if ((State is Screen.GameOver or Screen.Ending or Screen.Help) && (accept || back)) ToTitle();
    }
    public void StartGame(int index, bool isPractice)
    {
        Lives = 3; Power = 0; Score = 0; Coins = 0; StarTime = 0; practice = isPractice; BeginLevel(index);
    }
    public void BeginLevel(int index)
    {
        LevelIndex = Math.Clamp(index, 0, 31);
        roomCache.Clear(); firedTriggers.Clear(); StarTime = 0; Invulnerable = 0; hurry = false; powerTransitionClock = 0; Sound.Pause(false);
        TimeLeft = Maps.Rooms[MainId].Time; LoadRoom(MainId, false);
        Sound.StopMusic(); State = Screen.Intro; StateClock = 1.65f; message = "WORLD " + MainId;
    }
    public void LoadRoom(string id, bool preserve = true, Vector2? position = null)
    {
        if (!Maps.Rooms.ContainsKey(id)) throw new InvalidDataException("Room not found: " + id);
        if (!preserve || !roomCache.TryGetValue(id, out var room)) roomCache[id] = room = new World(Maps.Rooms[id], Maps, MainId);
        World = room; Player.Position = position ?? room.Data.Start; Player.Velocity = Vector2.Zero; Player.Grounded = false; Player.Support = null;
        Player.Height = Power > 0 ? 32 : 16; Crouching = false;
        CameraX = MathF.Max(room.MinX, Player.Position.X - 40); CameraX = MathF.Min(CameraX, room.MaxX - 256);
        pipeCooldown = 1.0f; pipeClock = 0; FacingLeft = false;
        string music = World.Theme == "CastleWater" ? "Underwater" : World.IsWater ? "Underwater" : World.Theme is "Castle" or "Underground" ? World.Theme : "Overworld";
        if (State != Screen.Title && State != Screen.Select && !verifying) Sound.Music(music);
    }
    public override void _PhysicsProcess(double delta)
    {
        if (World == null) return;
        float dt = MathF.Min((float)delta, 1f / 30);
        if (State is not (Screen.Pause or Screen.Transform)) { Clock += dt; animationTime += dt; }
        Controls input = ReadControls();
        if (verifying) return;
        AdvanceState(dt, input);
        QueueRedraw();
    }
    private void AdvanceState(float dt, Controls input)
    {
        switch (State)
        {
            case Screen.Intro:
                if ((StateClock -= dt) <= 0) { State = Screen.Playing; PlayThemeMusic(); }
                break;
            case Screen.Playing: TickGame(dt, input); break;
            case Screen.Transform: TickPowerTransition(dt); break;
            case Screen.Dying:
                StateClock -= dt;
                if (StateClock < 2.1f) { Player.Velocity += new Vector2(0, 900 * dt); Player.Position += Player.Velocity * dt; }
                if (StateClock <= 0) FinishDeath();
                break;
            case Screen.Clear: TickClear(dt); break;
            case Screen.GameOver: StateClock -= dt; break;
        }
    }
    private void PlayThemeMusic() => Sound.Music(StarTime > 0 ? "StarMan" : (World.IsWater ? "Underwater" : World.Theme is "Castle" or "Underground" ? World.Theme : "Overworld") + (hurry ? "Hurry" : ""));
    private void TogglePause()
    {
        if (State == Screen.Playing) { State = Screen.Pause; pauseChoice = 0; Sound.Pause(true); Sound.Effect("Pause"); }
        else if (State == Screen.Pause) { State = Screen.Playing; Sound.Pause(false); }
    }
    private void ToTitle()
    {
        Sound.Pause(false); Sound.StopMusic(); State = Screen.Title; LevelIndex = 0; Power = 0; LoadRoom("1-1", false); SaveProfile();
    }
    private void LoadProfile()
    {
        if (!FileAccess.FileExists("user://progress.json")) return;
        try { profile = JsonSerializer.Deserialize(FileAccess.GetFileAsString("user://progress.json"), GameJsonContext.Default.Progress) ?? new(); }
        catch { profile = new(); }
        if (profile.Completed.Length != 32) profile.Completed = new bool[32];
    }
    private void SaveProfile()
    {
        if (verifying || (HasMeta("dsh_graphics_test") && GetMeta("dsh_graphics_test").AsBool())) return;
        profile.HighScore = Math.Max(profile.HighScore, Score);
        using var file = FileAccess.Open("user://progress.json", FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(profile, GameJsonContext.Default.Progress));
    }
    private async void CaptureScreenshot()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string dir = ProjectSettings.GlobalizePath("user://screenshots"); DirAccess.MakeDirRecursiveAbsolute(dir);
        string path = dir + "/mario-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
        GetViewport().GetTexture().GetImage().SavePng(path); GD.Print("Screenshot: " + path);
    }
}
