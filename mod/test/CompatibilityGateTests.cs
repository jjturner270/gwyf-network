using GwyfCommunity.Compatibility;
using Xunit;

namespace GwyfCommunity.Tests;

public class CompatibilityGateTests
{
    private static ClientConfig Config(
        int[]? builds = null,
        string min = "0.3.0",
        string latest = "0.3.0",
        bool ingest = true,
        int configVersion = 1,
        int protocol = 1) => new()
    {
        ProtocolVersion = protocol,
        ConfigVersion = configVersion,
        MinimumClientVersion = min,
        LatestClientVersion = latest,
        SupportedGameBuilds = builds ?? new[] { 303 },
        IngestEnabled = ingest,
    };

    [Fact]
    public void SupportedBuild_WithMatchingClient_AllowsObservation()
    {
        var gate = CompatibilityGate.Evaluate(Config(), "0.3.0", "303");
        Assert.Equal(ClientStatus.Working, gate.Status);
        Assert.True(gate.MayObserve);
    }

    [Fact]
    public void NewerClient_IsAllowed()
    {
        var gate = CompatibilityGate.Evaluate(Config(), "0.4.0", "303");
        Assert.Equal(ClientStatus.Working, gate.Status);
    }

    [Fact]
    public void UnsupportedGameBuild_BlocksObservation()
    {
        var gate = CompatibilityGate.Evaluate(Config(builds: new[] { 303 }), "0.3.0", "304");
        Assert.Equal(ClientStatus.UnsupportedGameBuild, gate.Status);
        Assert.False(gate.MayObserve);
        Assert.Equal("303", gate.SupportedBuildsSummary);
    }

    [Fact]
    public void UnknownGameBuild_BlocksObservation()
    {
        var gate = CompatibilityGate.Evaluate(Config(), "0.3.0", null);
        Assert.Equal(ClientStatus.UnsupportedGameBuild, gate.Status);
        Assert.False(gate.MayObserve);
    }

    [Fact]
    public void NonNumericGameBuild_BlocksObservation()
    {
        var gate = CompatibilityGate.Evaluate(Config(), "0.3.0", "2022.3.10f1");
        Assert.Equal(ClientStatus.UnsupportedGameBuild, gate.Status);
        Assert.False(gate.MayObserve);
    }

    [Fact]
    public void ClientBelowMinimum_RequiresUpdate()
    {
        var gate = CompatibilityGate.Evaluate(Config(min: "0.3.0"), "0.2.0", "303");
        Assert.Equal(ClientStatus.ClientUpdateRequired, gate.Status);
        Assert.False(gate.MayObserve);
    }

    [Fact]
    public void ClientAtMinimum_IsAllowed()
    {
        var gate = CompatibilityGate.Evaluate(Config(min: "0.3.0"), "0.3.0", "303");
        Assert.Equal(ClientStatus.Working, gate.Status);
    }

    [Fact]
    public void IngestDisabled_BlocksObservation()
    {
        var gate = CompatibilityGate.Evaluate(Config(ingest: false), "0.3.0", "303");
        Assert.Equal(ClientStatus.IngestDisabled, gate.Status);
        Assert.False(gate.MayObserve);
    }

    [Fact]
    public void UnfetchableConfig_BlocksObservation()
    {
        var gate = CompatibilityGate.Evaluate(null, "0.3.0", "303");
        Assert.Equal(ClientStatus.ConfigServerUnreachable, gate.Status);
        Assert.False(gate.MayObserve);
    }

    [Fact]
    public void MissingMinimumVersion_BlocksObservation()
    {
        var gate = CompatibilityGate.Evaluate(Config(min: ""), "0.3.0", "303");
        Assert.Equal(ClientStatus.ConfigServerUnreachable, gate.Status);
        Assert.False(gate.MayObserve);
    }

    [Fact]
    public void UnparsableMinimumVersion_BlocksObservation()
    {
        var gate = CompatibilityGate.Evaluate(Config(min: "three-dot-zero"), "0.3.0", "303");
        Assert.Equal(ClientStatus.ConfigServerUnreachable, gate.Status);
        Assert.False(gate.MayObserve);
    }

    [Fact]
    public void ServerProtocolMismatch_FailsClosed()
    {
        var gate = CompatibilityGate.Evaluate(Config(protocol: 2), "0.3.0", "303");
        Assert.Equal(ClientStatus.ClientUpdateRequired, gate.Status);
        Assert.False(gate.MayObserve);
    }

    [Fact]
    public void ClientBehindLatest_ButAtMinimum_IsAllowedWithNotice()
    {
        var gate = CompatibilityGate.Evaluate(Config(min: "0.3.0", latest: "0.4.0"), "0.3.0", "303");
        Assert.Equal(ClientStatus.Working, gate.Status);
        Assert.True(gate.MayObserve);
        Assert.True(gate.UpdateAvailable);
        Assert.Contains("0.4.0", gate.Detail);
    }

    [Fact]
    public void ClientAtLatest_HasNoUpdateNotice()
    {
        var gate = CompatibilityGate.Evaluate(Config(latest: "0.4.0"), "0.4.0", "303");
        Assert.Equal(ClientStatus.Working, gate.Status);
        Assert.True(gate.MayObserve);
        Assert.False(gate.UpdateAvailable);
    }

    [Fact]
    public void PostGateOutage_KeepsObservationRunning()
    {
        var granted = CompatibilityGate.Evaluate(Config(), "0.3.0", "303");
        Assert.True(granted.MayObserve);

        // ingest stops responding later: status label degrades for humans but the
        // gate is never revoked mid-session (the flush worker keeps retrying).
        Assert.Equal(ClientStatus.ServerUnreachable, LiveStatus.Current(granted, serviceOnline: false));
        Assert.True(granted.MayObserve);
        Assert.Equal(ClientStatus.Working, LiveStatus.Current(granted, serviceOnline: true));
    }

    [Theory]
    [InlineData("0.2.0", "0.3.0", -1)]
    [InlineData("0.3.0", "0.3.0", 0)]
    [InlineData("0.10.0", "0.9.0", 1)]
    [InlineData("0.3.0", "0.3", 0)]
    [InlineData("0.4.1", "0.4", 1)]
    public void Versions_CompareAsDottedNumbers(string a, string b, int expected)
    {
        Assert.Equal(expected, System.Math.Sign(CompatibilityGate.CompareVersions(a, b)));
    }
}