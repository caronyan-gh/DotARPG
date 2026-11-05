using DotARPG.Core;
using System.Numerics;

if (args.Contains("--sessions")) { SessionChecks.Run(); return; }

if (args.Contains("--town-smoke"))
{
    void Set(Adventure game, string property, object value) => typeof(Adventure).GetProperty(property)!.SetValue(game, value);
    var game = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Balanced);
    Set(game, nameof(Adventure.Health), game.MaxHealth / 2);
    Set(game, nameof(Adventure.Mana), 0);
    Set(game, nameof(Adventure.Gold), 0);
    game.Step(); game.Step();
    Require(game.Health == game.MaxHealth && game.Mana == game.MaxMana && game.RestVisits == 1,
        "HP半分・MP切れなら街で休み、金欠でも全回復する");
    Point town = game.World.Sites[0].Position;
    var approach = new[] { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) }
        .First(d => game.World.Walkable(new(town.X + d.X * 2, town.Y + d.Y * 2))
            && game.World.Walkable(new(town.X + d.X, town.Y + d.Y)));
    Set(game, nameof(Adventure.Hero), new Point(town.X + approach.X * 2, town.Y + approach.Y * 2));
    Set(game, nameof(Adventure.Health), game.MaxHealth - 1); Set(game, nameof(Adventure.Mana), game.MaxMana - 1);
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    typeof(Adventure).GetField("wait", flags)!.SetValue(game, 0);
    var route = (Queue<Point>)typeof(Adventure).GetField("path", flags)!.GetValue(game)!;
    route.Clear(); route.Enqueue(new(town.X + approach.X, town.Y + approach.Y));
    game.Step();
    Require(game.Health == game.MaxHealth && game.Mana == game.MaxMana && game.RestVisits == 2,
        "休息を目的にせず通りがかった街でもHPとMPを全回復する");
    Require(Adventure.DayAt(4350) == 2, "従来30日だった時点は2日になる");
    Console.WriteLine("PASS: low HP/empty MP, free camp, passing-town recovery, slower calendar.");
    var run = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Reckless);
    int previousVisits = 0, level7Day = 0;
    for (int tick = 0; tick < 60000 && !run.Won; tick++)
    {
        if (run.Finished && (run.DeathCount >= 4 || !run.ReviveAtBase())) break;
        run.Step();
        if (level7Day == 0 && run.Level >= 7) level7Day = run.Day;
        if (run.RestVisits > previousVisits)
        {
            Require(run.Health == run.MaxHealth && run.Mana == run.MaxMana, "実際の旅でも街で全回復する");
            previousVisits = run.RestVisits;
        }
    }
    Console.WriteLine($"Journey: won={run.Won}, Lv{run.Level}, stage={run.Stage}, tick={run.Tick}, midbosses={run.MidbossVictories}, rests={run.RestVisits}, gold={run.Gold}, deaths={run.DeathCount}, Lv7Day={level7Day}, objective={run.Objective}");
    Require(run.Won && run.MidbossVictories == run.World.Guardians.Count, "回復と段階的な育成を挟み、全中ボスとLv22のラスボスを攻略できる");
    return;
}

if (args.Contains("--restore-only"))
{
    var checkpoint = new JourneyCheckpointStore().Load() ?? throw new InvalidOperationException("保存なし");
    var restored = JourneyCheckpointStore.Replay(checkpoint);
    Console.WriteLine($"PASS: previous save restored, version={checkpoint.SimulationVersion}, tick={restored.Tick}, expanded={restored.World.ExpandedProgression}.");
    return;
}

