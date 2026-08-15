namespace GwyfCommunity.GameRuntime;

/// <summary>State directory resolution for the BepInEx plugin.</summary>
public static class ModPaths
{
    public static string GetStateDirectory(string gameRootDirectory)
    {
        // BepInEx 6: BepInEx/plugins/... next to BepInEx/. Persist under BepInEx/.
        var bepInEx = Path.Combine(gameRootDirectory, "BepInEx");
        if (Directory.Exists(bepInEx))
        {
            var state = Path.Combine(bepInEx, "gwyf-community");
            Directory.CreateDirectory(state);
            return state;
        }
        throw new DirectoryNotFoundException($"BepInEx directory not found under {gameRootDirectory}");
    }
}