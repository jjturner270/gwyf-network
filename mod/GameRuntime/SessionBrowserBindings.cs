using BepInEx.Logging;
using HarmonyLib;
using GwyfCommunity.Diagnostics;
using Il2CppInterop.Runtime;

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

    /// <summary>Per-session detail dumps; off by default so menu churn stays quiet.</summary>
    public bool Verbose { get; }

    public SessionBrowserBindings(IModLog log, ManualLogSource bepInExLog, DiagnosticsPanel diagnostics, bool verbose = false)
    {
        _log = log;
        _bepInExLog = bepInExLog;
        _diagnostics = diagnostics;
        Verbose = verbose;
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

    internal static string DumpRoomProps(SessionInfo session)
    {
        try
        {
            var props = session.RoomInfo?.CustomProperties;
            if (props is null) return "props=null";
            if (props.Count == 0) return "props=0";
            var sb = new System.Text.StringBuilder("props[");
            var e = props.Keys.GetEnumerator();
            var n = 0;
            while (e.MoveNext() && n < 32)
            {
                var key = e.Current?.ToString() ?? "?";
                object? value = null;
                try { value = props[key]; } catch { }
                if (n > 0) sb.Append(", ");
                sb.Append(key).Append('=').Append(PropValueToString(value));
                n++;
            }
            sb.Append(']');
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"props dump failed: {ex.Message}";
        }
    }

    internal static string PropValueToString(object? value)
    {
        if (value is not Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj) return "null";
        try
        {
            var clsName = ClassNameOf(obj);
            switch (clsName)
            {
                case "String":
                    return ReadNativeString(obj);
                case "Int32":
                    return obj.Unbox<int>().ToString();
                case "Boolean":
                    return obj.Unbox<bool>().ToString();
                default:
                    return $"<{clsName}>";
            }
        }
        catch (Exception ex)
        {
            return $"<unreadable:{ex.Message}>";
        }
    }

    /// <summary>
    /// Reads a room custom property as a managed CLR value (string/int/bool).
    /// Returns null when the key is absent, unreadable or not one of those.
    /// </summary>
    internal static object? ReadRoomProp(SessionInfo session, string key)
    {
        try
        {
            var props = session.RoomInfo?.CustomProperties;
            if (props is null || props.Count == 0) return null;
            object? value;
            try { value = props[key]; } catch { return null; }
            if (value is not Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj) return null;
            var clsName = ClassNameOf(obj);
            switch (clsName)
            {
                case "String":
                    return ReadNativeString(obj);
                case "Int32":
                    return obj.Unbox<int>();
                case "Boolean":
                    return obj.Unbox<bool>();
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    internal static string? RoomPropString(SessionInfo session, string key) =>
        ReadRoomProp(session, key) as string;

    private static string ClassNameOf(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj) =>
        System.Runtime.InteropServices.Marshal.PtrToStringUTF8(IL2CPP.il2cpp_class_get_name(obj.ObjectClass));

    private static string ReadNativeString(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj)
    {
        unsafe
        {
            return System.Runtime.InteropServices.Marshal.PtrToStringUni(
                new IntPtr(IL2CPP.il2cpp_string_chars(obj.Pointer)), IL2CPP.il2cpp_string_length(obj.Pointer));
        }
    }

    /// <summary>Host name arrives base64-encoded; decode when valid.</summary>
    internal static string? DecodeHostName(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        try
        {
            var bytes = Convert.FromBase64String(raw);
            var decoded = System.Text.Encoding.UTF8.GetString(bytes);
            return decoded.Contains('\0') ? raw : decoded;
        }
        catch (FormatException)
        {
            return raw;
        }
    }

    /// <summary>Course arrives as a localization key like Level.Name.PirateCove.</summary>
    internal static string NormalizeCourseKey(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        const string prefix = "Level.Name.";
        return raw.StartsWith(prefix) ? raw.Substring(prefix.Length) : raw;
    }
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
            owner.Log.Info($"[Hook] search refresh: {results.Count} sessions");
            if (owner.Verbose)
            {
                for (var i = 0; i < results.Count; i++)
                {
                    var s = results[i];
                    owner.Log.Info($"[Hook] session[{i}]: id={s.LobbyId ?? "null"} name={s.Name ?? "null"} members={s.MemberCount?.ToString() ?? "null"}/{s.Capacity?.ToString() ?? "null"} map={s.MapName ?? "null"} mode={s.GameMode ?? "null"}");
                    foreach (var meta in s.Metadata ?? new Dictionary<string, string>())
                    {
                        owner.Log.Info($"[Hook]   {meta.Key}={meta.Value}");
                    }
                }
            }

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
            var roomName = SessionBrowserBindings.AsString(session.RoomInfo?.Name);
            var props = session.RoomInfo?.CustomProperties;
            var platformRoomId = SessionBrowserBindings.RoomPropString(session, "PLATFORMROOMID");
            var hostName = SessionBrowserBindings.RoomPropString(session, "C1");
            var courseKey = SessionBrowserBindings.RoomPropString(session, "C2");
            return new LobbySnapshot
            {
                LobbyId = platformRoomId ?? SessionBrowserBindings.AsString(session.UniqueRoomId) ?? "",
                Name = SessionBrowserBindings.DecodeHostName(hostName) ?? SessionBrowserBindings.AsString(session.HostName) ?? "",
                MemberCount = session.PlayerCount,
                Capacity = session.MaxPlayerCount,
                MapName = SessionBrowserBindings.NormalizeCourseKey(courseKey) ?? SessionBrowserBindings.AsString(session.CourseName) ?? "",
                GameMode = session.GameMode.ToString(),
                Metadata = new Dictionary<string, string>
                {
                    ["lobbyType"] = session.LobbyType.ToString(),
                    ["isVisible"] = session.Isvisible.ToString(),
                    ["identifier"] = SessionBrowserBindings.AsString(session.Identifier) ?? "null",
                    ["roomName"] = roomName ?? "null",
                    ["roomProps"] = props is null ? "null" : props.Count.ToString(),
                    ["props"] = SessionBrowserBindings.DumpRoomProps(session),
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