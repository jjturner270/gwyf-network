using UnityEngine;

namespace GwyfCommunity.GameRuntime;

public static class GameBuildDetector
{
    /// <summary>Unity-reported game version at plugin load (verified context).</summary>
    public static string? Resolve()
    {
        try
        {
            return Application.version?.ToString();
        }
        catch
        {
            return null;
        }
    }
}