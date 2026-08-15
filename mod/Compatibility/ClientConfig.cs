using System.Text.Json.Serialization;

namespace GwyfCommunity.Compatibility;

/// <summary>
/// Compatibility contract from GET /v1/client/config. The server declares what
/// it accepts; the client decides whether it can participate. The server never
/// redefines how the client interprets the game — this governs participation
/// only (game build, client version, ingest availability).
/// </summary>
public sealed class ClientConfig
{
    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; }

    [JsonPropertyName("configVersion")]
    public int ConfigVersion { get; set; }

    [JsonPropertyName("minimumClientVersion")]
    public string MinimumClientVersion { get; set; } = "";

    [JsonPropertyName("latestClientVersion")]
    public string LatestClientVersion { get; set; } = "";

    [JsonPropertyName("supportedGameBuilds")]
    public int[] SupportedGameBuilds { get; set; } = Array.Empty<int>();

    [JsonPropertyName("ingestEnabled")]
    public bool IngestEnabled { get; set; }
}