using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotARPG.Core;

public sealed record JourneyCheckpoint(int SimulationVersion, int Seed, WorldStyle Style,
    SagaMemory? PreviousStory, PlayerTemperament Temperament, int Tick, string Title,
    EpisodePhase Phase, double OpeningElapsed, int SpeedIndex, bool Paused,
    int[]? RevivalTicks = null, string? PlayerId = null, string? PlayerName = null,
    string? GameId = null, string? RunId = null, int CombatRulesUpgradeTick = 0,
    int TownRulesUpgradeTick = 0, bool ExpandedProgression = true, int TravelRulesUpgradeTick = 0);

public sealed class JourneyCheckpointStore
{
    // Bump this when simulation changes would make an older replay produce a different journey.
    public const int SimulationVersion = 5;
    public string FilePath { get; }
    public string? Notice { get; private set; }
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public JourneyCheckpointStore(string? path = null) => FilePath = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotARPG", "current-journey.json");

    public JourneyCheckpoint? Load()
    {
        Notice = null;
        if (!File.Exists(FilePath)) return null;
        try { return Read(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Notice = "直前の保存を読めなかったため、ひとつ前の保存を確認しています。";
            try
            {
                if (File.Exists(FilePath + ".bak")) return Read(FilePath + ".bak");
            }
            catch (Exception backup) when (backup is IOException or UnauthorizedAccessException or JsonException) { }
            Notice = "前回の途中保存を読み出せませんでした。保存ファイルは残して、新しい旅を開きます。";
            return null;
        }
    }

    private static JourneyCheckpoint Read(string path)
    {
        var save = JsonSerializer.Deserialize<JourneyCheckpoint>(File.ReadAllText(path), Options)
            ?? throw new JsonException("保存データが空です。");
        if (save.SimulationVersion is not (2 or 3 or 4 or SimulationVersion) || !Enum.IsDefined(save.Style)
            || !Enum.IsDefined(save.Temperament) || !Enum.IsDefined(save.Phase)
            || save.Tick is < 0 or > 2_000_000 || save.CombatRulesUpgradeTick < 0 || save.CombatRulesUpgradeTick > save.Tick
            || save.TownRulesUpgradeTick < 0 || save.TownRulesUpgradeTick > save.Tick
            || save.TravelRulesUpgradeTick < 0 || save.TravelRulesUpgradeTick > save.Tick
            || string.IsNullOrWhiteSpace(save.Title)
            || !double.IsFinite(save.OpeningElapsed) || save.OpeningElapsed is < 0 or > EpisodeFlow.OpeningDuration
            || save.SpeedIndex is < 0 or > 2 || save.PreviousStory is { } previous
                && (previous.Style != save.Style || !previous.Survived || previous.Part is < 1 or >= 999))
            throw new JsonException("この版では読み出せない途中保存です。");
        int lastRevival = -1;
        foreach (int tick in save.RevivalTicks ?? [])
        {
            if (tick <= lastRevival || tick > save.Tick) throw new JsonException("再挑戦の記録が正しくありません。");
            lastRevival = tick;
        }
        return save;
    }

    public bool Save(JourneyCheckpoint save)
    {
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(save, Options));
            if (File.Exists(FilePath))
            {
                try { Read(FilePath); File.Copy(FilePath, FilePath + ".bak", true); }
                catch (JsonException) { File.Copy(FilePath, FilePath + ".unreadable", true); }
            }
            File.Move(temporary, FilePath, true);
            Notice = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notice = "途中保存を書き込めませんでした。空き容量や保存先の状態を確認してください。";
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public static Adventure Replay(JourneyCheckpoint save)
    {
        if (save.SimulationVersion is not (2 or 3 or 4 or SimulationVersion)) throw new InvalidDataException("保存データの版が異なります。");
        var game = new Adventure(new World(save.Seed, save.Style, save.SimulationVersion >= 4 && save.ExpandedProgression),
            save.PreviousStory, save.Temperament, save.PlayerId,
            save.SimulationVersion < 5 ? save.Tick : save.TravelRulesUpgradeTick);
        game.CombatRulesUpgradeTick = save.SimulationVersion == 2 ? save.Tick : save.CombatRulesUpgradeTick;
        game.TownRulesUpgradeTick = save.SimulationVersion < 4 ? save.Tick : save.TownRulesUpgradeTick;
        int revivalIndex = 0;
        int[] revivals = save.RevivalTicks ?? [];
        for (int tick = 0; tick <= save.Tick; tick++)
        {
            if (revivalIndex < revivals.Length && revivals[revivalIndex] == game.Tick)
            {
                if (!game.ReviveAtBase()) throw new InvalidDataException("再挑戦の状態を再現できませんでした。");
                revivalIndex++;
            }
            if (tick < save.Tick) game.Step();
        }
        if (game.Title != save.Title || game.Temperament != save.Temperament
            || (save.Phase == EpisodePhase.Ending) != game.Finished
            || save.Phase == EpisodePhase.Opening && save.Tick != 0)
            throw new InvalidDataException("旅の状態を正しく再現できませんでした。");
        return game;
    }
}
