using Godot;
using System.Text.Json;

namespace MarioStudy;

public partial class Game
{
    /// <summary>Read-only diagnostics for local editor testing; never used by gameplay.</summary>
    public string StudySnapshot() => JsonSerializer.Serialize(new
    {
        state = State.ToString(), main = MainId, room = World.Data.Id,
        score = Score, coins = Coins, lives = Lives, power = Power,
        camera = CameraX, time = TimeLeft, star = StarTime, protectedTime = Invulnerable,
        player = new { x = Player.Position.X, y = Player.Position.Y, vx = Player.Velocity.X, vy = Player.Velocity.Y, grounded = Player.Grounded, height = Player.Height, crouching = Crouching },
        enemies = World.Enemies.Count(e => !e.Removed), items = World.Items.Count(i => !i.Removed), shots = World.Shots.Count(s => !s.Removed),
        isWater = World.IsWater, theme = World.Theme, renderError = lastRenderError,
        music = Sound.MusicName, musicPlaying = Sound.IsMusicPlaying, musicPaused = Sound.IsMusicPaused,
        effectsPlaying = Sound.ActiveEffects, touch = showTouch, deviceType,
        display = DisplayServer.GetName(), driver = RenderingServer.GetCurrentRenderingDriverName(),
        frames = Engine.GetFramesDrawn(), fps = Engine.GetFramesPerSecond()
    });

    /// <summary>Place an editor-study actor in a room; ordinary play uses pipes and menus.</summary>
    public void StudyPlace(string room, float x, float y)
    {
        LoadRoom(room, false, new Vector2(x, y)); State = Screen.Playing;
        Invulnerable = StarTime = 0; Player.Velocity = Vector2.Zero;
    }
}
