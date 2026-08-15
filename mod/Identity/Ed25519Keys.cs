using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace GwyfCommunity.Identity;

public interface IObserverIdentity
{
    string ObserverId { get; }
    string PublicKey { get; }

    byte[] SignBytes(ReadOnlySpan<byte> payload);
    string Sign(string payload);
}

/// <summary>
/// Ed25519 key material and signing, backed by BouncyCastle (netstandard2.0-safe,
/// pure managed — compatible with the game's BepInEx runtime).
/// </summary>
public sealed class Ed25519Keys
{
    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly Ed25519PublicKeyParameters _publicKey;

    private Ed25519Keys(Ed25519PrivateKeyParameters privateKey)
    {
        _privateKey = privateKey;
        _publicKey = privateKey.GeneratePublicKey();
    }

    public string PublicKeyHex => Convert.ToHexString(_publicKey.GetEncoded()).ToLowerInvariant();
    public string PrivateKeyHex => Convert.ToHexString(_privateKey.GetEncoded()).ToLowerInvariant();

    public static Ed25519Keys Generate()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new Org.BouncyCastle.Security.SecureRandom()));
        var pair = generator.GenerateKeyPair();
        return new Ed25519Keys((Ed25519PrivateKeyParameters)pair.Private);
    }

    public static bool TryLoad(string publicKeyHex, string privateKeyHex, out Ed25519Keys keys)
    {
        keys = null!;
        try
        {
            if (string.IsNullOrEmpty(publicKeyHex) || string.IsNullOrEmpty(privateKeyHex)) return false;
            var privBytes = Convert.FromHexString(privateKeyHex);
            if (privBytes.Length != Ed25519PrivateKeyParameters.KeySize) return false;
            var priv = new Ed25519PrivateKeyParameters(privBytes, 0);
            var expectedPub = Convert.ToHexString(priv.GeneratePublicKey().GetEncoded()).ToLowerInvariant();
            if (!expectedPub.Equals(publicKeyHex.Trim().ToLowerInvariant())) return false;
            keys = new Ed25519Keys(priv);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string Sign(ReadOnlySpan<byte> payload)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, _privateKey);
        signer.BlockUpdate(payload.ToArray(), 0, payload.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }
}