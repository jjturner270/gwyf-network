using System.Text.Json;
using System.Text.Json.Serialization;

namespace GwyfCommunity.Network;

public sealed class ObservationQueue
{
    private const int MaxPending = 500;
    private const string QueuePath = "gwyf-observation-queue.json";

    private readonly List<object> _pending = new();
    private readonly HashSet<string> _recentSightingIds = new();
    private readonly object _lock = new();
    private TimeSpan _backoff = TimeSpan.FromSeconds(5);
    private int _consecutiveFailures;

    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    public event Action<string>? SubmissionFailed;
    public event Action? SubmissionSucceeded;

    public void Queue(object payload, string sightingId)
    {
        lock (_lock)
        {
            if (_pending.Count >= MaxPending) return;
            if (!_recentSightingIds.Add(sightingId)) return;
            _pending.Add(payload);
        }
    }

    public async Task FlushAsync(Func<object, CancellationToken, Task> submit)
    {
        List<object> batch;
        lock (_lock)
        {
            batch = _pending.ToList();
        }
        if (batch.Count == 0) return;

        try
        {
            await submit(batch, CancellationToken.None);
            lock (_lock)
            {
                var delivered = new HashSet<string>(batch.Select(IdOf));
                _pending.RemoveAll(p => delivered.Contains(IdOf(p)));
            }
            _consecutiveFailures = 0;
            _backoff = TimeSpan.FromSeconds(5);
            SubmissionSucceeded?.Invoke();
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _backoff = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, _consecutiveFailures)));
            SubmissionFailed?.Invoke(ex.Message);
        }
    }

    public void Persist()
    {
        lock (_lock)
        {
            var serialized = _pending.Select(p => JsonSerializer.Serialize(p)).ToList();
            File.WriteAllText(QueuePath, JsonSerializer.Serialize(serialized));
        }
    }

    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(QueuePath)) return;
            var stored = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(QueuePath));
            if (stored is not null)
            {
                foreach (var json in stored)
                {
                    var element = JsonSerializer.Deserialize<JsonElement>(json);
                    var schema = element.TryGetProperty("schema", out var s) ? s.GetString() : null;
                    switch (schema)
                    {
                        case "gwyf.lobby-sighting.v1":
                            _pending.Add(JsonSerializer.Deserialize<Models.LobbySighting>(json)!);
                            break;
                        case "gwyf.course-sighting.v1":
                            _pending.Add(JsonSerializer.Deserialize<Models.CourseSighting>(json)!);
                            break;
                        case "gwyf.runtime-event.v1":
                            _pending.Add(JsonSerializer.Deserialize<Models.RuntimeEvent>(json)!);
                            break;
                    }
                }
            }
            File.Delete(QueuePath);
        }
    }

    private static string IdOf(object payload) => payload switch
    {
        Models.LobbySighting s => s.SightingId,
        Models.CourseSighting c => c.SightingId,
        Models.RuntimeEvent e => e.SightingId,
        _ => "",
    };
}