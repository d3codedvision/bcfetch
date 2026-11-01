using System.Diagnostics;

namespace BcFetch.App;

/// <summary>Desktop notification through notify-send; silently does nothing when it is missing.</summary>
internal static class Notifier
{
    public static void Send(string title, string body)
    {
        try
        {
            var psi = new ProcessStartInfo("notify-send") { UseShellExecute = false };
            psi.ArgumentList.Add("--app-name=bcfetch");
            psi.ArgumentList.Add("--icon=bcfetch");
            psi.ArgumentList.Add(title);
            psi.ArgumentList.Add(body);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception)
        {
            // ponytail: no notify-send installed, so no notification; D-Bus client if that matters
        }
    }
}
