using Res = BcFetch.App.Properties.Resources;

namespace BcFetch.App;

/// <summary>Looks up a UI string by key in the current language (English when a language lacks it).</summary>
internal static class Tr
{
    public static string Get(string key, params object[] args)
    {
        var text = Res.ResourceManager.GetString(key, Res.Culture) ?? key;
        return args.Length == 0 ? text : string.Format(text, args);
    }
}
