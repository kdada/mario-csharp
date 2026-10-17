using Godot;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public sealed class Sound : IDisposable
{
    private readonly AudioStreamPlayer music;
    private readonly AudioStreamPlayer[] effects;
    private readonly Dictionary<string, AudioStream> cache = [];
    private readonly bool playable = DisplayServer.GetName() != "headless";
    private int next;
    private string current = "";
    public bool Muted { get; private set; }
    public string MusicName => current;
    public bool IsMusicPlaying => music.Playing;
    public bool IsMusicPaused => music.StreamPaused;
    public int ActiveEffects => effects.Count(player => player.Playing);
    public Sound(Node owner)
    {
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
    public void Music(string name, bool loop = true)
    {
        if (current == name && music.Playing) return;
        current = name;
        music.Stream = Load("res://Assets/Audio/BGM/" + name + ".mp3", loop);
        if (music.Stream != null && playable) music.Play();
    }
    public void Effect(string name)
    {
        var stream = Load("res://Assets/Audio/SFX/" + name + ".wav");
        if (stream == null || !playable) return;
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
