namespace GwyfCommunity.Config;

/// <summary>
/// Runtime configuration with passive defaults. Values arrive from the BepInEx
/// config binding (GameRuntime) or environment overrides (dev/test).
/// </summary>
public sealed class ModConfig
{
    public bool NetworkEnabled { get; set; } = true;
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:3000";

    public bool LobbyObservationEnabled { get; set; } = true;
    public bool CourseObservationEnabled { get; set; } = false;
    public bool ActiveRadarEnabled { get; set; } = false;

    public int MaxPendingItems { get; set; } = 5000;
    public int MaxLobbySightingAgeMinutes { get; set; } = 15;
    public int BatchSize { get; set; } = 50;
    public int FlushIntervalSeconds { get; set; } = 10;

    public bool ShowPanel { get; set; } = true;
    public bool VerboseLogging { get; set; } = false;

    public static ModConfig FromEnvironment()
    {
        var config = new ModConfig();
        if (Environment.GetEnvironmentVariable("GWYF_API_BASE_URL") is { Length: > 0 } api) config.ApiBaseUrl = api;
        if (Environment.GetEnvironmentVariable("GWYF_NETWORK_ENABLED") is { Length: > 0 } en) config.NetworkEnabled = en is "1" or "true";
        if (Environment.GetEnvironmentVariable("GWYF_LOBBY_OBSERVATION_ENABLED") is { Length: > 0 } lo)
            config.LobbyObservationEnabled = lo is "1" or "true";
        if (Environment.GetEnvironmentVariable("GWYF_MAX_LOBBY_AGE_MINUTES") is { Length: > 0 } age &&
            int.TryParse(age, out var minutes))
            config.MaxLobbySightingAgeMinutes = minutes;
        if (Environment.GetEnvironmentVariable("GWYF_VERBOSE") is { Length: > 0 } verbose)
            config.VerboseLogging = verbose is "1" or "true";
        return config;
    }
}