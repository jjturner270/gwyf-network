namespace GwyfCommunity.Hooks;

/// <summary>
/// Game-facing, normalized lobby observation. This is the boundary between the
/// game and the protocol: the hook knows nothing about HTTP, SQLite, Zod,
/// confidence or API routes.
///
/// Nullability is honest: an absent Name/MapName/GameMode means the game object
/// did not expose a value — the protocol layer descends to null, never "unknown".
/// </summary>
public sealed class LobbySnapshot
{
    public string? LobbyId { get; init; }
    public string? Name { get; init; }
    public int? MemberCount { get; init; }
    public int? Capacity { get; init; }
    public string? MapName { get; init; }
    public string? GameMode { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public interface ILobbyHook
{
    event Action<LobbySnapshot> LobbyObserved;
    event Action LobbySearchStarted;
    event Action LobbySearchCompleted;

    void Start();
    void Stop();
}

/// <summary>
/// Game-bound bindings provider (the actual Unity/Steam wiring). Implementations
/// live behind the GWYF_REFS compile boundary; an unavailable adapter reports
/// itself as such instead of throwing.
/// </summary>
public interface ILobbyHookBindings
{
    string AdapterName { get; }
    bool IsAttached { get; }

    void Attach(ILobbyHookSink sink);
    void Detach();
}

/// <summary>Normalized callbacks the bindings push into the hook.</summary>
public interface ILobbyHookSink
{
    void OnSearchStarted();
    void OnSearchCompleted(IReadOnlyList<LobbySnapshot> results);
    void OnHookError(string where, Exception ex);
}