using GwyfCommunity.Compatibility;
using GwyfCommunity.Observation;

namespace GwyfCommunity.Diagnostics;

/// <summary>
/// Engineering instrument for the real-client integration. All values are plain
/// data so the renderer can be console, BepInEx log, or a UI panel.
/// </summary>
public sealed class DiagnosticsPanel
{
    public string ObserverId { get; set; } = "";
    public string HookStatus { get; set; } = "UNAVAILABLE";
    public string HookAdapter { get; set; } = ModVersions.HookAdapter;
    public string GameBuild { get; set; } = "";
    public string ModVersion => ModVersions.Mod;

    public bool ServiceOnline { get; set; }
    public int PendingUploads { get; set; }
    public int LobbySightingsSent { get; set; }
    public int CourseSightingsSent { get; set; }

    public DateTimeOffset? LastSearchAt { get; set; }
    public int LastSearchLobbyCount { get; set; }
    public string? LastSearchId { get; set; }

    public DateTimeOffset? LastUploadAt { get; set; }
    public int LastUploadAccepted { get; set; }
    public int LastUploadRejected { get; set; }
    public int LastUploadDuplicates { get; set; }
    public string? LastError { get; set; }
    public string? LastRejectedReason { get; set; }

    public string ClockStatus { get; set; } = "OK";
    public string? ClockSkewEvidenceSightingId { get; set; }

    public int HookErrorCount { get; set; }
    public string? LastHookError { get; set; }

    public string? CurrentLobbyId { get; set; }
    public string? CurrentMapFingerprint { get; set; }
    public int ProtocolVersion => 1;

    /// <summary>Compatibility gate outcome. Null until the startup check resolves.</summary>
    public GateResult? Gate { get; set; }
    public bool GateResolved { get; set; }

    public string StatusLabel =>
        !GateResolved
            ? "CHECKING..."
            : LiveStatus.Current(Gate!, ServiceOnline).Label();

    public void RecordHookError(string where, Exception ex)
    {
        HookErrorCount++;
        LastHookError = $"{where}: {ex.Message}";
    }

    public void RecordSearch(int count, string[] lobbyIds)
    {
        LastSearchAt = DateTimeOffset.UtcNow;
        LastSearchLobbyCount = count;
        LastSearchId = count > 0 ? lobbyIds[0] : null;
    }

    public void RecordClockRejection(string sightingId)
    {
        ClockStatus = "CLIENT CLOCK OUT OF RANGE";
        ClockSkewEvidenceSightingId = sightingId;
    }

    public string Render()
    {
        var lines = new List<string>
        {
            $"GWYF Community Observer {ModVersion}",
            "",
            $"STATUS  {StatusLabel}",
        };

        if (!GateResolved)
        {
            lines.Add("");
            lines.Add("Checking compatibility with the community server...");
            lines.Add($"Game build:   {DisplayGameBuild}");
            lines.Add($"Hook adapter: {HookAdapter}");
            lines.Add($"Observer:     {ObserverId}");
            return Join(lines);
        }

        if (!Gate!.MayObserve)
        {
            lines.Add("");
            lines.Add(Gate.Detail);
            lines.Add("");
            lines.Add($"Hook:         UNAVAILABLE (never attached)");
            lines.Add($"Mod version:  {ModVersion}");
            lines.Add($"Hook adapter: {HookAdapter}");
            lines.Add($"Observer:     {ObserverId}");
            if (Gate.Status == ClientStatus.UnsupportedGameBuild)
            {
                lines.Add($"Installed build: {DisplayGameBuild}");
                lines.Add($"Supported builds: {Gate.SupportedBuildsSummary}");
            }
            if (Gate.Status is ClientStatus.ClientUpdateRequired or ClientStatus.IngestDisabled)
            {
                lines.Add($"Server minimum client: {Gate.Config?.MinimumClientVersion ?? "?"}");
            }
            return Join(lines);
        }

        var lastSearch = LastSearchAt is null
            ? "never"
            : $"{FormatAgo(LastSearchAt.Value)} ({LastSearchLobbyCount} lobbies)";
        var lastUpload = LastUploadAt is null
            ? "never"
            : $"{FormatAgo(LastUploadAt.Value)} ({LastUploadAccepted} accepted, {LastUploadDuplicates} dup, {LastUploadRejected} rejected)";

        lines.Add("");
        lines.Add($"Game build:   {DisplayGameBuild}");
        lines.Add($"Game support: SUPPORTED");
        lines.Add($"Hook:         {HookStatus} ({HookAdapter})");
        lines.Add($"Observer:     {ObserverId}");
        lines.Add($"Server:       {(ServiceOnline ? "CONNECTED" : "UNREACHABLE — observations still queue locally; retries continue")}");
        lines.Add($"Ingest:       ENABLED (config v{Gate.Config?.ConfigVersion ?? 0}, min client {Gate.Config?.MinimumClientVersion ?? "?"})");
        if (Gate.UpdateAvailable)
            lines.Add($"Update:       a newer client ({Gate.Config?.LatestClientVersion ?? "?"}) is available");
        lines.Add($"Last successful send: {lastUpload}");
        lines.Add($"Last batch:   {(LastRejectedReason is null ? lastUpload : LastRejectedReason)}");
        lines.Add($"Queue:        {PendingUploads} pending");
        lines.Add($"Last search:  {lastSearch}");
        lines.Add($"Clock:        {ClockStatus}");
        lines.Add($"Last error:   {LastError ?? "none"}");
        return Join(lines);
    }

    private string DisplayGameBuild =>
        string.IsNullOrEmpty(GameBuild) ? "unknown" : GameBuild;

    private static string Join(List<string> lines) => string.Join("\n", lines);

    private static string FormatAgo(DateTimeOffset at)
    {
        var delta = DateTimeOffset.UtcNow - at;
        if (delta.TotalSeconds < 5) return "just now";
        if (delta.TotalMinutes < 1) return $"{(int)delta.TotalSeconds}s ago";
        if (delta.TotalHours < 1) return $"{(int)delta.TotalMinutes}m ago";
        return $"{(int)delta.TotalHours}h ago";
    }
}