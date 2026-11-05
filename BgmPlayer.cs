using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using DotARPG.Core;

namespace DotARPG;

internal sealed class BgmPlayer : IDisposable
{
    private sealed record Settings(double Volume = .18, bool Muted = false);
    private readonly MediaPlayer player = new();
    private readonly bool persistSettings;
    private readonly HashSet<string> resources = typeof(BgmPlayer).Assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);
    private static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotARPG");
    private static string SettingsPath => Path.Combine(DirectoryPath, "audio.json");
    private string? track;
    private bool playRequested, ready, dirty, disposed;
    internal double Volume { get; private set; } = .18;
    internal bool Muted { get; private set; }
    internal bool Ready => ready;
    internal string? CurrentTrack => track;
    internal TimeSpan Position => player.Position;
    internal TimeSpan Duration => player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : TimeSpan.Zero;
    internal int LoopCount { get; private set; }
    internal string? LastError { get; private set; }
    internal event Action? PlaybackFailed;

    internal BgmPlayer(bool persistSettings = true)
    {
        this.persistSettings = persistSettings;
        if (persistSettings)
        {
            try
            {
                if (File.Exists(SettingsPath) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) is { } settings)
                {
                    Volume = double.IsFinite(settings.Volume) ? Math.Clamp(settings.Volume, 0, 1) : .18;
                    Muted = settings.Muted;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        ApplyVolume();
        player.MediaOpened += (_, _) => { if (disposed) return; ready = true; if (playRequested) player.Play(); };
        player.MediaEnded += (_, _) =>
        {
            if (disposed) return;
            player.Position = TimeSpan.Zero;
            LoopCount++;
            if (playRequested) player.Play();
        };
        player.MediaFailed += (_, error) => Fail(error.ErrorException);
    }

    internal void Update(WorldStyle style, bool majorBattle, bool playing)
    {
        if (disposed) return;
        string mapTrack = style.ToString().ToLowerInvariant();
        string? wanted = majorBattle && HasTrack("boss") ? "boss" : HasTrack(mapTrack) ? mapTrack : null;
        if (wanted != track)
        {
            player.Close(); ready = false; track = wanted; LastError = null;
            playRequested = playing;
            if (wanted != null)
            {
                try { player.Open(new Uri(ExtractTrack(wanted))); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
                { Fail(error); }
            }
            return;
        }
        if (playRequested == playing) return;
        playRequested = playing;
        if (ready) { if (playing) player.Play(); else player.Pause(); }
    }

    private bool HasTrack(string name) => resources.Contains("DotARPG.Assets.Music." + name + ".mp3");
    private static string ExtractTrack(string name)
    {
        using Stream stream = typeof(BgmPlayer).Assembly.GetManifestResourceStream("DotARPG.Assets.Music." + name + ".mp3")!;
        string digest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()[..12];
        string folder = Path.Combine(DirectoryPath, "Audio");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, name + "-" + digest + ".mp3");
        if (!File.Exists(file))
        {
            stream.Position = 0;
            using var output = File.Create(file);
            stream.CopyTo(output);
        }
        return file;
    }

    internal void SetVolume(double volume)
    {
        Volume = double.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : .18;
        dirty = true; ApplyVolume();
    }
    internal void SetMuted(bool muted) { Muted = muted; dirty = true; ApplyVolume(); }
    private void ApplyVolume() => player.Volume = Muted ? 0 : Volume;
    private void Fail(Exception error)
    {
        ready = false; LastError = error.Message; player.Close(); PlaybackFailed?.Invoke();
    }
    internal void SeekNearEndForVerification() => player.Position = Duration - TimeSpan.FromMilliseconds(150);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; player.Close();
        if (!persistSettings || !dirty) return;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(Volume, Muted)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