if (args.Contains("--language-smoke"))
{
    GameText.English = true;
    void English(string source)
    {
        string translated = GameText.Text(source);
        Require(!System.Text.RegularExpressions.Regex.IsMatch(translated, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]"),
            "未翻訳: " + source + " => " + translated);
    }
    foreach (var theme in Theme.All)
    {
        foreach (string text in new[] { theme.Label }.Concat(theme.Realms).Concat(theme.Heroes).Concat(theme.Places)
            .Concat(theme.Enemies).Concat(theme.Equipment).Concat(theme.Relics).Concat(theme.Villains).Concat(theme.Threats).Concat(theme.Endings)) English(text);
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
            for (int tier = 0; tier < 4; tier++) English(EquipmentCatalog.Get(theme.Style, slot, tier, "test").Name);
        var game = new Adventure(new World(31, theme.Style));
        English(game.Title + " III"); English(game.HeroName); English(game.Premise); English(game.Objective);
        foreach (var site in game.World.Sites) English(site.Name);
        foreach (var guardian in game.World.Guardians) English(guardian.Name);
        foreach (var log in game.Logs) English(log.Text);
    }
    Require(GameText.Text("山伏・蛍") == "Hotaru the Yamabushi", "生成された主人公の名前と役割を翻訳する");
    var journey = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Reckless);
    for (int tick = 0; tick < 22000 && !journey.Finished; tick++)
    {
        journey.Step();
        English(journey.Objective);
        foreach (var log in journey.Logs.Where(log => log.Tick == journey.Tick)) English(log.Text);
    }
    Require(journey.Finished, "エンディングまでの表示文を確認する");
    English(journey.Epilogue); English(journey.CreateMemory().Summary);
    GameText.English = false;
    Require(GameText.Text("白狐封魔録 III") == "白狐封魔録 III", "日本語への復帰で元の作品名を保つ");
    Console.WriteLine("PASS: all four themes, generated names/titles, equipment and opening logs translate; Japanese names remain intact.");
    return;
}

if (args.Contains("--guardians-smoke"))
{
    var game = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Reckless);
    var boss = game.World.Creatures.Single(c => c.Kind == EnemyKind.Boss);
    game.Step();
    Require(boss.GuardianProtection == game.World.Guardians.Count && boss.Defense > 98 + 4,
        "守護者が残っている間は最上位装備でもラスボスの守りを抜けない");
    int previous = boss.Defense;
    bool searchedUnvisited = false;
    for (int tick = 0; tick < 35000 && !game.Won; tick++)
    {
        if (game.Finished && (game.DeathCount > 3 || !game.ReviveAtBase())) break;
        game.Step();
        searchedUnvisited |= game.Objective.Contains("まだ訪れていない");
        if (game.MidbossVictories > 0) Require(boss.Defense < previous, "中ボスを倒すとラスボスの守りが弱まる");
    }
    Console.WriteLine($"Journey: won={game.Won}, deaths={game.DeathCount}, midbosses={game.MidbossVictories}/{game.World.Guardians.Count}, Lv{game.Level}, tick={game.Tick}, searched={searchedUnvisited}, objective={game.Objective}");
    Require(game.Won && game.World.Guardians.All(g => !g.Alive), "中ボスを全員倒してからラスボスを倒し、クリアする");
    Require(Enum.GetValues<GearSlot>().All(slot => game.Equipment[slot].Tier == 3), "各中ボスの装備を揃えて決戦に臨む");
    Require(searchedUnvisited, "歯が立たないと気づいた後に未踏の土地を探る");
    var scout = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Careful);
    bool warned = false, attackedFirst = false;
    for (int tick = 0; tick < 22000 && !scout.Finished && !warned && !attackedFirst; tick++)
    {
        scout.Step();
        warned = scout.Logs.Any(log => log.Text.Contains("近くの") && log.Text.Contains("未踏の土地"));
        attackedFirst = scout.Combat?.Opponent.Kind == EnemyKind.Boss;
    }
    Require(warned && !attackedFirst, "慎重派は周囲の雑魚から察してラスボスと戦う前に探索へ戻る");
    Console.WriteLine("PASS: all guardians defeated, full equipment collected, final boss cleared.");
    return;
}

