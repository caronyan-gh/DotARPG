using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DotARPG.Core;
using MapPoint = DotARPG.Core.Point;

namespace DotARPG;

public sealed class WorldView : FrameworkElement
{
    private Adventure? adventure;
    private BitmapSource? terrain;
    private WriteableBitmap? fogMask;
    private ImageBrush? fogOpacity;
    private readonly byte[] fogPixels = new byte[World.Width * World.Height * 4];
    private readonly Typeface typeface = new("Yu Gothic UI");
    private double phase, lastTime = double.NaN;
    private double cameraZoom = 1;
    private Battle? encounterBattle;
    private Rect? encounterInfoPanel;
    private Vector2 cameraCenter = new(World.Width / 2f, World.Height / 2f);
    private static readonly Vector2 BattleOrigin = new(9, 13);
    private const float BattleScale = .4f;
    private static readonly SolidColorBrush HeroBrush = Brush(0xFFFBE6);
    private static readonly SolidColorBrush DangerBrush = Brush(0xEE8066);
    private static readonly SolidColorBrush[] EnemyBrushes = Enum.GetValues<EnemyKind>().Select(k => Brush(EnemyColors.Get(k))).ToArray();
    private static readonly SolidColorBrush TownBrush = Brush(0xE6C17F);
    private static readonly SolidColorBrush RuinBrush = Brush(0xACBCAE);
    private static readonly SolidColorBrush TrailBrush = Brush(0xF4E4A6, .43);
    internal double CameraZoom => cameraZoom;

