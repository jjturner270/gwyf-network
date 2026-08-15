namespace GwyfCommunity.Setup;

/// <summary>
/// Finds the Golf With Your Friends install without asking the user:
/// Steam registry keys first, then every Steam library listed in
/// libraryfolders.vdf. Returns only installs that contain the game exe.
/// </summary>
internal static class SteamLocator
{
    public sealed class Candidate
    {
        public string GameRoot { get; init; }
        public string SteamLibrary { get; init; }
        public string DiscoveredVia { get; init; }
    }

    public static IReadOnlyList<Candidate> FindGameInstalls()
    {
        var results = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var steamRoot in FindSteamRoots())
        {
            TryAdd(results, seen, Path.Combine(steamRoot, "steamapps", "common", BuildInfo.GameFolderName), steamRoot, "registry");
            foreach (var library in ReadLibraryFolders(steamRoot))
            {
                TryAdd(results, seen, Path.Combine(library, "steamapps", "common", BuildInfo.GameFolderName), library, "libraryfolders.vdf");
            }
        }

        return results;
    }

    private static void TryAdd(List<Candidate> results, HashSet<string> seen, string gameRoot, string steamLibrary, string via)
    {
        try
        {
            var exe = Path.Combine(gameRoot, "Golf With Your Friends.exe");
            if (File.Exists(exe) && seen.Add(Path.GetFullPath(gameRoot)))
            {
                results.Add(new Candidate { GameRoot = Path.GetFullPath(gameRoot), SteamLibrary = steamLibrary, DiscoveredVia = via });
            }
        }
        catch (Exception)
        {
            // unreadable path; skip
        }
    }

    private static IEnumerable<string> FindSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[]
        {
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
        })
        {
            try
            {
                var value = Microsoft.Win32.Registry.GetValue(hive, "SteamPath", null) ??
                            Microsoft.Win32.Registry.GetValue(hive, "InstallPath", null);
                if (value is string path && path.Length > 0)
                {
                    // Steam stores forward-slash paths ("c:/program files (x86)/steam").
                    path = path.Replace('/', Path.DirectorySeparatorChar).Trim('"');
                    if (Directory.Exists(path)) roots.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception)
            {
                // registry key absent on non-Steam machines
            }
        }

        return roots;
    }

    /// <summary>Parses steamapps/libraryfolders.vdf for "path" values (multi-disk installs).</summary>
    private static IEnumerable<string> ReadLibraryFolders(string steamRoot)
    {
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        var found = new List<string>();
        try
        {
            var text = File.ReadAllText(vdf);
            // Steam writes "path" "D:\SteamLibrary"; tolerate / and escaped quotes.
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(text,
                    "\"path\"\\s+\"((?:\\\\.|[^\"])*)\""))
            {
                var path = m.Groups[1].Value
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace("\\\\", "\\");
                if (path.Length > 0 && Directory.Exists(path))
                    found.Add(path);
            }
        }
        catch (Exception)
        {
            // unreadable vdf; the registry root may still work
        }

        foreach (var path in found) yield return path;
    }
}