if (args.Contains("--lethality-smoke") || args.Contains("--damage-only"))
{
    var overlevel = new Creature(new Point(0, 0), "Lv5", EnemyKind.Beast) { Level = 5 };
    var dangerous = new Creature(new Point(0, 0), "Lv7", EnemyKind.Beast) { Level = 7 };
    var lethal = new Creature(new Point(0, 0), "Lv8", EnemyKind.Beast) { Level = 8 };
    var naked = new Creature(new Point(0, 0), "Lv7", EnemyKind.Beast) { Level = 7 };
    var boss = new Creature(new Point(0, 0), "boss", EnemyKind.Boss) { Level = 14 };
    Require(CombatRules.IncomingDamage(overlevel, 1, 4, -2) >= 30
        && CombatRules.IncomingDamage(overlevel, 1, 4, 3) <= 45, "4レベル差は一撃で約半分、逃げる相手");
    Require(CombatRules.IncomingDamage(dangerous, 1, 4, -2) >= 55
        && CombatRules.IncomingDamage(dangerous, 1, 4, 3) <= 65, "6レベル差の一撃はHP75の約8割");
    Require(CombatRules.IncomingDamage(lethal, 1, 4, -2) >= 75, "7レベル差からは初期装備では一撃死");
    Require(CombatRules.IncomingDamage(naked, 7, 8, -2) >= 111, "適正レベルでも防具不足なら一撃で倒れる");
    Require(CombatRules.IncomingDamage(boss, 14, 29, -2) >= 153, "ボス用防具が足りないと一撃で倒れる");
    Require(CombatRules.IncomingDamage(boss, 14, 42, 3) < 153, "防具を揃えるとボスの一撃に耐える");
    var gear = new EquipmentLoadout(WorldStyle.Japanese);
    var armor = EquipmentCatalog.Get(WorldStyle.Japanese, GearSlot.Armor, 3, "midboss");
    Require(!gear.Equip(armor, 1) && gear.Equip(armor, armor.RequiredLevel), "上位防具は必要レベルまで装備できない");
    int EscapeTrials(int gap)
    {
        int escaped = 0;
        for (int seed = 0; seed < 100; seed++)
        {
            var enemy = new Creature(new Point(0, 0), "pursuer") { Level = 1 + gap, Health = 1000, MaxHealth = 1000 };
            var battle = new Battle(enemy, new Random(seed), heroLevel: 1, ambushed: true);
            int health = 75;
            for (int tick = 0; tick < 500 && health > 0 && !battle.Escaped; tick++)
                if (battle.Step(7, false, true) == BattleEvent.HeroHurt) health -= CombatRules.IncomingDamage(enemy, 1, 4);
            if (health > 0 && battle.Escaped) escaped++;
        }
        return escaped;
    }
    int escape4 = EscapeTrials(4), escape6 = EscapeTrials(6);
    int wins2 = 0;
    for (int seed = 0; seed < 32; seed++)
    {
        var enemy = new Creature(new Point(0, 0), "challenger") { Level = 3, Health = 36, MaxHealth = 36 };
        var battle = new Battle(enemy, new Random(seed), PlayerPolicy.For(PlayerTemperament.Reckless), heroLevel: 1);
        int health = 75;
        for (int tick = 0; tick < 1000 && health > 0 && enemy.Alive; tick++)
            if (battle.Step(7, false) == BattleEvent.HeroHurt) health -= CombatRules.IncomingDamage(enemy, 1, 4);
        if (health > 0 && !enemy.Alive) wins2++;
    }
    Require(wins2 > 0, "2レベル差は接近・回避・反撃で勝てる");
    Console.WriteLine($"PASS: HP75, gap+4/+6/+7 damage = {CombatRules.IncomingDamage(overlevel, 1, 4)}/{CombatRules.IncomingDamage(dangerous, 1, 4)}/{CombatRules.IncomingDamage(lethal, 1, 4)}; escape +4/+6 = {escape4}/{escape6} out of 100; gear level requirement enforced.");
    Console.WriteLine($"PASS: won {wins2}/32 actual battles against +2 levels.");
    Require(escape6 > 0 && escape6 < escape4, "6レベル差からの離脱は4レベル差より難しく、幸運なら逃げられる");
    if (args.Contains("--damage-only")) return;

    var original = new Adventure(new World(31, WorldStyle.Japanese, false), temperament: PlayerTemperament.Careful)
        { CombatRulesUpgradeTick = int.MaxValue, TownRulesUpgradeTick = int.MaxValue };
    for (int i = 0; i < 1200 && !original.Finished; i++) original.Step();
    var oldSave = new JourneyCheckpoint(2, 31, WorldStyle.Japanese, null, original.Temperament,
        original.Tick, original.Title, original.Finished ? EpisodePhase.Ending : EpisodePhase.Playing,
        EpisodeFlow.OpeningDuration, 0, false);
    var upgraded = JourneyCheckpointStore.Replay(oldSave);
    Require(upgraded.Health == original.Health && upgraded.Experience == original.Experience && upgraded.Hero == original.Hero,
        "前の版の途中保存は当時の状態で復元する");
    for (int i = 0; i < 300 && !upgraded.Finished; i++) upgraded.Step();
    var newSave = oldSave with { SimulationVersion = JourneyCheckpointStore.SimulationVersion, Tick = upgraded.Tick,
        Phase = upgraded.Finished ? EpisodePhase.Ending : EpisodePhase.Playing, CombatRulesUpgradeTick = upgraded.CombatRulesUpgradeTick,
        TownRulesUpgradeTick = upgraded.TownRulesUpgradeTick, ExpandedProgression = upgraded.World.ExpandedProgression };
    var restored = JourneyCheckpointStore.Replay(newSave);
    Require(restored.Health == upgraded.Health && restored.Experience == upgraded.Experience && restored.Hero == upgraded.Hero,
        "新バランスに移行した保存も正しく復元する");
    Console.WriteLine("PASS: saved journey survives balance upgrade and subsequent save/replay.");
    var game = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Reckless);
    for (int tick = 0; tick < 18000 && !game.Finished; tick++) game.Step();
    Console.WriteLine($"Journey: finished={game.Finished}, won={game.Won}, Lv{game.Level}, stage={game.Stage}, HP={game.Health}/{game.MaxHealth}, DEF={game.DefensePower}, midbosses={game.MidbossVictories}, damage={game.DamageTaken}, dodges={game.Dodges}, objective={game.Objective}");
    Require(game.Finished && (!game.Won || game.Equipment[GearSlot.Weapon].Tier == 3 && game.DefensePower >= 40),
        "冒険をクリアするにはボス用の武器と防具を揃える");
    var unprepared = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Reckless);
    while (unprepared.World.InTown(unprepared.Hero)) unprepared.Step();
    unprepared.World.Creatures.Add(new Creature(unprepared.Hero, "危険地域の敵", EnemyKind.Beast)
        { Level = 14, Health = 1000, MaxHealth = 1000, Alerted = true });
    for (int tick = 0; tick < 600 && !unprepared.Finished; tick++) unprepared.Step();
    Require(unprepared.Finished && !unprepared.Won && unprepared.DeathCount == 1, "格上に襲われると実際の被弾で死亡する");
    Require(unprepared.ReviveAtBase() && !unprepared.Finished && unprepared.Health == unprepared.MaxHealth, "死んだ後は拠点から再挑戦できる");
    Console.WriteLine($"PASS: underleveled journey died from an attack and revived at Lv{unprepared.Level}.");
    return;
}

