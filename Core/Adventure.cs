namespace DotARPG.Core;

public enum LogTone { Quiet, Normal, Discovery, Danger, Ending }
public sealed record LogEntry(int Tick, string Text, LogTone Tone);
public enum StoryKind { Restore, Rescue, Seal }

public sealed class Adventure
{
    public World World { get; }
    public Exploration Exploration { get; }
    public Point Hero { get; private set; }
    public string HeroName { get; }
    public string SeriesId { get; }
    public string BaseTitle { get; }
    public int Part { get; }
    public string Title => BaseTitle + (Part > 1 ? " " + Chronicle.Roman(Part) : "");
    public string Villain { get; }
    public string Premise { get; }
    public string Objective { get; private set; } = "";
    public StoryKind Story { get; }
    public int Tick { get; private set; }
    public const int TicksPerDay = 2400;
    public static int DayAt(int tick) => 1 + tick / TicksPerDay;
    public int Day => DayAt(Tick);
    public int Health { get; private set; } = 75;
    public int Mana { get; private set; } = 24;
    public int Experience { get; private set; }
    public int Level
    {
        get
        {
            int level = 1;
            while (Experience >= ExperienceForLevel(level + 1)) level++;
            return level;
        }
    }
    public static int ExperienceForLevel(int level) => 35 * (level - 1) * level / 2 + 15 * (level - 1);
    public int ExperienceInLevel => Experience - ExperienceForLevel(Level);
    public int ExperienceToNextLevel => ExperienceForLevel(Level + 1) - ExperienceForLevel(Level);
    public int MaxHealth => 75 + (Level - 1) * 6;
    public int MaxMana => 24 + (Level - 1) * 3;
    public EquipmentLoadout Equipment { get; }
    public int AttackPower => 3 + Math.Min(Level, World.ExpandedProgression ? 22 : 12) * 2 + Equipment.Attack;
    public int DefensePower => 2 + Math.Min(Level, World.ExpandedProgression ? 22 : 12) / 2 + Equipment.Defense;
    public PlayerTemperament Temperament { get; }
    public string? PlayerId { get; }
    public int DeathCount { get; private set; }
    // Earlier saves replay their original combat rules up to this tick, then use the new balance.
    public int CombatRulesUpgradeTick { get; internal set; }
    public int TownRulesUpgradeTick { get; internal set; }
    public IReadOnlyList<int> RevivalTicks => revivalTicks;
    public Site RespawnSite { get; private set; }
    public bool Training { get; private set; }
    public bool Surveying { get; private set; }
    public int SurveyDetours { get; private set; }
    public int TrainingVictories { get; private set; }
    public int Retreats { get; private set; }
    public int RestVisits { get; private set; }
    public int Gold { get; private set; } = 12;
    public string Region => World.RegionName(Hero);
    public IReadOnlyList<int> DepartureLevels => departureLevels;
    public string Weapon => Equipment[GearSlot.Weapon].Name;
    public string Armor => Equipment[GearSlot.Armor].Name;
    public string Helmet => Equipment[GearSlot.Helmet].Name;
    public string Gloves => Equipment[GearSlot.Gloves].Name;
    public string Boots => Equipment[GearSlot.Boots].Name;
    public string Charm => Equipment[GearSlot.Charm].Name;
    public IReadOnlyDictionary<string, int> Inventory => inventory;
    public Battle? Combat { get; private set; }
    public int Dodges { get; private set; }
    public int DamageTaken { get; private set; }
    public int MagicCasts { get; private set; }
    public int ItemsUsed { get; private set; }
    public int MidbossVictories { get; private set; }
    public int EnemyInitiatedBattles { get; private set; }
    public string Status => Finished ? Won ? "旅を終えた" : "倒れた" : Combat is { } fight
        ? fight.Retreating ? "離脱中" : fight.Dashing ? "回避中" : fight.Phase == BattlePhase.Windup ? "攻撃を警戒" : "交戦中"
        : resting ? "宿へ撤退中" : World.Pursuers > 0 ? "敵に追われている" : supplying ? "装備を調達" : Training ? "鍛錬中" : Surveying ? "未踏地を探索" : wait > 0 ? "探索中" : "移動中";
    public int Stage { get; private set; }
    public int Victories { get; private set; }
    public bool Finished { get; private set; }
    public bool Won { get; private set; }
    public int FinishedAt { get; private set; }
    public string? DeathCause { get; private set; }
    public string Epilogue { get; private set; } = "";
    public List<LogEntry> Logs { get; } = [];
    public Queue<Point> Trail { get; } = new();
    public IReadOnlyList<Site> Destinations => destinations;
    private readonly Site[] destinations;
    private readonly Queue<Point> path = new();
    private int wait;
    private int lastEncounter = -100;
    private int nextFlavor;
    private Creature? opponent;
    private bool resting;
    private bool boss;
    private readonly Random random;
    private readonly string relic;
    private readonly Dictionary<string, int> inventory = new() { ["傷薬"] = 2, ["霊薬"] = 1 };
    private readonly Creature finalBoss;
    private readonly PlayerPolicy policy;
    private readonly List<int> departureLevels = [];
    private Creature? huntingTarget;
    private int preparationLevel;
    private int extraPreparation;
    private int itemCooldown;
    private int currentBand;
    private bool announcedTraining;
    private readonly List<int> revivalTicks = [];
    private bool supplying;
    private Creature? equipmentGuardian;
    private bool finalBossDefeated;
    private readonly List<GearItem> carriedEquipment = [];
    private bool bossPreparationNeeded;
    private int ineffectiveBossHits;

