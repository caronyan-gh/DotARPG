using System.Reflection;
using DotARPG.Core;

internal static class TravelChecks
{
    public static void Run()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static void Set(Adventure game, string property, object value) => typeof(Adventure).GetProperty(property)!.SetValue(game, value);
        static object? Call(Adventure game, string method, params object[] args) => typeof(Adventure)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(game, args);
        var game = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Balanced);
        var path = (Queue<Point>)typeof(Adventure).GetField("path", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        typeof(Adventure).GetField("lastEncounter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, 10000);
        Site home = game.RespawnSite;
        Site second = game.World.Sites.Where(t => t.IsTown).MaxBy(t => t.Position.Distance(home.Position))!;
        Site unknown = game.World.Sites.First(t => t.IsTown && t != home && t != second);
        Check(game.VisitedTowns.SequenceEqual([home]) && game.Inventory["マナポーション"] == 2 && game.Inventory["帰還の巻物"] == 1,
            "初期の所持品と訪問済みの町");

        Set(game, nameof(Adventure.Hero), second.Position);
        path.Clear(); path.Enqueue(second.Position);
        game.Step();
        Check(game.VisitedTowns.Contains(second) && !game.VisitedTowns.Contains(unknown), "到着した町だけポータルが開通する");
        Set(game, nameof(Adventure.Hero), home.Position);
        Call(game, "Route", second.Position);
        Check(game.Hero == second.Position && game.LastTransit is { Portal: true } && path.Count == 0, "町のゲートで別の訪問済みの町へ移動する");
        Call(game, "Route", unknown.Position);
        Check(game.Hero != unknown.Position && !game.VisitedTowns.Contains(unknown), "未訪問の町へは転移できない");

        Site field = game.World.Sites.Where(t => !t.IsTown).MaxBy(t => game.VisitedTowns.Min(v => v.Position.Distance(t.Position)))!;
        Set(game, nameof(Adventure.Hero), field.Position);
        path.Clear(); path.Enqueue(field.Position);
        Set(game, nameof(Adventure.Mana), 0);
        int potions = game.Inventory["マナポーション"];
        game.Step();
        Check(game.Mana > 0 && game.Inventory["マナポーション"] == potions - 1 && game.Hero == field.Position, "移動中のMP切れはマナポで回復する");
        Set(game, nameof(Adventure.Health), 1);
        game.Step();
        Check(game.LastTransit is { Portal: false } && game.VisitedTowns.Any(t => t.Position == game.Hero)
            && game.Inventory["帰還の巻物"] == 0 && game.Health == 1, "消耗時は巻物で既知の町へ帰還する。転移自体には回復効果がない");
        game.Step();
        Check(game.Health == game.MaxHealth && game.Mana == game.MaxMana, "帰還後に町で休んで全回復する");
        Set(game, nameof(Adventure.Gold), 500);
        Call(game, "RestInTown");
        Check(game.Inventory["マナポーション"] == 2 && game.Inventory["帰還の巻物"] == 1, "町でマナポと帰還の巻物を補充する");

        JourneyCheckpoint Capture(Adventure run, int version) => new(version, 31, WorldStyle.Japanese, null,
            run.Temperament, run.Tick, run.Title, run.Finished ? EpisodePhase.Ending : EpisodePhase.Playing,
            EpisodeFlow.OpeningDuration, 0, false, run.RevivalTicks.ToArray(), TravelRulesUpgradeTick: run.TravelRulesUpgradeTick);
        var original = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Balanced);
        for (int i = 0; i < 1200 && !original.Finished; i++) original.Step();
        var replayed = JourneyCheckpointStore.Replay(Capture(original, JourneyCheckpointStore.SimulationVersion));
        Check(original.Hero == replayed.Hero && original.Health == replayed.Health && original.Mana == replayed.Mana
            && original.Inventory.SequenceEqual(replayed.Inventory) && original.VisitedTowns.ToHashSet().SetEquals(replayed.VisitedTowns)
            && original.LastTransit == replayed.LastTransit, "新しい途中保存から消耗品と訪問済みポータルを再現する");
        var legacy = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Balanced, travelRulesUpgradeTick: 600);
        for (int i = 0; i < 600; i++) legacy.Step();
        replayed = JourneyCheckpointStore.Replay(Capture(legacy, 4) with { TravelRulesUpgradeTick = 0 });
        Check(legacy.Hero == replayed.Hero && legacy.Health == replayed.Health && legacy.Mana == replayed.Mana
            && legacy.Inventory.SequenceEqual(replayed.Inventory), "旧版の途中保存は元のルールで保存位置まで再現する");
        replayed.Step();
        Check(replayed.Inventory.ContainsKey("マナポーション") && replayed.Inventory.ContainsKey("帰還の巻物"), "旧版の続きから新しい消耗品を有効にする");
        var upgraded = JourneyCheckpointStore.Replay(Capture(replayed, JourneyCheckpointStore.SimulationVersion));
        Check(replayed.Hero == upgraded.Hero && replayed.Mana == upgraded.Mana && replayed.Inventory.SequenceEqual(upgraded.Inventory),
            "旧版から移行した旅を再保存しても同じ状態に戻る");
        Console.WriteLine("PASS: mana potions, shop restocking, recall, visited-town portals, current replay and v4 save upgrade.");
    }
}
