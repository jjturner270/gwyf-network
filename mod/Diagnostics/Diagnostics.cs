namespace GwyfCommunity.Diagnostics;

public sealed class DiagnosticsPanel
{
    public string ObserverId { get; set; } = "";
    public bool ServiceOnline { get; set; }
    public int PendingUploads { get; set; }
    public int LobbySightingsSent { get; set; }
    public int CourseSightingsSent { get; set; }
    public string? CurrentLobbyId { get; set; }
    public string? CurrentMapFingerprint { get; set; }
    public int ProtocolVersion => 1;
    public string ClientVersion => "0.1.0";

    public void Render()
    {
        Console.WriteLine("GWYF COMMUNITY NETWORK");
        Console.WriteLine($"Observer        {ObserverId}");
        Console.WriteLine($"Service         {(ServiceOnline ? "ONLINE" : "OFFLINE")}");
        Console.WriteLine($"Pending uploads {PendingUploads}");
        Console.WriteLine($"Lobby sightings {LobbySightingsSent}");
        Console.WriteLine($"Course sightings{SightingsLabel(CourseSightingsSent)}");
        Console.WriteLine($"Current lobby   {CurrentLobbyId ?? "-"}");
        Console.WriteLine($"Current map     {CurrentMapFingerprint ?? "-"}");
    }

    private static string SightingsLabel(int count) => count.ToString().PadLeft(2);
}