    public Adventure(World world, SagaMemory? previous = null, PlayerTemperament? temperament = null, string? playerId = null)
    {
        World = world;
        Equipment = new EquipmentLoadout(world.Theme.Style);
        Exploration = new Exploration(world);
        random = world.Random;
        PlayerTemperament rolled = (PlayerTemperament)random.Next(Enum.GetValues<PlayerTemperament>().Length);
        Temperament = temperament ?? previous?.Temperament ?? rolled;
        PlayerId = playerId ?? previous?.PlayerId;
        policy = PlayerPolicy.For(Temperament);
        Hero = world.Sites[0].Position;
        RespawnSite = world.Sites[0];
        Exploration.Reveal(Hero);
        Theme t = world.Theme;
        string role = t.Heroes[random.Next(t.Heroes.Length)];
        string[] names = t.Style switch
        {
            WorldStyle.Japanese => ["蓮", "鈴", "楓", "朔", "千景", "蛍"],
            WorldStyle.Western => ["リオ", "エリン", "アッシュ", "セラ", "ノア", "ルカ"],
            WorldStyle.Chinese => ["青蘭", "白玲", "天佑", "飛燕", "子龍", "月華"],
            _ => ["ラシード", "ライラ", "サミル", "ナディア", "ファリド", "ヌーラ"]
        };
        string hero = role + "・" + names[random.Next(names.Length)];
        if (previous is not null && (previous.Style != t.Style || !previous.Survived))
            throw new ArgumentException("続編は同じ世界観で生き残った主人公を引き継ぎます。", nameof(previous));
        HeroName = previous?.HeroName ?? hero;
        Villain = t.Villains[random.Next(t.Villains.Length)];
        relic = t.Relics[random.Next(t.Relics.Length)];
        Story = (StoryKind)random.Next(3);
        string[] prefixes = t.Style switch
        {
            WorldStyle.Japanese => ["朧月", "八雲", "常夜", "白狐", "花霞"],
            WorldStyle.Western => ["灰冠", "暁光", "霧影", "星霜", "銀月"],
            WorldStyle.Chinese => ["青嵐", "雲夢", "玄天", "白雲", "龍泉"],
            _ => ["砂海", "琥珀", "千夜", "瑠璃", "流星"]
        };
        string[] endings = t.Style switch
        {
            WorldStyle.Japanese => ["秘宝譚", "帰郷譚", "封魔録"],
            WorldStyle.Western => ["の聖遺物", "の旅人", "の誓約"],
            WorldStyle.Chinese => ["剣譜", "侠客伝", "封妖記"],
            _ => ["の星詠み", "の隊商", "の魔神譚"]
        };
        string title = prefixes[random.Next(prefixes.Length)] + endings[(int)Story];
        SeriesId = previous?.Id ?? $"{t.Style}-{world.Seed:X8}";
        BaseTitle = previous?.BaseTitle ?? title;
        Part = previous?.Part + 1 ?? 1;
        Premise = t.Threats[random.Next(t.Threats.Length)] + "。";
        var unguarded = world.Sites.Where(s => !s.IsTown && world.Guardians.All(g => g.Position != s.Position))
            .OrderBy(s => s.Position.Distance(Hero)).ToArray();
        int middleDistance = unguarded[^1].Position.Distance(Hero) / 2;
        var guarded = world.ExpandedProgression ? world.Guardians.MinBy(g => g.Level)!
            : world.Guardians.MinBy(g => Math.Abs(g.Position.Distance(Hero) - middleDistance))!;
        guarded.RewardSlot = GearSlot.Weapon;
        GearSlot[] otherRewards = [GearSlot.Armor, GearSlot.Helmet, GearSlot.Gloves, GearSlot.Boots, GearSlot.Charm];
        int rewardIndex = 0;
        foreach (var other in world.Guardians.Where(g => g != guarded)) other.RewardSlot = otherRewards[rewardIndex++ % otherRewards.Length];
        destinations = [unguarded[0], world.Sites.First(s => s.Position == guarded.Position), unguarded[^1]];
        int bossLevel = world.ExpandedProgression ? 22 : 14;
        int bossHealth = 320 + bossLevel * 18;
        finalBoss = new Creature(destinations[2].Position, Villain, EnemyKind.Boss) { Level = bossLevel, Health = bossHealth, MaxHealth = bossHealth };
        world.Creatures.Add(finalBoss);
        world.EstablishBossDomain(finalBoss);
        Add($"『{Title}』", LogTone.Discovery);
        if (previous != null)
        {
            Add($"かつて{previous.WorldName}を旅した、{HeroName}。", LogTone.Discovery);
            Add($"前の旅では、{previous.Summary}", LogTone.Quiet);
        }
        Add($"{world.Name} ―― {Premise}", LogTone.Discovery);
        Add($"{HeroName}が{world.Sites[0].Name}を発った。");
        Add(Story switch
        {
            StoryKind.Restore => $"失われた{relic}を探す旅が始まる。",
            StoryKind.Rescue => $"{Villain}に連れ去られた人々を探す。",
            _ => $"{Villain}を封じる術を求めて。"
        });
        nextFlavor = 26;
        SetDestination();
    }

