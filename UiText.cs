using System.IO;
using System.Windows;
using System.Windows.Controls;
using DotARPG.Core;

namespace DotARPG;

internal sealed class UiText
{
    private readonly List<Action> updates = [];
    private static string PreferencePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotARPG", "language.txt");

    public UiText(DependencyObject root) => Capture(root);
    private void Capture(DependencyObject element)
    {
        if (element is TextBlock { Text.Length: > 0 } text)
        {
            string original = text.Text;
            updates.Add(() => text.Text = GameText.Text(original));
        }
        if (element is ContentControl { Content: string originalContent } content)
            updates.Add(() => content.Content = GameText.Text(originalContent));
        if (element is FrameworkElement { ToolTip: string originalTip } control)
            updates.Add(() => control.ToolTip = GameText.Text(originalTip));
        foreach (object child in LogicalTreeHelper.GetChildren(element))
            if (child is DependencyObject dependency) Capture(dependency);
    }
    public void Apply() { foreach (Action update in updates) update(); }

    public static bool LoadEnglish()
    {
        try { return File.Exists(PreferencePath) && File.ReadAllText(PreferencePath).Trim() == "en"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
    public static bool SaveLanguage()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, GameText.English ? "en" : "ja");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