if (args.Contains("--combat-smoke"))
{
    var boss = new Creature(new Point(0, 0), "boss", EnemyKind.Boss) { Level = 14 };
    var ordinary = new EquipmentLoadout(WorldStyle.Chinese);
    foreach (GearSlot slot in Enum.GetValues<GearSlot>()) ordinary.Equip(EquipmentCatalog.Get(WorldStyle.Chinese, slot, 2, "shop"));
    int normalAttack = 3 + 12 * 2 + ordinary.Attack;
    Require(CombatRules.Damage(normalAttack, boss.Defense, 2) == 1, "通常武器ではボスのDEFを突破できない");
    ordinary.Equip(EquipmentCatalog.Get(WorldStyle.Chinese, GearSlot.Weapon, 3, "midboss"));
    Require(CombatRules.Damage(3 + 12 * 2 + ordinary.Attack, boss.Defense) > 15, "中ボスの専用武器でボスのDEFを突破できる");
    Console.WriteLine("PASS: ordinary weapon is ineffective; midboss weapon breaks boss defense.");
    foreach (var temperament in new[] { PlayerTemperament.Careful, PlayerTemperament.Reckless })
    {
        var game = new Adventure(new World(31, WorldStyle.Japanese), temperament: temperament);
        Require(game.World.Creatures.Where(c => c.Kind == EnemyKind.Boss).Single().Level > 10, "ボスは高レベルで配置される");
        for (int tick = 0; tick < 35000 && !game.Finished; tick++) game.Step();
        if (game.Won) Require(game.Equipment[GearSlot.Weapon].Tier == 3 && game.MidbossVictories > 0,
            "中ボスの装備を集めてからクリアする");
        Require(game.EnemyInitiatedBattles > 0, "敵の側から襲ってくる");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            temperament, game.Finished, game.Won, game.Level, game.Stage, game.Tick, game.MidbossVictories,
            game.EnemyInitiatedBattles, game.AttackPower, game.DefensePower, game.Weapon,
            Objective = game.Objective, Tail = game.Logs.TakeLast(4).Select(l => l.Text).ToArray()
        }));
    }
    return;
}

