using Godot;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public sealed class Sound : IDisposable
{
    // Source library's TitleBGM.json explicitly selects this recording for its SMB1 title variant.
    public const string TitleMusicName = "TitleSMB";
    public const string DeathMusicName = "PlayerDie";
    public const float DeathHangSeconds = .7f;
    private readonly AudioStreamPlayer music;
    private readonly AudioStreamPlayer[] effects;
    private readonly Dictionary<string, AudioStream> cache = [];
    private readonly bool playable;
    private int next;
    private string current = "";
    public bool Muted { get; private set; }
    public bool PlaybackEnabled => playable;
    public string MusicName => current;
    public string MusicPath => current.Length == 0 ? "" : MusicAssetPath(current);
    public double MusicDurationSeconds => music.Stream?.GetLength() ?? 0;
    public bool MusicLoops => music.Stream is AudioStreamMP3 mp3 ? mp3.Loop : music.Stream is AudioStreamWav wav && wav.LoopMode != AudioStreamWav.LoopModeEnum.Disabled;
    public string LastEffectName { get; private set; } = "";
    public bool IsMusicPlaying => music.Playing;
    public bool IsMusicPaused => music.StreamPaused;
    public int ActiveEffects => effects.Count(player => player.Playing);
    public double DeathMusicDurationSeconds => Load(MusicAssetPath(DeathMusicName))?.GetLength() ?? 0;
    // Preserve the original 0.7s hang + 2.1s motion, extending only if the actual recording needs longer.
    public float DeathDurationSeconds => DeathHangSeconds + MathF.Max(2.1f, (float)DeathMusicDurationSeconds + .1f);
    public float DeathMotionRemainingSeconds => DeathDurationSeconds - DeathHangSeconds;
    public Sound(Node owner, bool playbackEnabled = true)
    {
        playable = playbackEnabled && DisplayServer.GetName() != "headless";
        music = new AudioStreamPlayer { VolumeDb = -10, MaxPolyphony = 1 };
        owner.AddChild(music);
        effects = Enumerable.Range(0, 10).Select(_ => new AudioStreamPlayer { VolumeDb = -7 }).ToArray();
        foreach (var player in effects) owner.AddChild(player);
    }
    private AudioStream? Load(string path, bool loop = false)
    {
        if (cache.TryGetValue(path, out var cached)) return cached;
        if (!FileAccess.FileExists(path)) { GD.PushWarning("Missing sound: " + path); return null; }
        AudioStream stream;
        if (path.EndsWith(".mp3")) stream = new AudioStreamMP3 { Data = FileAccess.GetFileAsBytes(path), Loop = loop };
        else stream = AudioStreamWav.LoadFromFile(path);
        cache[path] = stream;
        return stream;
    }
    public static string MusicAssetPath(string name) => "res://Assets/Audio/BGM/" + name + (name == DeathMusicName ? ".wav" : ".mp3");
    public double EffectDurationSeconds(string name) => Load("res://Assets/Audio/SFX/" + name + ".wav")?.GetLength() ?? 0;
    public void PlayTitleMusic() { Pause(false); Music(TitleMusicName); }
    public void BeginDeath() { Pause(false); StopMusic(); Effect("DieSting"); }
    public void PlayDeathMusic() => Music(DeathMusicName, false);
    public void Music(string name, bool loop = true)
    {
        if (current == name && music.Playing && MusicLoops == loop) return;
        current = name;
        music.Stream = Load(MusicAssetPath(name), loop);
        // A cached recording may have been inspected or used with a different loop policy.
        if (music.Stream is AudioStreamMP3 mp3) mp3.Loop = loop;
        else if (music.Stream is AudioStreamWav wav) wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
        if (music.Stream != null && playable) music.Play();
    }
    public void Effect(string name)
    {
        var stream = Load("res://Assets/Audio/SFX/" + name + ".wav");
        if (stream == null) return;
        LastEffectName = name;
        if (!playable) return;
        var player = effects[next++ % effects.Length]; player.Stream = stream; player.Play();
    }
    public void Dispose()
    {
        music.Stop(); music.Stream = null;
        foreach (var player in effects) { player.Stop(); player.Stream = null; }
        foreach (var stream in cache.Values) stream.Dispose(); cache.Clear();
    }
    public void StopMusic() { music.Stop(); current = ""; }
    public void Pause(bool value) => music.StreamPaused = value;
    public void ToggleMute() { Muted = !Muted; AudioServer.SetBusMute(0, Muted); }
}
