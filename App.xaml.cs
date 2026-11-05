using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DotARPG.Core;

namespace DotARPG;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? Argument(string name)
        {
            int index = Array.IndexOf(e.Args, name);
            return index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null;
        }
        string? snapshot = Argument("--snapshot");
        GameText.English = Argument("--lang") is { } language ? language.Equals("en", StringComparison.OrdinalIgnoreCase)
            : snapshot == null && UiText.LoadEnglish();
        string? overlayChecks = Argument("--verify-overlays");
        if (overlayChecks != null)
        {
            try { File.WriteAllText(overlayChecks, OverlayLayoutChecks.Run()); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(overlayChecks, error.ToString()); Shutdown(1); }
            return;
        }
        string? languageVerification = Argument("--verify-language");
        string? resizeVerification = Argument("--verify-resize");
        string? verification = resizeVerification ?? languageVerification ?? Argument("--verify-ui");
        if (verification != null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    string result = resizeVerification != null ? UiChecks.RunResize()
                        : languageVerification != null ? UiChecks.RunLanguage() : await UiChecks.Run(verification);
                    File.WriteAllText(verification, result);
                    Shutdown(0);
                }
                catch (Exception error)
                {
                    File.WriteAllText(verification, error.ToString());
                    Shutdown(1);
                }
            }));
            return;
        }
        WorldStyle? style = Enum.TryParse<WorldStyle>(Argument("--theme"), true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
        int? seed = int.TryParse(Argument("--seed"), out int number) ? number : null;
        PlayerTemperament? temperament = snapshot != null && Enum.TryParse<PlayerTemperament>(Argument("--temperament"), true, out var temperamentValue)
            && Enum.IsDefined(temperamentValue) ? temperamentValue : null;
        var window = new MainWindow(style, seed, snapshot == null, temperament);
        MainWindow = window;
        if (snapshot != null)
        {
            window.Left = -10000;
            window.Top = -10000;
            window.ShowInTaskbar = false;
            window.Width = int.TryParse(Argument("--width"), out int captureWidth) ? Math.Clamp(captureWidth, 960, 3000) : 1500;
            window.Height = int.TryParse(Argument("--height"), out int captureHeight) ? Math.Clamp(captureHeight, 600, 2000) : 960;
        }
        window.Show();
        if (snapshot != null)
        {
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    string? scene = Argument("--scene");
                    window.AdvanceForSnapshot(int.TryParse(Argument("--ticks"), out int ticks) ? Math.Clamp(ticks, 0, 120000) : 170, Argument("--encounter"), scene);
                    window.UpdateLayout();
                    FrameworkElement surface = window.Root;
                    if (scene == "data")
                    {
                        PlayerDataWindow data = window.CreatePlayerDataWindow();
                        data.WindowStartupLocation = WindowStartupLocation.Manual;
                        data.Left = data.Top = -10000; data.ShowInTaskbar = false;
                        data.Show(); data.UpdateLayout(); surface = data.CaptureSurface;
                    }
                    var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(surface);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(snapshot))!);
                    using var output = File.Create(snapshot);
                    encoder.Save(output);
                }
                catch (Exception error)
                {
                    File.WriteAllText(snapshot + ".error.txt", error.ToString());
                    Environment.ExitCode = 1;
                }
                finally { Shutdown(); }
            }));
        }
    }
}
