using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GwyfCommunity.Identity;

namespace GwyfCommunity.Network;

public sealed class NetworkClient
{
    private readonly HttpClient _http = new();
    private readonly Uri _baseUrl;
    private readonly ObserverIdentity _identity;
    private readonly string _userAgent;

    public NetworkClient(Uri baseUrl, ObserverIdentity identity)
    {
        _baseUrl = baseUrl;
        _identity = identity;
        _userAgent = $"gwyf-community-mod/{identity.ObserverId}";
    }

    public async Task RegisterObserverAsync(string clientVersion, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            publicKey = _identity.PublicKeyHex,
            clientVersion,
            protocolVersion = 1,
        });
        using var response = await SendAsync(HttpMethod.Post, "/v1/observers", body, ct: ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendBatchAsync(IEnumerable<object> payloads, CancellationToken ct)
    {
        var batch = new
        {
            schema = "gwyf.batch.v1",
            protocolVersion = 1,
            items = payloads,
        };
        var body = JsonSerializer.Serialize(batch);
        using var response = await SendAsync(HttpMethod.Post, "/v1/ingest/batch", body, ct: ct);
        response.EnsureSuccessStatusCode();
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
}