using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using BcFetch.Core;
using Res = BcFetch.App.Properties.Resources;

namespace BcFetch.App;

/// <summary>Version, project links and the changelog (plain text, fetched from the repository).</summary>
internal sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = Res.labelListBoxItemAbout.Replace("_", "");
        Width = 640;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var changelog = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "mono" }, Text = ReadChangelog() };
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(-10, 0, 0, 0) };
        foreach (var (text, url) in new[] { (Constants.URL_WEBSITE, Constants.URL_WEBSITE), (Res.labelIssues, Constants.URL_ISSUES), (Res.textBlockReleasesWebsite, Constants.URL_RELEASES) })
        {
            var button = new Button { Content = text, Classes = { "quiet" } };
            button.Click += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            links.Children.Add(button);
        }

        var header = new StackPanel
        {
            Margin = new(20, 16, 20, 8),
            Spacing = 6,
            Children = { new TextBlock { Text = $"bcfetch {Constants.APP_VERSION_FORMATTED}", FontSize = 22, FontWeight = Avalonia.Media.FontWeight.Bold }, links, new TextBlock { Text = Res.labelChangelogTitle } },
        };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        changelog.Margin = new(20, 0, 20, 20);
        root.Children.Add(changelog);
        Content = root;
    }

    /// <summary>CHANGELOG.md is embedded at build time (the repository may be private).</summary>
    private static string ReadChangelog()
    {
        using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("CHANGELOG.md");
        return stream == null ? "" : new StreamReader(stream).ReadToEnd();
    }
}
