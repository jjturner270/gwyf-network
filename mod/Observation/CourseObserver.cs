using GwyfCommunity.Models;

namespace GwyfCommunity.Observation;

public interface ICourseObservationSource
{
    event Action<CourseLoaded> CourseLoaded;
}

public sealed class CourseLoaded
{
    public string DisplayName { get; init; } = "";
    public string SceneName { get; init; } = "";
    public int HoleCount { get; init; }
    public int ObjectCount { get; init; }
    public string? Fingerprint { get; init; }
}

public sealed class CourseObserver
{
    private readonly Action<CourseSighting> _onSighting;
    private readonly Func<string> _observerId;
    private readonly string _clientVersion;
    private string? _currentScene;
    private string? _currentFingerprint;

    public CourseObserver(
        ICourseObservationSource source,
        Action<CourseSighting> onSighting,
        Func<string> observerId,
        string clientVersion = "0.1.0")
    {
        _onSighting = onSighting;
        _observerId = observerId;
        _clientVersion = clientVersion;
        source.CourseLoaded += HandleCourseLoaded;
    }

    public void AttachFingerprint(string sceneName, string fingerprint)
    {
        _currentFingerprint = fingerprint;
        if (_currentScene == sceneName) _currentScene = null;
    }

    public static CourseSighting ToSighting(CourseLoaded course, string? lobbyId, string observerId, string clientVersion)
    {
        return new CourseSighting
        {
            LobbyId = lobbyId,
            Course = new CourseInfo
            {
                DisplayName = course.DisplayName,
                SceneName = course.SceneName,
                Fingerprint = course.Fingerprint,
                HoleCount = course.HoleCount,
                ObjectCount = course.ObjectCount,
            },
            Observer = new ObserverInfo { InstallationId = observerId, ClientVersion = clientVersion },
        };
    }

    private void HandleCourseLoaded(CourseLoaded course)
    {
        _currentScene = course.SceneName;
        _onSighting(ToSighting(course, null, _observerId(), _clientVersion));
    }
}