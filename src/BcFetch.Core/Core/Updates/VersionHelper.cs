using System;

namespace BcFetch.Core.Updates;

internal static class VersionHelper
{
    public static bool IsNewerVersion(this Version version)
    {
        return Constants.APP_VERSION.CompareTo(version) < 0;
    }
}
