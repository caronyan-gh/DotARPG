using System.Numerics;
using System.Windows;
using DotARPG.Core;

namespace DotARPG;

internal static class OverlayLayoutChecks
{
    internal static string Run()
    {
        int cases = 0, assertions = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            assertions++;
        }
        Vector2[] anchors =
        [new(2, 2), new(World.Width - 2, 2), new(2, World.Height - 2), new(World.Width - 2, World.Height - 2),
            new(World.Width / 2, 2), new(World.Width / 2, World.Height - 2), new(2, World.Height / 2),
            new(World.Width - 2, World.Height / 2), new(World.Width / 2, World.Height / 2)];
        (double Width, double Height)[] sizes = [(640, 561), (1180, 921), (1600, 1000)];
        foreach (var size in sizes)
        foreach (Vector2 anchor in anchors)
        foreach (double zoom in new[] { 2.3, 2.8 })
        foreach (float progress in new[] { 0f, .15f, .5f, 1f })
        {
            cases++;
            Vector2 focus = anchor + new Vector2(2.6f, -.6f);
            Vector2 center = Vector2.Lerp(new(World.Width / 2f, World.Height / 2f), focus, progress);
            double currentZoom = 1 + (zoom - 1) * progress;
            var projection = MapOverlayLayout.ProjectCamera(size.Width, size.Height, currentZoom, center);
            Vector2 origin = anchor - new Vector2(9, 13) * .4f;
            projection = MapOverlayLayout.KeepCombatVisible(projection, size.Width, size.Height, origin);
            Rect combat = MapOverlayLayout.ProtectedCombatArea(projection, origin);
            var safe = new Rect(23.99, 87.99, size.Width - 47.98, size.Height - 227.98);
            Check(safe.Contains(combat), "四隅・辺・ズーム途中でも戦闘領域を安全な画面内に置く");
            Rect? hud = MapOverlayLayout.EncounterPanel(size.Width, size.Height, combat);
            Check(hud.HasValue && !hud.Value.IntersectsWith(combat), "戦闘情報の帯が戦闘に重ならない");
            Rect information = hud ?? throw new InvalidOperationException("戦闘情報の配置先がありません。");
            var point = projection.Project(focus);
            var label = MapOverlayLayout.BossLabel(size.Width, size.Height, 210, 22, point.X, point.Y, [information], combat);
            Check(label == null || !label.Value.IntersectsWith(combat), "名前を配置できない場合も戦闘を覆わない");
            if (progress == 1)
            {
                var centered = MapOverlayLayout.ProjectCamera(size.Width, size.Height, zoom, focus).Project(focus);
                Check(Math.Abs(centered.X - size.Width / 2) < .001 && Math.Abs(centered.Y - size.Height / 2) < .001,
                    "地図の端の戦闘でもカメラは現地を中央へ寄せられる");
            }
        }
        var blocked = new Rect(0, 0, 640, 561);
        Check(MapOverlayLayout.BossLabel(640, 561, 210, 22, 320, 280, [], blocked) == null,
            "すべての候補が戦闘を覆う場合は名前を描かない");
        var oldHudPosition = new Rect((1180 - 294) / 2d, 921 - 119, 294, 69);
        var moved = MapOverlayLayout.EncounterPanel(1180, 921, oldHudPosition);
        Check(moved.HasValue && !moved.Value.IntersectsWith(oldHudPosition), "下端で戦っている途中は情報帯を別の位置へ逃がす");
        return System.Text.Json.JsonSerializer.Serialize(new { cases, assertions, passed = true }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
