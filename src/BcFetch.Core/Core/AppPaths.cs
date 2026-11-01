namespace BcFetch.Core;

internal static class AppPaths
{
    /// <summary>$XDG_CONFIG_HOME/bcfetch, i.e. ~/.config/bcfetch on Linux.</summary>
    public static readonly string ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bcfetch");
}
