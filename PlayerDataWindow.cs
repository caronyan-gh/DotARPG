using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DotARPG.Core;

namespace DotARPG;

public sealed class PlayerDataWindow : Window
{
    private readonly PlayerLibrary library;
    private readonly ListBox players;
    private readonly TextBlock summary;
    private readonly DataGrid history;
    private readonly Image portrait;
    private static readonly Dictionary<PlayerTemperament, BitmapImage> portraits = [];
    public FrameworkElement CaptureSurface { get; }

    public PlayerDataWindow(PlayerLibrary library, string selectedPlayer)
    {
        this.library = library;
        Title = GameText.Text("DotARPG — プレイデータ"); Width = 1040; Height = 720; MinWidth = 900; MinHeight = 600;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/DotARPG.ico"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(17, 30, 34)); Foreground = Brushes.Beige;
        FontFamily = new FontFamily("Yu Gothic UI"); FontSize = 12;
        var root = new Grid { Margin = new Thickness(25), Background = Background };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(new TextBlock { Text = GameText.Text("プレイヤーたちの記録"), FontSize = 24, Margin = new Thickness(0, 0, 0, 20) });
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(body, 1); root.Children.Add(body);
        players = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 42, 44)), Foreground = Brushes.Beige,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 20, 0), Padding = new Thickness(8)
        };
        foreach (var profile in library.Players)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Image { Source = Portrait(profile), Width = 76, Height = 88, Stretch = Stretch.UniformToFill });
            row.Children.Add(new TextBlock
            {
                Text = GameText.Text(profile.Name), VerticalAlignment = VerticalAlignment.Center, FontSize = 14,
                Margin = new Thickness(10, 0, 0, 0), Foreground = Brushes.Beige
            });
            players.Items.Add(new ListBoxItem { Content = row, Tag = profile, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8) });
        }
        body.Children.Add(players);
        var detail = new Grid(); detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detail.RowDefinitions.Add(new RowDefinition());
        Grid.SetColumn(detail, 1); body.Children.Add(detail);
        summary = new TextBlock { FontSize = 14, LineHeight = 26, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
        var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); heading.ColumnDefinitions.Add(new ColumnDefinition());
        portrait = new Image { Width = 142, Height = 142, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 18, 18) };
        heading.Children.Add(portrait);
        Grid.SetColumn(summary, 1); summary.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(summary); detail.Children.Add(heading);
        history = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            Background = new SolidColorBrush(Color.FromRgb(26, 44, 45)), Foreground = Brushes.Black,
            GridLinesVisibility = DataGridGridLinesVisibility.None, BorderThickness = new Thickness(0), RowHeight = 32
        };
        AddColumn("作品", "Title", 210); AddColumn("状態", "Status", 65); AddColumn("死亡", "Deaths", 50);
        AddColumn("Lv", "Level", 40); AddColumn("日数", "Day", 50); AddColumn("踏破", "Explored", 65);
        Grid.SetRow(history, 1); detail.Children.Add(history);
        players.SelectionChanged += (_, _) => RefreshPlayer();
        players.SelectedItem = players.Items.Cast<ListBoxItem>().FirstOrDefault(p => ((PlayerProfile)p.Tag).Id == selectedPlayer)
            ?? players.Items.Cast<ListBoxItem>().FirstOrDefault();
        Content = CaptureSurface = root;
    }
    private void AddColumn(string header, string path, int width) => history.Columns.Add(new DataGridTextColumn
    { Header = GameText.Text(header), Binding = new System.Windows.Data.Binding(path), Width = width });
    private void RefreshPlayer()
    {
        if (players.SelectedItem is not ListBoxItem { Tag: PlayerProfile profile }) return;
        portrait.Source = Portrait(profile);
        var runs = library.Runs.Where(r => r.PlayerId == profile.Id).OrderByDescending(r => r.UpdatedAt).ToArray();
        summary.Text = GameText.Text($"PLAYER  {profile.Name}\nプレイ {runs.Length}回　クリア {runs.Count(r => r.Status == "クリア")}回　死亡 {runs.Sum(r => r.Deaths)}回　討伐 {runs.Sum(r => r.Victories)}体");
        history.ItemsSource = runs.Select(r => new
        {
            Title = GameText.Text(library.Games.FirstOrDefault(g => g.Id == r.GameId)?.Title ?? "不明な作品"),
            Status = GameText.English && r.Status == "死亡" ? "Fallen" : GameText.Text(r.Status), r.Deaths, r.Level, r.Day,
            Explored = r.Explored.ToString("P1")
        }).ToArray();
    }

    private static BitmapImage Portrait(PlayerProfile profile)
    {
        if (portraits.TryGetValue(profile.Temperament, out var cached)) return cached;
        string name = profile.Temperament switch
        {
            PlayerTemperament.Careful => "sui", PlayerTemperament.Balanced => "yuzu",
            PlayerTemperament.Reckless => "rei", _ => "haru"
        };
        var image = new BitmapImage();
        image.BeginInit(); image.UriSource = new Uri($"pack://application:,,,/Assets/Portraits/{name}.png");
        image.DecodePixelWidth = 512; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
        portraits[profile.Temperament] = image;
        return image;
    }
}