    public void Step()
    {
        Tick++;
        if (Finished) return;
        if (Tick > CombatRulesUpgradeTick) finalBoss.GuardianProtection = World.Guardians.Count(g => g.Alive);
        if (itemCooldown > 0) itemCooldown--;
        World.MoveCreatures(Hero, opponent, Tick);
        if (opponent == null && Tick - lastEncounter > 8 && !World.InTown(Hero))
        {
            Creature? attacker = World.Creatures.Where(c => c.Alive && c.Alerted && c.Position.Distance(Hero) <= 2)
                .MinBy(c => c.Position.Distance(Hero));
            if (attacker != null) { StartFight(attacker, true); return; }
        }
        if (wait > 0) { wait--; return; }
        if (opponent != null) { Fight(); return; }
        if (Tick > TownRulesUpgradeTick && !resting && NeedsRest())
        {
            ReturnToTown();
            return;
        }
        if (!resting && Tick > CombatRulesUpgradeTick && Stage >= 2 && !bossPreparationNeeded
            && Temperament is PlayerTemperament.Careful or PlayerTemperament.Meticulous
            && finalBoss.Alive && finalBoss.GuardianProtection > 0
            && (Tick > TownRulesUpgradeTick || Hero.Distance(finalBoss.Home) <= 65))
        {
            Creature? warning = World.Creatures.Where(c => c.Alive && c.Kind is not EnemyKind.Boss and not EnemyKind.Midboss
                && c.Position.Distance(Hero) <= 28).MaxBy(c => c.Level);
            if (warning != null && (Tick > TownRulesUpgradeTick && warning.Level >= Level + 3 || Hero.Distance(finalBoss.Home) <= 65
                && (warning.Level >= Level - 1 || CombatRules.IncomingDamage(warning, Level, DefensePower) >= MaxHealth * .15)))
            {
                bossPreparationNeeded = true;
                Add($"近くの{warning.Name}はLv.{warning.Level}。この先はまだ危険そうだ。未踏の土地から調べよう。", LogTone.Quiet);
                SetDestination();
                return;
            }
        }
        if (resting && path.Count == 0)
        {
            RestInTown();
            resting = false;
            SetDestination();
            return;
        }
        if (path.Count == 0)
        {
            if (supplying) { supplying = false; BuyEquipment(World.MarketTier(Hero)); SetDestination(); }
            else if (equipmentGuardian is { Alive: true } guardian)
            {
                if (Hero.Distance(guardian.Position) <= 2) StartFight(guardian);
                else Route(guardian.Position);
            }
            else if (Training) ReachHunt();
            else if (Surveying) SetDestination();
            else Arrive();
            return;
        }
        Trail.Enqueue(Hero);
        if (Trail.Count > 38) Trail.Dequeue();
        Point previousHero = Hero;
        Hero = path.Dequeue();
        Exploration.Reveal(Hero);
        if (World.Sites.FirstOrDefault(s => s.IsTown && s.Position.Distance(Hero) < 2) is { } camp)
        {
            RespawnSite = camp;
            if (Tick > TownRulesUpgradeTick && previousHero.Distance(camp.Position) >= 2
                && (Health < MaxHealth || Mana < MaxMana))
            {
                RestInTown();
                if (resting)
                {
                    resting = false;
                    path.Clear();
                    SetDestination();
                }
                return;
            }
        }
        int band = World.RegionBand(Hero);
        if (band != currentBand)
        {
            currentBand = band;
            Add($"{Region}へ入った。敵の気配が変わった。", LogTone.Discovery);
        }
        if (Tick - lastEncounter > 24 && (Tick <= TownRulesUpgradeTick || !World.InTown(Hero)))
        {
            int engageRange = Temperament == PlayerTemperament.Reckless ? 6 : 1;
            Creature? nearby = World.Creatures.FirstOrDefault(c => c.Alive && c.Position.Distance(Hero) <= engageRange
                && (c.Kind != EnemyKind.Boss || Stage >= 2 && !Training && !Surveying)
                && (c.Kind != EnemyKind.Midboss || c == equipmentGuardian || !Training && c.Home == destinations[1].Position));
            if (nearby != null) { StartFight(nearby); return; }
        }
        if (Tick >= nextFlavor)
        {
            nextFlavor = Tick + random.Next(35, 75);
            TravelEvent();
        }
        if (Tick % 240 == 0) Mana = Math.Min(MaxMana, Mana + 1);
    }

