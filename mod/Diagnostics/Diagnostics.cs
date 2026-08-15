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
        var lastSearch = LastSearchAt is null
            ? "never"
            : $"{FormatAgo(LastSearchAt.Value)} ({LastSearchLobbyCount} lobbies)";
        var lastUpload = LastUploadAt is null
            ? "never"
            : $"{FormatAgo(LastUploadAt.Value)} ({LastUploadAccepted} accepted, {LastUploadDuplicates} dup, {LastUploadRejected} rejected)";

        return string.Join("\n",
            "GWYF COMMUNITY NETWORK",
            $"HOOK            {HookStatus}",
            $"GAME BUILD      {GameBuild}",
            $"OBSERVER        {ObserverId}",
            $"API             {(ServiceOnline ? "ONLINE" : "OFFLINE")}",
            $"QUEUE           {PendingUploads} pending",
            $"LAST SEARCH     {lastSearch}",
            $"LAST UPLOAD     {lastUpload}",
            $"CLOCK           {ClockStatus}",
            $"LAST ERROR      {LastError ?? "none"}");
    }

    private static string FormatAgo(DateTimeOffset at)
    {
        var delta = DateTimeOffset.UtcNow - at;
        if (delta.TotalSeconds < 5) return "just now";
        if (delta.TotalMinutes < 1) return $"{(int)delta.TotalSeconds}s ago";
        if (delta.TotalHours < 1) return $"{(int)delta.TotalMinutes}m ago";
        return $"{(int)delta.TotalHours}h ago";
    }
}