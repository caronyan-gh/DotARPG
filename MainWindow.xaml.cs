using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DotARPG.Core;

namespace DotARPG;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly int[] speeds = [1, 4, 16];
    private readonly Chronicle? chronicle;
    private readonly JourneyCheckpointStore? checkpoints;
    private readonly PlayerLibrary library;
    private readonly bool persistent;
    private readonly PlayerTemperament? previewTemperament;
    private Adventure adventure = null!;
    private GameDefinition definition = null!;
    private PlayerProfile player = null!;
    private EpisodeFlow flow = new();
    private EpisodePhase? displayedPhase;
    private string runId = "";
    private WorldStyle? preferredStyle;
    private int speedIndex, lastLogTick = -1, lastLogCount = -1;
    private bool paused, fullscreen, generationPending, remembered, restoring, closed;
    private double previous, accumulator, sinceSave;
    private WindowState previousState;
    private string inventorySignature = "";
    private readonly TaskCompletionSource<bool> resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly UiText uiText;
    private readonly BgmPlayer? music;
    private static string T(string text) => GameText.Text(text);
    internal Adventure CurrentAdventure => adventure;
    internal EpisodePhase CurrentPhase => flow.Phase;
    internal Task<bool> ResumeCompleted => resumed.Task;

    public MainWindow(WorldStyle? style = null, int? seed = null, bool startTimer = true, PlayerTemperament? previewTemperament = null, string? stateDirectory = null)
    {
        InitializeComponent();
        WindowAspect.Attach(this);
        persistent = startTimer;
        music = persistent ? new BgmPlayer() : null;
        if (music != null) music.PlaybackFailed += () => adventure?.Notify("BGMを再生できませんでした。");
        this.previewTemperament = previewTemperament;
        Width = Math.Max(MinWidth, Math.Min(1500, SystemParameters.WorkArea.Width * .94));
        Height = Math.Max(MinHeight, Math.Min(960, SystemParameters.WorkArea.Height * .94));
        SizeChanged += (_, _) =>
        {
            StoryScroll.MaxHeight = Math.Max(260, Root.ActualHeight - 100);
        };
        foreach (var entry in EnemyColors.Legend)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 11, 6) };
            item.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 4, Height = 4, Margin = new Thickness(0, 0, 5, 0),
                Fill = new SolidColorBrush(Color.FromRgb((byte)(entry.Color >> 16), (byte)(entry.Color >> 8), (byte)entry.Color))
            });
            item.Children.Add(new TextBlock { Text = entry.Label, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(165, 183, 165)) });
            LegendPanel.Children.Add(item);
        }
        uiText = new UiText(this);
        uiText.Apply();
        LanguageButton.Content = GameText.English ? "日本語" : "English";
        UpdateAudioButton();
        preferredStyle = style;
        chronicle = persistent ? new Chronicle(stateDirectory == null ? null : System.IO.Path.Combine(stateDirectory, "chronicle.json")) : null;
        checkpoints = persistent ? new JourneyCheckpointStore(stateDirectory == null ? null : System.IO.Path.Combine(stateDirectory, "current-journey.json")) : null;
        library = new PlayerLibrary(persistent ? stateDirectory == null ? null : System.IO.Path.Combine(stateDirectory, "players.json")
            : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DotARPG-preview-" + Guid.NewGuid().ToString("N") + ".json"));
        UpdateThemeButton();
        timer.Tick += Update;
        Closed += (_, _) => { closed = true; timer.Stop(); music?.Dispose(); };
        Closing += SaveOnClosing;
        JourneyCheckpoint? resume = seed == null && style == null ? checkpoints?.Load() : null;
        if (resume != null)
        {
            restoring = true;
            ShowLoading();
            Loaded += async (_, _) => await RestoreJourney(resume);
        }
        else
        {
            Generate(seed);
            if (checkpoints?.Notice is { } notice) adventure.Notify(notice);
            if (startTimer) timer.Start();
            resumed.TrySetResult(true);
        }
    }

    private void ShowLoading()
    {
        StoryOverlay.Visibility = Visibility.Visible;
        StoryEyebrow.Text = "CONTINUE";
        StoryTitle.Text = T("旅の記憶をたどっています");
        StorySubtitle.Text = T("前回閉じた場所から再開します");
        StoryNarrative.Text = "";
        StorySummary.Text = "";
        PrimaryStoryButton.Visibility = RetryStoryButton.Visibility = OtherPlayerButton.Visibility = Visibility.Collapsed;
        StoryDataButton.Visibility = Visibility.Collapsed;
        StoryHint.Text = T("少しだけお待ちください。");
    }

    private async Task RestoreJourney(JourneyCheckpoint save)
    {
        try
        {
            Adventure restored = await Task.Run(() => JourneyCheckpointStore.Replay(save));
            if (closed) return;
            adventure = restored;
            definition = library.Games.FirstOrDefault(g => g.Id == save.GameId)
                ?? new(save.GameId ?? GameId(restored), save.Seed, save.Style, save.PreviousStory, restored.Title, restored.World.ExpandedProgression);
            player = library.Players.FirstOrDefault(p => p.Id == save.PlayerId)
                ?? PlayerProfile.Create(save.Temperament) with { Id = save.PlayerId ?? PlayerProfile.Create(save.Temperament).Id };
            runId = save.RunId ?? Guid.NewGuid().ToString("N");
            speedIndex = save.SpeedIndex;
            SpeedButton.Content = $"{speeds[speedIndex]}×";
            flow.Restore(save.Phase, save.OpeningElapsed);
            remembered = false;
            SetupAdventure();
            SetPaused(save.Paused);
            if (adventure.Finished) RememberEnding();
            adventure.Notify("前回の旅の続きに戻った。");
            ShowPresentation();
        }
        catch (Exception error) when (error is System.IO.IOException or InvalidOperationException or ArgumentException)
        {
            if (closed) return;
            Generate();
            adventure.Notify("前回の旅を再現できませんでした。新しい旅を始めます。");
        }
        finally
        {
            restoring = false;
            if (!closed) { previous = clock.Elapsed.TotalSeconds; timer.Start(); }
            resumed.TrySetResult(!closed && adventure != null);
        }
    }

    private static string GameId(Adventure game) => $"{game.World.Theme.Style}-{game.World.Seed:X8}-{game.Title}";
    private PlayerProfile ChoosePlayer(string? except = null) => library.Players.Where(p => p.Id != except).OrderBy(_ => Random.Shared.Next()).First();

    private void Generate(int? seed = null, GameDefinition? replay = null, PlayerProfile? requestedPlayer = null)
    {
        if (replay == null && !seed.HasValue && persistent && library.Games.Count > 0 && Random.Shared.NextDouble() < .15)
        {
            var eligible = library.Games.Where(g => preferredStyle == null || g.Style == preferredStyle).ToArray();
            if (eligible.Length > 0)
            {
                replay = eligible[Random.Shared.Next(eligible.Length)];
                string? lastPlayer = library.Runs.Where(r => r.GameId == replay.Id).MaxBy(r => r.UpdatedAt)?.PlayerId;
                requestedPlayer = ChoosePlayer(lastPlayer);
            }
        }
        int worldSeed = replay?.Seed ?? seed ?? Random.Shared.Next(int.MaxValue);
        SagaMemory? prior = replay?.PreviousStory ?? (seed.HasValue || replay != null ? null : chronicle?.ChooseSequel(Random.Shared, preferredStyle));
        WorldStyle style = replay?.Style ?? prior?.Style ?? preferredStyle ?? (WorldStyle)Random.Shared.Next(Theme.All.Length);
        player = requestedPlayer ?? (!persistent ? library.Players.First(p => p.Temperament == (previewTemperament ?? PlayerTemperament.Balanced))
            : previewTemperament is { } requestedTemperament ? library.Players.First(p => p.Temperament == requestedTemperament)
            : prior != null ? library.Players.FirstOrDefault(p => p.Id == prior.PlayerId)
                ?? library.Players.First(p => p.Temperament == prior.Temperament) : ChoosePlayer());
        adventure = new Adventure(new World(worldSeed, style, replay?.ExpandedProgression ?? true), prior, player.Temperament, player.Id);
        definition = replay ?? new(GameId(adventure), worldSeed, style, prior, adventure.Title, adventure.World.ExpandedProgression);
        runId = Guid.NewGuid().ToString("N");
        flow = new EpisodeFlow();
        remembered = false;
        SetPaused(false);
        SetupAdventure();
        ShowPresentation();
        SaveProgress();
    }

    private void SetupAdventure()
    {
        Map.SetAdventure(adventure);
        RefreshNames();
        inventorySignature = "";
        accumulator = sinceSave = 0;
        previous = clock.Elapsed.TotalSeconds;
        lastLogTick = lastLogCount = -1;
        displayedPhase = null;
        library.Register(definition);
        RefreshLogs();
        UpdateMusic();
    }

    private void RefreshNames()
    {
        RealmTitle.Text = T(adventure.Title);
        Title = T($"DotARPG — {adventure.Title} · {player.Name}");
        WorldInfo.Text = T($"{adventure.World.Theme.Label}  /  {adventure.World.Name}");
        HeroNameText.Text = T(adventure.HeroName);
        PlayerNameText.Text = T($"PLAYER  {player.Name}");
    }

    private void ChangeLanguage(object sender, RoutedEventArgs e)
    {
        GameText.English = !GameText.English;
        uiText.Apply();
        LanguageButton.Content = GameText.English ? "日本語" : "English";
        UpdateThemeButton();
        UpdateAudioButton();
        if (restoring) ShowLoading();
        else if (adventure != null)
        {
            RefreshNames();
            inventorySignature = "";
            lastLogTick = lastLogCount = -1;
            displayedPhase = null;
            ShowPresentation();
            RefreshLogs();
            Map.InvalidateVisual();
        }
        if (persistent && !UiText.SaveLanguage()) adventure?.Notify("言語設定を保存できませんでした。");
    }

    private void Update(object? sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalSeconds;
        double elapsed = Math.Min(.25, now - previous);
        previous = now;
        AdvanceFrame(elapsed, now);
    }

    internal void AdvanceFrame(double elapsed, double now)
    {
        if (restoring || adventure == null) return;
        if (flow.Phase == EpisodePhase.Opening)
        {
            flow.Advance(elapsed);
            ShowPresentation();
        }
        else if (flow.Phase == EpisodePhase.Playing && !paused && !generationPending)
        {
            accumulator += elapsed * (adventure.Combat is { Major: true } ? 1 : speeds[speedIndex]);
            while (accumulator >= .085)
            {
                accumulator -= .085;
                adventure.Step();
                if (adventure.Combat is { Major: true }) accumulator = Math.Min(accumulator, .085);
                if (adventure.Finished)
                {
                    flow.Finish();
                    accumulator = 0;
                    RememberEnding();
                    ShowPresentation();
                    SaveProgress();
                    break;
                }
            }
        }
        UpdateMusic();
        Map.Refresh(now, freeze: paused || flow.Phase != EpisodePhase.Playing);
        RefreshLogs();
        sinceSave += elapsed;
        if (persistent && sinceSave >= 30) { sinceSave = 0; SaveProgress(); }
    }

    private void RememberEnding()
    {
        if (remembered || !adventure.Finished) return;
        remembered = true;
        if (chronicle != null && !chronicle.Remember(adventure)) adventure.Notify(chronicle.Error!);
    }

    private void ShowPresentation()
    {
        if (flow.Phase == EpisodePhase.Playing)
        {
            StoryOverlay.Visibility = Visibility.Collapsed;
            displayedPhase = flow.Phase;
            return;
        }
        StoryOverlay.Visibility = Visibility.Visible;
        if (flow.Phase == EpisodePhase.Opening)
            StoryHint.Text = T($"まもなく冒険が始まります（{Math.Ceiling(EpisodeFlow.OpeningDuration - flow.OpeningElapsed):0}秒） · Enterで観戦へ");
        if (displayedPhase == flow.Phase) return;
        displayedPhase = flow.Phase;
        StoryTitle.Text = T(adventure.Title);
        StorySubtitle.Text = T($"{adventure.World.Theme.Label} · {adventure.World.Name}　／　PLAYER {player.Name}");
        PrimaryStoryButton.Visibility = Visibility.Visible;
        StoryDataButton.Visibility = Visibility.Visible;
        if (flow.Phase == EpisodePhase.Opening)
        {
            StoryEyebrow.Text = T(adventure.Part > 1 ? "PROLOGUE  /  ふたたびの旅" : "PROLOGUE  /  小さな旅のはじまり");
            string purpose = adventure.Story switch
            {
                StoryKind.Restore => "失われた宝を求めて。",
                StoryKind.Rescue => "捕らわれた人々を救うために。",
                _ => "目覚めた災いを、もう一度封じるために。"
            };
            StoryNarrative.Text = T($"{adventure.Premise}\n\n{adventure.HeroName}は、{adventure.World.Sites[0].Name}を発つ。\n{purpose}");
            StorySummary.Text = T(definition.PreviousStory is { } prior ? $"前の旅では、{prior.Summary}" : "小さな点が、この広い世界を歩き始める。");
            PrimaryStoryButton.Content = T("冒険を眺める");
            RetryStoryButton.Visibility = OtherPlayerButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            StoryEyebrow.Text = T(adventure.Won ? "EPILOGUE  /  旅の完結" : "EPILOGUE  /  ここで途絶えた旅");
            StoryNarrative.Text = T(adventure.Epilogue + (adventure.Won ? "" : $"\n{adventure.DeathCause}との戦いで、力尽きた。"));
            StorySummary.Text = T($"{adventure.HeroName}　Lv.{adventure.Level}\n{adventure.Day}日間の旅 · 討伐 {adventure.Victories} · 死亡 {adventure.DeathCount}回 · 踏破 {adventure.Exploration.Fraction:P1}");
            PrimaryStoryButton.Content = T("次の冒険を開始");
            RetryStoryButton.Visibility = adventure.Won ? Visibility.Collapsed : Visibility.Visible;
            OtherPlayerButton.Visibility = Visibility.Visible;
            StoryHint.Text = T("次の冒険を選ぶまで、この旅の終わりに留まります。\n×や「終了」で閉じても、この画面から再開できます。");
        }
        StoryOverlay.BeginAnimation(OpacityProperty, null);
        StoryOverlay.Opacity = 1;
    }

    private void PrimaryStoryAction(object sender, RoutedEventArgs e)
    {
        if (restoring) return;
        if (flow.Phase == EpisodePhase.Opening)
        {
            flow.Start(); accumulator = 0; ShowPresentation(); SaveProgress();
        }
        else if (flow.Phase == EpisodePhase.Ending) RequestWorld();
    }
    private void RetryAdventure(object sender, RoutedEventArgs e)
    {
        if (restoring || !adventure.ReviveAtBase()) return;
        flow.Retry(); remembered = false; SetPaused(false); Map.SetAdventure(adventure);
        displayedPhase = null; ShowPresentation(); RefreshLogs(); SaveProgress();
    }
    private void ReplayWithOtherPlayer(object sender, RoutedEventArgs e) => RequestWorld(definition, ChoosePlayer(player.Id));
    private void ExitGame(object sender, RoutedEventArgs e) => Close();

    private bool SaveProgress(bool interrupted = false)
    {
        if (!persistent || adventure == null || restoring) return true;
        library.Record(runId, definition, player, adventure, interrupted);
        bool history = library.Save();
        var save = new JourneyCheckpoint(JourneyCheckpointStore.SimulationVersion, definition.Seed, definition.Style,
            definition.PreviousStory, player.Temperament, adventure.Tick, adventure.Title, flow.Phase,
            flow.OpeningElapsed, speedIndex, paused, adventure.RevivalTicks.ToArray(), player.Id, player.Name, definition.Id, runId,
            adventure.CombatRulesUpgradeTick, adventure.TownRulesUpgradeTick, adventure.World.ExpandedProgression);
        bool progress = checkpoints!.Save(save);
        if (!history || !progress) adventure.Notify(checkpoints.Notice ?? library.Notice ?? "保存できませんでした。");
        return history && progress;
    }
    private void SaveOnClosing(object? sender, CancelEventArgs e)
    {
        if (SaveProgress(true)) return;
        if (MessageBox.Show(this, T("途中保存を書き込めませんでした。このまま終了しますか？\n前回の保存は残っています。"), "DotARPG", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) e.Cancel = true;
    }

    internal PlayerDataWindow CreatePlayerDataWindow()
    {
        library.Record(runId, definition, player, adventure);
        if (persistent) library.Save();
        return new PlayerDataWindow(library, player.Id) { Owner = this };
    }
    private void ShowPlayerData(object sender, RoutedEventArgs e)
    {
        if (restoring) return;
        bool oldPause = paused;
        SetPaused(true);
        try { CreatePlayerDataWindow().ShowDialog(); }
        finally { SetPaused(oldPause); }
    }

    private void RefreshLogs()
    {
        LevelText.Text = $"Lv.{adventure.Level:00}   EXP {adventure.ExperienceInLevel}/{adventure.ExperienceToNextLevel}";
        HealthText.Text = $"{adventure.Health} / {adventure.MaxHealth}";
        HealthBar.Maximum = adventure.MaxHealth; HealthBar.Value = adventure.Health;
        ManaText.Text = $"{adventure.Mana} / {adventure.MaxMana}";
        ManaBar.Maximum = adventure.MaxMana; ManaBar.Value = adventure.Mana;
        StatusText.Text = T(adventure.Status);
        StatusText.ToolTip = StatusText.Text;
        DeathsText.Text = T($"死亡 {adventure.DeathCount} 回");
        MapRealm.Text = T($"{adventure.World.Name} · {adventure.Region}（敵Lv.{adventure.World.RegionLevel(adventure.Hero)}）· 踏破 {adventure.Exploration.Fraction:P1}");
        CombatStatsText.Text = T($"ATK {adventure.AttackPower:00}  DEF {adventure.DefensePower:00}\n討伐 {adventure.Victories} · 回避 {adventure.Dodges}");
        Creature? enemy = adventure.Combat?.Opponent;
        EnemyNameText.Text = T(enemy != null ? $"{enemy.Name}  Lv.{enemy.Level}" : "交戦中の敵：—");
        EnemyNameText.ToolTip = EnemyNameText.Text;
        EnemyStatsText.Text = enemy != null ? $"HP {enemy.Health}/{enemy.MaxHealth}  ATK {enemy.Attack}  DEF {enemy.Defense}" : "HP —  ATK —  DEF —";
        WeaponText.Text = T(adventure.Weapon); ArmorText.Text = T(adventure.Armor); CharmText.Text = T(adventure.Charm);
        HelmetText.Text = T(adventure.Helmet); GlovesText.Text = T(adventure.Gloves); BootsText.Text = T(adventure.Boots);
        string signature = string.Join(';', adventure.Inventory.Select(i => $"{i.Key}:{i.Value}"));
        ItemCountText.Text = T($"{adventure.Inventory.Values.Sum()} 個 · {adventure.Gold} G");
        if (signature != inventorySignature)
        {
            inventorySignature = signature;
            InventoryPanel.Children.Clear();
            foreach (var item in adventure.Inventory.Where(i => i.Value > 0))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                row.Children.Add(new TextBlock { Text = T(item.Key), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(183, 195, 175)) });
                row.Children.Add(new TextBlock { Text = $"× {item.Value}", HorizontalAlignment = HorizontalAlignment.Right, FontFamily = new FontFamily("Consolas"), FontSize = 11 });
                InventoryPanel.Children.Add(row);
            }
        }
        ObjectiveText.Text = T(adventure.Objective);
        DayLabel.Text = $"DAY {adventure.Day:00}";
        NextWorldHint.Text = "";
        int tick = adventure.Logs.Count == 0 ? -1 : adventure.Logs[^1].Tick;
        if (lastLogTick == tick && lastLogCount == adventure.Logs.Count) return;
        lastLogTick = tick; lastLogCount = adventure.Logs.Count;
        LogPanel.Children.Clear();
        var logs = adventure.Logs.TakeLast(6).ToArray();
        for (int i = 0; i < logs.Length; i++)
        {
            LogEntry entry = logs[i];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7), Opacity = .43 + .57 * (i + 1) / logs.Length };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = $"{Adventure.DayAt(entry.Tick):00}", FontSize = 10, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Color.FromRgb(128, 155, 142)), Margin = new Thickness(0, 2, 0, 0) });
            var message = new TextBlock
            {
                Text = T(entry.Text), TextWrapping = TextWrapping.Wrap, FontSize = 11, LineHeight = 18,
                Foreground = new SolidColorBrush(entry.Tone switch
                {
                    LogTone.Danger => Color.FromRgb(232, 153, 125), LogTone.Discovery => Color.FromRgb(224, 204, 145),
                    LogTone.Ending => Color.FromRgb(255, 232, 177), LogTone.Quiet => Color.FromRgb(161, 184, 162), _ => Color.FromRgb(218, 222, 203)
                })
            };
            Grid.SetColumn(message, 1); row.Children.Add(message); LogPanel.Children.Add(row);
        }
        LogScroll.ScrollToEnd();
    }

    private void TogglePause(object sender, RoutedEventArgs e) { if (flow.Phase == EpisodePhase.Playing) SetPaused(!paused); }
    private void SetPaused(bool value)
    {
        paused = value; PauseButton.Content = paused ? "▷" : "Ⅱ";
        PauseOverlay.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        UpdateMusic();
    }

    private void UpdateMusic()
    {
        if (adventure == null) return;
        music?.Update(adventure.World.Theme.Style, adventure.Combat is { Major: true },
            flow.Phase == EpisodePhase.Playing && !paused && !restoring && !generationPending);
    }
    private void UpdateAudioButton()
    {
        MusicButton.IsEnabled = music != null;
        MusicButton.Content = music is { Muted: true } or { Volume: <= 0 } ? "♫×" : "♫";
        MusicButton.ToolTip = T("BGM音量・ミュート");
    }
    private void ChooseAudio(object sender, RoutedEventArgs e)
    {
        if (music == null) return;
        var menu = CreateAudioMenu(music);
        MusicButton.ContextMenu = menu; menu.PlacementTarget = MusicButton; menu.IsOpen = true;
    }
    internal ContextMenu CreateAudioMenu(BgmPlayer audio)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("AudioMenuStyle") };
        var itemStyle = (Style)FindResource("AudioMenuItemStyle");
        var mute = new MenuItem { Style = itemStyle, Header = T("ミュート"), IsCheckable = true, IsChecked = audio.Muted, StaysOpenOnClick = true };
        mute.Click += (_, _) => { audio.SetMuted(mute.IsChecked); UpdateAudioButton(); };
        menu.Items.Add(mute);
        var panel = new StackPanel { Margin = new Thickness(4), Width = 160 };
        var label = new TextBlock { Text = T("BGM音量") + $" {audio.Volume:P0}", Foreground = new SolidColorBrush(Color.FromRgb(201, 185, 140)) };
        var slider = new Slider { Style = (Style)FindResource("AudioVolumeSliderStyle"), Minimum = 0, Maximum = 100, Value = audio.Volume * 100, TickFrequency = 5, Margin = new Thickness(0, 7, 0, 3) };
        slider.ValueChanged += (_, _) => { audio.SetVolume(slider.Value / 100); label.Text = T("BGM音量") + $" {audio.Volume:P0}"; UpdateAudioButton(); };
        panel.Children.Add(label); panel.Children.Add(slider);
        menu.Items.Add(new MenuItem { Style = itemStyle, Header = panel, StaysOpenOnClick = true });
        return menu;
    }
    private void ChangeSpeed(object sender, RoutedEventArgs e)
    {
        speedIndex = (speedIndex + 1) % speeds.Length; SpeedButton.Content = $"{speeds[speedIndex]}×";
    }
    private void NewWorld(object sender, RoutedEventArgs e) => RequestWorld();
    private void RequestWorld(GameDefinition? replay = null, PlayerProfile? other = null)
    {
        if (generationPending || restoring) return;
        SaveProgress(true);
        generationPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try { Generate(replay: replay, requestedPlayer: other); }
            finally { generationPending = false; }
        }));
    }
    private void UpdateThemeButton() => ThemeButton.Content = T($"世界観：{(preferredStyle is { } style ? Theme.Get(style).Label : "おまかせ")} ▾");
    private void ChooseTheme(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        void AddItem(string label, WorldStyle? style)
        {
            var item = new MenuItem { Header = T(label), IsCheckable = true, IsChecked = preferredStyle == style };
            item.Click += (_, _) => { preferredStyle = style; UpdateThemeButton(); RequestWorld(); }; menu.Items.Add(item);
        }
        AddItem("おまかせ（毎回変わる）", null);
        foreach (Theme theme in Theme.All) AddItem(theme.Label, theme.Style);
        ThemeButton.ContextMenu = menu; menu.PlacementTarget = ThemeButton; menu.IsOpen = true;
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat || restoring) return;
        if (e.Key == Key.Enter && flow.Phase == EpisodePhase.Opening) { PrimaryStoryAction(this, e); e.Handled = true; }
        else if (e.Key == Key.Space && flow.Phase == EpisodePhase.Playing) { SetPaused(!paused); e.Handled = true; }
        else if (e.Key == Key.N) { RequestWorld(); e.Handled = true; }
        else if (e.Key == Key.F11)
        {
            fullscreen = !fullscreen;
            if (fullscreen)
            {
                previousState = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
            }
            else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; WindowState = previousState; }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && fullscreen)
            OnKeyDown(this, new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.F11) { RoutedEvent = Keyboard.KeyDownEvent });
    }

    internal void AdvanceForSnapshot(int ticks, string? encounter = null, string? scene = null)
    {
        if (scene == "opening") { ShowPresentation(); StoryOverlay.BeginAnimation(OpacityProperty, null); StoryOverlay.Opacity = 1; return; }
        flow.Start();
        for (int i = 0; i < ticks && !adventure.Finished; i++) adventure.Step();
        if (scene == "after-midboss")
            for (int i = 0; i < 120000 && !adventure.Finished && adventure.MidbossVictories == 0; i++) adventure.Step();
        else if (scene is "ending" or "retry")
            for (int i = 0; i < 120000 && !adventure.Finished; i++) adventure.Step();
        else if (encounter is "boss" or "midboss" or "dodge")
            for (int i = 0; i < 120000 && !adventure.Finished; i++)
            {
                adventure.Step();
                if (adventure.Combat is { Major: true } battle
                    && (encounter != "boss" || battle.Opponent.Kind == EnemyKind.Boss)
                    && (encounter != "midboss" || battle.Opponent.Kind == EnemyKind.Midboss)
                    && (encounter == "dodge" ? battle.Dashing : battle.Phase == BattlePhase.Windup && battle.WindupProgress > .3f)) break;
            }
        if (adventure.Finished) flow.Finish();
        ShowPresentation();
        if (scene == "retry") RetryAdventure(this, new RoutedEventArgs());
        StoryOverlay.BeginAnimation(OpacityProperty, null); StoryOverlay.Opacity = 1;
        RefreshLogs(); Map.Refresh(1, true);
    }

    internal void StopTimerForVerification() => timer.Stop();
    internal string CurrentPlayerId => player.Id;
    internal void ClickStartForVerification() => PrimaryStoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal void ClickRetryForVerification() => RetryStoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal void ClickOtherPlayerForVerification() => OtherPlayerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal void PauseForVerification() => SetPaused(true);
}