    private void SetDestination()
    {
        Site destination = destinations[Stage];
        int required = Math.Max(2, World.RegionLevel(destination.Position) + (Stage == 0 ? 0 : 1));
        Creature? explorationTarget = null;
        if (World.ExpandedProgression && Stage >= 2)
        {
            if (bossPreparationNeeded)
                explorationTarget = World.Guardians.Where(g => g.Alive)
                    .OrderBy(g => g.Level > Level + 2 ? 1 : 0)
                    .ThenBy(g => g.Level > Level + 2 ? g.Level : 0)
                    .ThenBy(g => Exploration.Visibility(g.Home) > 80 ? 1 : 0)
                    .ThenBy(g => Hero.Distance(g.Home)).FirstOrDefault();
            required = explorationTarget?.Level ?? (World.Guardians.Any(g => g.Alive) ? Level : finalBoss.Level);
        }
        preparationLevel = Math.Max(2, required + policy.PreparationMargin + extraPreparation);
        supplying = false;
        equipmentGuardian = null;
        var purchases = WantedEquipment().ToArray();
        if (Tick > CombatRulesUpgradeTick && purchases.Length > 0)
            preparationLevel = Math.Max(preparationLevel, purchases.Max(i => i.RequiredLevel));
        int budget = purchases.Sum(i => EquipmentCatalog.Price(i.Slot, i.Tier));
        Training = Level < preparationLevel || Gold < budget;
        Surveying = false;
        if (Training)
        {
            if (!announcedTraining) Add("この先は手強そうだ。まず周辺の平原で腕を磨く。", LogTone.Quiet);
            announcedTraining = true;
            FindHunt();
            return;
        }
        announcedTraining = false;
        huntingTarget = null;
        if (purchases.Length > 0)
        {
            int tier = Stage == 0 ? 1 : 2;
            Site market = World.Sites.Where(s => s.IsTown && World.MarketTier(s.Position) >= tier)
                .MinBy(s => Hero.Distance(s.Position))!;
            supplying = true;
            Objective = $"{market.Name}で武器と防具を整える";
            Route(market.Position);
            return;
        }
        bool searchUnvisited = Tick > CombatRulesUpgradeTick;
        bool prepareBoss = searchUnvisited ? bossPreparationNeeded
            : !finalBossDefeated && (Equipment[GearSlot.Weapon].Tier < 3 || DefensePower < 40);
        if (Stage >= 2 && prepareBoss)
        {
            equipmentGuardian = explorationTarget ?? World.Guardians.Where(g => g.Alive && (searchUnvisited || Equipment[g.RewardSlot].Tier < 3))
                .OrderBy(g => searchUnvisited ? Exploration.Visibility(g.Home) > 80 ? 1 : 0
                    : Equipment[GearSlot.Weapon].Tier < 3 ? g.RewardSlot == GearSlot.Weapon ? 0 : 100000
                    : g.RewardSlot == GearSlot.Armor ? 0 : g.RewardSlot == GearSlot.Helmet ? 100 : 200)
                .ThenBy(g => Hero.Distance(g.Position)).FirstOrDefault();
            if (equipmentGuardian != null)
            {
                Objective = searchUnvisited ? Exploration.Visibility(equipmentGuardian.Home) > 80
                    ? $"手がかりを求め、{equipmentGuardian.Name}のもとへ" : "まだ訪れていない土地を探る"
                    : $"ボス対策の装備を求め、{equipmentGuardian.Name}へ";
                Route(equipmentGuardian.Position);
                return;
            }
        }
        if (Temperament == PlayerTemperament.Meticulous && Exploration.NextSurveyPoint(Hero, Math.Min(13, required)) is { } unexplored)
        {
            Surveying = true;
            SurveyDetours++;
            if (SurveyDetours == 1 || SurveyDetours % 30 == 0) Add("まだ足を運んでいない場所がある。少し寄り道をしよう。", LogTone.Quiet);
            Objective = $"未踏の陸地を歩く · 踏破 {Exploration.Fraction:P0}";
            Route(unexplored);
            return;
        }
        if (finalBossDefeated) { Finish(true); return; }
        if (departureLevels.Count <= Stage)
        {
            departureLevels.Add(Level);
            Add($"Lv.{Level}になり、{World.RegionName(destination.Position)}への旅支度を整えた。", LogTone.Discovery);
        }
        Objective = Stage switch
        {
            0 => Story == StoryKind.Rescue ? $"{destination.Name}で行方を追う" : $"{destination.Name}で手がかりを探す",
            1 => Story == StoryKind.Rescue ? $"{destination.Name}へ救出に向かう" : $"{destination.Name}へ{relic}を探しに",
            _ => $"{destination.Name}で{Villain}に挑む"
        };
        Route(destination.Position);
    }