if (args.Contains("--balance"))
{
    var rows = new List<object>();
    int unfinished = 0;
    foreach (PlayerTemperament temperament in Enum.GetValues<PlayerTemperament>().Where(t => !args.Contains("--meticulous") || t == PlayerTemperament.Meticulous))
    for (int seed = 0; seed < 8; seed++)
    {
        var game = new Adventure(new World(seed * 7907 + 31, (WorldStyle)(seed % 4)), temperament: temperament);
        for (int tick = 0; tick < 60000 && !game.Finished; tick++) game.Step();
        if (!game.Finished) unfinished++;
        var row = new { temperament, seed, game.Finished, game.Won, game.Level, game.Stage, game.Tick, game.TrainingVictories,
            game.RestVisits, game.Retreats, game.DeathCause, game.SurveyDetours, Explored = game.Exploration.Fraction, Departures = game.DepartureLevels.ToArray() };
        rows.Add(row);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(row));
    }
    Directory.CreateDirectory(".tmp");
    File.WriteAllText(".tmp/balance.json", System.Text.Json.JsonSerializer.Serialize(rows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    if (unfinished > 0) Environment.ExitCode = 1;
    return;
}

int worlds = 0, won = 0, lost = 0, ticks = 0;
int dodges = 0, spells = 0, consumed = 0, guardians = 0, trained = 0, returns = 0;
Adventure? survivor = null;
var plots = new HashSet<StoryKind>();
var layouts = new HashSet<string>();
foreach (WorldStyle style in Enum.GetValues<WorldStyle>())
{
    for (int seed = 0; seed < 12; seed++)
    {
        var world = new World(seed * 7907 + 31, style);
        Require(world.Sites.Count == World.SiteCount && world.Sites.Count > 8, "広い世界に多数の拠点が生成される");
        Require(world.Guardians.Count >= 4 && world.Creatures.Count > 700, "世界に敵と中ボスが配置される");
        Require(world.Creatures.Select(c => c.Kind).Distinct().Count() >= 5, "複数の種類の敵が共存する");
        Require(world.RegionLevel(world.Sites[0].Position) == 1
            && world.RegionLevel(world.Sites.MaxBy(s => s.Position.Distance(world.Sites[0].Position))!.Position) >= 10,
            "開始地点は弱い敵、遠い地域ほど強い敵になる");
        foreach (var site in world.Sites)
        {
            Require(world.Walkable(site.Position), "拠点は上陸できる場所にある");
            var path = world.FindPath(world.Sites[0].Position, site.Position);
            Require(site == world.Sites[0] || path.Count > 0, "すべての拠点に歩いて行ける");
            var previous = world.Sites[0].Position;
            foreach (var point in path)
            {
                Require(world.Walkable(point) && point.Distance(previous) == 1, "経路が海や離れたタイルを飛び越えない");
                previous = point;
            }
        }
        var game = new Adventure(world);
        Require(world.Creatures.Count(c => c.Kind is EnemyKind.Boss or EnemyKind.Midboss) >= 5,
            "ボスと中ボスは旅の開始時点で名前付きの実体として配置される");
        plots.Add(game.Story);
        int explored = game.Exploration.ExploredTiles;
        for (int step = 0; step < 60000 && !game.Finished; step++)
        {
            game.Step();
            Require(world.Walkable(game.Hero), "主人公が陸地から外れない");
            Require(game.Health >= 0 && game.Health <= game.MaxHealth, "HPが最大値の範囲内にある");
            Require(game.Mana >= 0 && game.Mana <= game.MaxMana, "MPが最大値の範囲内にある");
            Require(game.Inventory.Values.All(n => n >= 0), "アイテムが不足したまま使用されない");
            Require(game.Exploration.ExploredTiles >= explored && game.Exploration.Visibility(game.Hero) == 255,
                "訪れた場所は開き、離れても再び隠れない");
            explored = game.Exploration.ExploredTiles;
        }
        Require(game.Finished, "冒険が立ち往生せず結末を迎える");
        Require(game.DepartureLevels.All(level => level >= 2), "育つまでは次の地域へ出発しない");
        if (game.Won)
        {
            won++; survivor ??= game;
            Require(game.Stage == 2 && game.MidbossVictories > 0, "中ボスと目的を突破してから勝利する");
            Require(game.Inventory.Count >= 3, "取得した重要アイテムが所持品に入る");
            if (game.Temperament == PlayerTemperament.Meticulous)
                Require(game.SurveyDetours > 0 && game.Exploration.Fraction == 1, "几帳面なプレイヤーは陸地を踏破してから最後のボスを倒す");
        }
        else lost++;
        if (!game.Won) Require(game.Health == 0 && game.DeathCause != null, "敗北は実際の被弾とHP切れで生じる");
        trained += game.TrainingVictories;
        returns += game.RestVisits;
        dodges += game.Dodges; spells += game.MagicCasts; consumed += game.ItemsUsed; guardians += game.MidbossVictories;
        ticks += game.Tick;
        worlds++;
        layouts.Add(string.Join(';', world.Sites.Select(s => s.Position.ToString())));
    }
    Console.WriteLine($"{Theme.Get(style).Label}: 12 worlds completed");
}
var first = new World(20261105, WorldStyle.Chinese);
var second = new World(20261105, WorldStyle.Chinese);
Require(first.Tiles.SequenceEqual(second.Tiles) && first.Sites.SequenceEqual(second.Sites), "同じ種から同じ世界を再現できる");
var a = new Adventure(first);
var b = new Adventure(second);
for (int i = 0; i < 1000; i++) { a.Step(); b.Step(); }
Require(a.Hero == b.Hero && a.Logs.SequenceEqual(b.Logs), "同じ種から冒険も再現できる");
Require(layouts.Count == 12, "種ごとに地形と拠点配置が変わる");
Require(plots.Count == 3, "3種類の筋書きが生成される");
Require(survivor != null, "続編を作れる生存者がいる");
Require(won > 0, "目的を達成できる冒険がある");
Require(lost > 0 && trained > 0 && returns > 0, "死亡・狩りによる鍛錬・帰還と休息が実際に発生する");
Require(dodges > 0 && spells > 0 && consumed > 0 && guardians > 0, "回避・術・アイテムの消費・中ボス撃破が実際に起きる");
var memory = survivor!.CreateMemory();
var sequel = new Adventure(new World(9876, memory.Style), memory);
Require(sequel.Part == 2 && sequel.Title.EndsWith(" II"), "続編にIIが付く");
Require(sequel.HeroName == survivor.HeroName && sequel.SeriesId == survivor.SeriesId, "同じ主人公と作品を引き継ぐ");
Require(sequel.Temperament == survivor.Temperament, "続編でも隠しプレイヤー性格を引き継ぐ");
Require(sequel.Logs.Any(l => l.Text.Contains(memory.Summary)), "前作の出来事を回想する");
Require(Chronicle.Roman(3) == "III" && Chronicle.Roman(4) == "IV" && Chronicle.Roman(12) == "XII", "続編の作品番号を表せる");
string archivePath = Path.Combine(Path.GetTempPath(), "DotARPG-check-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    var archive = new Chronicle(archivePath);
    Require(!archive.Remember(sequel) && !File.Exists(archivePath), "未完の旅を完結済みとして記録しない");
    Require(archive.Remember(survivor), "完結した冒険を保存できる");
    archive = new Chronicle(archivePath);
    Require(archive.Sagas.Single().HeroName == survivor.HeroName, "再起動しても主人公を覚えている");
    Require(archive.Sagas.Single().Temperament == survivor.Temperament, "隠し性格も保存から復元される");
    int picked = 0;
    var selection = new Random(55);
    for (int i = 0; i < 1000; i++) if (archive.ChooseSequel(selection) != null) picked++;
    Require(picked is > 150 and < 250, "基本は新作で、ときどき続編が混じる");
    Require(archive.ChooseSequel(new Random(1), (WorldStyle)(((int)memory.Style + 1) % 4)) == null, "世界観を指定したときは別作風の続編が割り込まない");
    for (int i = 0; i < 60000 && !sequel.Finished; i++) sequel.Step();
    Require(sequel.Finished && archive.Remember(sequel), "続編も完結して保存できる");
    archive = new Chronicle(archivePath);
    Require(archive.Sagas.Single().Part == 2, "シリーズを二重登録せずIIの記憶へ更新する");
    var legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(archivePath))!.AsArray();
    legacy[0]!.AsObject().Remove("Temperament");
    File.WriteAllText(archivePath + ".legacy", legacy.ToJsonString());
    Require(new Chronicle(archivePath + ".legacy").Sagas.Single().Temperament == PlayerTemperament.Balanced,
        "性格が無い旧版の記憶も引き続き読める");
    File.WriteAllText(archivePath, "broken json");
    var recovered = new Chronicle(archivePath);
    Require(recovered.Sagas.Single().Part == 1 && recovered.Error != null, "記憶の破損時にバックアップへ戻れる");
}
finally
{
    foreach (string suffix in new[] { "", ".bak", ".unreadable", ".legacy" })
        if (File.Exists(archivePath + suffix)) File.Delete(archivePath + suffix);
}
Console.WriteLine($"PASS: {worlds} worlds, {won} victories, {lost} deaths, {ticks} simulation steps; routes, story completion, and determinism verified.");
Console.WriteLine("PASS: titles, II/III numbering, hero continuity, previous-story callbacks, 20% sequel selection, persistent memory, and recovery verified.");
Console.WriteLine($"PASS: {dodges} geometric dodges, {spells} spells, {consumed} items consumed, {guardians} midboss victories; HP/MP and inventory stayed valid.");
Console.WriteLine($"PASS: {trained} training victories, {returns} town rest stops; regional progression, mortality, and hidden-player continuity verified.");

