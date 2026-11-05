using DotARPG.Core;

internal static class SessionChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        var flow = new EpisodeFlow();
        flow.Advance(3);
        Check(flow.Phase == EpisodePhase.Opening, "短いOPが途中で消えない");
        flow.Advance(4);
        Check(flow.Phase == EpisodePhase.Playing, "OPの後は自動で観戦が始まる");
        flow.Finish(); flow.Advance(10000);
        Check(flow.Phase == EpisodePhase.Ending, "結末後は明示操作まで待つ");
        flow.Retry(); Check(flow.Phase == EpisodePhase.Playing, "再挑戦はOPを挟まず旅へ戻る");

        string directory = Path.Combine(Path.GetTempPath(), "DotARPG-session-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var library = new PlayerLibrary(Path.Combine(directory, "players.json"));
            var a = library.Players.Single(p => p.Temperament == PlayerTemperament.Reckless);
            var b = library.Players.Single(p => p.Temperament == PlayerTemperament.Careful);
            var source = new Adventure(new World(31, WorldStyle.Japanese), temperament: a.Temperament, playerId: a.Id);
            var game = new GameDefinition("same-game", 31, WorldStyle.Japanese, null, source.Title);
            var saves = new JourneyCheckpointStore(Path.Combine(directory, "journey.json"));
            JourneyCheckpoint Capture(Adventure adventure, EpisodePhase phase) => new(
                JourneyCheckpointStore.SimulationVersion, 31, WorldStyle.Japanese, null, adventure.Temperament,
                adventure.Tick, adventure.Title, phase, phase == EpisodePhase.Opening ? 2.4 : EpisodeFlow.OpeningDuration,
                1, true, adventure.RevivalTicks.ToArray(), adventure.PlayerId, a.Name, game.Id, "run-a");
            var opening = Capture(source, EpisodePhase.Opening);
            Check(saves.Save(opening) && saves.Load() == opening with { RevivalTicks = saves.Load()!.RevivalTicks }, "OPの途中保存を読み出せる");
            for (int i = 0; i < 5000 && !(source.Combat is { Phase: BattlePhase.Windup }); i++) source.Step();
            Check(source.Combat != null && !source.Finished, "戦闘の途中に保存できる");
            var combat = Capture(source, EpisodePhase.Playing);
            Check(saves.Save(combat), "戦闘中の保存を書き込める");
            var restored = JourneyCheckpointStore.Replay(saves.Load()!);
            Compare(source, restored, Check);
            for (int i = 0; i < 300; i++) { source.Step(); restored.Step(); }
            Compare(source, restored, Check);
            File.WriteAllText(saves.FilePath, "broken json");
            Check(saves.Load() is { Phase: EpisodePhase.Opening } && saves.Notice != null, "途中保存が壊れた場合は前の保存へ戻る");
            Check(saves.Save(Capture(source, source.Finished ? EpisodePhase.Ending : EpisodePhase.Playing)), "壊れた保存を退避して再保存できる");
            for (int i = 0; i < 120000 && !source.Finished; i++) source.Step();
            Check(source.Finished && !source.Won && source.DeathCount == 1, "実際の被弾による死亡を一度だけ数える");
            var ending = Capture(source, EpisodePhase.Ending);
            restored = JourneyCheckpointStore.Replay(ending);
            Compare(source, restored, Check);
            var archive = new Chronicle(Path.Combine(directory, "chronicle.json"));
            Check(archive.Remember(source), "死亡した旅も記録できる");
            int experience = source.Experience, explored = source.Exploration.ExploredTiles, deadEnemies = source.World.Creatures.Count(c => !c.Alive);
            Check(source.ReviveAtBase(), "死亡後は拠点で復活できる");
            Check(!source.Finished && source.Health == source.MaxHealth && source.Hero == source.RespawnSite.Position
                && source.Experience == experience && source.Exploration.ExploredTiles == explored
                && source.World.Creatures.Count(c => !c.Alive) == deadEnemies && source.DeathCount == 1,
                "復活は育成・踏破・倒した敵・死亡数を保つ");
            Check(saves.Save(Capture(source, EpisodePhase.Playing)), "復活直後にも保存できる");
            restored = JourneyCheckpointStore.Replay(saves.Load()!);
            Compare(source, restored, Check);
            for (int i = 0; i < 300; i++) { source.Step(); restored.Step(); }
            Compare(source, restored, Check);

            var fresh = new Adventure(new World(game.Seed, game.Style), game.PreviousStory, b.Temperament, b.Id);
            Check(fresh.Title == source.Title && fresh.HeroName == source.HeroName && fresh.World.Tiles.SequenceEqual(source.World.Tiles)
                && fresh.World.Sites.SequenceEqual(source.World.Sites), "違うプレイヤーでも同じ作品・主人公・世界を再現する");
            Check(fresh.Tick == 0 && fresh.Level == 1 && fresh.DeathCount == 0 && fresh.Exploration.Fraction < .01
                && fresh.Inventory["傷薬"] == 2 && fresh.World.Creatures.All(c => c.Alive), "別プレイヤーのプレイは最初からの独立した状態になる");
            Check(fresh.PlayerId != source.PlayerId && fresh.Temperament != source.Temperament, "別プレイヤーには独立した隠し性格がある");
            library.Record("run-a", game, a, source);
            library.Record("run-a", game, a, source);
            library.Record("run-b", game, b, fresh);
            Check(library.Save(), "プレイデータを保存できる");
            var reloaded = new PlayerLibrary(library.FilePath);
            Check(reloaded.Runs.Count == 2 && reloaded.Runs.Single(r => r.Id == "run-a").Deaths == source.DeathCount,
                "同じプレイを二重計上せず、プレイヤー別に死亡数を保存する");
            for (int i = 0; i < 120000 && !fresh.Finished; i++) fresh.Step();
            Check(fresh.Won && archive.Remember(fresh) && new Chronicle(archive.FilePath).Sagas.Single().Survived,
                "同じ作品が再挑戦や別プレイヤーでクリアされたら、死亡記録から完結へ更新できる");
            library.Record("run-b", game, b, fresh); library.Save();
            Check(new PlayerLibrary(library.FilePath).Runs.Single(r => r.Id == "run-b").Status == "クリア", "クリア結果をプレイデータに反映する");
            File.WriteAllText(library.FilePath, "broken json");
            var recovered = new PlayerLibrary(library.FilePath);
            Check(recovered.Players.Count >= 4 && recovered.Save(), "プレイデータもバックアップから復帰して保存できる");
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
        Console.WriteLine($"PASS: {checks} session checks; OP/ending, resume during combat and after revival, independent players, history, and recovery.");
    }

    private static void Compare(Adventure a, Adventure b, Action<bool, string> check)
    {
        check(a.Tick == b.Tick && a.Title == b.Title && a.HeroName == b.HeroName && a.PlayerId == b.PlayerId
            && a.Hero == b.Hero && a.Level == b.Level && a.Experience == b.Experience && a.Health == b.Health && a.Mana == b.Mana
            && a.DeathCount == b.DeathCount && a.Finished == b.Finished && a.Won == b.Won && a.Gold == b.Gold
            && a.Inventory.SequenceEqual(b.Inventory) && a.Trail.SequenceEqual(b.Trail) && a.Logs.SequenceEqual(b.Logs)
            && a.Exploration.ExploredTiles == b.Exploration.ExploredTiles && a.RevivalTicks.SequenceEqual(b.RevivalTicks),
            "途中保存から現在地・能力・持ち物・踏破・死亡・ログを再現する");
        check(a.World.Creatures.Select(c => (c.Position, c.Health)).SequenceEqual(b.World.Creatures.Select(c => (c.Position, c.Health))),
            "敵の位置と残りHPも再現する");
        check(a.Combat?.Hero == b.Combat?.Hero && a.Combat?.Enemy == b.Combat?.Enemy && a.Combat?.Phase == b.Combat?.Phase
            && a.Combat?.PhaseTick == b.Combat?.PhaseTick && a.Combat?.LastEvent == b.Combat?.LastEvent,
            "攻撃予兆や回避を含む戦闘の瞬間も再現する");
    }
}
