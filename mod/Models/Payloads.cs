using System.Text.Json.Serialization;

namespace GwyfCommunity.Models;

public sealed class LobbySighting
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = "gwyf.lobby-sighting.v1";

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; } = 1;

    [JsonPropertyName("sightingId")]
    public string SightingId { get; set; } = Guid.NewGuid().ToString("D");

    [JsonPropertyName("observedAt")]
    public string ObservedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    [JsonPropertyName("lobby")]
    public LobbyInfo Lobby { get; set; } = new();

    [JsonPropertyName("observer")]
    public ObserverInfo Observer { get; set; } = new();
}

public sealed class LobbyInfo
{
    [JsonPropertyName("steamLobbyId")]
    public string SteamLobbyId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("members")]
    public int Members { get; set; }

    [JsonPropertyName("capacity")]
    public int Capacity { get; set; }

    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";

    [JsonPropertyName("rawMetadata")]
    public Dictionary<string, object>? RawMetadata { get; set; }
}

public sealed class ObserverInfo
{
    [JsonPropertyName("installationId")]
    public string InstallationId { get; set; } = "";

    [JsonPropertyName("clientVersion")]
    public string ClientVersion { get; set; } = "0.1.0";

    [JsonPropertyName("searchScope")]
    public string? SearchScope { get; set; }
}

public sealed class CourseSighting
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = "gwyf.course-sighting.v1";

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; } = 1;

    [JsonPropertyName("sightingId")]
    public string SightingId { get; set; } = Guid.NewGuid().ToString("D");

    [JsonPropertyName("observedAt")]
    public string ObservedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    [JsonPropertyName("lobbyId")]
    public string? LobbyId { get; set; }

    [JsonPropertyName("course")]
    public CourseInfo Course { get; set; } = new();

    [JsonPropertyName("observer")]
    public ObserverInfo Observer { get; set; } = new();
}

public sealed class CourseInfo
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; } = "";

    [JsonPropertyName("fingerprint")]
    public string? Fingerprint { get; set; }

    [JsonPropertyName("holeCount")]
    public int? HoleCount { get; set; }

    [JsonPropertyName("objectCount")]
    public int? ObjectCount { get; set; }
}

public sealed class RuntimeEvent
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = "gwyf.runtime-event.v1";

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; } = 1;

    [JsonPropertyName("sightingId")]
    public string SightingId { get; set; } = Guid.NewGuid().ToString("D");

    [JsonPropertyName("observedAt")]
    public string ObservedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = "";

    [JsonPropertyName("context")]
    public Dictionary<string, object>? Context { get; set; }

    [JsonPropertyName("observer")]
    public ObserverInfo Observer { get; set; } = new();
}