using GwyfCommunity.Observation;

namespace GwyfCommunity.Compatibility;

/// <summary>
/// Distinct, human-readable participation states. Never collapse into a generic
/// "offline": each state says what is wrong and whether participation is possible.
/// </summary>
public enum ClientStatus
{
    /// <summary>Compatibility confirmed; observation may run.</summary>
    Working,
    /// <summary>The compatibility config could not be retrieved or was malformed.</summary>
    ConfigServerUnreachable,
    /// <summary>The installed game build is unknown or not accepted by the server.</summary>
    UnsupportedGameBuild,
    /// <summary>This client version is older than the server's minimum.</summary>
    ClientUpdateRequired,
    /// <summary>The server has disabled ingest, or the user opted out in config.</summary>
    IngestDisabled,
    /// <summary>Compatibility was granted but the server is not reachable right now.</summary>
    ServerUnreachable,
}

public static class ClientStatusLabels
{
    public static string Label(this ClientStatus status) => status switch
    {
        ClientStatus.Working => "WORKING",
        ClientStatus.ConfigServerUnreachable => "CONFIG SERVER UNREACHABLE",
        ClientStatus.UnsupportedGameBuild => "UNSUPPORTED GAME BUILD",
        ClientStatus.ClientUpdateRequired => "CLIENT UPDATE REQUIRED",
        ClientStatus.IngestDisabled => "INGEST DISABLED",
        ClientStatus.ServerUnreachable => "SERVER UNREACHABLE",
        _ => "UNKNOWN",
    };
}

/// <summary>
/// The outcome of evaluating the compatibility gate. MayObserve is the single
/// authorization bit every observation source must honor.
/// </summary>
public sealed class GateResult
{
    public ClientStatus Status { get; }
    public string Detail { get; }
    public ClientConfig? Config { get; }
    public bool UpdateAvailable { get; }

    public bool MayObserve => Status == ClientStatus.Working;

    public GateResult(ClientStatus status, string detail, ClientConfig? config, bool updateAvailable = false)
    {
        Status = status;
        Detail = detail;
        Config = config;
        UpdateAvailable = updateAvailable;
    }

    public string SupportedBuildsSummary =>
        Config is null || Config.SupportedGameBuilds.Length == 0
            ? "none"
            : string.Join(", ", Config.SupportedGameBuilds);
}

/// <summary>
/// Fail-closed compatibility decision. Evaluated once per launch, before any
/// game-bound observation starts; there is no caching between launches, so every
/// launch re-resolves against the current server contract. A null or malformed
/// config always blocks observation.
/// </summary>
public static class CompatibilityGate
{
    public static GateResult Evaluate(ClientConfig? config, string clientVersion, string? gameBuild)
    {
        if (config is null)
            return Blocked(ClientStatus.ConfigServerUnreachable,
                "Could not retrieve the compatibility contract (server offline, timeout, or no config served). No observation started.",
                null);

        if (string.IsNullOrWhiteSpace(config.MinimumClientVersion) || config.SupportedGameBuilds is null)
            return Blocked(ClientStatus.ConfigServerUnreachable,
                "The compatibility contract is malformed (missing minimum client version or supported builds). No observation started.",
                config);

        if (!IsWellFormedVersion(config.MinimumClientVersion))
            return Blocked(ClientStatus.ConfigServerUnreachable,
                $"The compatibility contract has an unparsable minimum client version ({config.MinimumClientVersion}). No observation started.",
                config);

        if (config.ProtocolVersion != ModVersions.ProtocolVersion)
            return Blocked(ClientStatus.ClientUpdateRequired,
                $"The server speaks compatibility protocol v{config.ProtocolVersion} but this client speaks v{ModVersions.ProtocolVersion}. Update the client to participate.",
                config);

        if (string.IsNullOrWhiteSpace(gameBuild))
            return Blocked(ClientStatus.UnsupportedGameBuild,
                "The installed game build could not be determined, so compatibility cannot be confirmed. No observation started.",
                config);

        if (!int.TryParse(gameBuild.Trim(), out var build))
            return Blocked(ClientStatus.UnsupportedGameBuild,
                $"Installed game build \"{gameBuild.Trim()}\" is not recognized as a supported build identifier. No observation started.",
                config);

        if (!config.SupportedGameBuilds.Contains(build))
            return Blocked(ClientStatus.UnsupportedGameBuild,
                $"Installed game build {build} is not supported (supported: {FormatList(config.SupportedGameBuilds)}). No observation started.",
                config);

        if (CompareVersions(clientVersion, config.MinimumClientVersion) < 0)
            return Blocked(ClientStatus.ClientUpdateRequired,
                $"Client {clientVersion} is older than the server's minimum {config.MinimumClientVersion}. Update to keep observing.",
                config);

        if (!config.IngestEnabled)
            return Blocked(ClientStatus.IngestDisabled,
                "The server has disabled ingest right now. No observation started.",
                config);

        // Being behind latest is NOT a blocker: >= minimum is enough to observe.
        // Only surface the notice so diagnostics never conflate "not latest"
        // with "unsupported".
        var updateAvailable = CompareVersions(clientVersion, config.LatestClientVersion) < 0;
        var detail = updateAvailable
            ? $"Compatibility confirmed. A newer client ({config.LatestClientVersion}) is available."
            : "Compatibility confirmed.";
        return new GateResult(ClientStatus.Working, detail, config, updateAvailable);
    }

    /// <summary>Dotted numeric version compare (missing segments count as 0).</summary>
    public static int CompareVersions(string a, string b)
    {
        var aParts = a.Split('.');
        var bParts = b.Split('.');
        var count = Math.Max(aParts.Length, bParts.Length);
        for (var i = 0; i < count; i++)
        {
            var av = i < aParts.Length && int.TryParse(aParts[i], out var x) ? x : 0;
            var bv = i < bParts.Length && int.TryParse(bParts[i], out var y) ? y : 0;
            if (av != bv) return av < bv ? -1 : 1;
        }
        return 0;
    }

    private static bool IsWellFormedVersion(string value)
    {
        var parts = value.Split('.');
        if (parts.Length == 0) return false;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out _)) return false;
        }
        return true;
    }

    private static string FormatList(IReadOnlyCollection<int> builds) => string.Join(", ", builds);

    private static GateResult Blocked(ClientStatus status, string detail, ClientConfig? config) => new(status, detail, config);
}

/// <summary>
/// Derives the live status label after the gate: a granted gate degrades to
/// ServerUnreachable when ingest is unreachable, but observation continues —
/// a mid-session outage never revokes an already-granted gate.
/// </summary>
public static class LiveStatus
{
    public static ClientStatus Current(GateResult gate, bool serviceOnline) =>
        !gate.MayObserve
            ? gate.Status
            : serviceOnline
                ? ClientStatus.Working
                : ClientStatus.ServerUnreachable;
}