    public WorldView()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        ClipToBounds = true;
    }

    public void SetAdventure(Adventure value)
    {
        adventure = value;
        terrain = CreateTerrain(value.World);
        fogMask = new WriteableBitmap(World.Width, World.Height, 96, 96, PixelFormats.Pbgra32, null);
        fogOpacity = new ImageBrush(fogMask) { Stretch = Stretch.Fill };
        UpdateFog(true);
        cameraZoom = 1;
        cameraCenter = new(World.Width / 2f, World.Height / 2f);
        lastTime = double.NaN;
        InvalidateVisual();
    }

    public void Refresh(double time, bool completeTransition = false, bool freeze = false)
    {
        double elapsed = double.IsNaN(lastTime) ? 0 : Math.Clamp(time - lastTime, 0, .1);
        lastTime = time;
        UpdateFog();
        if (freeze) return;
        phase = time;
        Vector2 target = new(World.Width / 2f, World.Height / 2f);
        double zoom = 1;
        if (adventure?.Combat is { Major: true } battle)
        {
            target = (BattleToWorld(battle.Hero) + BattleToWorld(battle.Enemy)) / 2;
            zoom = battle.Opponent.Kind == EnemyKind.Boss ? 2.8 : 2.3;
        }
        double blend = completeTransition ? 1 : 1 - Math.Exp(-elapsed * 5);
        cameraZoom += (zoom - cameraZoom) * blend;
        cameraCenter = Vector2.Lerp(cameraCenter, target, (float)blend);
        InvalidateVisual();
    }

    private Vector2 BattleToWorld(Vector2 point) => new Vector2(adventure!.Hero.X, adventure.Hero.Y) + (point - BattleOrigin) * BattleScale;
    private Vector2 HeroPosition => adventure!.Combat is { } battle ? BattleToWorld(battle.Hero) : new(adventure.Hero.X, adventure.Hero.Y);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (adventure == null || terrain == null || ActualWidth <= 0 || ActualHeight <= 0) return;
        World world = adventure.World;
        MapProjection projection = MapOverlayLayout.ProjectCamera(ActualWidth, ActualHeight, cameraZoom, cameraCenter);
        if (adventure.Combat is { Major: true })
            projection = MapOverlayLayout.KeepCombatVisible(projection, ActualWidth, ActualHeight, BattleToWorld(Vector2.Zero));
        double scale = projection.Scale, left = projection.Left, top = projection.Top;
        Rect protectedCombat = adventure.Combat != null
            ? MapOverlayLayout.ProtectedCombatArea(projection, BattleToWorld(Vector2.Zero)) : Rect.Empty;
        if (adventure.Combat is { Major: true } currentBattle)
        {
            // Keep the chosen corner until it would cover the battle or leave the viewport.
            if (encounterBattle != currentBattle || encounterInfoPanel is not { } previousPanel
                || !new Rect(0, 0, ActualWidth, ActualHeight).Contains(previousPanel)
                || previousPanel.IntersectsWith(protectedCombat))
                encounterInfoPanel = MapOverlayLayout.EncounterPanel(ActualWidth, ActualHeight, protectedCombat);
            encounterBattle = currentBattle;
        }
        else { encounterBattle = null; encounterInfoPanel = null; }
        Rect? encounterPanel = encounterInfoPanel;
        dc.DrawRectangle(Brush(world.Theme.Sea), null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.PushTransform(new TranslateTransform(left, top));
        dc.PushTransform(new ScaleTransform(scale, scale));
        // Overview and combat share this single bitmap, coordinate system, and camera.
        dc.DrawImage(terrain, new Rect(0, 0, World.Width, World.Height));
        DrawUnexplored(dc, scale);
        foreach (MapPoint p in adventure.Trail) dc.DrawRectangle(TrailBrush, null, new Rect(p.X, p.Y, 1, 1));
        foreach (var site in world.Sites)
        {
            var p = site.Position;
            Brush brush = site.IsTown ? TownBrush : RuinBrush;
            dc.DrawRectangle(brush, null, new Rect(p.X, p.Y, 1, 1));
            if (site.IsTown)
            {
                dc.DrawRectangle(brush, null, new Rect(p.X - 2, p.Y + 1, 1, 1));
                dc.DrawRectangle(brush, null, new Rect(p.X + 1, p.Y - 2, 1, 1));
            }
            else dc.DrawRectangle(Brush(0xCDD7B9, .3), null, new Rect(p.X - 1, p.Y - 1, 3, 3));
        }
        double dot = Math.Max(1, 1 / scale);
        foreach (var enemy in world.Creatures)
        {
            if (!enemy.Alive && enemy.Kind == EnemyKind.Midboss)
            {
                var defeated = Brush(0x8C9490);
                dc.DrawLine(new Pen(defeated, .6), new(enemy.Home.X - 1, enemy.Home.Y - 1), new(enemy.Home.X + 1, enemy.Home.Y + 1));
                dc.DrawLine(new Pen(defeated, .6), new(enemy.Home.X + 1, enemy.Home.Y - 1), new(enemy.Home.X - 1, enemy.Home.Y + 1));
            }
            if (enemy.Alive && enemy != adventure.Combat?.Opponent
                && (enemy.Kind is EnemyKind.Boss or EnemyKind.Midboss || adventure.Exploration.Visibility(enemy.Position) > 80))
                DrawEnemy(dc, enemy, new(enemy.Position.X, enemy.Position.Y), dot);
        }
        if (adventure.Combat is { } combat)
        {
            DrawCombat(dc, combat, scale);
            DrawEnemy(dc, combat.Opponent, BattleToWorld(combat.Enemy), dot);
        }
        Vector2 hero = HeroPosition;
        if (adventure.Health > 0)
        {
            // The actor remains one dot; its locator is drawn at a constant screen size below.
            dc.DrawRectangle(Brush(0x15211E), null, new Rect(hero.X - .45, hero.Y - .45, dot + .9, dot + .9));
            dc.DrawRectangle(HeroBrush, null, new Rect(hero.X, hero.Y, dot, dot));
        }
        else
        {
            dc.DrawLine(new Pen(DangerBrush, .6), new(hero.X - 1, hero.Y - 1), new(hero.X + 2, hero.Y + 2));
            dc.DrawLine(new Pen(DangerBrush, .6), new(hero.X + 2, hero.Y - 1), new(hero.X - 1, hero.Y + 2));
        }
        dc.Pop();
        dc.Pop();

        var occupiedLabels = new List<Rect>();
        foreach (var site in world.Sites.Where(s => s.IsTown || adventure.Destinations.Contains(s)))
        {
            var text = Text(site.Name, 10, 0xE1DDC2, adventure.Exploration.Visibility(site.Position) > 80 ? .7 : .48);
            double x = left + (site.Position.X + .5) * scale - text.Width / 2;
            double y = top + (site.Position.Y + 2.5) * scale;
            if (x < 4 || y < 4 || x + text.Width > ActualWidth - 4 || y + text.Height > ActualHeight - 4) continue;
            var rect = new Rect(x - 4, y - 1, text.Width + 8, text.Height + 2);
            if (!protectedCombat.IsEmpty && rect.IntersectsWith(protectedCombat)
                || encounterPanel is { } panel && rect.IntersectsWith(panel)) continue;
            dc.DrawRoundedRectangle(Brush(0x142C2B, .6), null, rect, 2, 2);
            dc.DrawText(text, new(x, y));
            occupiedLabels.Add(rect);
        }
        occupiedLabels.Add(new Rect(left + hero.X * scale - 30, top + hero.Y * scale - 32, 95, 65));
        occupiedLabels.Add(new Rect(12, 12, Math.Min(450, ActualWidth - 24), 58));
        occupiedLabels.Add(new Rect(12, ActualHeight - 45, 280, 32));
        if (encounterPanel is { } reservedPanel) occupiedLabels.Add(reservedPanel);
        DrawBossNames(dc, left, top, scale, occupiedLabels, protectedCombat);
        if (adventure.Health > 0) DrawHeroLocator(dc, left + (hero.X + .5) * scale, top + (hero.Y + .5) * scale);
        if (adventure.Combat is { Major: true } battle && encounterPanel is { } information) DrawEncounterInfo(dc, battle, information);
    }

    private void UpdateFog(bool entireMap = false)
    {
        if (adventure == null || fogMask == null) return;
        MapArea? changed = adventure.Exploration.ConsumeDirtyArea();
        if (!entireMap && changed == null) return;
        MapArea area = entireMap ? new(0, 0, World.Width, World.Height) : changed!.Value;
        for (int y = area.Y; y < area.Y + area.Height; y++)
        for (int x = area.X; x < area.X + area.Width; x++)
        {
            int index = y * World.Width + x;
            byte alpha = adventure.World.Mainland[index] ? (byte)(255 - adventure.Exploration.Visibility(new(x, y))) : (byte)0;
            int pixel = index * 4;
            fogPixels[pixel] = fogPixels[pixel + 1] = fogPixels[pixel + 2] = fogPixels[pixel + 3] = alpha;
        }
        fogMask.WritePixels(new Int32Rect(area.X, area.Y, area.Width, area.Height), fogPixels,
            World.Width * 4, (area.Y * World.Width + area.X) * 4);
    }

    private void DrawUnexplored(DrawingContext dc, double scale)
    {
        if (fogOpacity == null) return;
        // A cool, darker cover contrasts with the untouched terrain colors after exploration.
        double spacing = 9 / scale;
        var line = new LineGeometry(new(0, spacing), new(spacing, 0));
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(null, new Pen(Brush(0x061820, .48), 1.8 / scale), line));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(Brush(0xA2B8C0, .28), .7 / scale), line));
        var hatch = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, spacing, spacing), Stretch = Stretch.Fill
        };
        hatch.Freeze();
        dc.PushOpacityMask(fogOpacity);
        var bounds = new Rect(0, 0, World.Width, World.Height);
        dc.DrawRectangle(Brush(0x152330, .40), null, bounds);
        dc.DrawRectangle(hatch, null, bounds);
        dc.Pop();
    }

    private void DrawBossNames(DrawingContext dc, double left, double top, double scale, List<Rect> occupied, Rect protectedArea)
    {
        foreach (Creature enemy in adventure!.World.Creatures.Where(c => c.Kind == EnemyKind.Midboss || c.Alive && c.Kind == EnemyKind.Boss)
            .OrderByDescending(c => c.Kind))
        {
            // The engaged enemy is already named in its HUD; a second label would cover the fight.
            if (adventure.Combat?.Opponent == enemy) continue;
            Vector2 position = enemy.Alive ? new(enemy.Position.X, enemy.Position.Y) : new(enemy.Home.X, enemy.Home.Y);
            double x = left + (position.X + .5) * scale, y = top + (position.Y + .5) * scale;
            if (x < 0 || y < 0 || x > ActualWidth || y > ActualHeight) continue;
            uint color = enemy.Alive ? EnemyColors.Get(enemy.Kind) : 0x8C9490u;
            var text = Text($"{enemy.Name} Lv.{enemy.Level}", enemy.Kind == EnemyKind.Boss ? 11 : 10, color);
            text.MaxTextWidth = 220;
            text.MaxLineCount = 1;
            text.Trimming = TextTrimming.CharacterEllipsis;
            double width = text.Width + 12, height = text.Height + 6;
            Rect? placement = MapOverlayLayout.BossLabel(ActualWidth, ActualHeight, width, height, x, y, occupied, protectedArea);
            if (placement is not { } best) continue;
            dc.DrawLine(new Pen(Brush(color, .65), .75), new(x, y), new(Math.Clamp(x, best.Left, best.Right), Math.Clamp(y, best.Top, best.Bottom)));
            dc.DrawRoundedRectangle(Brush(0x102124, .9), new Pen(Brush(color, .4), .6), best, 2, 2);
            dc.DrawText(text, new(best.X + 6, best.Y + 3));
            occupied.Add(best);
        }
    }

    private void DrawEnemy(DrawingContext dc, Creature enemy, Vector2 position, double dot)
    {
        Brush brush = EnemyBrushes[(int)enemy.Kind];
        bool serpent = enemy.Kind is EnemyKind.Boss or EnemyKind.Midboss && enemy.Name.IndexOfAny(['龍', '竜', '蛇']) >= 0;
        if (serpent)
        {
            // A dragon is only four connected dots, even when the camera moves closer.
            Vector2 away = adventure?.Combat?.Opponent == enemy ? position - HeroPosition : Vector2.UnitX;
            away = away.LengthSquared() > .001f ? Vector2.Normalize(away) : Vector2.UnitX;
            var sideways = new Vector2(-away.Y, away.X);
            for (int i = 3; i >= 0; i--)
            {
                Vector2 segment = position + away * i + sideways * (float)(Math.Sin(phase * 2.8 - i * .8) * .32);
                double x = segment.X, y = segment.Y;
                dc.DrawRectangle(brush, null, new Rect(x, y, Math.Max(1.05, dot), Math.Max(1.05, dot)));
            }
        }
        else
        {
            double size = enemy.Kind switch { EnemyKind.Boss => 2.2, EnemyKind.Midboss => 1.7, _ => dot };
            size = Math.Max(size, dot);
            dc.DrawRectangle(brush, null, new Rect(position.X - (size - 1) / 2, position.Y - (size - 1) / 2, size, size));
        }
    }

    private void DrawCombat(DrawingContext dc, Battle battle, double scale)
    {
        Vector2 first = BattleToWorld(Vector2.Zero);
        var bounds = new Rect(first.X, first.Y, Battle.Width * BattleScale, Battle.Height * BattleScale);
        if (battle.Phase is BattlePhase.Windup or BattlePhase.Impact)
        {
            Vector2 target = BattleToWorld(battle.Target);
            bool impact = battle.Phase == BattlePhase.Impact;
            Brush warning = Brush(impact ? 0xFFB36Au : 0xFF5968u, impact ? .65 : .16 + battle.WindupProgress * .24);
            dc.PushClip(new RectangleGeometry(bounds));
            switch (battle.Shape)
            {
                case AttackShape.Sweep:
                    dc.DrawRectangle(warning, null, new Rect(bounds.X, target.Y - 2.1 * BattleScale, bounds.Width, 4.2 * BattleScale));
                    break;
                case AttackShape.Cross:
                    dc.DrawRectangle(warning, null, new Rect(bounds.X, target.Y - 1.7 * BattleScale, bounds.Width, 3.4 * BattleScale));
                    dc.DrawRectangle(warning, null, new Rect(target.X - 1.7 * BattleScale, bounds.Y, 3.4 * BattleScale, bounds.Height));
                    break;
                default:
                    double radius = (battle.Major ? 4 : 2.7) * BattleScale;
                    dc.DrawEllipse(warning, new Pen(Brush(0xFFAB90, .75), .6 / scale), new(target.X, target.Y), radius, radius);
                    break;
            }
            dc.Pop();
        }
        Vector2 hero = BattleToWorld(battle.Hero), enemy = BattleToWorld(battle.Enemy);
        if (battle.Dashing)
        {
            Vector2 from = BattleToWorld(battle.PreviousHero);
            dc.DrawLine(new Pen(Brush(0xF3F2BD, .55), .6), new(from.X, from.Y), new(hero.X, hero.Y));
            for (int i = 1; i <= 3; i++)
            {
                Vector2 p = Vector2.Lerp(hero, from, i * .20f);
                dc.DrawRectangle(Brush(0xFFFBE6, .32 / i), null, new Rect(p.X, p.Y, 1, 1));
            }
        }
        if (battle.Flash <= 0) return;
        switch (battle.LastEvent)
        {
            case BattleEvent.HeroSpell:
                dc.DrawLine(new Pen(Brush(0x91E2E0, .8), .7 / scale), new(hero.X, hero.Y), new(enemy.X, enemy.Y));
                dc.DrawEllipse(Brush(0xCAF9E4), null, new((hero.X + enemy.X) / 2, (hero.Y + enemy.Y) / 2), .45, .45);
                break;
            case BattleEvent.HeroHit:
                dc.DrawLine(new Pen(HeroBrush, .7 / scale), new(enemy.X - 1, enemy.Y + 1), new(enemy.X + 1, enemy.Y - 1));
                break;
            case BattleEvent.HeroHurt:
                dc.DrawEllipse(null, new Pen(DangerBrush, 1 / scale), new(hero.X, hero.Y), 2, 2);
                break;
        }
    }

    private void DrawHeroLocator(DrawingContext dc, double x, double y)
    {
        double pulse = (Math.Sin(phase * 3.5) + 1) / 2;
        dc.DrawEllipse(null, new Pen(Brush(0x071B1D, .55), 2.5), new(x, y), 12, 12);
        dc.DrawEllipse(null, new Pen(Brush(0xFFE5A0, .75 + pulse * .25), 1.1), new(x, y), 12, 12);
        dc.DrawEllipse(null, new Pen(Brush(0xFFE5A0, .15 + .2 * pulse), 1), new(x, y), 17 + pulse * 5, 17 + pulse * 5);
        // The small arrow and label make the hero obvious over forest, snow, and towns.
        dc.DrawLine(new Pen(Brush(0xFFE5A0), 1.5), new(x - 3, y - 22), new(x, y - 18));
        dc.DrawLine(new Pen(Brush(0xFFE5A0), 1.5), new(x + 3, y - 22), new(x, y - 18));
        if (adventure!.Combat != null) return;
        var text = Text("旅人", 11, 0xFFF3CC);
        double tx = Math.Clamp(x + 18, 4, Math.Max(4, ActualWidth - text.Width - 8));
        double ty = Math.Clamp(y - 27, 4, Math.Max(4, ActualHeight - text.Height - 4));
        dc.DrawRoundedRectangle(Brush(0x102124, .85), null, new Rect(tx - 4, ty - 2, text.Width + 8, text.Height + 4), 2, 2);
        dc.DrawText(text, new(tx, ty));
    }

    private void DrawEncounterInfo(DrawingContext dc, Battle battle, Rect panel)
    {
        const double width = 270;
        double left = panel.Left + 12, top = panel.Top + 9;
        dc.DrawRoundedRectangle(Brush(0x0C2024, .85), null, panel, 3, 3);
        var name = Text($"{battle.Opponent.Name} Lv.{battle.Opponent.Level}", 12, 0xE8D6B1);
        name.MaxTextWidth = width;
        name.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(name, new(left, top));
        dc.DrawRectangle(Brush(0x3E3035), null, new Rect(left, top + 23, width, 3));
        dc.DrawRectangle(EnemyBrushes[(int)battle.Opponent.Kind], null, new Rect(left, top + 23, width * battle.Opponent.Health / battle.Opponent.MaxHealth, 3));
        string action = battle.Dashing ? "回避！" : battle.Phase switch
        {
            BattlePhase.Windup => "攻撃予兆…",
            BattlePhase.Impact => battle.LastEvent == BattleEvent.Dodged ? "かわした！　反撃の好機" : "被弾！",
            BattlePhase.Recovery => "敵の隙へ反撃",
            _ => "間合いを詰める"
        };
        dc.DrawText(Text(action, 11, 0xBCD3B9), new(left, top + 34));
        dc.DrawText(Text($"{cameraZoom:0.0}×", 10, 0x829C8C), new(left + width - 28, top + 36));
    }

    private FormattedText Text(string text, double size, uint color, double opacity = 1) => new(GameText.Text(text),
        CultureInfo.GetCultureInfo(GameText.English ? "en-US" : "ja-JP"), FlowDirection.LeftToRight, typeface, size, Brush(color, opacity), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static BitmapSource CreateTerrain(World world)
    {
        Theme theme = world.Theme;
        uint[] palette = [theme.Sea, theme.Shallows, theme.Sand, theme.Grass, theme.Forest, theme.Mountain, theme.Snow];
        var pixels = new byte[World.Width * World.Height * 4];
        for (int y = 0; y < World.Height; y++)
        for (int x = 0; x < World.Width; x++)
        {
            int i = y * World.Width + x;
            Terrain tile = world.Tiles[i];
            uint color = palette[(int)tile];
            uint hash = unchecked((uint)(x * 374761393 + y * 668265263 + world.Seed));
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            int variation = (int)(hash % 13) - 6;
            if (tile == Terrain.Sea) variation = hash % 120 == 0 ? 10 : 0;
            if (tile == Terrain.Forest) variation = hash % 4 == 0 ? -13 : variation;
            if (tile is Terrain.Mountain or Terrain.Snow) variation += x > 0 && world.Tiles[i - 1] < tile ? 13 : -3;
            if (tile == Terrain.Plain && world.Theme.Style == WorldStyle.Oriental)
                variation = (int)(Math.Sin(y * .8 + x * .12) * 4) + variation / 2;
            if (world.Roads[i]) { color = Mix(color, theme.Road, .5); variation = -4; }
            int offset = i * 4;
            pixels[offset] = (byte)Math.Clamp((int)(color & 255) + variation, 0, 255);
            pixels[offset + 1] = (byte)Math.Clamp((int)((color >> 8) & 255) + variation, 0, 255);
            pixels[offset + 2] = (byte)Math.Clamp((int)((color >> 16) & 255) + variation, 0, 255);
            pixels[offset + 3] = 255;
        }
        var bitmap = BitmapSource.Create(World.Width, World.Height, 96, 96, PixelFormats.Bgra32, null, pixels, World.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static SolidColorBrush Brush(uint rgb, double opacity = 1)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static uint Mix(uint first, uint second, double amount)
    {
        uint r = (uint)(((first >> 16) & 255) * (1 - amount) + ((second >> 16) & 255) * amount);
        uint g = (uint)(((first >> 8) & 255) * (1 - amount) + ((second >> 8) & 255) * amount);
        uint b = (uint)((first & 255) * (1 - amount) + (second & 255) * amount);
        return r << 16 | g << 8 | b;
    }
}
