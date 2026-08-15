namespace GwyfCommunity.Observation;

public sealed class SceneFingerprint
{
    public string Compute(string sceneName, IEnumerable<SceneObjectSnapshot> objects)
    {
        var canonical = objects
            .Where(o => o.IsStatic)
            .Select(o => new
            {
                o.Name,
                o.Type,
                o.HoleIndex,
                o.ParentChain,
                Position = (o.PositionX + o.PositionY + o.PositionZ).ToString("0.000000"),
            })
            .OrderBy(o => o.ParentChain)
            .ThenBy(o => o.Name)
            .ThenBy(o => o.Type)
            .Select(o => $"{o.Type}|{o.Name}|{o.HoleIndex}|{o.ParentChain}|{o.Position}");

        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", canonical));
        var hash = sha.ComputeHash(bytes);
        return "map_sha256_" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class SceneObjectSnapshot
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string ParentChain { get; init; } = "";
    public bool IsStatic { get; init; }
    public float PositionX { get; init; }
    public float PositionY { get; init; }
    public float PositionZ { get; init; }
    public int HoleIndex { get; init; }
}