    private void FindHunt()
    {
        int desired = Math.Min(World.ExpandedProgression ? 21 : 12, Math.Max(1, Level - 2));
        var candidates = World.Creatures.Where(c => c.Alive && c.Kind is not EnemyKind.Boss and not EnemyKind.Midboss
            && c.Level <= Level + policy.RiskTolerance && c.Level >= desired).ToArray();
        if (candidates.Length == 0)
            candidates = World.Creatures.Where(c => c.Alive && c.Kind is not EnemyKind.Boss and not EnemyKind.Midboss && c.Level <= Level + policy.RiskTolerance).ToArray();
        huntingTarget = candidates.MinBy(c => Hero.Distance(c.Position)
            + (World.Tiles[World.Index(c.Position)] is Terrain.Plain or Terrain.Sand ? 0 : 90)
            + Math.Abs(c.Level - Level) * 25);
        if (huntingTarget == null) throw new InvalidOperationException("鍛錬できる敵がいません。");
        Objective = Level < preparationLevel ? $"周辺で鍛錬 Lv.{Level} → {preparationLevel} / 次：{World.RegionName(destinations[Stage].Position)}"
            : $"装備を整える資金集め · {Gold} G";
        Route(huntingTarget.Position);
    }

    private void ReachHunt()
    {
        if (Level >= preparationLevel && Gold >= WantedEquipment().Sum(i => EquipmentCatalog.Price(i.Slot, i.Tier))) { SetDestination(); return; }
        if (huntingTarget is not { Alive: true }) { FindHunt(); return; }
        if (Hero.Distance(huntingTarget.Position) < 2) StartFight(huntingTarget);
        else Route(huntingTarget.Position);
    }

    private void ReturnToTown()
    {
        Site inn = World.Sites.Where(s => s.IsTown).MinBy(s => Hero.Distance(s.Position))!;
        Add($"消耗が激しい。{inn.Name}へ戻る。", LogTone.Danger);
        Objective = $"{inn.Name}で休息と補給";
        resting = true;
        Route(inn.Position);
    }

    private bool NeedsRest() => Tick > TownRulesUpgradeTick
        ? Health < MaxHealth * Math.Max(policy.RestRatio, Temperament == PlayerTemperament.Reckless ? .58 : .70)
            || Mana < Math.Max(7, MaxMana * .25)
        : Health < MaxHealth * policy.RestRatio;

    private void RestInTown()
    {
        RestVisits++;
        int fee = 8 + Level * 2;
        bool canPay = Gold >= fee;
        if (canPay)
        {
            Gold -= fee;
            Health = MaxHealth;
            Mana = MaxMana;
            Add("宿代を払い、傷を癒やした。", LogTone.Discovery);
        }
        else if (Tick > TownRulesUpgradeTick)
        {
            Health = MaxHealth;
            Mana = MaxMana;
            Add("宿代が足りない。集落で長めに野営し、HPとMPを全回復した。", LogTone.Discovery);
        }
        else
        {
            Health = Math.Min(MaxHealth, Health + MaxHealth / 2);
            Mana = Math.Min(MaxMana, Mana + MaxMana / 2);
            Add("宿代が足りない。集落の端で野営した。", LogTone.Quiet);
        }
        if (Gold >= 10 && inventory["傷薬"] < 2) { Gold -= 10; inventory["傷薬"]++; Add("店で傷薬を一つ補充した。", LogTone.Quiet); }
        BuyEquipment(World.MarketTier(Hero));
        wait = !canPay && Tick > TownRulesUpgradeTick ? 75 : 45;
    }

    private void Route(Point destination)
    {
        path.Clear();
        foreach (Point p in World.FindPath(Hero, destination, Level + policy.RiskTolerance)) path.Enqueue(p);
        if (Hero != destination && path.Count == 0) throw new InvalidOperationException("目的地への道がありません。");
    }

    private void Arrive()
    {
        Site site = destinations[Stage];
        if (Stage == 1 && World.Guardians.FirstOrDefault(g => g.Alive && g.Home == site.Position) is { } guardian)
        {
            StartFight(guardian);
            return;
        }
        Add($"{site.Name}にたどり着いた。", LogTone.Discovery);
        if (Stage == 0)
        {
            Add(Story switch
            {
                StoryKind.Restore => $"石碑に{relic}の在り処が記されていた。",
                StoryKind.Rescue => "焚き火の跡。捕らわれた人々は、まだ生きている。",
                _ => $"古い書に{Villain}の真の名を見つけた。"
            });
            Gold += 25;
            Add("依頼の礼に、25 Gを受け取った。", LogTone.Discovery);
            Stage++;
            extraPreparation = 0;
            SetDestination();
            wait = 20;
        }
        else if (Stage == 1)
        {
            Add(Story switch
            {
                StoryKind.Restore => $"{relic}を手に入れた。淡い光が宿る。",
                StoryKind.Rescue => "牢の鍵を砕いた。人々は故郷へ逃げ出した。",
                _ => $"{relic}に封印の力が満ちた。"
            }, LogTone.Discovery);
            inventory[relic] = 1;
            Health = Math.Min(MaxHealth, Health + 10);
            Stage++;
            extraPreparation = 0;
            SetDestination();
            wait = 25;
        }
        else
        {
            StartFight(finalBoss);
        }
    }

