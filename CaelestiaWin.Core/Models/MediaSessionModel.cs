using CaelestiaWin.Core.Common;

namespace CaelestiaWin.Core.Models;

public sealed class MediaSessionModel : ObservableObjectBase
{
    private bool _isAvailable = true;
    private bool _hasSession;
    private string _trackTitle = "Nothing playing";
    private string _artist = "Start media in another app to see it here";
    private bool _isPlaying;
    private string _sourceApp = string.Empty;
    private string _artworkPath = string.Empty;

    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }

    public bool HasSession
    {
        get => _hasSession;
        set => SetProperty(ref _hasSession, value);
    }

    public string TrackTitle
    {
        get => _trackTitle;
        set => SetProperty(ref _trackTitle, value);
    }

    public string Artist
    {
        get => _artist;
        set => SetProperty(ref _artist, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public string SourceApp
    {
        get => _sourceApp;
        set => SetProperty(ref _sourceApp, value);
    }

    public string ArtworkPath
    {
        get => _artworkPath;
        set => SetProperty(ref _artworkPath, value);
    }
}
