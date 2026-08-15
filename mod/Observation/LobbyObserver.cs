using System.Security.Cryptography;
using GwyfCommunity.Hooks;
using GwyfCommunity.Models;

namespace GwyfCommunity.Observation;

/// <summary>
/// The observer owns protocol conversion: LobbySnapshot (game-facing) →
/// LobbySighting (protocol). Each vanilla search is one correlation batch; each
/// lobby result becomes one independent sighting. Repeated observations of the
/// same lobby with different observedAt are valid evidence of continued
/// existence — deduplication only ever applies to identical sighting ids.
/// </summary>
public sealed class LobbyObserver
{
    private readonly Action<LobbySighting> _onSighting;
    private readonly Action<string, string> _onRuntimeEvent;
    private readonly Func<string> _observerId;
    private readonly Func<DateTimeOffset> _clock;
    private readonly HashSet<string> _recentIds = new();

    private string? _activeSearchId;
    private string? _activeBatchId;

    public LobbyObserver(
        ILobbyHook source,
        Action<LobbySighting> onSighting,
        Action<string, string> onRuntimeEvent,
        Func<string> observerId,
        Func<DateTimeOffset>? clock = null)
    {
        _onSighting = onSighting;
        _onRuntimeEvent = onRuntimeEvent;
        _observerId = observerId;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        source.LobbySearchStarted += OnSearchStarted;
        source.LobbySearchCompleted += OnSearchCompleted;
        source.LobbyObserved += HandleSnapshot;
    }

    internal string? ActiveSearchId => _activeSearchId;

    private void OnSearchStarted()
    {
        _activeSearchId = "search_" + NewCorrelationPart();
        _activeBatchId = "batch_" + NewCorrelationPart();
    }

    private void OnSearchCompleted()
    {
        _activeSearchId = null;
        _activeBatchId = null;
    }

    private void HandleSnapshot(LobbySnapshot snapshot)
    {
        // honest nullability: only emit a sighting when the identity and the two
        // required counts are actually present in the game object
        var lobbyId = snapshot.LobbyId?.Trim();
        if (string.IsNullOrEmpty(lobbyId))
        {
            // only surface hook issues during an active search; the refresh loop
            // otherwise calls back with placeholder sessions on the menu screen
            if (_activeSearchId is not null)
                OnHookIssue($"lobby without id skipped: {snapshot.Name ?? "?"}");
            return;
        }
        if (snapshot.MemberCount is not int members || snapshot.Capacity is not int capacity)
        {
            OnHookIssue($"lobby {lobbyId} skipped: member/capacity counts unavailable");
            return;
        }

        var sighting = new LobbySighting
        {
            SightingId = Guid.NewGuid().ToString("D"),
            ObservedAt = _clock().ToString("O"),
            Lobby = new LobbyInfo
            {
                SteamLobbyId = lobbyId,
                Name = Sanitize(snapshot.Name, 64),
                Members = members,
                Capacity = capacity,
                Map = Sanitize(snapshot.MapName, 128),
                Mode = Sanitize(snapshot.GameMode, 64),
                RawMetadata = BuildRawMetadata(snapshot),
            },
            Observer = new ObserverInfo
            {
                InstallationId = _observerId(),
                ClientVersion = ModVersions.Client,
                GameBuild = ModVersions.GameBuild,
                SearchScope = "default",
            },
        };

        if (_recentIds.Add(sighting.SightingId))
        {
            _onSighting(sighting);
            if (_recentIds.Count > 4096) _recentIds.Remove(_recentIds.First());
        }
    }

    private Dictionary<string, object>? BuildRawMetadata(LobbySnapshot snapshot)
    {
        if (_activeBatchId is null && _activeSearchId is null &&
            (snapshot.Metadata is null || snapshot.Metadata.Count == 0))
        {
            return null;
        }

        var meta = new Dictionary<string, object>();
        if (snapshot.Metadata is not null)
        {
            foreach (var pair in snapshot.Metadata) meta[pair.Key] = pair.Value;
        }
        if (_activeBatchId is not null) meta["observationBatchId"] = _activeBatchId;
        if (_activeSearchId is not null) meta["searchId"] = _activeSearchId;
        return meta;
    }

    private void OnHookIssue(string issue)
    {
        _onRuntimeEvent("submission-failed", issue);
    }

    public void RecordJoinedLobby(string steamLobbyId)
    {
        _onRuntimeEvent("lobby-joined", steamLobbyId);
    }

    public void RecordExitedLobby(string steamLobbyId)
    {
        _onRuntimeEvent("lobby-exited", steamLobbyId);
    }

    internal static string? Sanitize(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    private static string NewCorrelationPart()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>Version surfaces shared by all emitters and the diagnostics panel.</summary>
public static class ModVersions
{
    public const string Mod = "0.3.0";
    public const string Client = "0.3.0";
    public const string HookAdapter = "gwyf-lobby-hook-v1";
    public static string? GameBuild { get; set; }
    public static int ProtocolVersion => 1;
}