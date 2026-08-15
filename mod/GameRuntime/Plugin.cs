using BepInEx;
using GwyfCommunity.Config;
using GwyfCommunity.Diagnostics;
using GwyfCommunity.GameRuntime;
using GwyfCommunity.Hooks;
using GwyfCommunity.Hooks.Game;
using GwyfCommunity.Identity;
using GwyfCommunity.Models;
using GwyfCommunity.Network;
using GwyfCommunity.Observation;

namespace GwyfCommunity;

[BepInPlugin("com.gwyf.community", "GWYF Community Network", "0.2.0")]
[BepInProcess("Golf With Your Friends.exe")]
[BepInProcess("Golf With Your Friends")]
public sealed class Plugin : BaseUnityPlugin
{
    private ObserverIdentity _identity = null!;
    private DiagnosticsPanel _diagnostics = new();
    private ObservationQueue _queue = null!;
    private NetworkClient _network = null!;
    private UnityLobbyHook _lobbyHook = null!;
    private LobbyObserver _lobbyObserver = null!;
    private CourseObserver _courseObserver = null!;
    private CancellationTokenSource _shutdown = new();
    private Task _flushWorker = Task.CompletedTask;

    private void Awake()
    {
        Logger.LogInfo("GWYF Community Network starting");

        var log = new GameLog(Logger);
        var envConfig = ModConfig.FromEnvironment();
        var config = new ModConfig
        {
            NetworkEnabled = envConfig.NetworkEnabled,
            ApiBaseUrl = envConfig.ApiBaseUrl,
            LobbyObservationEnabled = envConfig.LobbyObservationEnabled,
            MaxLobbySightingAgeMinutes = envConfig.MaxLobbySightingAgeMinutes,
        };

        // identity ------------------------------------------------------------
        var stateDir = ModPaths.GetStateDirectory(Paths.GameRootPath);
        _identity = new ObserverIdentity(Path.Combine(stateDir, "observer_identity.json"));
        _identity.LoadOrCreate();
        _diagnostics.ObserverId = _identity.ObserverId;
        _diagnostics.GameBuild = ModVersions.GameBuild ??= GameBuildDetector.Resolve();
        Logger.LogInfo($"observer identity {_identity.ObserverId} (game build {_diagnostics.GameBuild})");

        // network + queue -----------------------------------------------------
        _network = new NetworkClient(new Uri(config.ApiBaseUrl), _identity, ModVersions.Client);
        _queue = new ObservationQueue(
            Path.Combine(stateDir, "observation_queue.jsonl"),
            maxPendingItems: config.MaxPendingItems,
            maxItemAge: TimeSpan.FromMinutes(config.MaxLobbySightingAgeMinutes),
            batchSize: config.BatchSize);
        _queue.Load();

        _queue.SubmissionFailed += reason =>
        {
            _diagnostics.LastError = reason;
            _diagnostics.ServiceOnline = false;
            _diagnostics.PendingUploads = _queue.PendingCount;
            Logger.LogWarning($"observation submission failed: {reason}");
        };
        _queue.SubmissionSucceeded += (accepted, duplicates, rejected) =>
        {
            _diagnostics.ServiceOnline = true;
            _diagnostics.LastError = null;
            _diagnostics.LastUploadAt = DateTimeOffset.UtcNow;
            _diagnostics.LastUploadAccepted += accepted;
            _diagnostics.LastUploadDuplicates += duplicates;
            _diagnostics.LastUploadRejected += rejected;
            _diagnostics.PendingUploads = _queue.PendingCount;
            if (rejected > 0)
            {
                _diagnostics.LastRejectedReason = $"{rejected} of {accepted + duplicates + rejected} rejected by server";
                Logger.LogWarning(_diagnostics.LastRejectedReason);
                if (_network.LastRejectedReason is { } reason &&
                    reason.Contains("clock", StringComparison.OrdinalIgnoreCase))
                {
                    _diagnostics.RecordClockRejection(_network.LastRejectedSightingId ?? "");
                    Logger.LogWarning($"CLOCK: server rejected {_network.LastRejectedSightingId} ({reason})");
                }
            }
            else
            {
                _diagnostics.LastRejectedReason = null;
            }
        };

        // observation sources ------------------------------------------------
        _lobbyHook = new UnityLobbyHook(log, _diagnostics,
            config.LobbyObservationEnabled ? new SessionBrowserBindings(log, Logger, _diagnostics) : null);
        _lobbyHook.Start();

        var observerId = () => _identity.ObserverId;

        _lobbyObserver = new LobbyObserver(
            _lobbyHook,
            sighting => _queue.Queue(sighting),
            (eventType, context) => _queue.Queue(new RuntimeEvent
            {
                EventType = eventType,
                Context = new Dictionary<string, object> { ["context"] = context },
            }),
            observerId);

        // course observation remains opt-in until fingerprints are validated live
        _courseObserver = null; // CourseObserver wiring arrives with REAL GAME HOOK slice
        Logger.LogInfo($"course observation disabled (real game hook pending)");

        // flush worker --------------------------------------------------------
        _flushWorker = Task.Run(() => FlushWorkerAsync(config, _shutdown.Token));
    }

    private async Task FlushWorkerAsync(ModConfig config, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(config.FlushIntervalSeconds);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, ct);
                if (!config.NetworkEnabled) continue;
                await _queue.FlushAsync(_network.SendBatchAsync, ct);
                _diagnostics.PendingUploads = _queue.PendingCount;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _diagnostics.LastError = ex.Message;
            }
        }
    }

    private void OnDestroy()
    {
        _shutdown.Cancel();
        _lobbyHook.Stop();
        _queue.Persist();
        Logger.LogInfo("GWYF Community Network stopped");
    }
}