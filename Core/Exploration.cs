namespace DotARPG.Core;

public readonly record struct MapArea(int X, int Y, int Width, int Height);

public sealed class Exploration
{
    public const int Radius = 32;
    private const int ClearRadius = 24, SectorSize = 16;
    private const int Columns = World.Width / SectorSize, Rows = World.Height / SectorSize;
    private readonly World world;
    private readonly byte[] visibility = new byte[World.Width * World.Height];
    private readonly int[] remaining = new int[Columns * Rows];
    private readonly Point?[] representatives = new Point?[Columns * Rows];
    private int dirtyLeft = World.Width, dirtyTop = World.Height, dirtyRight = -1, dirtyBottom = -1;
    public int ReachableTiles { get; }
    public int ExploredTiles { get; private set; }
    public double Fraction => ReachableTiles == 0 ? 1 : ExploredTiles / (double)ReachableTiles;

    public Exploration(World world)
    {
        this.world = world;
        for (int y = 0; y < World.Height; y++)
        for (int x = 0; x < World.Width; x++)
        {
            var p = new Point(x, y);
            if (!world.Walkable(p)) continue;
            ReachableTiles++;
            int sector = y / SectorSize * Columns + x / SectorSize;
            remaining[sector]++;
            var center = new Point(x / SectorSize * SectorSize + SectorSize / 2, y / SectorSize * SectorSize + SectorSize / 2);
            if (representatives[sector] is not { } old || p.Distance(center) < old.Distance(center)) representatives[sector] = p;
        }
    }

    public byte Visibility(Point position) => World.InBounds(position) ? visibility[World.Index(position)] : (byte)0;

    public void Reveal(Point center)
    {
        int left = Math.Max(0, center.X - Radius), right = Math.Min(World.Width - 1, center.X + Radius);
        int top = Math.Max(0, center.Y - Radius), bottom = Math.Min(World.Height - 1, center.Y + Radius);
        bool changed = false;
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            int index = y * World.Width + x;
            if (!world.Mainland[index] || visibility[index] == 255) continue;
            int distance = (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y);
            if (distance > Radius * Radius) continue;
            byte value = distance <= ClearRadius * ClearRadius ? (byte)255
                : (byte)Math.Clamp((Radius - Math.Sqrt(distance)) * 255 / (Radius - ClearRadius), 0, 254);
            if (value <= visibility[index]) continue;
            visibility[index] = value;
            changed = true;
            if (value == 255)
            {
                ExploredTiles++;
                remaining[y / SectorSize * Columns + x / SectorSize]--;
            }
        }
        if (!changed) return;
        dirtyLeft = Math.Min(dirtyLeft, left);
        dirtyTop = Math.Min(dirtyTop, top);
        dirtyRight = Math.Max(dirtyRight, right);
        dirtyBottom = Math.Max(dirtyBottom, bottom);
    }

    public MapArea? ConsumeDirtyArea()
    {
        if (dirtyRight < dirtyLeft) return null;
        var result = new MapArea(dirtyLeft, dirtyTop, dirtyRight - dirtyLeft + 1, dirtyBottom - dirtyTop + 1);
        dirtyLeft = World.Width; dirtyTop = World.Height; dirtyRight = dirtyBottom = -1;
        return result;
    }

    public Point? NextSurveyPoint(Point from, int safeLevel)
    {
        // Select a nearby unfinished sector, then an actual walkable, unrevealed tile.
        foreach (int sector in Enumerable.Range(0, remaining.Length)
            .Where(i => remaining[i] > 0 && representatives[i] is { } p && world.RegionLevel(p) <= safeLevel)
            .OrderBy(i => from.Distance(representatives[i]!.Value)))
        {
            int left = sector % Columns * SectorSize, top = sector / Columns * SectorSize;
            Point? nearest = null;
            int nearestDistance = int.MaxValue;
            for (int y = top; y < top + SectorSize; y++)
            for (int x = left; x < left + SectorSize; x++)
            {
                var p = new Point(x, y);
                if (!world.Walkable(p) || Visibility(p) == 255 || world.RegionLevel(p) > safeLevel) continue;
                int distance = p.Distance(from);
                if (distance < nearestDistance) { nearest = p; nearestDistance = distance; }
            }
            if (nearest != null) return nearest;
        }
        return null;
    }
}
