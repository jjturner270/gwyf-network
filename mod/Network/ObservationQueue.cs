using System.Text;
using System.Text.Json;
using GwyfCommunity.Models;

namespace GwyfCommunity.Network;

/// <summary>
/// Append-friendly observation queue. Game events are enqueued and control
/// returns immediately; a worker drains batches through the network client.
///
/// Persistence: JSONL file (one envelope line, then one JSON payload per line),
/// appended on enqueue, rewritten atomically after acknowledgements. Replays on
/// startup. The server deduplicates by sightingId, so an ack lost between server
/// and client is safe to retry.
/// </summary>
public sealed class ObservationQueue
{
    private readonly List<QueueItem> _pending = new();
    private readonly HashSet<string> _pendingIds = new();
    private readonly LinkedList<string> _recentIds = new();
    private readonly Dictionary<string, QueueItem> _recentById = new();
    private readonly object _lock = new();

    private readonly string _queueFilePath;
    private readonly int _maxPendingItems;
    private readonly TimeSpan _maxItemAge;
    private readonly int _batchSize;
    private readonly IntervalBackoff _backoff;

    private int _consecutiveFailures;
    private DateTimeOffset _lastFlushAttemptAt;

    public event Action<string>? SubmissionFailed;
    public event Action<int, int, int>? SubmissionSucceeded; // accepted, duplicates, rejected

    public int PendingCount
    {
        get { lock (_lock) return _pending.Count; }
    }

    public string? LastSubmissionError { get; private set; }
    public DateTimeOffset? LastSuccessfulFlushAt { get; private set; }

    public ObservationQueue(
        string queueFilePath,
        int maxPendingItems = 5000,
        TimeSpan? maxItemAge = null,
        int batchSize = 50)
    {
        _queueFilePath = queueFilePath;
        _maxPendingItems = maxPendingItems;
        _maxItemAge = maxItemAge ?? TimeSpan.FromMinutes(15);
        _batchSize = batchSize;
        _backoff = new IntervalBackoff();
    }

    /// <summary>Queues a payload. Never blocks, never throws for game events.</summary>
    public void Queue(Models.LobbySighting payload) => Enqueue(PayloadId(payload), payload.ObservedAt, payload);
    public void Queue(Models.CourseSighting payload) => Enqueue(PayloadId(payload), payload.ObservedAt, payload);
    public void Queue(Models.RuntimeEvent payload) => Enqueue(PayloadId(payload), payload.ObservedAt, payload);

