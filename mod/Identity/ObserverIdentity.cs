using System.Security.Cryptography;
using System.Text;

namespace GwyfCommunity.Identity;

/// <summary>
/// Persistent per-installation identity. The observer id derivation MUST mirror
/// the TypeScript server exactly: sha256 over the UTF-8 bytes of the lower-case
/// public-key hex string, truncated to 32 hex chars.
/// </summary>
public sealed class ObserverIdentity : IObserverIdentity
{
    private readonly string _keyFilePath;
    private Ed25519Keys? _adapter;

    public string ObserverId { get; private set; } = "";
    public string PublicKey { get; private set; } = "";
    public string PublicKeyHex => PublicKey;

    public ObserverIdentity(string keyFilePath)
    {
        _keyFilePath = keyFilePath;
    }

    /// <summary>Loads an existing identity or creates a new one. Never throws.</summary>
    public void LoadOrCreate()
    {
        try
        {
            if (File.Exists(_keyFilePath))
            {
                var stored = System.Text.Json.JsonSerializer.Deserialize<StoredKey>(File.ReadAllText(_keyFilePath));
                if (stored is not null && Ed25519Keys.TryLoad(stored.PublicKeyHex, stored.PrivateKeyHex, out _adapter))
                {
                    PublicKey = stored.PublicKeyHex;
                    ObserverId = DeriveObserverId(PublicKey);
                    return;
                }
            }
        }
        catch (Exception)
        {
            // corrupt or unreadable identity state: regenerate below
        }

        _adapter = Ed25519Keys.Generate();
        PublicKey = _adapter.PublicKeyHex;
        ObserverId = DeriveObserverId(PublicKey);
        Persist();
    }

    /// <summary>Deletes the local identity state. The next LoadOrCreate makes a fresh identity.</summary>
    public void Reset()
    {
        try
        {
            if (File.Exists(_keyFilePath)) File.Delete(_keyFilePath);
        }
        catch (Exception)
        {
            // deletion failure must never break the game
        }
        _adapter = null;
        ObserverId = "";
        PublicKey = "";
    }

    /// <summary>Ed25519 signature (64 bytes, base64) over the exact payload bytes.</summary>
    public string Sign(ReadOnlySpan<byte> payload)
    {
        if (_adapter is null) LoadOrCreate();
        return _adapter!.Sign(payload);
    }

    public string Sign(string payload) => Sign(Encoding.UTF8.GetBytes(payload));

    public byte[] SignBytes(ReadOnlySpan<byte> payload)
    {
        if (_adapter is null) LoadOrCreate();
        return Convert.FromBase64String(_adapter!.Sign(payload));
    }

    private void Persist()
    {
        try
        {
            var dir = Path.GetDirectoryName(_keyFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(new StoredKey
            {
                PublicKeyHex = PublicKey,
                PrivateKeyHex = _adapter!.PrivateKeyHex,
            });
            var tmp = _keyFilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _keyFilePath, overwrite: true);
        }
        catch (Exception)
        {
            // identity persistence failure must never break the game
        }
    }

    public static string DeriveObserverId(string publicKeyHex)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(publicKeyHex));
        return "obs_" + Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }

    private sealed class StoredKey
    {
        public string PublicKeyHex { get; set; } = "";
        public string PrivateKeyHex { get; set; } = "";
    }
}