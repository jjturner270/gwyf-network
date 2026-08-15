using GwyfCommunity.Hooks;
using GwyfCommunity.Models;
using GwyfCommunity.Network;
using GwyfCommunity.Observation;
using Xunit;

namespace GwyfCommunity.Tests;

public class LobbyObserverTests
{
    private sealed class FakeHook : ILobbyHook
    {
        public event Action<LobbySnapshot>? LobbyObserved;
        public event Action? LobbySearchStarted;
        public event Action? LobbySearchCompleted;

        public List<LobbySnapshot> Observed { get; } = new();

        public void Start() { }
        public void Stop() { }

        public void RaiseSearchStarted() => LobbySearchStarted?.Invoke();
        public void RaiseSearchCompleted() => LobbySearchCompleted?.Invoke();
        public void RaiseObserved(LobbySnapshot snapshot)
        {
            Observed.Add(snapshot);
            LobbyObserved?.Invoke(snapshot);
        }

        public void OnSearchStarted() { }
        public void OnSearchCompleted(IReadOnlyList<LobbySnapshot> results) { }
        public void OnHookError(string where, Exception ex) { }
    }

    [Fact]
    public void Snapshot_ProducesProtocolSighting()
    {
        var fake = new FakeHook();
        var sightings = new List<LobbySighting>();
        var events = new List<(string Type, string Context)>();
        var clock = new Func<DateTimeOffset>(() => new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero));
        var observer = new LobbyObserver(fake, sightings.Add, (t, c) => events.Add((t, c)), () => "obs_test", clock);

        fake.RaiseSearchStarted();
        fake.RaiseObserved(new LobbySnapshot
        {
            LobbyId = "room-01",
            Name = "Maple Room",
            MemberCount = 3,
            Capacity = 12,
            MapName = "Castle",
            GameMode = "Classic",
            Metadata = new Dictionary<string, string> { ["isVisible"] = "True" },
        });
        fake.RaiseSearchCompleted();

        var sighting = Assert.Single(sightings);
        Assert.Equal("gwyf.lobby-sighting.v1", sighting.Schema);
        Assert.Equal("room-01", sighting.Lobby.SteamLobbyId);
        Assert.Equal("Maple Room", sighting.Lobby.Name);
        Assert.Equal(3, sighting.Lobby.Members);
        Assert.Equal("Castle", sighting.Lobby.Map);
        Assert.Equal("Classic", sighting.Lobby.Mode);
        Assert.Equal("obs_test", sighting.Observer.InstallationId);
        Assert.Equal("2026-08-15T12:00:00.0000000+00:00", sighting.ObservedAt);

        Assert.Contains("observationBatchId", sighting.Lobby.RawMetadata!.Keys);
        Assert.Contains("searchId", sighting.Lobby.RawMetadata.Keys);
    }

    [Fact]
    public void Snapshot_WithoutId_IsSkipped()
    {
        var fake = new FakeHook();
        var sightings = new List<LobbySighting>();
        var observer = new LobbyObserver(fake, sightings.Add, (_, _) => { }, () => "obs_test");

        fake.RaiseObserved(new LobbySnapshot
        {
            LobbyId = "  ",
            Name = "NoId",
            MemberCount = 1,
            Capacity = 4,
        });

        Assert.Empty(sightings);
    }

    [Fact]
    public void Snapshot_MissingCounts_IsSkipped()
    {
        var fake = new FakeHook();
        var sightings = new List<LobbySighting>();
        var events = new List<(string, string)>();
        var observer = new LobbyObserver(fake, sightings.Add, (t, c) => events.Add((t, c)), () => "obs_test");

        fake.RaiseObserved(new LobbySnapshot { LobbyId = "room-02", Name = "NoCounts" });

        Assert.Empty(sightings);
        Assert.NotEmpty(events);
    }

    [Fact]
    public void LongNames_AreTruncatedToProtocolLimits()
    {
        var fake = new FakeHook();
        var sightings = new List<LobbySighting>();
        var observer = new LobbyObserver(fake, sightings.Add, (_, _) => { }, () => "obs_test");

        fake.RaiseObserved(new LobbySnapshot
        {
            LobbyId = "room-03",
            Name = new string('N', 100),
            MemberCount = 1,
            Capacity = 4,
        });

        var sighting = Assert.Single(sightings);
        Assert.Equal(64, sighting.Lobby.Name!.Length);
    }
}

public class ObservationQueueTests
{
    private static string TempQueuePath() =>
        Path.Combine(Path.GetTempPath(), "gwyf-test-queue-" + Guid.NewGuid().ToString("N") + ".jsonl");

