namespace GwyfCommunity.Hooks;

/// <summary>
/// Member-name strings for the game-bound lobby seam, kept in ONE place.
/// Names are verified against the installed build (docs/gwyf-hook-inventory.md)
/// — they are implementation details, not architectural concepts. Resolution is
/// attempted once at startup; on failure the hook reports HOOK UNAVAILABLE and
/// the game continues untouched.
/// </summary>
public static class ReflectionBindings
{
    // macOS IL2CPP build 256 (Team17 session framework)
    public const string SessionBrowserControllerType = "SessionBrowserUIController";
    public const string SessionInfoType = "SessionInfo";
    public const string SessionInfoUIControllerType = "SessionInfoUIController";
    public const string EnumerateSessionInfosGameStateType = "EnumerateSessionInfosGameState";

    public const string SessionBrowserUpdateSessionInfos = "UpdateSessionInfos";
    public const string EnumerateRequestSessionInfos = "RequestSessionInfos";
    public const string EnumerateOnComplete = "OnSessionInfoEnumerationComplete";
    public const string EnumerateUpdateSessionInfos = "UpdateSessionInfos";

    public const string SessionInfoControllersField = "m_controllers";
    public const string SessionInfoField = "m_sessionInfo";

    // session info accessors (all verified in metadata)
    public static readonly string[] SessionInfoAccessors =
    {
        "UniqueRoomId", "PlayerCount", "MaxPlayerCount", "HostName", "CourseName",
        "GameMode", "CustomCourse", "CustomMode", "Password", "Identifier",
        "LobbyType", "Isvisible", "IsCustomRandom", "GameCountdown",
    };

    // Windows Mono builds observed in the wild (third-party evidence only — unverified here)
    public const string PublicLobbyListUiType = "PublicLobbyListUI";
    public const string PublicLobbyListEntryType = "PublicLobbyListEntry";
    public const string PublicLobbyListEntriesField = "_entries";

    /// <summary>Attempts a once-per-call type resolution without throwing.</summary>
    public static System.Type? TryResolveType(string typeName)
    {
        try
        {
            return System.Type.GetType(typeName, throwOnError: false) ??
                   AppDomain.CurrentDomain.GetAssemblies()
                       .Select(a => a.GetType(typeName, throwOnError: false))
                       .FirstOrDefault(t => t is not null);
        }
        catch (Exception)
        {
            return null;
        }
    }
}