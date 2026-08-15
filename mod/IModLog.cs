namespace GwyfCommunity;

/// <summary>Minimal logger seam so the core mod compiles without BepInEx references.</summary>
public interface IModLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class ConsoleModLog : IModLog
{
    public void Info(string message) => Console.WriteLine($"[GWYF.Network] {message}");
    public void Warn(string message) => Console.WriteLine($"[GWYF.Network][WARN] {message}");
    public void Error(string message) => Console.WriteLine($"[GWYF.Network][ERROR] {message}");
}