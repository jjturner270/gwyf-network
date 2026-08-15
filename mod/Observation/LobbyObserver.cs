using GwyfCommunity.Models;

namespace GwyfCommunity.Observation;

public interface ILobbyObservationSource
{
    event Action<LobbySighting> LobbySearchResultReceived;
}

public sealed class LobbyObserver
{
    private readonly Action<LobbySighting> _onSighting;
    private readonly Action<string, string> _onRuntimeEvent;
    private readonly HashSet<string> _recentIds = new();

    public LobbyObserver(ILobbyObservationSource source, Action<LobbySighting> onSighting, Action<string, string> onRuntimeEvent)
    {
        _onSighting = onSighting;
        _onRuntimeEvent = onRuntimeEvent;
        source.LobbySearchResultReceived += HandleSearchResult;
    }

    public static LobbySighting Normalize(
        string steamLobbyId,
        string name,
        int members,
        int capacity,
        string map,
        string mode,
        string observerId,
        string clientVersion)
    {
        return new LobbySighting
        {
            Lobby = new LobbyInfo
            {
                SteamLobbyId = steamLobbyId,
                Name = name,
                Members = members,
                Capacity = capacity,
                Map = map,
                Mode = mode,
            },
            Observer = new ObserverInfo
            {
                InstallationId = observerId,
                ClientVersion = clientVersion,
                SearchScope = "default",
            },
        };
    }

    public void RecordJoinedLobby(string steamLobbyId)
    {
        _onRuntimeEvent("lobby-joined", steamLobbyId);
    }

    public void RecordExitedLobby(string steamLobbyId)
    {
        _onRuntimeEvent("lobby-exited", steamLobbyId);
    }

    private void HandleSearchResult(LobbySighting sighting)
    {
        if (_recentIds.Add(sighting.SightingId))
        {
            _onSighting(sighting);
            if (_recentIds.Count > 4096) _recentIds.Remove(_recentIds.First());
        }
    }
}