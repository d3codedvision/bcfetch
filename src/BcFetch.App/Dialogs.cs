using Avalonia.Controls;
using Avalonia.Layout;

namespace BcFetch.App;

internal static class Dialogs
{
    /// <summary>Modal OK/Cancel box. Returns true when the OK button is pressed.</summary>
    public static async Task<bool> ConfirmAsync(Window owner, string text, string okText, string cancelText)
    {
        var dialog = new Window
        {
            Title = "bcfetch",
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 450,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = okText, MinWidth = 90 };
        var cancel = new Button { Content = cancelText, MinWidth = 90 };
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel
        {
            Margin = new(15),
            Spacing = 15,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        return await dialog.ShowDialog<bool>(owner);
    }
}