int actualDodges = 0, actualHits = 0, movement = 0;
for (int seed = 0; seed < 40; seed++)
{
    var enemy = new Creature(new Point(0, 0), "test boss", EnemyKind.Boss) { Health = 500, MaxHealth = 500 };
    var battle = new Battle(enemy, new Random(seed));
    for (int step = 0; step < 3000 && enemy.Alive; step++)
    {
        var previousHero = battle.Hero;
        int previousHealth = enemy.Health;
        BattleEvent action = battle.Step(12, false);
        if (previousHero != battle.Hero) movement++;
        if (action == BattleEvent.Dodged) { actualDodges++; Require(!battle.Threatens(battle.Hero), "回避成功は実際に攻撃範囲を抜けた場合だけ"); }
        if (action == BattleEvent.HeroHurt) { actualHits++; Require(battle.Threatens(battle.Hero), "被弾は実際に攻撃範囲に残った場合だけ"); }
        if (enemy.Health < previousHealth) Require(Vector2.Distance(battle.Hero, battle.Enemy) < 2.8f, "近接攻撃は間合いに入るまで当たらない");
    }
    Require(!enemy.Alive, "接近と回避を繰り返して戦闘が完結する");
}
Require(actualDodges > 0 && actualHits > 0 && movement > 100, "戦闘は位置移動により回避と命中の両方が生じる");
Console.WriteLine($"PASS: real-time arena collision checks; {actualDodges} misses, {actualHits} hits, {movement} movement steps.");

