using BepInEx;
using BepInEx.Unity.IL2CPP;
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
public sealed class Plugin : BasePlugin
{
    private ObserverIdentity _identity = null!;
    private DiagnosticsPanel _diagnostics = new();
    private ObservationQueue _queue = null!;
    private NetworkClient _network = null!;
    private UnityLobbyHook _lobbyHook = null!;
    private LobbyObserver _lobbyObserver = null!;
    private CancellationTokenSource _shutdown = new();
    private Task _flushWorker = Task.CompletedTask;

    public override void Load()
    {
        Log.LogInfo("GWYF Community Network starting");

        var log = new GameLog(Log);
        var config = BindConfig();

        // identity ------------------------------------------------------------
        var stateDir = ModPaths.GetStateDirectory(Paths.GameRootPath);
        _identity = new ObserverIdentity(Path.Combine(stateDir, "observer_identity.json"));
        _identity.LoadOrCreate();
        _diagnostics.ObserverId = _identity.ObserverId;
        _diagnostics.GameBuild = ModVersions.GameBuild ??= GameBuildDetector.Resolve() ?? "";
        Log.LogInfo($"observer identity {_identity.ObserverId} (game build {_diagnostics.GameBuild})");
        WriteStateFiles(stateDir);

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
            var detail = _network.LastError is { } networkError && networkError != reason
                ? $"{reason} ({networkError})"
                : reason;
            _diagnostics.LastError = detail;
            _diagnostics.ServiceOnline = false;
            _diagnostics.PendingUploads = _queue.PendingCount;
            Log.LogWarning($"observation submission failed: {detail}");
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
            var settled = accepted + duplicates + rejected;
            Log.LogInfo($"flushed {settled} sighting{(settled == 1 ? "" : "s")} (accepted={accepted}, duplicates={duplicates}, rejected={rejected})");
            if (rejected > 0)
            {
                _diagnostics.LastRejectedReason = $"{rejected} of {accepted + duplicates + rejected} rejected by server";
                Log.LogWarning(_diagnostics.LastRejectedReason);
                if (_network.LastRejectedReason is { } reason &&
                    reason.Contains("clock", StringComparison.OrdinalIgnoreCase))
                {
                    _diagnostics.RecordClockRejection(_network.LastRejectedSightingId ?? "");
                    Log.LogWarning($"CLOCK: server rejected {_network.LastRejectedSightingId} ({reason})");
                }
            }
            else
            {
                _diagnostics.LastRejectedReason = null;
            }
        };

        // observation sources ------------------------------------------------
        _lobbyHook = new UnityLobbyHook(log, _diagnostics,
            config.LobbyObservationEnabled ? new SessionBrowserBindings(log, Log, _diagnostics, config.VerboseLogging) : null);
        _lobbyHook.Start();

        var observerId = () => _identity.ObserverId;

        _lobbyObserver = new LobbyObserver(
            _lobbyHook,
            sighting => _queue.Queue(sighting),
            (eventType, context) => _queue.Queue(new RuntimeEvent
            {
                EventType = eventType,
                Context = new Dictionary<string, object> { ["context"] = context },
                Observer = new ObserverInfo
                {
                    InstallationId = _identity.ObserverId,
                    ClientVersion = ModVersions.Client,
                    GameBuild = ModVersions.GameBuild,
                    SearchScope = "default",
                },
            }),
            observerId);

        // course observation remains opt-in until fingerprints are validated live
        Log.LogInfo("course observation disabled (real game hook pending)");

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
                if (!_network.IsRegistered)
                {
                    try
                    {
                        await _network.RegisterObserverAsync(ModVersions.Client, _diagnostics.GameBuild, ct);
                        _network.IsRegistered = true;
                        Log.LogInfo($"observer {_identity.ObserverId} registered");
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"observer registration failed: {ex.Message}");
                    }
                }
                await _queue.FlushAsync(_network.SendBatchAsync, ct);
                _diagnostics.PendingUploads = _queue.PendingCount;
                WriteStateFiles(ModPaths.GetStateDirectory(Paths.GameRootPath));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _diagnostics.LastError = ex.Message;
                WriteStateFiles(ModPaths.GetStateDirectory(Paths.GameRootPath));
            }
        }
    }

    /// <summary>
    /// BepInEx config file (BepInEx/config/com.gwyf.community.cfg) is the source
    /// of truth; environment variables remain as overrides for dev/test runs.
    /// </summary>
    private ModConfig BindConfig()
    {
        var env = ModConfig.FromEnvironment();
        return new ModConfig
        {
            NetworkEnabled = Config.Bind("network", "enabled", env.NetworkEnabled, "Send observations to the community API").Value,
            ApiBaseUrl = Config.Bind("network", "apiBaseUrl", env.ApiBaseUrl, "Community API base URL").Value,
            LobbyObservationEnabled = Config.Bind("observation", "lobbyEnabled", env.LobbyObservationEnabled, "Observe the vanilla public lobby browser").Value,
            CourseObservationEnabled = Config.Bind("observation", "courseEnabled", env.CourseObservationEnabled, "Observe courses (opt-in until fingerprints are validated)").Value,
            ActiveRadarEnabled = Config.Bind("observation", "activeRadar", env.ActiveRadarEnabled, "Proactively scan lobbies (experimental; keep false)").Value,
            MaxPendingItems = Config.Bind("queue", "maxPendingItems", env.MaxPendingItems, "Bounded queue depth").Value,
            MaxLobbySightingAgeMinutes = Config.Bind("queue", "maxSightingAgeMinutes", env.MaxLobbySightingAgeMinutes, "Drop queued sightings older than this").Value,
            BatchSize = Config.Bind("queue", "batchSize", env.BatchSize, "Sightings per signed batch").Value,
            FlushIntervalSeconds = Config.Bind("queue", "flushIntervalSeconds", env.FlushIntervalSeconds, "Seconds between flush attempts").Value,
            VerboseLogging = Config.Bind("diagnostics", "verboseLogging", env.VerboseLogging, "Per-session debug dumps (noisy)").Value,
        };
    }

    /// <summary>Support surface: observer id + rendered diagnostics as files.</summary>
    private void WriteStateFiles(string stateDir)
    {
        try
        {
            File.WriteAllText(Path.Combine(stateDir, "observer-id.txt"), _identity.ObserverId);
            File.WriteAllText(Path.Combine(stateDir, "diagnostics.txt"), _diagnostics.Render());
        }
        catch (Exception)
        {
            // writing support files must never break the game
        }
    }

    private void OnDestroy()
    {
        _shutdown.Cancel();
        _lobbyHook.Stop();
        _queue.Persist();
        try
        {
            File.WriteAllText(Path.Combine(ModPaths.GetStateDirectory(Paths.GameRootPath), "diagnostics.txt"), _diagnostics.Render());
        }
        catch (Exception)
        {
            // best effort
        }
        Log.LogInfo("GWYF Community Network stopped");
    }
}
