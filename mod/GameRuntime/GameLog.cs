using BepInEx.Logging;

namespace GwyfCommunity.GameRuntime;

/// <summary>BepInEx logger adapter for the game-free core modules (IModLog).</summary>
public sealed class GameLog : IModLog
{
    private readonly ManualLogSource _logger;

    public GameLog(ManualLogSource logger) => _logger = logger;

    public void Debug(string message) => _logger.LogDebug(message);
    public void Info(string message) => _logger.LogInfo(message);
    public void Warn(string message) => _logger.LogWarning(message);
    public void Error(string message) => _logger.LogError(message);
}