var careful = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Careful);
var reckless = new Adventure(new World(31, WorldStyle.Japanese), temperament: PlayerTemperament.Reckless);
foreach (var game in new[] { careful, reckless })
    for (int tick = 0; tick < 6000 && !game.Finished && game.DepartureLevels.Count == 0; tick++) game.Step();
Require(careful.DepartureLevels.Count > 0 && reckless.DepartureLevels.Count > 0, "両方の性格が準備後に出発できる");
Require(careful.DepartureLevels[0] > reckless.DepartureLevels[0] && careful.TrainingVictories > reckless.TrainingVictories,
    "同じ世界で慎重なプレイヤーは多く狩り、強く育ってから出発する");
Require(Adventure.ExperienceForLevel(8) - Adventure.ExperienceForLevel(7)
    > Adventure.ExperienceForLevel(3) - Adventure.ExperienceForLevel(2), "高レベルほど育つのに経験値が必要になる");
Console.WriteLine("PASS: same-world players make different preparation decisions; legacy saves remain compatible.");

var knowledge = new Exploration(first);
Point origin = first.Sites[0].Position;
Require(knowledge.Fraction == 0, "新しい世界は未踏の状態で始まる");
knowledge.Reveal(origin);
Require(knowledge.Visibility(origin) == 255 && knowledge.Fraction is > 0 and < .1, "開始地点の周囲だけを開く");
int initiallyExplored = knowledge.ExploredTiles;
knowledge.Reveal(origin);
Require(knowledge.ExploredTiles == initiallyExplored, "再訪で踏破率を二重計上しない");
Require(knowledge.ConsumeDirtyArea() != null && knowledge.ConsumeDirtyArea() == null, "更新した範囲だけを描画へ渡す");
for (int leg = 0; leg < 2000; leg++)
{
    Point? target = knowledge.NextSurveyPoint(origin, 7);
    if (target == null) break;
    Require(first.Walkable(target.Value), "几帳面なプレイヤーが選ぶ寄り道先は徒歩で行ける");
    knowledge.Reveal(target.Value);
    origin = target.Value;
}
Require(knowledge.Fraction == 1 && knowledge.NextSurveyPoint(origin, 7) == null,
    "未踏地の探索は取りこぼしや無限ループなく、全踏破で終わる");
Console.WriteLine("PASS: permanent exploration, dirty-area updates, reachable survey targets, and complete land coverage verified.");

static void Require(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}
