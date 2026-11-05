using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotARPG.Core;

public sealed record PlayerProfile(string Id, string Name, PlayerTemperament Temperament);
public sealed record GameDefinition(string Id, int Seed, WorldStyle Style, SagaMemory? PreviousStory, string Title, bool ExpandedProgression = false);
public sealed record PlayerRun(string Id, string GameId, string PlayerId, string Status, int Deaths,
    int Level, int Day, int Victories, double Explored, int Ticks, DateTimeOffset UpdatedAt);

public sealed class PlayerLibrary
{
    private sealed record LibraryData(List<PlayerProfile> Players, List<GameDefinition> Games, List<PlayerRun> Runs);
    private LibraryData data;
    public IReadOnlyList<PlayerProfile> Players => data.Players;
    public IReadOnlyList<GameDefinition> Games => data.Games;
    public IReadOnlyList<PlayerRun> Runs => data.Runs;
    public string FilePath { get; }
    public string? Notice { get; private set; }
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public PlayerLibrary(string? path = null)
    {
        FilePath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotARPG", "players.json");
        try { data = Read(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Notice = "プレイデータを読み出せませんでした。";
            try { data = Read(FilePath + ".bak"); }
            catch (Exception backup) when (backup is IOException or UnauthorizedAccessException or JsonException) { data = Empty(); }
        }
        if (data.Players.Count == 0) data.Players.AddRange(CreatePlayers());
    }

    private LibraryData Read(string? path = null)
    {
        string file = path ?? FilePath;
        if (!File.Exists(file)) return Empty();
        var result = JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(file), Options) ?? throw new JsonException("空のプレイデータです。");
        if (result.Players == null || result.Games == null || result.Runs == null
            || result.Players.Any(p => string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name) || !Enum.IsDefined(p.Temperament)))
            throw new JsonException("プレイデータの形式が違います。");
        return result;
    }
    private static LibraryData Empty() => new([], [], []);
    private static IEnumerable<PlayerProfile> CreatePlayers()
    {
        string[] names = ["すい", "ゆず", "レイ", "ハル"];
        return Enum.GetValues<PlayerTemperament>().Select((t, i) => new PlayerProfile(Guid.NewGuid().ToString("N"), names[i], t));
    }

    public void Register(GameDefinition game)
    {
        if (data.Games.All(g => g.Id != game.Id)) data.Games.Add(game);
    }
    public void Record(string runId, GameDefinition game, PlayerProfile player, Adventure adventure, bool interrupted = false)
    {
        Register(game);
        if (data.Players.All(p => p.Id != player.Id)) data.Players.Add(player);
        string status = adventure.Finished ? adventure.Won ? "クリア" : "死亡" : interrupted ? "中断" : "冒険中";
        var run = new PlayerRun(runId, game.Id, player.Id, status, adventure.DeathCount, adventure.Level,
            adventure.Day, adventure.Victories, adventure.Exploration.Fraction, adventure.Tick, DateTimeOffset.UtcNow);
        data.Runs.RemoveAll(r => r.Id == runId);
        data.Runs.Add(run);
    }
    public bool Save()
    {
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            // Keep records produced by another open window as well.
            LibraryData onDisk;
            bool currentReadable = true;
            try { onDisk = Read(); }
            catch (JsonException)
            {
                currentReadable = false;
                File.Copy(FilePath, FilePath + ".unreadable", true);
                onDisk = Read(FilePath + ".bak");
            }
            data = new(
                data.Players.Concat(onDisk.Players).DistinctBy(p => p.Id).ToList(),
                data.Games.Concat(onDisk.Games).DistinctBy(g => g.Id).ToList(),
                data.Runs.Concat(onDisk.Runs).GroupBy(r => r.Id).Select(g => g.MaxBy(r => r.UpdatedAt)!).ToList());
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options));
            if (currentReadable && File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
            File.Move(temporary, FilePath, true);
            Notice = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Notice = "プレイデータを保存できませんでした。";
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
