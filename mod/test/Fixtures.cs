using System.Text.Json;
using GwyfCommunity.Identity;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace GwyfCommunity.Tests;

public static class Fixtures
{
    private static string? _root;

    public static string Root
    {
        get
        {
            if (_root is null)
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "scripts", "fixtures")))
                {
                    dir = dir.Parent;
                }
                _root = dir is null
                    ? throw new DirectoryNotFoundException("scripts/fixtures not found (run tests from repo?)")
                    : dir.FullName;
            }
            return _root;
        }
    }

    public static string FixtureDir => Path.Combine(Root, "scripts", "fixtures");

    public static string Read(string relative) => File.ReadAllText(Path.Combine(FixtureDir, relative));

    public static string CanonicalLobbySighting => Read("canonical-lobby-sighting.txt");

    public static string SignatureLobbySighting => Read("canonical-lobby-sighting.sig.txt").Trim();

    public static string ObserverId => Read("test-observer-id.txt").Trim();

    public static (string PublicKeyHex, string PrivateKeyHex) TestKeyPair
    {
        get
        {
            using var doc = JsonDocument.Parse(Read(Path.Combine("signing", "test-keypair.json")));
            var root = doc.RootElement;
            return (root.GetProperty("publicKeyHex").GetString()!, root.GetProperty("privateKeyHex").GetString()!);
        }
    }

    public static bool VerifySignature(string publicKeyHex, byte[] data, byte[] signature)
    {
        var pub = new Ed25519PublicKeyParameters(Convert.FromHexString(publicKeyHex), 0);
        var verifier = new Ed25519Signer();
        verifier.Init(false, pub);
        verifier.BlockUpdate(data, 0, data.Length);
        return verifier.VerifySignature(signature);
    }

    public static bool VerifySignature(string publicKeyHex, string data, string signatureBase64)
        => VerifySignature(publicKeyHex, System.Text.Encoding.UTF8.GetBytes(data), Convert.FromBase64String(signatureBase64));
}