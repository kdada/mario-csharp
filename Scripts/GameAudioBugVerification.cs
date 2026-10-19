using Godot;
using System.Security.Cryptography;
using System.Text.Json;
using FileAccess = Godot.FileAccess;
using GameSound = MarioStudy.Sound;

namespace MarioStudy;

public partial class Game
{
    /// <summary>Checks original file identities, resource metadata and callers' clocks without playing audio.</summary>
    private void VerifyAudioBugFixes()
    {
        int first = verificationChecks.Count, failures = verificationFailures;
        bool processing = IsPhysicsProcessing(), previousVerifying = verifying, previousPractice = practice;
        var previousSound = Sound;
        verifying = true; SetPhysicsProcess(false);
        var probeOwner = new Node { Name = "AudioMetadataProbe" }; AddChild(probeOwner);
        var probe = new GameSound(probeOwner, playbackEnabled: false);
        Sound = probe;
        void Identity(string relative, string expected)
        {
            string path = "res://Assets/Audio/" + relative;
            Check("original audio file exists " + relative, FileAccess.FileExists(path));
            if (!FileAccess.FileExists(path)) return;
            string hash = Convert.ToHexString(SHA256.HashData(FileAccess.GetFileAsBytes(path))).ToLowerInvariant();
            Check("audio matches existing source-library recording " + relative, hash == expected, hash);
        }
        void DeathTimeline(int initialLives, bool isPractice, int hz)
        {
            MechanicsWorld(); practice = isPractice; Lives = initialLives;
            probe.Music("Overworld");
            float duration = probe.DeathDurationSeconds, dt = 1f / hz;
            float elapsed = 0, musicAt = -1, motionAt = -1, originalY = Player.Position.Y;
            Die();
            string label = $"lives={initialLives} practice={isPractice} {hz}Hz";
            Check("death starts with short original sting and silent BGM hang " + label,
                State == Screen.Dying && MathF.Abs(StateClock - duration) < .001f && probe.LastEffectName == "DieSting" && probe.MusicName == "" && !probe.IsMusicPaused);
            bool silentHang = true, keepsDeathMusic = true;
            for (int frame = 0; frame < MathF.Ceiling((duration + .2f) * hz) && State == Screen.Dying; frame++)
            {
                AdvanceState(dt, new()); elapsed += dt;
                if (musicAt < 0 && probe.MusicName == GameSound.DeathMusicName) musicAt = elapsed;
                if (motionAt < 0 && MathF.Abs(Player.Position.Y - originalY) > .001f && State == Screen.Dying) motionAt = elapsed;
                if (elapsed < GameSound.DeathHangSeconds - dt) silentHang &= probe.MusicName == "";
                if (elapsed > GameSound.DeathHangSeconds + dt * 1.1f && State == Screen.Dying)
                    keepsDeathMusic &= probe.MusicName == GameSound.DeathMusicName && !probe.MusicLoops;
            }
            Check("death music follows hang rather than replacing the sting immediately " + label,
                silentHang && musicAt >= GameSound.DeathHangSeconds - .001f && musicAt <= GameSound.DeathHangSeconds + dt + .002f, $"musicAt={musicAt:F4}");
            Check("death music begins with the existing death motion " + label, musicAt >= 0 && motionAt >= 0 && MathF.Abs(musicAt - motionAt) <= dt + .001f);
            Check("full death melody has time to finish before any scene music " + label,
                musicAt >= 0 && keepsDeathMusic && elapsed - musicAt >= probe.DeathMusicDurationSeconds + .09,
                $"music window={elapsed - musicAt:F4}, recording={probe.DeathMusicDurationSeconds:F4}");
            if (initialLives == 1 && !isPractice)
            {
                Check("game over follows complete death melody and is not looped " + label,
                    State == Screen.GameOver && Lives == 0 && probe.MusicName == "GameOver" && !probe.MusicLoops);
                ToTitle(); Check("returning from game over starts title music " + label, State == Screen.Title && probe.MusicName == GameSound.TitleMusicName && probe.MusicLoops);
            }
            else
            {
                Check("respawn intro follows complete death music in silence " + label,
                    State == Screen.Intro && MathF.Abs(StateClock - 1.65f) < .001f && probe.MusicName == "" && Lives == (initialLives > 1 ? initialLives - 1 : 3));
                for (int frame = 0; frame < hz * 8 / 5; frame++) AdvanceState(dt, new());
                Check("respawn intro does not resume level music early " + label, State == Screen.Intro && probe.MusicName == "");
                for (int frame = 0; frame < hz && State == Screen.Intro; frame++) AdvanceState(dt, new());
                Check("level music resumes after the intro interval " + label, State == Screen.Playing && probe.MusicName == "Overworld" && probe.MusicLoops);
            }
            Check("audio timing verification produces no playback " + label, !probe.PlaybackEnabled && !probe.IsMusicPlaying && probe.ActiveEffects == 0);
        }
        try
        {
            Identity("BGM/TitleSMB.mp3", "79f340ba70812d8a8d5334815895d2e8da377ab8d53a8e65c44d4b3c98875be2");
            Identity("BGM/Overworld.mp3", "d03bccd36f1275d66eb20937283e79bbf445092457165c43b0f6f3881d153c9d");
            Identity("SFX/DieSting.wav", "58791598443a6e000f370a78c55321dd18811bdaf76d2ae1c9db7596641949bd");
            Identity("BGM/PlayerDie.wav", "481c56a40c756ad9b65c26479ebd34150b7fc526b140d595242e3f0cd5fa136d");
            using var title = JsonDocument.Parse(FileAccess.GetFileAsString("res://Assets/Audio/BGM/TitleBGM.json"));
            string titleSource = title.RootElement.GetProperty("variations").GetProperty("SMB1").GetProperty("source").GetString() ?? "";
            Check("title selects the source library's explicit SMB1 title variation", titleSource == GameSound.TitleMusicName + ".mp3");
            using var death = JsonDocument.Parse(FileAccess.GetFileAsString("res://Assets/Audio/BGM/PlayerDie.json"));
            Check("death music selects the original WAV resource rather than a nonexistent MP3",
                death.RootElement.GetProperty("variations").GetProperty("source").GetString() == "PlayerDie.wav" && GameSound.MusicAssetPath(GameSound.DeathMusicName).EndsWith("/PlayerDie.wav"));
            using (var wav = AudioStreamWav.LoadFromFile(GameSound.MusicAssetPath(GameSound.DeathMusicName)))
                Check("complete death WAV metadata contains all original samples", wav.MixRate == 44100 && !wav.Stereo && Math.Abs(wav.GetLength() - 87632d / 44100) < 1d / 44100);
            Check("short death sting and full melody are distinct resources",
                Math.Abs(probe.EffectDurationSeconds("DieSting") - 12280d / 44100) < 1d / 44100 && probe.DeathMusicDurationSeconds > 1.98 && probe.DeathMusicDurationSeconds < 2.0);
            Check("death state reserves the hang plus the complete melody and tail", probe.DeathDurationSeconds >= GameSound.DeathHangSeconds + probe.DeathMusicDurationSeconds + .099 && MathF.Abs(probe.DeathMotionRemainingSeconds - (probe.DeathDurationSeconds - GameSound.DeathHangSeconds)) < .001f);
            probe.PlayTitleMusic();
            Check("title loads a complete looping MP3 without output", probe.MusicName == GameSound.TitleMusicName && probe.MusicPath.EndsWith("/TitleSMB.mp3") && probe.MusicDurationSeconds > 32 && probe.MusicDurationSeconds < 33 && probe.MusicLoops && !probe.IsMusicPlaying);
            probe.Music(GameSound.TitleMusicName, false); Check("cached title accepts one-shot policy", !probe.MusicLoops);
            probe.Pause(true); probe.PlayTitleMusic(); Check("returning to title restores looping and clears pause", probe.MusicLoops && !probe.IsMusicPaused);
            probe.BeginDeath(); probe.PlayDeathMusic();
            Check("death melody uses BGM channel and never loops", probe.MusicName == GameSound.DeathMusicName && probe.MusicPath.EndsWith("/PlayerDie.wav") && !probe.MusicLoops && Math.Abs(probe.MusicDurationSeconds - probe.DeathMusicDurationSeconds) < .0001 && probe.ActiveEffects == 0);
            ToTitle(); Check("title caller starts the source title music", State == Screen.Title && probe.MusicName == GameSound.TitleMusicName && probe.MusicLoops);
            titleChoice = 3; HandleMenuKey(Key.J); Check("help retains title background music", State == Screen.Help && probe.MusicName == GameSound.TitleMusicName);
            HandleMenuKey(Key.Escape); titleChoice = 2; HandleMenuKey(Key.J);
            Check("level selection entered from title retains title music", State == Screen.Select && probe.MusicName == GameSound.TitleMusicName);
            StartGame(0, false); Check("starting a course stops title music for the intro", State == Screen.Intro && probe.MusicName == "");
            foreach (int hz in new[] { 30, 60, 120 })
            {
                DeathTimeline(2, false, hz);
                DeathTimeline(1, false, hz);
                DeathTimeline(1, true, hz);
            }
        }
        catch (Exception ex) { Check("audio bug verification exception", false, ex.ToString()); }
        finally
        {
            Sound = previousSound; probe.Dispose(); probeOwner.Free();
            practice = previousPractice; verifying = previousVerifying; SetPhysicsProcess(processing);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://Reports/Regression"));
            using var report = FileAccess.Open("res://Reports/Regression/title-death-audio.json", FileAccess.ModeFlags.Write);
            report.StoreString(JsonSerializer.Serialize(new { graphicsIncluded = false, audioPlaybackIncluded = false, sourceRepository = "https://github.com/JHDev2006/Super-Mario-Bros.-Remastered-Public", failures = verificationFailures - failures, checks = verificationChecks.Skip(first).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