    private IEnumerable<GearItem> WantedEquipment()
    {
        int tier = Stage == 0 ? 1 : 2;
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
            if ((Stage > 0 || slot is GearSlot.Weapon or GearSlot.Armor) && Equipment[slot].Tier < tier)
                yield return EquipmentCatalog.Get(World.Theme.Style, slot, tier, "町の店");
    }
    private void BuyEquipment(int marketTier)
    {
        foreach (var desired in WantedEquipment().ToArray())
        {
            int tier = Math.Min(marketTier, desired.Tier);
            if (tier <= Equipment[desired.Slot].Tier) continue;
            var item = EquipmentCatalog.Get(World.Theme.Style, desired.Slot, tier, "町の店");
            if (Tick > CombatRulesUpgradeTick && Level < item.RequiredLevel) continue;
            int price = EquipmentCatalog.Price(desired.Slot, tier);
            if (Gold < price) continue;
            Gold -= price;
            Equip(item);
        }
    }
    private void Equip(GearItem item)
    {
        if (Tick > CombatRulesUpgradeTick && Level < item.RequiredLevel)
        {
            if (Equipment[item.Slot].Tier >= item.Tier || carriedEquipment.Any(i => i.Slot == item.Slot && i.Tier >= item.Tier)) return;
            carriedEquipment.Add(item);
            inventory[item.Name] = 1;
            Add($"{item.Name}を保管した。装備にはLv.{item.RequiredLevel}が必要。", LogTone.Discovery);
            return;
        }
        if (Equipment.Equip(item, Tick > CombatRulesUpgradeTick ? Level : int.MaxValue))
            Add($"{item.Name}を装備した。", LogTone.Discovery);
    }

    private void StartFight(Creature creature, bool enemyInitiated = false)
    {
        opponent = creature;
        boss = creature.Kind == EnemyKind.Boss;
        ineffectiveBossHits = 0;
        Combat = new Battle(creature, random, policy, Level, Equipment[GearSlot.Boots].Tier * .06f, enemyInitiated,
            Tick > CombatRulesUpgradeTick);
        if (enemyInitiated) EnemyInitiatedBattles++;
        lastEncounter = Tick;
        Add(boss ? $"{Villain}が立ちはだかる。最後の戦い。"
            : creature.Kind == EnemyKind.Midboss ? $"中ボス・{creature.Name} Lv.{creature.Level}が行く手を阻む！"
            : enemyInitiated ? $"{creature.Name} Lv.{creature.Level}が襲いかかってきた！" : $"{creature.Name} Lv.{creature.Level}と遭遇した。", LogTone.Danger);
        wait = 3;
    }

