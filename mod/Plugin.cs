using System.Timers;
using BepInEx;
using BepInEx.Logging;
using GwyfCommunity.Diagnostics;
using GwyfCommunity.Identity;
using GwyfCommunity.Models;
using GwyfCommunity.Network;
using GwyfCommunity.Observation;
using Timer = System.Timers.Timer;

namespace GwyfCommunity;

[BepInPlugin("com.gwyf.community", "GWYF Community Network", "0.1.0")]
[BepInProcess("Golf With Your Friends.exe")]
public sealed class Plugin : BaseUnityPlugin
{
    private ObserverIdentity _identity = new();
    private ObservationQueue _queue = new();
    private NetworkClient _network = null!;
    private DiagnosticsPanel _diagnostics = new();
    private LobbyObserver _lobbyObserver = null!;
    private CourseObserver _courseObserver = null!;
    private Timer _flushTimer = new(10_000);

    private void Awake()
    {
        Logger.LogInfo("GWYF Community Network starting");

        _identity.LoadOrCreate();
        _diagnostics.ObserverId = _identity.ObserverId;

        var baseUrl = new Uri(Config.Bind("Network", "ApiBaseUrl", "https://api.gwyf.community").Value);
        _network = new NetworkClient(baseUrl, _identity);

        _queue = new ObservationQueue();
        _queue.Load();

        _queue.SubmissionFailed += reason => Logger.LogWarning($"observation submission failed: {reason}");
        _queue.SubmissionSucceeded += () =>
        {
            _diagnostics.ServiceOnline = true;
            _diagnostics.PendingUploads = _queue.PendingCount;
            _queue.Persist();
        };

        _lobbyObserver = new LobbyObserver(
            source: new UnityLobbyHook(Logger),
            onSighting: sighting => _queue.Queue(sighting, sighting.SightingId),
            onRuntimeEvent: (type, lobbyId) =>
                _queue.Queue(
                    new RuntimeEvent { EventType = type, Context = new Dictionary<string, object> { ["lobbyId"] = lobbyId } },
                    Guid.NewGuid().ToString("D")));

        _courseObserver = new CourseObserver(
            source: new UnityCourseHook(Logger),
            onSighting: sighting => _queue.Queue(sighting, sighting.SightingId),
            observerId: () => _identity.ObserverId);

        _flushTimer.Elapsed += async (_, _) =>
        {
            await _queue.FlushAsync((payloads, ct) => _network.SendBatchAsync(payloads, ct));
            _diagnostics.PendingUploads = _queue.PendingCount;
        };
        _flushTimer.AutoReset = true;
        _flushTimer.Start();

        Logger.LogInfo($"observer {_identity.ObserverId} ready");
    }

    private void OnDestroy()
    {
        _flushTimer.Stop();
        _queue.Persist();
    }
}

public sealed class UnityLobbyHook : ILobbyObservationSource
{
    public event Action<LobbySighting>? LobbySearchResultReceived;

    public UnityLobbyHook(ManualLogSource logger)
    {
        logger.LogInfo("UnityLobbyHook: wire lobby browser result events here");
    }
}

public sealed class UnityCourseHook : ICourseObservationSource
{
    public event Action<CourseLoaded>? CourseLoaded;

    public UnityCourseHook(ManualLogSource logger)
    {
        logger.LogInfo("UnityCourseHook: wire scene load events here");
    }
}