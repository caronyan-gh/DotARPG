using System.Numerics;
using System.Windows;
using DotARPG.Core;

namespace DotARPG;

internal readonly record struct MapProjection(double Scale, double Left, double Top)
{
    internal System.Windows.Point Project(Vector2 position) => new(Left + position.X * Scale, Top + position.Y * Scale);
}

internal static class MapOverlayLayout
{
    internal static MapProjection ProjectCamera(double width, double height, double zoom, Vector2 center)
    {
        double scale = Math.Min(width / World.Width, height / World.Height) * zoom;
        // The sea background fills outside the atlas. Do not pin a coastal fight to the viewport edge.
        return new(scale, width / 2 - center.X * scale, height / 2 - center.Y * scale);
    }

    internal static Rect ProtectedCombatArea(MapProjection projection, Vector2 worldArenaOrigin)
    {
        var start = projection.Project(worldArenaOrigin);
        var area = new Rect(start.X, start.Y, Battle.Width * .4 * projection.Scale, Battle.Height * .4 * projection.Scale);
        area.Inflate(42, 42);
        return area;
    }

    internal static MapProjection KeepCombatVisible(MapProjection projection, double width, double height, Vector2 worldArenaOrigin)
    {
        Rect area = ProtectedCombatArea(projection, worldArenaOrigin);
        double dx = area.Left < 24 ? 24 - area.Left : area.Right > width - 24 ? width - 24 - area.Right : 0;
        double dy = area.Top < 88 ? 88 - area.Top : area.Bottom > height - 140 ? height - 140 - area.Bottom : 0;
        return projection with { Left = projection.Left + dx, Top = projection.Top + dy };
    }

    internal static Rect? EncounterPanel(double width, double height, Rect protectedArea)
    {
        const double panelWidth = 294, panelHeight = 69;
        var candidates = new[]
        {
            new Rect((width - panelWidth) / 2, height - 119, panelWidth, panelHeight),
            new Rect((width - panelWidth) / 2, 82, panelWidth, panelHeight),
            new Rect(width - panelWidth - 18, 82, panelWidth, panelHeight),
            new Rect(width - panelWidth - 18, height - 119, panelWidth, panelHeight),
            new Rect(18, 82, panelWidth, panelHeight),
            new Rect(18, height - 119, panelWidth, panelHeight)
        };
        var viewport = new Rect(6, 6, Math.Max(0, width - 12), Math.Max(0, height - 12));
        var worldHeading = new Rect(12, 12, Math.Min(450, width - 24), 58);
        var keys = new Rect(12, height - 45, 280, 32);
        return candidates.Where(r => viewport.Contains(r) && !r.IntersectsWith(protectedArea)
            && !r.IntersectsWith(worldHeading) && !r.IntersectsWith(keys)).Select(r => (Rect?)r).FirstOrDefault();
    }

    internal static Rect? BossLabel(double viewWidth, double viewHeight, double width, double height,
        double x, double y, IReadOnlyList<Rect> occupied, Rect protectedArea)
    {
        var rect = new Rect(Math.Clamp(x - width / 2, 6, Math.Max(6, viewWidth - width - 6)),
            Math.Clamp(y - height - 11, 6, Math.Max(6, viewHeight - height - 6)), width, height);
        // Ordinary map labels disappear instead of jumping between alternate positions.
        if (!protectedArea.IsEmpty && rect.IntersectsWith(protectedArea)
            || occupied.Any(other => rect.IntersectsWith(other))) return null;
        return rect;
    }
}
