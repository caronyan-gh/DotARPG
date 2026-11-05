using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DotARPG.Core;

namespace DotARPG;

internal static class UiChecks
{
    internal static string RunRevival()
    {
        var window = new MainWindow(WorldStyle.Japanese, 31, false, PlayerTemperament.Reckless)
        { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
        bool originalLanguage = GameText.English;
        try
        {
            window.Show();
            window.AdvanceForSnapshot(0, scene: "ending");
            var game = window.CurrentAdventure;
            if (!game.Finished || game.Won || window.DeathBanner.Visibility != Visibility.Visible)
                throw new InvalidOperationException("死亡演出が出ていません。");
            int tick = game.Tick, deaths = game.DeathCount, level = game.Level, xp = game.Experience;
            int explored = game.Exploration.ExploredTiles;
            var gear = Enum.GetValues<GearSlot>().Select(slot => game.Equipment[slot]).ToArray();
            var inventory = game.Inventory.ToArray();
            window.SpeedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.SpeedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 15; i++) window.AdvanceFrame(.25, i * .25);
            if (window.CurrentPhase != EpisodePhase.Ending || game.Tick != tick)
                throw new InvalidOperationException("16倍速で死亡演出が短縮されました。");
            GameText.English = false;
            window.LanguageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (window.DeathBanner.Text != "YOU DIED" || !window.StoryHint.Text.StartsWith("Reviving at base in 1s."))
                throw new InvalidOperationException("死亡表示の英訳またはカウントダウンが正しくありません。");
            window.AdvanceFrame(.25, 4);
            if (!ReferenceEquals(window.CurrentAdventure, game) || window.CurrentPhase != EpisodePhase.Playing || game.Finished
                || game.Tick != tick || game.Hero != game.RespawnSite.Position || game.Health != game.MaxHealth || game.Mana != game.MaxMana
                || game.DeathCount != deaths || game.Level != level || game.Experience != xp || game.Exploration.ExploredTiles != explored
                || !gear.SequenceEqual(Enum.GetValues<GearSlot>().Select(slot => game.Equipment[slot])) || !inventory.SequenceEqual(game.Inventory)
                || game.RevivalTicks.Last() != tick || window.StoryOverlay.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("自動復活で育成・持ち物・踏破・死亡数または拠点位置が変わりました。");
            return "PASS: death banner and countdown; 4 real seconds even at 16x; localized; automatic full recovery at base in the same adventure, preserving progress and replay revival tick.";
        }
        finally { window.Close(); GameText.English = originalLanguage; }
    }

    internal static async Task<string> RunAudio()
    {
        using var music = new BgmPlayer(persistSettings: false);
        music.SetMuted(true);
        var resources = typeof(BgmPlayer).Assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);
        var results = new List<string>();
        foreach (WorldStyle style in Enum.GetValues<WorldStyle>())
        {
            music.Update(style, false, true);
            if (!resources.Contains($"DotARPG.Assets.Music.{style.ToString().ToLowerInvariant()}.mp3"))
            {
                if (music.Ready) throw new InvalidOperationException("未収録の世界観で曲が止まりませんでした。");
                continue;
            }
            for (int i = 0; i < 150 && !music.Ready && music.LastError == null; i++) await Task.Delay(50);
            if (!music.Ready) throw new InvalidOperationException($"{style}: MP3を開けませんでした: " + music.LastError);
            double duration = music.Duration.TotalSeconds;
            if (duration < 30) throw new InvalidOperationException("BGMの長さを取得できませんでした。");
            int previousLoops = music.LoopCount;
            music.SeekNearEndForVerification();
            for (int i = 0; i < 100 && music.LoopCount == previousLoops; i++) await Task.Delay(50);
            if (music.LoopCount != previousLoops + 1 || music.Position.TotalSeconds >= 5)
                throw new InvalidOperationException($"{style}: 曲末から先頭へループしませんでした。");
            music.Update(style, false, false);
            await Task.Delay(150);
            TimeSpan pausedAt = music.Position;
            await Task.Delay(200);
            if (Math.Abs((music.Position - pausedAt).TotalSeconds) > .1)
                throw new InvalidOperationException("一時停止中も曲が進んでいます。");
            results.Add($"{style} {duration:0.0}s");
        }
        if (resources.Contains("DotARPG.Assets.Music.boss.mp3"))
        {
            music.Update(WorldStyle.Japanese, true, true);
            for (int i = 0; i < 150 && !music.Ready && music.LastError == null; i++) await Task.Delay(50);
            if (!music.Ready || music.CurrentTrack != "boss") throw new InvalidOperationException("ボス曲へ切り替わりませんでした。");
            double duration = music.Duration.TotalSeconds;
            int previousLoops = music.LoopCount;
            music.SeekNearEndForVerification();
            for (int i = 0; i < 100 && music.LoopCount == previousLoops; i++) await Task.Delay(50);
            if (music.LoopCount != previousLoops + 1 || music.Position.TotalSeconds >= 5)
                throw new InvalidOperationException("ボス曲がループしませんでした。");
            music.Update(WorldStyle.Western, true, true);
            if (!music.Ready || music.CurrentTrack != "boss") throw new InvalidOperationException("世界観に共通するボス曲になっていません。");
            music.Update(WorldStyle.Japanese, false, true);
            for (int i = 0; i < 150 && !music.Ready && music.LastError == null; i++) await Task.Delay(50);
            if (!music.Ready || music.CurrentTrack != "japanese") throw new InvalidOperationException("戦闘後にマップ曲へ戻りませんでした。");
            results.Add($"Boss {duration:0.0}s (battle entry, loop, map return)");
        }
        return "PASS: track switching, decoding, actual end looping and pause: " + string.Join(", ", results) + "; verification was muted.";
    }

