using System.Reflection;

namespace GwyfCommunity.Setup;

/// <summary>
/// Owns mutation of the game installation on behalf of the setup exe:
/// deploys the embedded mod release zip, seeds the config file, records what
/// this installer did, and removes it all on uninstall. The mod observes the
/// game; the installer owns installation. That boundary stays deliberate.
/// </summary>
internal static class ModDeployer
{
    public const string PluginDirName = "GWYFCommunityNetwork";
    public const string ConfigFileName = "com.gwyf.community.cfg";
    public const string StateDirName = "gwyf-community";
    public const string SetupMarkerFileName = ".gwyf-community-setup.json";

    public static string PluginDir(string gameRoot) =>
        Path.Combine(gameRoot, "BepInEx", "plugins", PluginDirName);

    public static string ConfigPath(string gameRoot) =>
        Path.Combine(gameRoot, "BepInEx", "config", ConfigFileName);

    public static string StateDir(string gameRoot) =>
        Path.Combine(gameRoot, "BepInEx", StateDirName);

    public static string SetupMarkerPath(string gameRoot) =>
        Path.Combine(gameRoot, SetupMarkerFileName);

    public static bool IsInstalled(string gameRoot) =>
        File.Exists(Path.Combine(PluginDir(gameRoot), "GWYFCommunityNetwork.dll"));

    public static string ReadModVersion(string gameRoot)
    {
        try
        {
            var dll = Path.Combine(PluginDir(gameRoot), "GWYFCommunityNetwork.dll");
            return File.Exists(dll)
                ? System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).ProductVersion ?? "?"
                : "not installed";
        }
        catch (Exception)
        {
            return "?";
        }
    }

    /// <summary>Deploys the embedded mod release zip into the game root.</summary>
    public static void Deploy(string gameRoot, Action<int> progress, CancellationToken ct)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(BuildInfo.ModZipResource)
            ?? throw new InvalidOperationException(
                "setup exe missing its embedded mod package (re-run scripts/build-setup.ps1)");
        ZipTool.Extract(stream, gameRoot, progress, ct);
    }

    /// <summary>
    /// Seeds BepInEx config only when missing, so existing installs keep their
    /// settings. Values mirror the keys the mod binds in GameRuntime/Plugin.cs.
    /// </summary>
    public static bool EnsureConfig(string gameRoot)
    {
        var path = ConfigPath(gameRoot);
        if (File.Exists(path))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);

        var config = string.Join("\n",
            "# GWYF Community Network",
            $"# Seeded by GWYF-Community-Setup v{BuildInfo.SetupVersion}. A normal install",
            "# never needs this file; advanced users may edit it.",
            "",
            "[network]",
            "enabled = true",
            $"apiBaseUrl = \"{BuildInfo.IngestBaseUrl}\"",
            "",
            "[observation]",
            "lobbyEnabled = true",
            "courseEnabled = false",
            "activeRadar = false",
            "",
            "[queue]",
            "maxPendingItems = 5000",
            "maxSightingAgeMinutes = 15",
            "batchSize = 50",
            "flushIntervalSeconds = 10",
            "",
            "[diagnostics]",
            "verboseLogging = false",
            "");
        File.WriteAllText(path, config);
        return true;
    }

    /// <summary>
    /// Records what the installer did, including whether BepInEx was installed
    /// by us (the uninstaller needs to know if it may offer to remove BepInEx).
    /// </summary>
    public static void WriteSetupMarker(string gameRoot, bool installedBepInEx)
    {
        var payload = string.Join("\n",
            "{",
            $"  \"setupVersion\": \"{BuildInfo.SetupVersion}\",",
            $"  \"installedBepInEx\": {(installedBepInEx ? "true" : "false")},",
            $"  \"ingestUrl\": \"{BuildInfo.IngestBaseUrl}\",",
            $"  \"installedAtUtc\": \"{DateTimeOffset.UtcNow:O}\"",
            "}");
        File.WriteAllText(SetupMarkerPath(gameRoot), payload);
    }

    public static bool MarkerSaysWeInstalledBepInEx(string gameRoot)
    {
        try
        {
            var text = File.ReadAllText(SetupMarkerPath(gameRoot));
            return text.Contains("\"installedBepInEx\": true");
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Uninstall(string gameRoot)
    {
        TryDeleteDirectory(PluginDir(gameRoot));
        TryDeleteFile(ConfigPath(gameRoot));
        TryDeleteDirectory(StateDir(gameRoot));
        TryDeleteFile(SetupMarkerPath(gameRoot));
    }

    /// <summary>True if GWYF-specific files are gone and no other plugins remain.</summary>
    public static bool BepInExIsNowUseless(string gameRoot)
    {
        try
        {
            var plugins = Path.Combine(gameRoot, "BepInEx", "plugins");
            if (!Directory.Exists(plugins)) return true;
            return Directory.GetDirectories(plugins).Length == 0
                   && Directory.GetFiles(plugins, "*.dll").Length == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Removes a BepInEx install that this setup exe created.</summary>
    public static void RemoveBepInEx(string gameRoot)
    {
        foreach (var name in new[]
        {
            "BepInEx", "dotnet", "winhttp.dll", "doorstop_config.ini",
            "changelog.txt", ".doorstop_version",
        })
        {
            var path = Path.Combine(gameRoot, name);
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // a locked file is not a reason to fail the rest of the uninstall
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception) { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception) { }
    }
}