    private void Fight()
    {
        if (opponent == null || Combat == null) return;
        if (itemCooldown == 0 && Health < MaxHealth * policy.PotionRatio && UseItem("傷薬"))
        {
            Health = Math.Min(MaxHealth, Health + 30);
            itemCooldown = 60;
            Add("傷薬を使い、体勢を立て直した。", LogTone.Discovery);
        }
        if (itemCooldown == 0 && Mana < 7 && UseItem("霊薬")) { Mana = Math.Min(MaxMana, Mana + 18); itemCooldown = 60; Add("霊薬で魔力を補った。", LogTone.Quiet); }
        int retreatGap = Tick > CombatRulesUpgradeTick
            ? Temperament is PlayerTemperament.Careful or PlayerTemperament.Meticulous ? 3 : 4
            : policy.RiskTolerance + 2;
        if (Tick > CombatRulesUpgradeTick && boss && !bossPreparationNeeded
            && Temperament is PlayerTemperament.Careful or PlayerTemperament.Meticulous && AttackPower + 2 <= opponent.Defense)
        {
            bossPreparationNeeded = true;
            Add("異様に堅い守りだ。踏み込まず、まだ訪れていない土地から調べ直そう。", LogTone.Quiet);
        }
        bool testingBoss = Tick > CombatRulesUpgradeTick && boss && !bossPreparationNeeded
            && Temperament is PlayerTemperament.Balanced or PlayerTemperament.Reckless && ineffectiveBossHits < 2;
        bool retreat = Health < MaxHealth * policy.RetreatRatio
            || opponent.Level >= Level + retreatGap || AttackPower + 2 <= opponent.Defense && !testingBoss;
        if (retreat && !Combat.Retreating) Add("間合いを切り、逃げ道を探す。", LogTone.Danger);
        BattleEvent action = Combat.Step(AttackPower, Mana >= 7, retreat);
        if (Tick > CombatRulesUpgradeTick && boss && !bossPreparationNeeded
            && action is BattleEvent.HeroHit or BattleEvent.HeroSpell && Combat.LastDamage <= 2)
        {
            ineffectiveBossHits++;
            if (ineffectiveBossHits >= 2)
            {
                bossPreparationNeeded = true;
                Add("手応えがない……何かがおかしい。一度引いて、まだ行っていない土地を探ろう。", LogTone.Danger);
            }
        }
        switch (action)
        {
            case BattleEvent.HeroSpell:
                Mana -= 7;
                MagicCasts++;
                if (Combat.Major) Add("間合いを取り、術で追撃した！", LogTone.Discovery);
                break;
            case BattleEvent.Dodged:
                Dodges++;
                if (Combat.Major || Dodges % 3 == 0) Add($"{opponent.Name}の攻撃を、踏み込んでかわした！", LogTone.Discovery);
                break;
            case BattleEvent.HeroHurt:
                int variance = random.Next(-2, 4);
                int damage = Tick > CombatRulesUpgradeTick
                    ? CombatRules.IncomingDamage(opponent, Level, DefensePower, variance)
                    : CombatRules.Damage(opponent.Attack, DefensePower, variance);
                Health = Math.Max(0, Health - damage);
                DamageTaken += damage;
                Add($"{opponent.Name}の攻撃が命中。{damage}の傷を負った。", LogTone.Danger);
                if (Health == 0)
                {
                    if (Tick > CombatRulesUpgradeTick && (boss && finalBoss.GuardianProtection > 0
                        || Tick > TownRulesUpgradeTick && Stage >= 2 && opponent.Level >= Level + 4))
                    {
                        bossPreparationNeeded = true;
                        Add("ここはまだ早かった……次は、まだ行っていない土地を探ろう。", LogTone.Danger);
                    }
                    DeathCause = opponent.Name; Combat = null; Finish(false); return;
                }
                break;
        }
        if (Combat.Escaped)
        {
            if (Tick > CombatRulesUpgradeTick && !bossPreparationNeeded && (boss && finalBoss.GuardianProtection > 0
                || Tick > TownRulesUpgradeTick && Stage >= 2 && opponent.Level >= Level + 4))
            {
                bossPreparationNeeded = true;
                Add("このままでは勝てそうにない。まだ行っていない土地に手がかりがあるかもしれない。", LogTone.Quiet);
            }
            Retreats++;
            extraPreparation = Math.Min(2, extraPreparation + 1);
            Add($"{opponent.Name}から離脱した。鍛え直して出直そう。", LogTone.Danger);
            // An abandoned fight heals its opponent; it cannot be chipped down through retries.
            opponent.Health = opponent.MaxHealth;
            opponent = null;
            Combat = null;
            lastEncounter = Tick + (Tick > CombatRulesUpgradeTick ? 8 : 120);
            ReturnToTown();
            return;
        }
        if (opponent.Health <= 0)
        {
            Victories++;
            bool midboss = opponent.Kind == EnemyKind.Midboss;
            if (midboss)
            {
                MidbossVictories++;
                if (Tick > CombatRulesUpgradeTick)
                {
                    finalBoss.GuardianProtection = World.Guardians.Count(g => g.Alive);
                    if (bossPreparationNeeded) Add($"{Villain}を守る力が弱まった気がする。", LogTone.Discovery);
                }
                GearItem reward = EquipmentCatalog.Get(World.Theme.Style, opponent.RewardSlot, 3, opponent.Name);
                Add($"{opponent.Name}が、{reward.Name}を残した。", LogTone.Discovery);
                Equip(reward);
            }
            else if (!boss && random.Next(opponent.Kind == EnemyKind.Elite ? 2 : 14) == 0)
            {
                GearSlot slot = (GearSlot)random.Next(Enum.GetValues<GearSlot>().Length);
                Equip(EquipmentCatalog.Get(World.Theme.Style, slot, opponent.Kind == EnemyKind.Elite ? 2 : 1, opponent.Name));
            }
            if (Training && !Combat.Major) TrainingVictories++;
            int oldLevel = Level;
            Experience += Math.Max(2, opponent.ExperienceReward - Math.Max(0, Level - opponent.Level) * 4);
            Gold += 2 + opponent.Level * 2;
            if (Level > oldLevel)
            {
                Health = Math.Min(MaxHealth, Health + 6 * (Level - oldLevel));
                Mana = Math.Min(MaxMana, Mana + 3 * (Level - oldLevel));
                Add($"Lv.{Level}に上がった。少しずつ、力がついてきた。", LogTone.Discovery);
                foreach (var item in carriedEquipment.Where(i => i.RequiredLevel <= Level).ToArray())
                {
                    carriedEquipment.Remove(item);
                    inventory.Remove(item.Name);
                    Equip(item);
                }
            }
            if (midboss || random.Next(7) == 0)
            {
                inventory["傷薬"] = inventory.GetValueOrDefault("傷薬") + 1;
                Add("傷薬を拾った。", LogTone.Quiet);
            }
            Add($"{opponent.Name}を倒した。", boss ? LogTone.Ending : LogTone.Normal);
            opponent = null;
            Combat = null;
            if (boss)
            {
                finalBossDefeated = true;
                bool guardiansRemain = Tick > CombatRulesUpgradeTick && World.Guardians.Any(g => g.Alive);
                if (!guardiansRemain && (Temperament != PlayerTemperament.Meticulous || Exploration.Fraction == 1)) { Finish(true); return; }
                Add(guardiansRemain ? "まだ、この地に残る強敵がいる。旅は終わらない。"
                    : "最後の敵は倒した。だが、まだ歩いていない場所がある。", LogTone.Quiet);
            }
            if (NeedsRest() && !resting)
            {
                ReturnToTown();
            }
            else if (!resting) SetDestination();
            wait = 5;
            return;
        }
    }

