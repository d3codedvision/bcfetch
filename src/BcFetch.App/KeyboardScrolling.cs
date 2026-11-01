using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace BcFetch.App;

/// <summary>
/// Makes a focusable ScrollViewer scrollable with the keyboard. Avalonia scrolls a page for PageUp/PageDown only;
/// the arrow keys, Home and End do nothing, so a keyboard user could not read past the first rows.
/// </summary>
internal static class KeyboardScrolling
{
    private const double Line = 28;

    public static void Enable(ScrollViewer scroll)
    {
        scroll.KeyDown += (_, e) =>
        {
            // Leave Shift/Ctrl/Alt combinations to the control inside (text selection, shortcuts)
            if (e.KeyModifiers != KeyModifiers.None)
            {
                return;
            }

            var y = scroll.Offset.Y;
            var max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            var target = e.Key switch
            {
                Key.Down => y + Line,
                Key.Up => y - Line,
                Key.PageDown => y + scroll.Viewport.Height,
                Key.PageUp => y - scroll.Viewport.Height,
                Key.Home => 0,
                Key.End => max,
                _ => double.NaN,
            };
            if (double.IsNaN(target))
            {
                return;
            }

            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(target, 0, max));
            e.Handled = true;
        };
    }
}
