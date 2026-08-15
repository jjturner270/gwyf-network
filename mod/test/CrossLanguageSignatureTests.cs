using System.Text;
using GwyfCommunity.Identity;
using Xunit;

namespace GwyfCommunity.Tests;

public class CrossLanguageSignatureTests
{
    [Fact]
    public void CanonicalBytes_MatchTypeScriptGoldenFixture()
    {
        // builds the identical canonical tree the TS generator used
        var rawMetadata = CanonicalJson.Object(
            ("lobbyType", "Public"),
            ("isVisible", "True"),
            ("identifier", "6052dc1a-2f0e-4d84-b5e3-1d5cd616d419"),
            ("observationBatchId", "batch_abc123def456"),
            ("searchId", "search_1234abcd5678"));

        var lobby = CanonicalJson.Object(
            ("steamLobbyId", "6052dc1a-2f0e-4d84-b5e3-1d5cd616d419"),
            ("name", "TestLobby"),
            ("members", 3L),
            ("capacity", 12L),
            ("map", "Castle"),
            ("mode", "Classic"),
            ("rawMetadata", rawMetadata));

        var observer = CanonicalJson.Object(
            ("installationId", Fixtures.ObserverId),
            ("clientVersion", "0.2.0"),
            ("gameBuild", "256.999952"),
            ("searchScope", "default"));

        var payload = CanonicalJson.Object(
            ("schema", "gwyf.lobby-sighting.v1"),
            ("protocolVersion", 1L),
            ("sightingId", "2f5a2a4d-0a34-4dbb-9d8f-7c29a3f0c71e"),
            ("observedAt", "2026-08-15T12:00:00.0000000+00:00"),
            ("lobby", lobby),
            ("observer", observer));

        var canonical = CanonicalJson.Serialize(payload);

        Assert.Equal(Fixtures.CanonicalLobbySighting, canonical);
    }

    [Fact]
    public void Signature_MatchesTypeScriptGoldenSignature()
    {
        // Ed25519 is deterministic: identical bytes + identical key = identical
        // signature. This test fails if canonicalization or key derivation drift
        // across the two languages.
        var (pub, priv) = Fixtures.TestKeyPair;
        var keys = Ed25519Keys.TryLoad(pub, priv, out var loaded)
            ? loaded
            : throw new Xunit.Sdk.XunitException("fixture keypair failed to load");

        var canonicalBytes = Encoding.UTF8.GetBytes(Fixtures.CanonicalLobbySighting);
        var signature = keys.Sign(canonicalBytes);

        Assert.Equal(Fixtures.SignatureLobbySighting, signature);
        Assert.True(Fixtures.VerifySignature(pub, canonicalBytes, Convert.FromBase64String(signature)));
    }

    [Fact]
    public void ObserverId_MatchesTypeScriptDerivation()
    {
        var (pub, _) = Fixtures.TestKeyPair;
        Assert.Equal(Fixtures.ObserverId, ObserverIdentity.DeriveObserverId(pub));
    }
}

public class CanonicalJsonTests
{
    [Fact]
    public void Objects_SortKeysOrdinal()
    {
        var json = CanonicalJson.Serialize(CanonicalJson.Object(
            ("zebra", "1"), ("apple", "2"), ("banana", "3")));
        Assert.Equal("{\"apple\":\"2\",\"banana\":\"3\",\"zebra\":\"1\"}", json);
    }

    [Fact]
    public void Strings_EscapeDeterministically()
    {
        var json = CanonicalJson.Serialize(CanonicalValue.Str("a\"b\\c\nd\re\tf\u0001"));
        Assert.Equal("\"a\\\"b\\\\c\\nd\\re\\tf\\u0001\"", json);
    }

    [Fact]
    public void NestedArrays_PreserveOrder()
    {
        var json = CanonicalJson.Serialize(CanonicalValue.Arr(new[]
        {
            CanonicalValue.Null(),
            CanonicalValue.Bool(true),
            CanonicalValue.Bool(false),
            CanonicalValue.Number(42L),
        }));
        Assert.Equal("[null,true,false,42]", json);
    }
}

public class ObserverIdentityTests
{
    [Fact]
    public void LoadOrCreate_PersistsAndReloads()
    {
        var path = Path.Combine(Path.GetTempPath(), "gwyf-test-identity-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var first = new ObserverIdentity(path);
            first.LoadOrCreate();
            Assert.StartsWith("obs_", first.ObserverId);
            Assert.Equal(64, first.PublicKey.Length);

            var second = new ObserverIdentity(path);
            second.LoadOrCreate();
            Assert.Equal(first.ObserverId, second.ObserverId);
            Assert.Equal(first.PublicKey, second.PublicKey);

            var sig = second.Sign("payload");
            Assert.True(Fixtures.VerifySignature(first.PublicKey, "payload", sig));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reset_ClearsIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), "gwyf-test-identity-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var identity = new ObserverIdentity(path);
            identity.LoadOrCreate();
            identity.Reset();
            Assert.Equal("", identity.ObserverId);
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CorruptIdentityFile_Regenerates()
    {
        var path = Path.Combine(Path.GetTempPath(), "gwyf-test-identity-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{not json");
            var identity = new ObserverIdentity(path);
            identity.LoadOrCreate();
            Assert.StartsWith("obs_", identity.ObserverId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}