    internal static string RunResize()
    {
        var window = new MainWindow(WorldStyle.Japanese, 31, false)
        { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show(); window.UpdateLayout();
            nint handle = new WindowInteropHelper(window).Handle;
            GetWindowRect(handle, out var outer); GetClientRect(handle, out var client);
            int frameX = outer.Right - outer.Left - client.Right;
            int frameY = outer.Bottom - outer.Top - client.Bottom;
            nint memory = Marshal.AllocHGlobal(Marshal.SizeOf<SizingRect>());
            try
            {
                for (int edge = 1; edge <= 8; edge++)
                {
                    var proposed = outer; proposed.Right += 211; proposed.Bottom += 97;
                    Marshal.StructureToPtr(proposed, memory, false);
                    SendMessage(handle, 0x0214, edge, memory);
                    var result = Marshal.PtrToStructure<SizingRect>(memory);
                    double width = result.Right - result.Left - frameX, height = result.Bottom - result.Top - frameY;
                    if (Math.Abs(width - height * WindowAspect.ContentWidth / WindowAspect.ContentHeight) > 2)
                        throw new InvalidOperationException("縦横比が変化した: edge=" + edge);
                }
            }
            finally { Marshal.FreeHGlobal(memory); }
            if (Math.Abs(window.Map.ActualWidth / window.Map.ActualHeight - World.Width / (double)World.Height) > .0001 || window.Icon == null)
                throw new InvalidOperationException("地図全体またはアイコンの表示が正しくありません。");
            window.AdvanceForSnapshot(500); window.UpdateLayout();
            if (window.LogPanel.Children.Count != 6 || window.LogScroll.ViewportHeight < 180)
                throw new InvalidOperationException("ログ6件を表示するための高さが不足しています。");
            return $"PASS: all 8 resize edges preserve client aspect; world map fits; journal viewport {window.LogScroll.ViewportHeight:0}px for six entries.";
        }
        finally { window.Close(); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SizingRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out SizingRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out SizingRect rect);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    internal static string RunLanguage()
    {
        bool originalLanguage = GameText.English;
        MainWindow? window = null;
        try
        {
            GameText.English = false;
            window = new MainWindow(WorldStyle.Japanese, 31, false);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = window.Top = -10000; window.ShowInTaskbar = false; window.Show();
            var game = window.CurrentAdventure;
            string japaneseTitle = window.StoryTitle.Text;
            window.LanguageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!GameText.English || window.StoryTitle.Text == japaneseTitle || window.PrimaryStoryButton.Content.ToString() != "Watch Adventure")
                throw new InvalidOperationException("OPの言語切り替えが反映されませんでした。");
            void CheckText(DependencyObject root)
            {
                if (root == window.LanguageButton) return;
                string? text = root is TextBlock block ? block.Text : root is ContentControl { Content: string content } ? content : null;
                if (text != null && System.Text.RegularExpressions.Regex.IsMatch(text, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]") )
                    throw new InvalidOperationException("未翻訳の表示: " + text);
                foreach (object child in LogicalTreeHelper.GetChildren(root))
                    if (child is DependencyObject dependency) CheckText(dependency);
            }
            CheckText(window.Root);
            window.AdvanceForSnapshot(350);
            CheckText(window.Root);
            var data = window.CreatePlayerDataWindow();
            try { CheckText(data.CaptureSurface); }
            finally { data.Close(); }
            int tick = game.Tick, health = game.Health, experience = game.Experience;
            window.LanguageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (GameText.English || window.RealmTitle.Text != japaneseTitle || window.DeathsText.Text != $"死亡 {game.DeathCount} 回"
                || !ReferenceEquals(window.CurrentAdventure, game) || game.Tick != tick || game.Health != health || game.Experience != experience)
                throw new InvalidOperationException("日本語への切り替えで冒険の状態が変化しました。");
            return "PASS: English opening, live UI, logs and player records; Japanese switch preserves the same adventure, tick, HP and EXP.";
        }
        finally { window?.Close(); GameText.English = originalLanguage; }
    }

    internal static async Task<string> Run(string reportPath)
    {
        string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "ui-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        MainWindow Open(bool initial = false)
        {
            var window = initial ? new MainWindow(WorldStyle.Japanese, 31, true, PlayerTemperament.Reckless, directory)
                : new MainWindow(startTimer: true, stateDirectory: directory);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = window.Top = -10000; window.ShowInTaskbar = false;
            window.StopTimerForVerification(); window.Show();
            return window;
        }
        async Task WaitResume(MainWindow window)
        {
            if (await Task.WhenAny(window.ResumeCompleted, Task.Delay(60000)) != window.ResumeCompleted || !await window.ResumeCompleted)
                throw new InvalidOperationException("途中保存の復元が完了しませんでした。");
            window.StopTimerForVerification();
        }
        var first = Open(true);
        Check(first.CurrentPhase == EpisodePhase.Opening, "起動直後にOPが出る");
        for (int i = 0; i < 10; i++) first.AdvanceFrame(.2, i * .2);
        Check(first.CurrentAdventure.Tick == 0, "OP中は冒険が進まない");
        first.ClickStartForVerification(); first.AdvanceFrame(.3, 3);
        Check(first.CurrentPhase == EpisodePhase.Playing && first.CurrentAdventure.Tick > 0, "OPの開始ボタンで冒険が始まる");
        first.AdvanceForSnapshot(0, scene: "ending");
        Check(first.CurrentAdventure.Finished && !first.CurrentAdventure.Won, "死亡時にエンディングが出る");
        int finalTick = first.CurrentAdventure.Tick;
        for (int i = 0; i < 12; i++) first.AdvanceFrame(.25, 4 + i * .25);
        Check(first.CurrentPhase == EpisodePhase.Ending && first.CurrentAdventure.Tick == finalTick, "死亡演出中は冒険が止まる");
        first.Close();
        var ended = Open(); await WaitResume(ended);
        Check(ended.CurrentPhase == EpisodePhase.Ending && ended.CurrentAdventure.Tick == finalTick, "×で終了したエンディングを次回そのまま再開する");
        int explored = ended.CurrentAdventure.Exploration.ExploredTiles;
        int deaths = ended.CurrentAdventure.DeathCount;
        ended.ClickRetryForVerification();
        Check(ended.CurrentPhase == EpisodePhase.Playing && !ended.CurrentAdventure.Finished
            && ended.CurrentAdventure.Hero == ended.CurrentAdventure.RespawnSite.Position
            && ended.CurrentAdventure.DeathCount == deaths && ended.CurrentAdventure.Exploration.ExploredTiles == explored,
            "再挑戦ボタンで死亡数と踏破を保ったまま拠点へ復活する");
        ended.AdvanceForSnapshot(0, scene: "ending");
        string title = ended.CurrentAdventure.Title, oldPlayer = ended.CurrentPlayerId;
        int seed = ended.CurrentAdventure.World.Seed;
        ended.ClickOtherPlayerForVerification();
        await ended.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(ended.CurrentPhase == EpisodePhase.Opening && ended.CurrentAdventure.World.Seed == seed
            && ended.CurrentAdventure.Title == title && ended.CurrentPlayerId != oldPlayer
            && ended.CurrentAdventure.Tick == 0 && ended.CurrentAdventure.Level == 1 && ended.CurrentAdventure.DeathCount == 0
            && ended.CurrentAdventure.Exploration.Fraction < .01,
            "別のプレイヤーは同じ作品を育成も踏破も最初から始める");
        ended.Close();
        var opening = Open(); await WaitResume(opening);
        Check(opening.CurrentPhase == EpisodePhase.Opening && opening.CurrentAdventure.Tick == 0, "OPの途中で×を押してもOPから再開する");
        opening.ClickStartForVerification(); opening.AdvanceFrame(2, 10); opening.PauseForVerification();
        int tick = opening.CurrentAdventure.Tick, hp = opening.CurrentAdventure.Health;
        var hero = opening.CurrentAdventure.Hero;
        string player = opening.CurrentPlayerId;
        opening.Close();
        var continued = Open(); await WaitResume(continued);
        Check(continued.CurrentPhase == EpisodePhase.Playing && continued.CurrentAdventure.Tick == tick
            && continued.CurrentAdventure.Health == hp && continued.CurrentAdventure.Hero == hero
            && continued.CurrentPlayerId == player, "×で保存した途中の位置・HP・プレイヤーを正しく再開する");
        var data = continued.CreatePlayerDataWindow();
        data.WindowStartupLocation = WindowStartupLocation.Manual; data.Left = data.Top = -10000; data.ShowInTaskbar = false;
        data.Show(); data.UpdateLayout();
        Check(data.CaptureSurface.ActualWidth > 0, "プレイデータ画面が開く");
        data.Close(); continued.Close();
        var library = new PlayerLibrary(Path.Combine(directory, "players.json"));
        Check(library.Runs.Any(r => r.Deaths > 0) && library.Runs.Select(r => r.PlayerId).Distinct().Count() >= 2,
            "死亡数と複数プレイヤーの記録が永続化される");
        return System.Text.Json.JsonSerializer.Serialize(new { passed = checks.Count, checks, stateDirectory = directory },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