    /// <summary>
    /// Appends one payload line for crash tolerance. A crash between server ack
    /// and file hygiene is repaired by server-side idempotency (unique sightingId).
    /// </summary>
    private void AppendLine(string json)
    {
        try
        {
            var dir = Path.GetDirectoryName(_queueFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (!File.Exists(_queueFilePath))
            {
                File.WriteAllText(_queueFilePath, "{\"v\":1,\"mod\":\"gwyf-community\"}\n");
            }
            using var writer = new StreamWriter(_queueFilePath, append: true);
            writer.Write(json);
            writer.Write('\n');
        }
        catch (Exception)
        {
            // enqueue must never break the game
        }
    }

    private void Enqueue(string sightingId, string observedAt, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        lock (_lock)
        {
            if (_pendingIds.Contains(sightingId)) return;
            if (_recentById.ContainsKey(sightingId))
            {
                // an id we already handled recently: keep the existing pending copy if any
                return;
            }

            if (_pending.Count >= _maxPendingItems)
            {
                // drop oldest queued evidence; newest stays
                var oldest = _pending[0];
                _pending.RemoveAt(0);
                _pendingIds.Remove(oldest.SightingId);
            }

            var item = new QueueItem { SightingId = sightingId, Schema = PayloadSchema(payload), ObservedAt = observedAt, Json = json };
            _pending.Add(item);
            _pendingIds.Add(sightingId);
            Remember(sightingId);
            AppendLine(json);
        }
    }

    private void Remember(string sightingId)
    {
        _recentIds.AddLast(sightingId);
        _recentById[sightingId] = new QueueItem { SightingId = sightingId };
        while (_recentIds.Count > 4096)
        {
            var evicted = _recentIds.First!;
            _recentIds.RemoveFirst();
            _recentById.Remove(evicted.Value);
        }
    }

    /// <summary>
    /// Drains one batch. Returns immediately; network work happens on the caller's
    /// background worker. Items older than the age policy are discarded without
    /// upload. The submit callback returns null for transport failure (backoff).
    /// </summary>
    public async Task FlushAsync(Func<IReadOnlyList<object>, CancellationToken, Task<IngestAck?>> submit, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        _lastFlushAttemptAt = now;

        DiscardExpired(now);

        List<QueueItem> batch;
        lock (_lock)
        {
            batch = _pending.Take(_batchSize).ToList();
        }
        if (batch.Count == 0) return;

        var payloads = batch.Select(b => JsonSerializer.Deserialize<JsonElement>(b.Json)).Cast<object>().ToList();

        IngestAck? ack;
        try
        {
            ack = await submit(payloads, ct);
        }
        catch (Exception ex)
        {
            ack = null;
            LastSubmissionError = ex.Message;
        }

        if (ack is null)
        {
            _consecutiveFailures++;
            _backoff.RegisterFailure(_consecutiveFailures);
            LastSubmissionError ??= "no acknowledgement";
            SubmissionFailed?.Invoke(LastSubmissionError);
            return;
        }

        // server processed the batch: remove exactly what was stored. Every item
        // that received a per-item verdict (accepted / duplicate / rejected) is
        // settled — rejected items cannot succeed on retry and are dropped with
        // the diagnostics surfacing the reason. Only transport failures keep items.
        var settled = new HashSet<int>(ack.Items.Where(i => i.Index >= 0).Select(i => i.Index));
        lock (_lock)
        {
            for (var i = batch.Count - 1; i >= 0; i--)
            {
                if (settled.Contains(i))
                {
                    _pendingIds.Remove(batch[i].SightingId);
                    _pending.Remove(batch[i]);
                }
            }
        }
        Persist();

        _consecutiveFailures = 0;
        _backoff.Reset();
        LastSuccessfulFlushAt = DateTimeOffset.UtcNow;
        LastSubmissionError = null;
        SubmissionSucceeded?.Invoke(ack.Accepted, ack.Duplicates, ack.Rejected);
    }

    /// <summary>Drops items older than the configured maximum age policy.</summary>
    public void DiscardExpired(DateTimeOffset? now = null)
    {
        now ??= DateTimeOffset.UtcNow;
        List<QueueItem> expired;
        lock (_lock)
        {
            expired = _pending
                .Where(i => ParseObservedAt(i.ObservedAt) is DateTimeOffset at && now.Value - at > _maxItemAge)
                .ToList();
            foreach (var item in expired)
            {
                _pendingIds.Remove(item.SightingId);
                _pending.Remove(item);
            }
        }
        if (expired.Count > 0) Persist();
    }

    public int FlushDelayMsRemaining(DateTimeOffset now) => _backoff.RemainingMs(now);

    public void Persist()
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_queueFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("{\"v\":1,\"mod\":\"gwyf-community\"}");
                foreach (var item in _pending) sb.AppendLine(item.Json);
                var tmp = _queueFilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                File.Move(tmp, _queueFilePath, overwrite: true);
            }
            catch (Exception)
            {
                // persistence failure never breaks the game
            }
        }
    }

    /// <summary>
    /// Replays persisted queue state. A structurally broken file is quarantined
    /// (renamed) and a fresh queue starts, preserving identity elsewhere.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_queueFilePath)) return;
            List<QueueItem> loaded;
            try
            {
                var lines = File.ReadAllLines(_queueFilePath);
                if (lines.Length == 0 || !lines[0].StartsWith("{\"v\"")) throw new InvalidDataException("bad envelope");
                loaded = new List<QueueItem>();
                for (var i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var element = JsonSerializer.Deserialize<JsonElement>(line);
                    if (!element.TryGetProperty("sightingId", out var idEl) || idEl.GetString() is null) continue;
                    loaded.Add(new QueueItem
                    {
                        SightingId = idEl.GetString()!,
                        Schema = element.TryGetProperty("schema", out var sch) ? sch.GetString() ?? "" : "",
                        ObservedAt = element.TryGetProperty("observedAt", out var at) ? at.GetString() ?? "" : "",
                        Json = line,
                    });
                }
            }
            catch (Exception)
            {
                Quarantine();
                return;
            }

            _pending.Clear();
            _pendingIds.Clear();
            foreach (var item in loaded)
            {
                if (_pendingIds.Add(item.SightingId))
                {
                    _pending.Add(item);
                    Remember(item.SightingId);
                }
            }
            DiscardExpired();
        }
    }

    private void Quarantine()
    {
        try
        {
            var corrupt = _queueFilePath + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.Move(_queueFilePath, corrupt);
        }
        catch (Exception)
        {
            try { File.Delete(_queueFilePath); }
            catch (Exception) { /* ignore */ }
        }
    }

    private static DateTimeOffset? ParseObservedAt(string iso)
    {
        if (DateTimeOffset.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
            return at;
        return null;
    }

    private static string PayloadId(object payload) => payload switch
    {
        Models.LobbySighting s => s.SightingId,
        Models.CourseSighting c => c.SightingId,
        Models.RuntimeEvent e => e.SightingId,
        _ => throw new ArgumentException($"unsupported payload {payload.GetType().Name}"),
    };

    private static string PayloadSchema(object payload) => payload switch
    {
        Models.LobbySighting => "gwyf.lobby-sighting.v1",
        Models.CourseSighting => "gwyf.course-sighting.v1",
        Models.RuntimeEvent => "gwyf.runtime-event.v1",
        _ => "unknown",
    };

    private sealed class QueueItem
    {
        public string SightingId { get; set; } = "";
        public string Schema { get; set; } = "";
        public string ObservedAt { get; set; } = "";
        public string Json { get; set; } = "";
    }
}

/// <summary>Jittered exponential backoff: 1s, 2s, 5s, 10s, 30s, 60s caps, ±20% jitter.</summary>
public sealed class IntervalBackoff
{
    private static readonly long[] IntervalsMs = { 1000, 2000, 5000, 10_000, 30_000, 60_000 };
    private static readonly Random Jitter = new();

    private long _nextDueMs;
    private int _failureIndex;

    public bool Due(DateTimeOffset now) => now.ToUnixTimeMilliseconds() >= _nextDueMs;

    public void RegisterFailure(int consecutiveFailures)
    {
        _failureIndex = Math.Clamp(consecutiveFailures - 1, 0, IntervalsMs.Length - 1);
        var baseMs = IntervalsMs[_failureIndex];
        var jitter = 0.8 + 0.4 * Jitter.NextDouble();
        _nextDueMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(baseMs * jitter);
    }

    public int RemainingMs(DateTimeOffset now)
    {
        var r = _nextDueMs - now.ToUnixTimeMilliseconds();
        return r > 0 ? (int)r : 0;
    }

    public void Reset()
    {
        _failureIndex = 0;
        _nextDueMs = 0;
    }

    public static double JitterFactor() => 0.8 + 0.4 * Jitter.NextDouble();
}