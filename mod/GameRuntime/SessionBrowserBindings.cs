using BepInEx.Logging;
using HarmonyLib;
using GwyfCommunity.Diagnostics;

namespace GwyfCommunity.Hooks.Game;

/// <summary>
/// Game-bound adapter for the GWYF lobby browser (build 256 Mac / IL2CPP seam).
///
/// Verified seam (docs/gwyf-hook-inventory.md):
///   - postfix  SessionBrowserUIController.UpdateSessionInfos
///               walks m_controllers[i].m_sessionInfo
///   - prefix   EnumerateSessionInfosGameState.RequestSessionInfos
///
/// Rules:
///   - hooks never trigger searches; only observe.
///   - the result list is frozen into a managed array before any sink callback,
///     so asynchronous game mutations cannot corrupt an enumeration.
///   - no value is read from SessionInfo.Password; raw privacy policy lives in
///     docs/lobby-metadata-inventory.md.
///   - an unrecognized game build (wrong member names) produces zero exceptions:
///     each read is individually guarded and a single capture error marks the
///     whole invocation as degraded instead of aborting.
/// </summary>
public sealed class SessionBrowserBindings : ILobbyHookBindings
{
    private readonly IModLog _log;
    private readonly DiagnosticsPanel _diagnostics;
    private readonly ManualLogSource _bepInExLog;
    private ILobbyHookSink? _sink;

    public string AdapterName => "gwyf-lobby-hook-v1";
    public bool IsAttached => _sink is not null;

    public SessionBrowserBindings(IModLog log, ManualLogSource bepInExLog, DiagnosticsPanel diagnostics)
    {
        _log = log;
        _bepInExLog = bepInExLog;
        _diagnostics = diagnostics;
    }

    public void Attach(ILobbyHookSink sink)
    {
        if (_sink is not null) throw new InvalidOperationException("bindings already attached");
        _sink = sink;
        PluginPatches.Install(this, _log);
        _log.Info("[Hook] session browser bindings installed");
    }

    public void Detach()
    {
        PluginPatches.Uninstall();
        _sink = null;
    }

    internal IModLog Log => _log;
    internal ManualLogSource BepInExLog => _bepInExLog;
    internal DiagnosticsPanel Diagnostics => _diagnostics;
    internal ILobbyHookSink? Sink => _sink;

    /// <summary>Formats a session info value for diagnostics (never the password).</summary>
    internal static string? AsString(Il2CppSystem.String? value) => value?.ToString();
}

/// <summary>
/// Harmony patch wiring. Static because Harmony patches are static targets;
/// the owning bindings instance routes through the sink.
/// </summary>
[HarmonyPatch]
internal static class PluginPatches
{
    private static SessionBrowserBindings? _owner;

    internal static void Install(SessionBrowserBindings owner, IModLog log)
    {
        _owner = owner;
        try
        {
            new Harmony("com.gwyf.community").PatchAll(typeof(PluginPatches).Assembly);
            log.Info("[Hook] harmony patches applied");
        }
        catch (Exception ex)
        {
            _owner = null;
            log.Error($"[Hook] harmony patch failed: {ex.Message}");
            throw;
        }
    }

    internal static void Uninstall()
    {
        // Harmony has no per-owner uninstall; static owner removal stops routing.
        _owner = null;
    }

    // ReSharper disable once UnusedMember.Global
    [HarmonyPatch(typeof(SessionBrowserUIController), nameof(SessionBrowserUIController.UpdateSessionInfos))]
    [HarmonyPostfix]
    private static void SessionBrowserUpdatePostfix(SessionBrowserUIController __instance)
    {
        var owner = _owner;
        if (owner is null) return;

        try
        {
            var list = PluginReflection.GetControllers(__instance);
            var results = PluginReflection.SnapshotControllers(list, owner);

            _owner?.Diagnostics.RecordSearch(results.Count, results.Select(r => r.LobbyId ?? "").ToArray());

            // lifecycle: this refresh completes the previous search batch, opens a new
            // one when the enumeration state requested it.
            _owner?.Sink?.OnSearchCompleted(results);
        }
        catch (Exception ex)
        {
            owner.Log.Warn($"[Hook] UpdateSessionInfos ob failed: {ex.Message}");
        }
    }

