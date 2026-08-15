using System.Reflection;

namespace GwyfCommunity.Setup;

/// <summary>
/// Build-time facts for the setup exe. The pipeline (scripts/build-setup.ps1)
/// embeds the mod release zip and bakes the ingest endpoint via
/// Generated/Baked.cs; everything else lives in this file so a stranger never
/// has to touch configuration.
/// </summary>
internal static partial class BuildInfo
{
    /// <summary>Setup exe version. Mirrors the .csproj &lt;Version&gt;.</summary>
    public const string SetupVersion = "0.3.0";

    /// <summary>Steam AppID of Golf With Your Friends.</summary>
    public const int GameSteamAppId = 431240;

    /// <summary>Expected game folder name under steamapps/common.</summary>
    public const string GameFolderName = "Golf With Your Friends";

    /// <summary>
    /// BepInEx 6 bleeding-edge build verified against game build 303
    /// (older BE builds crash this game at boot; see mod README).
    /// Pinned on purpose: reproducible, known-good installs.
    /// </summary>
    public const int BepInExBuild = 785;
    public const string BepInExAsset =
        "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785+6abdba4.zip";
    public const string BepInExZipUrl =
        "https://builds.bepinex.dev/projects/bepinex_be/785/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip";

    /// <summary>
    /// SHA-256 of the pinned BepInEx asset. The bootstrapper refuses to extract
    /// anything that does not match — a false positive for supply-chain tampering
    /// or in-flight corruption fails the install instead of shipping an
    /// untrusted runtime into the game.
    /// </summary>
    public const string BepInExZipSha256 =
        "2a7cbf74d26abe4765c3e662db1721b923bac39849ebfef2ca5dc7de7e2d9b7f";

    /// <summary>Embedded mod release zip resource name.</summary>
    public const string ModZipResource = "mod-release.zip";

    public static string DisplayName =>
        $"GWYF Community Network — Setup v{SetupVersion}";

    public static string AssemblyVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? SetupVersion;
}