    private bool UseItem(string item)
    {
        if (inventory.GetValueOrDefault(item) <= 0) return false;
        inventory[item]--;
        ItemsUsed++;
        return true;
    }

    private void TravelEvent()
    {
        int roll = random.Next(12);
        if (roll == 0) { Gold += 3; Add("道端で小銭を拾った。", LogTone.Quiet); return; }
        if (roll == 1 && inventory["傷薬"] < 3)
        {
            inventory["傷薬"]++;
            Add("薬草を摘み、傷薬を一つこしらえた。", LogTone.Quiet);
            return;
        }
        string[] flavor = World.Theme.Style switch
        {
            WorldStyle.Japanese => ["遠くで、寺の鐘が鳴った。", "道祖神に、そっと手を合わせた。", "風に混じって、笛の音がした。", "狐が一匹、道を横切った。", "峠の茶屋で、噂を聞いた。"],
            WorldStyle.Western => ["古い道標には、読めない文字。", "行き交う商人と、パンを分け合った。", "廃墟の窓に、一瞬だけ灯りが見えた。", "風が、森の奥から何かを運んでくる。", "誰かの足跡が、街道から逸れている。"],
            WorldStyle.Chinese => ["竹林の奥から、琴の音が流れてきた。", "茶売りの老人が、仙山の噂をした。", "白い鶴が、雲に消えた。", "崖の石段に、古い詩が刻まれていた。", "霧の向こうに、楼閣が見えた。"],
            _ => ["隊商とすれ違った。香辛料の匂い。", "砂の中から、古い銀貨を拾った。", "星の並びを確かめ、歩き続ける。", "蜃気楼の街は、近づくと消えた。", "遠い祈りの声が、砂丘を渡った。"]
        };
        Add(flavor[random.Next(flavor.Length)], LogTone.Quiet);
    }

    private void Finish(bool won)
    {
        if (!won) DeathCount++;
        Finished = true;
        Won = won;
        FinishedAt = Tick;
        Objective = won ? "この小さな旅は、伝説になった" : "ひとつの旅が、ここで終わった";
        Epilogue = won ? World.Theme.Endings[random.Next(World.Theme.Endings.Length)] : $"{HeroName}は、帰らぬ人となった。";
        Add(Epilogue, LogTone.Ending);
        Add("また別の世界で、新しい物語が待っている。", LogTone.Quiet);
    }

    public bool ReviveAtBase()
    {
        if (!Finished || Won) return false;
        revivalTicks.Add(Tick);
        if (opponent != null) opponent.Health = opponent.MaxHealth;
        opponent = null;
        Combat = null;
        Finished = false;
        FinishedAt = 0;
        DeathCause = null;
        Epilogue = "";
        Health = MaxHealth;
        Mana = MaxMana;
        Hero = RespawnSite.Position;
        Trail.Clear();
        path.Clear();
        resting = false;
        Training = Surveying = false;
        lastEncounter = Tick;
        extraPreparation = Math.Min(2, extraPreparation + 1);
        wait = 12;
        Add($"{RespawnSite.Name}で目を覚ました。{DeathCount}度の死を越えて、再び。", LogTone.Discovery);
        SetDestination();
        return true;
    }

    private void Add(string text, LogTone tone = LogTone.Normal)
    {
        Logs.Add(new(Tick, text, tone));
        if (Logs.Count > 160) Logs.RemoveAt(0);
    }

    public void Notify(string message) => Add(message, LogTone.Quiet);

    public SagaMemory CreateMemory()
    {
        if (!Finished) throw new InvalidOperationException("まだ旅は終わっていません。");
        string summary = !Won ? $"{DeathCause ?? Villain}との戦いに倒れた。" : Story switch
        {
            StoryKind.Restore => $"{relic}を取り戻し、{Villain}を倒した。",
            StoryKind.Rescue => $"{Villain}に捕らわれた人々を救った。",
            _ => $"{Villain}を封じ、災いを鎮めた。"
        };
        return new(SeriesId, BaseTitle, HeroName, World.Theme.Style, Part, World.Name, Villain, summary, Won, DateTimeOffset.UtcNow, Temperament, PlayerId);
    }
}
