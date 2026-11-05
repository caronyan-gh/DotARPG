using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotARPG.Core;

public sealed record SagaMemory(
    string Id, string BaseTitle, string HeroName, WorldStyle Style, int Part,
    string WorldName, string Villain, string Summary, bool Survived, DateTimeOffset UpdatedAt,
    PlayerTemperament Temperament = PlayerTemperament.Balanced, string? PlayerId = null);

public sealed class Chronicle
{
    public const double SequelChance = .20;
    public IReadOnlyList<SagaMemory> Sagas => sagas;
    public string? Error { get; private set; }
    public string FilePath { get; }
    private List<SagaMemory> sagas = [];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public Chronicle(string? path = null)
    {
        FilePath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotARPG", "chronicle.json");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(FilePath)) return;
        try { sagas = Read(FilePath); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Error = "旅の記憶を読み出せませんでした。";
            try
            {
                if (File.Exists(FilePath + ".bak"))
                {
                    sagas = Read(FilePath + ".bak");
                    Error = "ひとつ前の旅の記憶を読み出しました。";
                }
            }
            catch (Exception backupError) when (backupError is IOException or JsonException or UnauthorizedAccessException) { }
        }
    }

    private static List<SagaMemory> Read(string path)
    {
        var memories = JsonSerializer.Deserialize<List<SagaMemory>>(File.ReadAllText(path), JsonOptions) ?? [];
        // Invalid entries must never create anonymous heroes or negative sequel numbers.
        return memories.Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Id)
            && !string.IsNullOrWhiteSpace(m.BaseTitle) && !string.IsNullOrWhiteSpace(m.HeroName)
            && Enum.IsDefined(m.Style) && Enum.IsDefined(m.Temperament) && m.Part is >= 1 and < 1000)
            .OrderByDescending(m => m.UpdatedAt).Take(40).ToList();
    }

    public SagaMemory? ChooseSequel(Random random, WorldStyle? preferredStyle = null)
    {
        var eligible = sagas.Where(s => s.Survived && s.Part < 999 && (preferredStyle == null || s.Style == preferredStyle)).ToArray();
        if (eligible.Length == 0 || random.NextDouble() >= SequelChance) return null;
        return eligible[random.Next(eligible.Length)];
    }

    public bool Remember(Adventure game)
    {
        if (!game.Finished) return false;
        var memory = game.CreateMemory();
        var existing = sagas.FirstOrDefault(s => s.Id == memory.Id);
        if (existing != null && (existing.Part > memory.Part
            || existing.Part == memory.Part && (existing.Survived && !memory.Survived
                || existing.Survived == memory.Survived && existing.Summary == memory.Summary))) return true;
        var updated = sagas.Where(s => s.Id != memory.Id).Prepend(memory).Take(40).ToList();
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            // Only replace the backup when the current file is readable.
            if (File.Exists(FilePath))
            {
                try { Read(FilePath); File.Copy(FilePath, FilePath + ".bak", true); }
                catch (JsonException) { File.Copy(FilePath, FilePath + ".unreadable", true); }
            }
            File.WriteAllText(temporary, JsonSerializer.Serialize(updated, JsonOptions));
            File.Move(temporary, FilePath, true);
            sagas = updated;
            Error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = "旅の記憶を保存できませんでした。";
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public static string Roman(int number)
    {
        if (number is < 1 or > 3999) return number.ToString();
        string result = "";
        (int Value, string Text)[] symbols = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        foreach (var symbol in symbols)
            while (number >= symbol.Value) { result += symbol.Text; number -= symbol.Value; }
        return result;
    }
}