    private static LobbySighting Sight(bool inBatch, string id, string observedAt = "") => new()
    {
        SightingId = id,
        ObservedAt = observedAt == "" ? DateTimeOffset.UtcNow.ToString("O") : observedAt,
        Lobby = new LobbyInfo
        {
            SteamLobbyId = id,
            Members = 1,
            Capacity = 4,
            RawMetadata = inBatch ? new Dictionary<string, object> { ["observationBatchId"] = "batch_x" } : null,
        },
        Observer = new ObserverInfo { InstallationId = "obs_test" },
    };

    [Fact]
    public void Queue_PersistsAndReloads()
    {
        var path = TempQueuePath();
        try
        {
            var queue = new ObservationQueue(path);
            var items = new[] { Sight(false, "a"), Sight(false, "b"), Sight(false, "c") };
            foreach (var item in items) queue.Queue(item);
            Assert.Equal(3, queue.PendingCount);

            var reloaded = new ObservationQueue(path);
            reloaded.Load();
            Assert.Equal(3, reloaded.PendingCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DuplicateSightingId_IsIgnored()
    {
        var path = TempQueuePath();
        try
        {
            var queue = new ObservationQueue(path);
            queue.Queue(Sight(false, "dup"));
            queue.Queue(Sight(false, "dup"));
            Assert.Equal(1, queue.PendingCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExpiredItems_AreDiscardedWithoutUpload()
    {
        var path = TempQueuePath();
        try
        {
            var queue = new ObservationQueue(path, maxItemAge: TimeSpan.FromMinutes(15));
            queue.Queue(Sight(false, "old", "2026-08-01T00:00:00.0000000+00:00"));
            queue.Queue(Sight(false, "fresh", DateTimeOffset.UtcNow.ToString("O")));
            Assert.Equal(2, queue.PendingCount);

            var submissions = new List<IReadOnlyList<object>>();
            queue.FlushAsync(async (batch, _) => { submissions.Add(batch); return new IngestAck(); }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Equal(1, queue.PendingCount);
            var freshOnly = Assert.Single(submissions);
            Assert.Single(freshOnly);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TransportFailure_KeepsItemsAndReportsError()
    {
        var queue = new ObservationQueue(TempQueuePath());
        queue.Queue(Sight(false, "keep"));

        string? lastFailure = null;
        queue.SubmissionFailed += reason => lastFailure = reason;

        queue.FlushAsync((_, _) => Task.FromResult<IngestAck?>(null), CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(1, queue.PendingCount);
        Assert.NotNull(lastFailure);
        Assert.NotNull(queue.LastSubmissionError);
    }

    [Fact]
    public void PartialAck_RemovesOnlyDeliveredIndexes()
    {
        var queue = new ObservationQueue(TempQueuePath(), batchSize: 50);
        queue.Queue(Sight(false, "i0"));
        queue.Queue(Sight(false, "i1"));
        queue.Queue(Sight(false, "i2"));

        queue.FlushAsync((batch, _) => Task.FromResult<IngestAck?>(new IngestAck
        {
            Accepted = 2,
            Items = new List<IngestItemResult>
            {
                new() { Index = 0, Action = "accepted" },
                new() { Index = 1, Action = "duplicate" },
                new() { Index = -1, Action = "rejected", Reason = "unregistered observer" },
            },
        }), CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void RejectedItems_AreRemovedNotRetried()
    {
        var queue = new ObservationQueue(TempQueuePath());
        queue.Queue(Sight(false, "bad"));

        int? rejectedSurfaced = null;
        queue.SubmissionSucceeded += (accepted, duplicates, rejected) => rejectedSurfaced = rejected;

        queue.FlushAsync((batch, _) => Task.FromResult<IngestAck?>(new IngestAck
        {
            Accepted = 0,
            Rejected = 1,
            Items = new List<IngestItemResult> { new() { Index = 0, Action = "rejected", Reason = "bad payload" } },
        }), CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(1, rejectedSurfaced);
    }

    [Fact]
    public void CorruptQueueFile_IsQuarantined()
    {
        var path = TempQueuePath();
        try
        {
            File.WriteAllText(path, "{\"v\":1,\"mod\":\"gwyf-community\"}\n{broken json\n");
            var queue = new ObservationQueue(path);
            queue.Load();
            Assert.Equal(0, queue.PendingCount);
            var quarantined = Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + ".corrupt-*");
            Assert.Single(quarantined);
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Backoff_SequenceMatchesPolicy()
    {
        var backoff = new IntervalBackoff();

        for (var failure = 1; failure <= 6; failure++)
        {
            backoff.RegisterFailure(failure);
            var delay = backoff.RemainingMs(DateTimeOffset.UtcNow);
            Assert.InRange(delay, (int)(Intervals[failure - 1] * 0.8), (int)(Intervals[failure - 1] * 1.2) + 1);
        }
    }

    private static readonly long[] Intervals = { 1000, 2000, 5000, 10_000, 30_000, 60_000 };
}