using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GwyfCommunity.Identity;

namespace GwyfCommunity.Network;

public sealed class IngestItemResult
{
    public int Index { get; set; }
    public string Action { get; set; } = ""; // accepted | duplicate | rejected
    public string? Reason { get; set; }
}

public sealed class IngestAck
{
    public int Accepted { get; set; }
    public int Duplicates { get; set; }
    public int Rejected { get; set; }
    public List<IngestItemResult> Items { get; set; } = new();

    public IEnumerable<int> DeliveredIndexes => Items
        .Where(i => i.Action is "accepted" or "duplicate")
        .Select(i => i.Index);
}

/// <summary>
/// Production network path. Sends signed batches to the existing ingest API and
/// surfaces per-item acknowledgements so the queue can remove exactly what the
/// server stored. Never throws for network-level failures (caller decides whether
/// to retry with backoff); throws only for programming errors.
/// </summary>
public sealed class NetworkClient
{
    private readonly HttpClient _http;
    private readonly Uri _baseUrl;
    private readonly IObserverIdentity _identity;
    private readonly string _userAgent;
    private readonly TimeSpan _requestTimeout;

    public string? LastError { get; private set; }
    public string? LastRejectedReason { get; private set; }
    public string? LastRejectedSightingId { get; private set; }
    public bool IsRegistered { get; set; }

    public NetworkClient(Uri baseUrl, IObserverIdentity identity, string modVersion, TimeSpan? requestTimeout = null)
    {
        _baseUrl = baseUrl;
        _identity = identity;
        _userAgent = $"gwyf-community-mod/{modVersion}";
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        _http = new HttpClient { Timeout = _requestTimeout };
    }

    public async Task RegisterObserverAsync(string clientVersion, string? gameBuild, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            publicKey = _identity.PublicKey,
            clientVersion,
            gameBuild = gameBuild ?? (object?)null,
            protocolVersion = 1,
        });
        using var response = await SendAsync(HttpMethod.Post, "/v1/observers", body, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Sends one signed batch. Returns null when the request failed at the
    /// transport level or the server rejected the batch outright
    /// (401/403/413/429/5xx) — callers retry with backoff. Returns the ack when
    /// the server processed the batch (202), including per-item rejections.
    /// </summary>
    public async Task<IngestAck?> SendBatchAsync(IReadOnlyList<object> payloads, CancellationToken ct)
    {
        var batch = new
        {
            schema = "gwyf.batch.v1",
            protocolVersion = 1,
            items = payloads,
        };
        var body = JsonSerializer.Serialize(batch);
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "/v1/ingest/batch", body, ct);
            if (response.IsSuccessStatusCode)
            {
                LastError = null;
                var ack = JsonSerializer.Deserialize<IngestAck>(await response.Content.ReadAsStringAsync(ct));
                var rejected = ack?.Items.FirstOrDefault(i => i.Action == "rejected");
                LastRejectedReason = rejected?.Reason;
                LastRejectedSightingId = rejected is null ? null : ExtractSightingId(payloads, rejected.Index);
                return ack ?? new IngestAck();
            }

            var status = (int)response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            LastError = $"HTTP {status}: {Truncate(responseBody, 200)}";
            if (status == 401) LastError = $"observer not accepted ({Truncate(responseBody, 120)}) — check registration";
            if (status == 413) LastError = "batch too large";
            if (status == 429) LastError = "rate limited";
            return null;
        }
        catch (TaskCanceledException)
        {
            LastError = $"request timeout ({_requestTimeout.TotalSeconds:0}s)";
            return null;
        }
        catch (HttpRequestException ex)
        {
            LastError = $"network failure: {ex.Message}";
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string body,
        CancellationToken ct)
    {
        var message = new HttpRequestMessage(method, new Uri(_baseUrl, path))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        message.Headers.UserAgent.ParseAdd(_userAgent);
        message.Headers.Add("x-observer-id", _identity.ObserverId);
        message.Headers.Add("x-observer-signature", _identity.Sign(body));
        return await _http.SendAsync(message, ct);
    }

    private static string Truncate(string s, int max)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ');
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static string? ExtractSightingId(IReadOnlyList<object> payloads, int index)
    {
        if (index < 0 || index >= payloads.Count || payloads[index] is not JsonElement element) return null;
        return element.TryGetProperty("sightingId", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
    }
}