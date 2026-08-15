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
        return "map_sha256_" + Convert.ToHexStringLower(hash);
    }
}

public sealed class SceneObjectSnapshot
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string ParentChain { get; init; }
    public required bool IsStatic { get; init; }
    public required float PositionX { get; init; }
    public required float PositionY { get; init; }
    public required float PositionZ { get; init; }
    public required int HoleIndex { get; init; }
}