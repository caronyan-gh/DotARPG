namespace DotARPG.Core;

public enum Terrain : byte { Sea, Shallows, Sand, Plain, Forest, Mountain, Snow }
public readonly record struct Point(int X, int Y)
{
    public int Distance(Point other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}
public sealed record Site(Point Position, string Name, bool IsTown);
public sealed class Creature(Point position, string name, EnemyKind kind = EnemyKind.Beast)
{
    public Point Position { get; set; } = position;
    public Point Home { get; } = position;
    public string Name { get; } = name;
    public EnemyKind Kind { get; } = kind;
    public int Level { get; set; } = 1;
    public int Attack => Kind switch { EnemyKind.Boss => 24 + Level * 5, EnemyKind.Midboss => 15 + Level * 5, _ => 7 + Level * 4 };
    public int Defense => Kind switch { EnemyKind.Boss => 46 + Level * 2 + GuardianProtection * 30, EnemyKind.Midboss => 10 + Level, _ => Level + (Kind == EnemyKind.Elite ? 4 : 0) };
    public int GuardianProtection { get; set; }
    public int RequiredDefense => Level * 2 + (Kind switch { EnemyKind.Boss => 12, EnemyKind.Midboss => 8, _ => 2 });
    public GearSlot RewardSlot { get; set; }
    public bool Alerted { get; set; }
    public int SightRange => Kind switch { EnemyKind.Boss => 28, EnemyKind.Midboss => 22, EnemyKind.Elite => 18, _ => 12 };
    public int ExperienceReward => (Kind is EnemyKind.Boss or EnemyKind.Midboss ? 35 : 8) + Level * 5;
    public int Health { get; set; } = 10;
    public int MaxHealth { get; set; } = 10;
    public bool Alive => Health > 0;
}

public sealed class World
{
    public const int Width = 1024, Height = 768;
    public const int SiteCount = 24;
    public int Seed { get; }
    public bool ExpandedProgression { get; }
    public Theme Theme { get; }
    public string Name { get; }
    public Terrain[] Tiles { get; } = new Terrain[Width * Height];
    public bool[] Roads { get; } = new bool[Width * Height];
    public bool[] Mainland { get; private set; } = new bool[Width * Height];
    public List<Site> Sites { get; } = [];
    public List<Creature> Creatures { get; } = [];
    public List<Creature> Guardians { get; } = [];
    public Random Random { get; }
    public int Pursuers { get; private set; }
    private int farthestSite;
    private readonly byte[] threat = new byte[Width * Height];
    private readonly bool[] townSafety = new bool[Width * Height];
    private static readonly int[] RegionLevels = [1, 4, 7, 10];
    private static readonly int[] ExpandedRegionLevels = [1, 5, 9, 14];
    private Creature? domainBoss;
    private readonly float[] elevation = new float[Width * Height];
    private static readonly (int X, int Y)[] Directions = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    public World(int seed, WorldStyle style, bool expandedProgression = true)
    {
        Seed = seed;
        ExpandedProgression = expandedProgression;
        Random = new Random(seed);
        Theme = Theme.Get(style);
        Name = Theme.Realms[Random.Next(Theme.Realms.Length)];
        GenerateTerrain();
        FindMainland();
        PlaceSites();
        farthestSite = Sites.Max(s => s.Position.Distance(Sites[0].Position));
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++) threat[y * Width + x] = (byte)(expandedProgression ? ExpandedRegionLevels : RegionLevels)[RegionBand(new(x, y))];
        foreach (var town in Sites.Where(s => s.IsTown))
            for (int y = Math.Max(0, town.Position.Y - 10); y <= Math.Min(Height - 1, town.Position.Y + 10); y++)
            for (int x = Math.Max(0, town.Position.X - 10); x <= Math.Min(Width - 1, town.Position.X + 10); x++)
                if (new Point(x, y).Distance(town.Position) <= 10) townSafety[y * Width + x] = true;
        for (int i = 1; i < Sites.Count; i++)
        {
            var nearest = Sites.Take(i).MinBy(s => s.Position.Distance(Sites[i].Position))!;
            foreach (Point p in FindPath(Sites[i].Position, nearest.Position))
                Roads[Index(p)] = true;
        }
        var land = Enumerable.Range(0, Mainland.Length).Where(i => Mainland[i]).ToArray();
        for (int i = 0; i < 900; i++)
        {
            int tile = land[Random.Next(land.Length)];
            var position = new Point(tile % Width, tile / Width);
            if (Sites.Any(s => s.Position.Distance(position) < 8)) continue;
            Creatures.Add(CreateCreature(position));
        }
        foreach (var site in Sites.Where(s => !s.IsTown).Where((_, i) => i % 3 == 0))
        {
            int level = RegionLevel(site.Position) + 1;
            int health = 60 + level * 18;
            var guardian = new Creature(site.Position, site.Name + "の守護者", EnemyKind.Midboss) { Level = level, Health = health, MaxHealth = health };
            Guardians.Add(guardian);
            Creatures.Add(guardian);
            RaiseThreat(guardian.Home, 32, level);
        }
        if (expandedProgression)
        {
            int rank = 0;
            foreach (var guardian in Guardians.OrderBy(g => g.Home.Distance(Sites[0].Position)))
            {
                guardian.Level = Math.Max(guardian.Level, 5 + rank++ * 3);
                guardian.Health = guardian.MaxHealth = 60 + guardian.Level * 18;
                RaiseThreat(guardian.Home, 32, guardian.Level);
            }
        }
    }

    public Creature CreateCreature(Point position)
    {
        int species = Random.Next(Theme.Enemies.Length);
        EnemyKind[][] kinds =
        [
            [EnemyKind.Beast, EnemyKind.Spirit, EnemyKind.Undead, EnemyKind.Undead, EnemyKind.Elite],
            [EnemyKind.Beast, EnemyKind.Humanoid, EnemyKind.Undead, EnemyKind.Humanoid, EnemyKind.Elite],
            [EnemyKind.Humanoid, EnemyKind.Undead, EnemyKind.Spirit, EnemyKind.Humanoid, EnemyKind.Elite],
            [EnemyKind.Beast, EnemyKind.Humanoid, EnemyKind.Undead, EnemyKind.Spirit, EnemyKind.Elite]
        ];
        EnemyKind kind = kinds[(int)Theme.Style][species];
        int level = RegionLevel(position) + (kind == EnemyKind.Elite ? 1 : 0);
        int health = 18 + level * 6 + (kind == EnemyKind.Elite ? 14 : 0);
        return new(position, Theme.Enemies[species], kind) { Level = level, Health = health, MaxHealth = health };
    }

    public int RegionBand(Point position)
    {
        return Math.Clamp(position.Distance(Sites[0].Position) * 4 / Math.Max(1, farthestSite + 1), 0, 3);
    }
    public int RegionLevel(Point position) => InBounds(position) ? threat[Index(position)] : 14;
    public int MarketTier(Point position) => RegionBand(position) == 0 ? 1 : 2;
    public bool InTown(Point position) => InBounds(position) && townSafety[Index(position)];
    public void EstablishBossDomain(Creature boss)
    {
        domainBoss = boss;
        RaiseThreat(boss.Home, 200, ExpandedProgression ? 14 : 10);
        RaiseThreat(boss.Home, 140, ExpandedProgression ? 18 : 12);
        RaiseThreat(boss.Home, 70, ExpandedProgression ? 21 : 13);
        foreach (Creature creature in Creatures.Where(c => c != boss))
        {
            int level = Math.Max(creature.Level, RegionLevel(creature.Home));
            if (level == creature.Level) continue;
            creature.Level = level;
            creature.Health = creature.MaxHealth = creature.Kind == EnemyKind.Midboss ? 60 + level * 18 : 18 + level * 6 + (creature.Kind == EnemyKind.Elite ? 14 : 0);
        }
    }
    private void RaiseThreat(Point center, int radius, int level)
    {
        for (int y = Math.Max(0, center.Y - radius); y <= Math.Min(Height - 1, center.Y + radius); y++)
        for (int x = Math.Max(0, center.X - radius); x <= Math.Min(Width - 1, center.X + radius); x++)
            if (new Point(x, y).Distance(center) <= radius) threat[y * Width + x] = (byte)Math.Max(threat[y * Width + x], level);
    }
    public string RegionName(Point position)
    {
        if (domainBoss != null && position.Distance(domainBoss.Home) <= 140) return domainBoss.Name + "の支配域";
        string[] names = Theme.Style switch
        {
            WorldStyle.Japanese => ["里外れの平原", "妖しの森", "鬼の山路", "黄泉の奥地"],
            WorldStyle.Western => ["はじまりの平原", "辺境の森", "竜の山地", "灰の奥地"],
            WorldStyle.Chinese => ["郷里の平原", "霊竹の山野", "仙山の峠", "黒雲の奥地"],
            _ => ["オアシスの草原", "隊商の砂丘", "赤岩の峡谷", "忘却の砂海"]
        };
        return names[RegionBand(position)];
    }

    public static int Index(Point p) => p.Y * Width + p.X;
    public static bool InBounds(Point p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
    public bool Walkable(Point p) => InBounds(p) && Mainland[Index(p)];

    private void GenerateTerrain()
    {
        float offsetX = (float)Random.NextDouble() * 30;
        float offsetY = (float)Random.NextDouble() * 30;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            float nx = (x - Width / 2f) / (Width / 2f);
            float ny = (y - Height / 2f) / (Height / 2f);
            float broad = Noise(x / 135f + offsetX, y / 135f + offsetY);
            float detail = Noise(x / 38f + 41 + offsetX, y / 38f + 53 + offsetY);
            float fine = Noise(x / 10f + 93 + offsetX, y / 10f + offsetY);
            float height = .62f + (broad - .5f) * .65f + (detail - .5f) * .24f
                + (fine - .5f) * .07f - nx * nx * .36f - ny * ny * .42f;
            float moisture = Noise(x / 58f + offsetY, y / 58f + offsetX + 73);
            int index = y * Width + x;
            int edge = Math.Min(Math.Min(x, Width - 1 - x), Math.Min(y, Height - 1 - y));
            height = Math.Min(height, .15f + edge * .018f);
            elevation[index] = height;
            Tiles[index] = height switch
            {
                < .235f => Terrain.Sea,
                < .27f => Terrain.Shallows,
                < .305f => Terrain.Sand,
                > .77f => Terrain.Snow,
                > .66f => Terrain.Mountain,
                _ => moisture > .51f ? Terrain.Forest : Terrain.Plain
            };
        }
        // Rivers follow the height field. All destinations are selected afterwards.
        for (int river = 0; river < 14; river++)
        {
            int start = Random.Next(Tiles.Length);
            for (int n = 0; n < 300 && elevation[start] < .62f; n++) start = Random.Next(Tiles.Length);
            var current = new Point(start % Width, start / Width);
            var visited = new HashSet<Point>();
            for (int n = 0; n < 500 && visited.Add(current); n++)
            {
                if (!InBounds(current) || Tiles[Index(current)] <= Terrain.Shallows) break;
                Tiles[Index(current)] = Terrain.Shallows;
                Point? next = null;
                float lowest = float.MaxValue;
                foreach (var d in Directions)
                {
                    var p = new Point(current.X + d.X, current.Y + d.Y);
                    if (!InBounds(p) || visited.Contains(p)) continue;
                    if (elevation[Index(p)] < lowest) { next = p; lowest = elevation[Index(p)]; }
                }
                if (next is null || lowest > elevation[Index(current)] + .018f) break;
                current = next.Value;
            }
        }
    }

    private float Noise(float x, float y)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
        return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
    }

    private float Hash(int x, int y)
    {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + Seed));
        h = (h ^ (h >> 13)) * 1274126177u;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    private void FindMainland()
    {
        var seen = new bool[Tiles.Length];
        List<int> largest = [];
        for (int i = 0; i < Tiles.Length; i++)
        {
            if (seen[i] || Tiles[i] <= Terrain.Shallows) continue;
            List<int> component = [];
            var queue = new Queue<int>();
            queue.Enqueue(i);
            seen[i] = true;
            while (queue.TryDequeue(out int current))
            {
                component.Add(current);
                var p = new Point(current % Width, current / Width);
                foreach (var d in Directions)
                {
                    var next = new Point(p.X + d.X, p.Y + d.Y);
                    if (!InBounds(next)) continue;
                    int j = Index(next);
                    if (seen[j] || Tiles[j] <= Terrain.Shallows) continue;
                    seen[j] = true;
                    queue.Enqueue(j);
                }
            }
            if (component.Count > largest.Count) largest = component;
        }
        if (largest.Count < 100) throw new InvalidOperationException("冒険できる陸地がありません。");
        foreach (int i in largest) Mainland[i] = true;
    }

    private void PlaceSites()
    {
        var candidates = Enumerable.Range(0, Mainland.Length)
            .Where(i => Mainland[i] && Tiles[i] is not Terrain.Mountain and not Terrain.Snow)
            .Select(i => new Point(i % Width, i / Width)).ToArray();
        string[] regions = Theme.Style switch
        {
            WorldStyle.Japanese => ["", "奥", "遠つ"],
            WorldStyle.Western => ["", "北の", "旧"],
            WorldStyle.Chinese => ["", "上", "古"],
            _ => ["", "東の", "古の"]
        };
        var names = regions.SelectMany(r => Theme.Places.Select(p => r + p)).OrderBy(_ => Random.Next()).ToArray();
        var plains = candidates.Where(p => Tiles[Index(p)] == Terrain.Plain).ToArray();
        Point first = plains.Length > 0 ? plains[Random.Next(plains.Length)] : candidates[Random.Next(candidates.Length)];
        Sites.Add(new(first, names[0], true));
        for (int i = 1; i < names.Length; i++)
        {
            Point best = first;
            int bestScore = -1;
            for (int attempt = 0; attempt < 700; attempt++)
            {
                Point p = candidates[Random.Next(candidates.Length)];
                int score = Sites.Min(s => s.Position.Distance(p));
                if (score > bestScore) { best = p; bestScore = score; }
            }
            Sites.Add(new(best, names[i], i % 4 == 0 || i < 3));
        }
    }

    public List<Point> FindPath(Point start, Point goal, int safeLevel = int.MaxValue)
    {
        if (!Walkable(start) || !Walkable(goal)) return [];
        if (start == goal) return [];
        var parents = new int[Tiles.Length];
        Array.Fill(parents, -1);
        var queue = new PriorityQueue<int, float>();
        var costs = new float[Tiles.Length];
        Array.Fill(costs, float.PositiveInfinity);
        var closed = new bool[Tiles.Length];
        int from = Index(start), target = Index(goal);
        parents[from] = from;
        costs[from] = 0;
        queue.Enqueue(from, 0);
        while (queue.TryDequeue(out int current, out _))
        {
            if (closed[current]) continue;
            closed[current] = true;
            if (current == target)
            {
                List<Point> result = [];
                for (int j = target; j != from; j = parents[j]) result.Add(new(j % Width, j / Width));
                result.Reverse();
                return result;
            }
            var p = new Point(current % Width, current / Width);
            foreach (var d in Directions)
            {
                var next = new Point(p.X + d.X, p.Y + d.Y);
                if (!Walkable(next)) continue;
                int index = Index(next);
                if (closed[index]) continue;
                float terrainCost = Tiles[index] switch
                {
                    Terrain.Forest => 2.0f,
                    Terrain.Mountain => 5.5f,
                    Terrain.Snow => 8f,
                    Terrain.Sand => 1.4f,
                    _ => 1.1f
                };
                float cost = costs[current] + (Roads[index] ? .8f : terrainCost + Noise(next.X / 7f, next.Y / 7f) * .8f);
                if (safeLevel != int.MaxValue) cost += Math.Max(0, RegionLevel(next) - safeLevel) * 12;
                if (cost >= costs[index]) continue;
                costs[index] = cost;
                parents[index] = current;
                queue.Enqueue(index, cost + next.Distance(goal) * .8f);
            }
        }
        return [];
    }

    public void MoveCreatures(Point hero, Creature? engaged, int tick)
    {
        Pursuers = 0;
        bool safe = InTown(hero);
        foreach (var creature in Creatures)
        {
            if (!creature.Alive || creature == engaged) continue;
            int distance = creature.Position.Distance(hero);
            int leash = creature.Kind is EnemyKind.Boss or EnemyKind.Midboss ? 24 : 65;
            if (safe || distance > creature.SightRange * 2 || creature.Position.Distance(creature.Home) > leash) creature.Alerted = false;
            else if (distance <= creature.SightRange) creature.Alerted = true;
            if (creature.Alerted) Pursuers++;
            if (creature.Alerted && tick % 2 == 0 || !creature.Alerted && creature.Position != creature.Home && tick % 3 == 0)
            {
                Point target = creature.Alerted ? hero : creature.Home;
                var choices = Directions.Select(d => new Point(creature.Position.X + d.X, creature.Position.Y + d.Y))
                    .Where(p => Walkable(p) && !InTown(p)).OrderBy(p => p.Distance(target)).ToArray();
                if (choices.Length > 0) creature.Position = choices[0];
            }
            else if (!creature.Alerted && creature.Kind is not EnemyKind.Midboss and not EnemyKind.Boss && Random.Next(7) == 0)
            {
                var d = Directions[Random.Next(Directions.Length)];
                var next = new Point(creature.Position.X + d.X, creature.Position.Y + d.Y);
                if (Walkable(next) && !InTown(next)) creature.Position = next;
            }
        }
    }
}