    // ReSharper disable once UnusedMember.Global
    [HarmonyPatch(typeof(EnumerateSessionInfosGameState), nameof(EnumerateSessionInfosGameState.RequestSessionInfos))]
    [HarmonyPrefix]
    private static void RequestSessionPrefix()
    {
        var owner = _owner;
        if (owner is null) return;

        try
        {
            owner.Sink?.OnSearchStarted();
        }
        catch (Exception ex)
        {
            owner.Log.Warn($"[Hook] search-start event failed: {ex.Message}");
        }
    }
}

/// <summary>Reflection on the game types, isolated so a single wrong name degrades cleanly.</summary>
internal static class PluginReflection
{
    /// <summary>Hard cap on entries we will enumerate from a list object.</summary>
    private const int MaxEnumerated = 256;
    /// <summary>Above this the collection is treated as juggernaut-marshalled and suspicious.</summary>
    private const int JuggernautThreshold = 10_000;

    internal static Il2CppSystem.Collections.Generic.List<SessionInfoUIController> GetControllers(
        SessionBrowserUIController browser) => browser.m_controllers;

    internal static List<LobbySnapshot> SnapshotControllers(
        Il2CppSystem.Collections.Generic.List<SessionInfoUIController>? list,
        SessionBrowserBindings owner)
    {
        var results = new List<LobbySnapshot>();
        if (list is null)
        {
            owner.Diagnostics.RecordHookError("session-list", new Exception("m_controllers null"));
            return results;
        }

        if (list.Count >= JuggernautThreshold)
        {
            // juggernaut marshalling: player count floods the count; do not trust it.
            owner.Diagnostics.RecordHookError("session-list", new Exception("juggernaut count " + list.Count));
            var fallback = Math.Min(list.Count, MaxEnumerated);
            for (var i = 0; i < fallback; i++)
            {
                try
                {
                    var controller = list[i];
                    var session = controller.m_sessionInfo;
                    if (session is null) continue;
                    results.Add(ToSnapshot(session, owner, out var ok));
                    if (!ok) break;
                }
                catch
                {
                    break;
                }
            }
        }
        else
        {
            var count = Math.Min(list.Count, MaxEnumerated);
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var controller = list[i];
                    var session = controller.m_sessionInfo;
                    if (session is null) continue;
                    results.Add(ToSnapshot(session, owner, out var ok));
                    if (!ok) break;
                }
                catch (Exception ex)
                {
                    owner.Diagnostics.RecordHookError($"entry[{i}]", ex);
                    break;
                }
            }
        }
        return results;
    }

    private static LobbySnapshot ToSnapshot(SessionInfo session, SessionBrowserBindings owner, out bool ok)
    {
        try
        {
            ok = true;
            return new LobbySnapshot
            {
                LobbyId = SessionBrowserBindings.AsString(session.UniqueRoomId),
                Name = SessionBrowserBindings.AsString(session.HostName),
                MemberCount = session.PlayerCount,
                Capacity = session.MaxPlayerCount,
                MapName = SessionBrowserBindings.AsString(session.CourseName),
                GameMode = session.GameMode.ToString(),
                Metadata = new Dictionary<string, string>
                {
                    ["lobbyType"] = session.LobbyType.ToString(),
                    ["isVisible"] = session.Isvisible.ToString(),
                    ["identifier"] = SessionBrowserBindings.AsString(session.Identifier) ?? "null",
                },
            };
        }
        catch (Exception ex)
        {
            ok = false;
            owner.Diagnostics.RecordHookError("sessionInfo", ex);
            return new LobbySnapshot();
        }
    }
}