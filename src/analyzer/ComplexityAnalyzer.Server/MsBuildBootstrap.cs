using Microsoft.Build.Locator;

namespace ComplexityAnalyzer.Server;

/// <summary>
/// Registers MSBuild before any Workspaces.MSBuild types are loaded.
/// Must stay in a type that does not reference MSBuildWorkspace.
/// </summary>
internal static class MsBuildBootstrap
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered) return;
            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();
            _registered = true;
        }
    }
}
