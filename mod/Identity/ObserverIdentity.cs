using System.Security.Cryptography;

namespace GwyfCommunity.Identity;

public sealed class ObserverIdentity
{
    private static readonly string KeyPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GwyfCommunityNetwork", "observer-key.json");

    public string PublicKeyHex { get; private set; } = "";
    public string PrivateKeyHex { get; private set; } = "";
    public string ObserverId { get; private set; } = "";

    public void LoadOrCreate()
    {
        try
        {
            if (File.Exists(KeyPath))
            {
                var stored = System.Text.Json.JsonSerializer.Deserialize<StoredKey>(File.ReadAllText(KeyPath));
                if (stored is not null && stored.PublicKeyHex.Length == 128 && stored.PrivateKeyHex.Length == 128)
                {
                    PublicKeyHex = stored.PublicKeyHex;
                    PrivateKeyHex = stored.PrivateKeyHex;
                    ObserverId = DeriveObserverId(PublicKeyHex);
                    return;
                }
            }
        }
        catch (IOException)
        {
        }

        PublicKeyHex = RandomNumberGenerator.GetHexString(64);
        PrivateKeyHex = RandomNumberGenerator.GetHexString(64);
        ObserverId = DeriveObserverId(PublicKeyHex);
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
        File.WriteAllText(KeyPath, System.Text.Json.JsonSerializer.Serialize(new StoredKey
        {
            PublicKeyHex = PublicKeyHex,
            PrivateKeyHex = PrivateKeyHex,
        }));
    }

    public string Sign(string body)
    {
        throw new NotImplementedException("ed25519 signing: plug in NSec.Cryptography or Chaos.NaCl against the game's netstandard2.0 BepInEx runtime");
    }

    private static string DeriveObserverId(string publicKeyHex)
    {
        var hash = SHA256.HashData(Convert.FromHexString(publicKeyHex));
        return "obs_" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private sealed class StoredKey
    {
        public string PublicKeyHex { get; set; } = "";
        public string PrivateKeyHex { get; set; } = "";
    }
}