using GwyfCommunity.Diagnostics;

namespace GwyfCommunity.Hooks;

/// <summary>
/// The game-facing lobby hook. Passively observes the vanilla lobby browser that
/// the game itself opened; it never triggers searches, never mutates results.
///
/// Boundary rules:
///  - the hook only knows about LobbySnapshot and lifecycle events
///  - every game boundary is isolated: an exception from an adapter is recorded
///    in diagnostics and never propagates into vanilla logic
///  - no polling: bindings push events only
/// </summary>
public sealed class UnityLobbyHook : ILobbyHook, ILobbyHookSink
{
    private readonly IModLog _log;
    private readonly DiagnosticsPanel _diagnostics;
    private readonly ILobbyHookBindings? _bindings;
    private readonly object _lock = new();
    private bool _running;

    public event Action<LobbySnapshot>? LobbyObserved;
    public event Action? LobbySearchStarted;
    public event Action? LobbySearchCompleted;

    public string AdapterName => _bindings?.AdapterName ?? "none";

    public UnityLobbyHook(IModLog log, DiagnosticsPanel diagnostics, ILobbyHookBindings? bindings)
    {
        _log = log;
        _diagnostics = diagnostics;
        _bindings = bindings;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_running) return;
            if (_bindings is null)
            {
                _log.Warn("[Hook] no lobby bindings available; hook unavailable");
                _diagnostics.HookStatus = "UNAVAILABLE";
                return;
            }
            try
            {
                _bindings.Attach(this);
                _running = true;
                _diagnostics.HookStatus = "CONNECTED";
                _log.Info($"[Hook] attached adapter {_bindings.AdapterName}");
            }
            catch (Exception ex)
            {
                _diagnostics.HookStatus = "UNAVAILABLE";
                _log.Error($"[Hook] attach failed: {ex.Message}");
                OnHookError("attach", ex);
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            try
            {
                _bindings?.Detach();
            }
            catch (Exception ex)
            {
                OnHookError("detach", ex);
            }
            _running = false;
        }
    }

    public void OnSearchStarted()
    {
        try
        {
            _log.Info("[Hook] lobby search started");
            LobbySearchStarted?.Invoke();
        }
        catch (Exception ex)
        {
            OnHookError("search-started", ex);
        }
    }

    public void OnSearchCompleted(IReadOnlyList<LobbySnapshot> results)
    {
        try
        {
            foreach (var snapshot in results)
            {
                LobbyObserved?.Invoke(snapshot);
            }
            LobbySearchCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            OnHookError("search-completed", ex);
        }
    }

    public void OnHookError(string where, Exception ex)
    {
        try
        {
            _diagnostics.RecordHookError(where, ex);
            _log.Warn($"[Hook] {where}: {ex.Message}");
        }
        catch (Exception)
        {
            // diagnostics must never break the game either
        }
    }
}