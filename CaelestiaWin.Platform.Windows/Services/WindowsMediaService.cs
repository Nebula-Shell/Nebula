using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using CaelestiaWin.Platform.Windows.Interop;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsMediaService(
    IUiDispatcher uiDispatcher,
    IMediaArtworkResolver artworkResolver,
    IDiagnosticLogService logService) : IMediaService, IDisposable
{
    private const int WmAppcommand = 0x0319;
    private const int SmtoAbortIfHung = 0x0002;
    private const int RefreshDebounceMilliseconds = 150;
    private const int AppcommandMediaNextTrack = 11;
    private const int AppcommandMediaPreviousTrack = 12;
    private const int AppcommandMediaPlayPause = 14;
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPrevTrack = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyeventfKeyup = 0x0002;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly PeriodicTimer _refreshTimer = new(TimeSpan.FromSeconds(1));
    private CancellationTokenSource? _lifetimeCts;
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _activeSession;
    private CancellationTokenSource? _eventRefreshCts;
    private nint _lastMediaWindowHandle;
    private long _lastFallbackDispatchTicks;
    private bool _started;
    private bool _loggedUnavailable;
    private bool _disableWinRtBridge;
    private bool _managerEventsAttached;
    private readonly Dictionary<string, string> _artworkCache = new(StringComparer.OrdinalIgnoreCase);
    private string _lastResolvedTrackTitle = string.Empty;
    private string _lastResolvedArtist = string.Empty;
    private string _lastResolvedSourceApp = string.Empty;

    public MediaSessionModel CurrentSession { get; } = new();

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _lifetimeCts = new CancellationTokenSource();
        _ = InitializeAsync(_lifetimeCts.Token);
        _ = PollAsync(_lifetimeCts.Token);
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _lifetimeCts?.Cancel();
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;
        _eventRefreshCts?.Cancel();
        _eventRefreshCts?.Dispose();
        _eventRefreshCts = null;
        DetachSessionEvents();
        _sessionManager = null;
        _activeSession = null;
        _lastMediaWindowHandle = nint.Zero;
        _ = uiDispatcher.InvokeAsync(ResetUnavailableState);
    }

    public async Task PlayPauseAsync(CancellationToken cancellationToken = default)
    {
        if (await TryExecuteSessionCommandAsync("TryTogglePlayPauseAsync", AppcommandMediaPlayPause, VkMediaPlayPause, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await SendMediaCommandFallbackAsync(AppcommandMediaPlayPause, VkMediaPlayPause, cancellationToken).ConfigureAwait(false);
    }

    public async Task NextAsync(CancellationToken cancellationToken = default)
    {
        if (await TryExecuteSessionCommandAsync("TrySkipNextAsync", AppcommandMediaNextTrack, VkMediaNextTrack, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await SendMediaCommandFallbackAsync(AppcommandMediaNextTrack, VkMediaNextTrack, cancellationToken).ConfigureAwait(false);
    }

    public async Task PreviousAsync(CancellationToken cancellationToken = default)
    {
        if (await TryExecuteSessionCommandAsync("TrySkipPreviousAsync", AppcommandMediaPreviousTrack, VkMediaPrevTrack, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await SendMediaCommandFallbackAsync(AppcommandMediaPreviousTrack, VkMediaPrevTrack, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Stop();
        _refreshTimer.Dispose();
        _refreshLock.Dispose();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _sessionManager = await RequestSessionManagerAsync().ConfigureAwait(false);
            if (_sessionManager is null)
            {
                throw new InvalidOperationException("GSMTC session manager request returned null.");
            }

            AttachSessionManagerEvents(_sessionManager);
            logService.Info("Windows media session bridge initialized.");
            await RefreshCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _disableWinRtBridge = true;
            if (!_loggedUnavailable)
            {
                _loggedUnavailable = true;
                logService.Warn("Windows media metadata bridge is unavailable. Nebula will use audio-session fallback mode.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });
            }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshCurrentSessionAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_disableWinRtBridge)
            {
                _sessionManager ??= await RequestSessionManagerAsync().ConfigureAwait(false);
                AttachSessionManagerEvents(_sessionManager);
            }

            if (_sessionManager is not null)
            {
                var session = PickBestSession(_sessionManager);
                SetActiveSession(session);

                if (session is null)
                {
                    await RefreshAudioSessionFallbackAsync().ConfigureAwait(false);
                    return;
                }

                var mediaProperties = await session.TryGetMediaPropertiesAsync();
                var playbackInfo = session.GetPlaybackInfo();
                var sourceApp = ToDisplayName(session.SourceAppUserModelId);
                var trackTitle = string.IsNullOrWhiteSpace(mediaProperties.Title) ? "Unknown track" : mediaProperties.Title;
                var artist = string.IsNullOrWhiteSpace(mediaProperties.Artist) ? sourceApp : mediaProperties.Artist;
                var artworkPath = await TryImportSessionArtworkAsync(
                    mediaProperties.Thumbnail,
                    session.SourceAppUserModelId,
                    trackTitle,
                    artist,
                    cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(artworkPath))
                {
                    artworkPath = await artworkResolver.ResolveAsync(
                        new MediaArtworkRequest(sourceApp, trackTitle, artist, null, true),
                        cancellationToken).ConfigureAwait(false);
                }

                RememberResolvedMetadata(trackTitle, artist, sourceApp);

                await uiDispatcher.InvokeAsync(() =>
                {
                    CurrentSession.IsAvailable = true;
                    CurrentSession.HasSession = true;
                    CurrentSession.TrackTitle = trackTitle;
                    CurrentSession.Artist = artist;
                    CurrentSession.SourceApp = sourceApp;
                    CurrentSession.IsPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    CurrentSession.ArtworkPath = artworkPath ?? string.Empty;
                }).ConfigureAwait(false);
                return;
            }

            await RefreshAudioSessionFallbackAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_disableWinRtBridge)
            {
                _disableWinRtBridge = true;
                logService.Warn("Media metadata polling failed. Nebula will switch to audio-session fallback mode.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });
            }

            SetActiveSession(null);
            await RefreshAudioSessionFallbackAsync().ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<bool> TryExecuteSessionCommandAsync(string methodName, int appCommand, byte virtualKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _activeSession ??= _sessionManager is null ? null : PickBestSession(_sessionManager);
            if (_activeSession is null)
            {
                return false;
            }

            var handled = methodName switch
            {
                "TryTogglePlayPauseAsync" => await _activeSession.TryTogglePlayPauseAsync(),
                "TrySkipNextAsync" => await _activeSession.TrySkipNextAsync(),
                "TrySkipPreviousAsync" => await _activeSession.TrySkipPreviousAsync(),
                _ => false
            };

            if (handled)
            {
                ScheduleMediaRefresh(TimeSpan.FromMilliseconds(200), debounce: false);
                ScheduleMediaRefresh(TimeSpan.FromMilliseconds(850), debounce: false);
            }

            return handled;
        }
        catch (Exception exception)
        {
            logService.Warn("Direct media session command failed. Falling back to media keys.", new Dictionary<string, object?>
            {
                ["method"] = methodName,
                ["error"] = exception.Message,
                ["appCommand"] = appCommand,
                ["virtualKey"] = virtualKey
            });
            return false;
        }
    }

    private async Task SendMediaCommandFallbackAsync(int appCommand, byte virtualKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastFallbackDispatchTicks) < 450)
        {
            return;
        }

        Interlocked.Exchange(ref _lastFallbackDispatchTicks, now);

        try
        {
            await Task.Run(() =>
            {
                var handledByKnownMediaWindow = _lastMediaWindowHandle != nint.Zero
                                                && TrySendAppCommand(_lastMediaWindowHandle, appCommand);
                if (!handledByKnownMediaWindow)
                {
                    SendMediaKey(virtualKey);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logService.Warn("Fallback media command dispatch failed.", new Dictionary<string, object?>
            {
                ["appCommand"] = appCommand,
                ["virtualKey"] = virtualKey,
                ["error"] = exception.Message
            });
        }

        ScheduleMediaRefresh(TimeSpan.FromMilliseconds(250), debounce: false);
        ScheduleMediaRefresh(TimeSpan.FromMilliseconds(900), debounce: false);
    }

    private void AttachSessionManagerEvents(GlobalSystemMediaTransportControlsSessionManager? sessionManager)
    {
        if (sessionManager is null || _managerEventsAttached)
        {
            return;
        }

        try
        {
            sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
            sessionManager.SessionsChanged += OnSessionsChanged;
            _managerEventsAttached = true;
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to subscribe to media session manager events. Polling remains active.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
    }

    private void DetachSessionEvents()
    {
        try
        {
            if (_sessionManager is not null && _managerEventsAttached)
            {
                _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _sessionManager.SessionsChanged -= OnSessionsChanged;
            }
        }
        catch
        {
        }

        _managerEventsAttached = false;
        SetActiveSession(null);
    }

    private void SetActiveSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_activeSession, session))
        {
            return;
        }

        try
        {
            if (_activeSession is not null)
            {
                _activeSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _activeSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            }
        }
        catch
        {
        }

        _activeSession = session;

        try
        {
            if (_activeSession is not null)
            {
                _activeSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _activeSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            }
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to subscribe to active media session events. Polling remains active.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        ScheduleMediaRefresh(TimeSpan.FromMilliseconds(RefreshDebounceMilliseconds));
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        ScheduleMediaRefresh(TimeSpan.FromMilliseconds(RefreshDebounceMilliseconds));
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        ScheduleMediaRefresh(TimeSpan.FromMilliseconds(RefreshDebounceMilliseconds));
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        ScheduleMediaRefresh(TimeSpan.FromMilliseconds(RefreshDebounceMilliseconds));
    }

    private void ScheduleMediaRefresh(TimeSpan delay, bool debounce = true)
    {
        if (!_started)
        {
            return;
        }

        CancellationToken token;
        if (debounce)
        {
            _eventRefreshCts?.Cancel();
            _eventRefreshCts?.Dispose();
            _eventRefreshCts = new CancellationTokenSource();
            token = _eventRefreshCts.Token;
        }
        else
        {
            token = _lifetimeCts?.Token ?? CancellationToken.None;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                await RefreshCurrentSessionAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logService.Warn("Scheduled media refresh failed.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });
            }
        }, CancellationToken.None);
    }

    private static async Task<GlobalSystemMediaTransportControlsSessionManager?> RequestSessionManagerAsync()
    {
        return await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    }

    private static GlobalSystemMediaTransportControlsSession? PickBestSession(GlobalSystemMediaTransportControlsSessionManager sessionManager)
    {
        var current = sessionManager.GetCurrentSession();
        if (current is not null)
        {
            return current;
        }

        var sessions = sessionManager.GetSessions();

        GlobalSystemMediaTransportControlsSession? firstSession = null;
        foreach (var session in sessions)
        {
            firstSession ??= session;
            var playbackInfo = session.GetPlaybackInfo();
            if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                return session;
            }
        }

        return firstSession;
    }

    private void ResetUnavailableState()
    {
        CurrentSession.IsAvailable = true;
        CurrentSession.HasSession = false;
        CurrentSession.TrackTitle = "Nothing playing";
        CurrentSession.Artist = "Start media in another app to see it here";
        CurrentSession.SourceApp = string.Empty;
        CurrentSession.IsPlaying = false;
        CurrentSession.ArtworkPath = string.Empty;
    }

    private async Task RefreshAudioSessionFallbackAsync()
    {
        var candidate = await Task.Run(TryGetActiveAudioSessionCandidate).ConfigureAwait(false);
        var artworkPath = string.Empty;

        if (candidate is not null)
        {
            var sourceApp = candidate.Value.SourceApp;
            var effectiveTitle = candidate.Value.TrackTitle;
            var effectiveArtist = candidate.Value.Artist;

            if (IsGenericFallbackTitle(effectiveTitle, sourceApp)
                && string.Equals(_lastResolvedSourceApp, sourceApp, StringComparison.OrdinalIgnoreCase)
                && !IsGenericFallbackTitle(_lastResolvedTrackTitle, sourceApp))
            {
                effectiveTitle = _lastResolvedTrackTitle;
                effectiveArtist = string.IsNullOrWhiteSpace(_lastResolvedArtist)
                    ? effectiveArtist
                    : _lastResolvedArtist;
            }

            artworkPath = await artworkResolver.ResolveAsync(
                new MediaArtworkRequest(sourceApp, effectiveTitle, effectiveArtist, candidate.Value.ExecutablePath, true)).ConfigureAwait(false)
                ?? string.Empty;

            candidate = candidate.Value with
            {
                TrackTitle = effectiveTitle,
                Artist = effectiveArtist,
                ArtworkPath = artworkPath
            };
        }

        _lastMediaWindowHandle = candidate?.WindowHandle ?? nint.Zero;

        await uiDispatcher.InvokeAsync(() =>
        {
            var sourceApp = candidate?.SourceApp ?? string.Empty;
            var incomingTitle = candidate?.TrackTitle ?? "Nothing playing";
            var shouldKeepPreviousTitle = candidate is not null
                                          && IsGenericFallbackTitle(incomingTitle, sourceApp)
                                          && CurrentSession.HasSession
                                          && string.Equals(CurrentSession.SourceApp, sourceApp, StringComparison.OrdinalIgnoreCase)
                                          && !IsGenericFallbackTitle(CurrentSession.TrackTitle, sourceApp);

            CurrentSession.IsAvailable = true;
            CurrentSession.HasSession = candidate is not null;
            CurrentSession.TrackTitle = shouldKeepPreviousTitle ? CurrentSession.TrackTitle : incomingTitle;
            CurrentSession.Artist = candidate?.Artist ?? "Start media in another app to see it here";
            CurrentSession.SourceApp = sourceApp;
            CurrentSession.IsPlaying = candidate?.IsPlaying ?? false;
            CurrentSession.ArtworkPath = artworkPath;

            if (candidate is not null && !IsGenericFallbackTitle(CurrentSession.TrackTitle, sourceApp))
            {
                RememberResolvedMetadata(CurrentSession.TrackTitle, CurrentSession.Artist, sourceApp);
            }
        }).ConfigureAwait(false);
    }

    private MediaFallbackCandidate? TryGetActiveAudioSessionCandidate()
    {
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;

        try
        {
            deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(AudioEndpointInterop.MmDeviceEnumeratorClsid)!)!;
            _ = deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
            var iid = AudioEndpointInterop.AudioSessionManager2Iid;
            _ = device.Activate(ref iid, AudioEndpointInterop.ClsctxAll, nint.Zero, out var managerObject);
            sessionManager = (IAudioSessionManager2)managerObject;

            _ = sessionManager.GetSessionEnumerator(out sessionEnumerator);
            _ = sessionEnumerator.GetCount(out var sessionCount);

            MediaFallbackCandidate? best = null;

            for (var index = 0; index < sessionCount; index++)
            {
                _ = sessionEnumerator.GetSession(index, out var sessionControl);
                try
                {
                    if (sessionControl is not IAudioSessionControl2 sessionControl2)
                    {
                        continue;
                    }

                    _ = sessionControl2.GetState(out var state);
                    if (state == AudioSessionState.Expired)
                    {
                        continue;
                    }

                    _ = sessionControl2.GetProcessId(out var processId);
                    if (processId == 0)
                    {
                        continue;
                    }

                    var peak = TryGetPeakValue(sessionControl);
                    var process = TryGetProcess((int)processId);
                    var sourceApp = process?.ProcessName ?? "Unknown App";
                    var executablePath = TryGetExecutablePath(process);
                    var displayName = TryGetDisplayName(sessionControl2);
                    var windowSnapshot = TryGetBestProcessFamilyWindowTitle(sourceApp, process);
                    var trackTitle = !string.IsNullOrWhiteSpace(windowSnapshot.Title)
                        ? windowSnapshot.Title
                        : !string.IsNullOrWhiteSpace(displayName)
                            ? displayName
                            : sourceApp;
                    var isPlaying = state == AudioSessionState.Active || peak > 0.01f;

                    if (!isPlaying && peak <= 0.001f)
                    {
                        continue;
                    }

                    var candidate = new MediaFallbackCandidate(
                        trackTitle,
                        "Audio session",
                        ToDisplayName(sourceApp),
                        isPlaying,
                        string.Empty,
                        executablePath,
                        windowSnapshot.WindowHandle,
                        ScoreFallbackCandidate(peak, trackTitle, sourceApp));

                    if (best is null || candidate.Score > best.Value.Score)
                    {
                        best = candidate;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(sessionControl);
                }
            }

            return best;
        }
        catch (Exception exception)
        {
            if (!_loggedUnavailable)
            {
                _loggedUnavailable = true;
                logService.Warn("Audio-session fallback media discovery failed.", new Dictionary<string, object?>
                {
                    ["error"] = exception.Message
                });
            }

            return null;
        }
        finally
        {
            if (sessionEnumerator is not null)
            {
                Marshal.ReleaseComObject(sessionEnumerator);
            }

            if (sessionManager is not null)
            {
                Marshal.ReleaseComObject(sessionManager);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (deviceEnumerator is not null)
            {
                Marshal.ReleaseComObject(deviceEnumerator);
            }
        }
    }

    private static float TryGetPeakValue(IAudioSessionControl sessionControl)
    {
        try
        {
            if (sessionControl is not IAudioMeterInformation meter)
            {
                return 0f;
            }

            _ = meter.GetPeakValue(out var peak);
            return peak;
        }
        catch
        {
            return 0f;
        }
    }

    private static Process? TryGetProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetExecutablePath(Process? process)
    {
        if (process is null)
        {
            return null;
        }

        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static WindowTitleCandidate TryGetBestProcessFamilyWindowTitle(string processName, Process? currentProcess)
    {
        var currentProcessTitle = currentProcess?.MainWindowTitle;
        var currentWindowHandle = currentProcess?.MainWindowHandle ?? nint.Zero;
        var enumeratedCurrent = currentProcess is null
            ? default
            : TryGetBestWindowTitleForProcess(currentProcess.Id, processName);

        if (!string.IsNullOrWhiteSpace(enumeratedCurrent.Title)
            && !IsGenericFallbackTitle(enumeratedCurrent.Title, processName))
        {
            return enumeratedCurrent;
        }

        if (!IsGenericFallbackTitle(currentProcessTitle, processName))
        {
            return new WindowTitleCandidate(currentProcessTitle, currentWindowHandle);
        }

        try
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    var candidate = TryGetBestWindowTitleForProcess(process.Id, processName);
                    if (!string.IsNullOrWhiteSpace(candidate.Title)
                        && !IsGenericFallbackTitle(candidate.Title, processName))
                    {
                        return candidate;
                    }

                    if (!string.IsNullOrWhiteSpace(process.MainWindowTitle)
                        && !IsGenericFallbackTitle(process.MainWindowTitle, processName))
                    {
                        return new WindowTitleCandidate(process.MainWindowTitle.Trim(), process.MainWindowHandle);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
        }

        return string.IsNullOrWhiteSpace(currentProcessTitle)
            ? default
            : new WindowTitleCandidate(currentProcessTitle.Trim(), currentWindowHandle);
    }

    private static WindowTitleCandidate TryGetBestWindowTitleForProcess(int processId, string processName)
    {
        var best = default(WindowTitleCandidate);

        _ = EnumWindows((hwnd, parameter) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out var windowProcessId);
            if (windowProcessId != (uint)processId || !IsWindow(hwnd))
            {
                return true;
            }

            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return true;
            }

            var builder = new StringBuilder(length + 1);
            _ = GetWindowText(hwnd, builder, builder.Capacity);
            var title = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (IsGenericFallbackTitle(title, processName) && !string.IsNullOrWhiteSpace(best.Title))
            {
                return true;
            }

            best = new WindowTitleCandidate(title, hwnd);
            return IsGenericFallbackTitle(title, processName);
        }, nint.Zero);

        return best;
    }

    private static float ScoreFallbackCandidate(float peak, string trackTitle, string sourceApp)
    {
        var score = peak;
        if (!IsGenericFallbackTitle(trackTitle, sourceApp))
        {
            score += 10f;
        }

        return score;
    }

    private static string? TryGetDisplayName(IAudioSessionControl2 sessionControl)
    {
        try
        {
            _ = sessionControl.GetDisplayName(out var displayName);
            return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        }
        catch
        {
            return null;
        }
    }

    private string? TryImportProcessArtwork(Process? process)
    {
        if (process is null)
        {
            return null;
        }

        try
        {
            var executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return null;
            }

            if (_artworkCache.TryGetValue(executablePath, out var cachedPath) && File.Exists(cachedPath))
            {
                return cachedPath;
            }

            var artworkDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NebulaShell",
                "media-artwork");
            Directory.CreateDirectory(artworkDirectory);

            var safeName = string.Concat(process.ProcessName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            var iconHandle = TryExtractIconHandle(executablePath);
            if (iconHandle == nint.Zero)
            {
                return null;
            }

            string artworkPath;
            try
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(96, 96));
                if (bitmapSource.CanFreeze)
                {
                    bitmapSource.Freeze();
                }

                artworkPath = ShellAssetCache.SaveBitmapSourceAsPng(artworkDirectory, safeName, bitmapSource);
            }
            finally
            {
                _ = DestroyIcon(iconHandle);
            }

            _artworkCache[executablePath] = artworkPath;
            return artworkPath;
        }
        catch (Exception exception)
        {
            logService.Warn("Media artwork import failed. The media widget will use its glyph fallback.", new Dictionary<string, object?>
            {
                ["process"] = process.ProcessName,
                ["error"] = exception.Message
            });
            return null;
        }
    }

    private async Task<string?> TryImportSessionArtworkAsync(
        IRandomAccessStreamReference? thumbnail,
        string sourceAppUserModelId,
        string? title,
        string? artist,
        CancellationToken cancellationToken)
    {
        if (thumbnail is null)
        {
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size is 0 or > 10_000_000)
            {
                return null;
            }

            var contentType = stream.ContentType ?? string.Empty;
            var extension = contentType.Contains("png", StringComparison.OrdinalIgnoreCase)
                ? ".png"
                : contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || contentType.Contains("jpg", StringComparison.OrdinalIgnoreCase)
                    ? ".jpg"
                    : ".jpg";

            var artworkDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NebulaShell",
                "media-artwork");
            Directory.CreateDirectory(artworkDirectory);

            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var size = (uint)stream.Size;
            var loaded = await reader.LoadAsync(size);
            if (loaded == 0)
            {
                return null;
            }

            var bytes = new byte[loaded];
            reader.ReadBytes(bytes);
            var artworkPath = await ShellAssetCache.SaveBytesAsync(
                artworkDirectory,
                "track",
                extension,
                bytes,
                cancellationToken).ConfigureAwait(false);
            return artworkPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logService.Warn("Media session artwork import failed. Nebula will fall back to app artwork.", new Dictionary<string, object?>
            {
                ["sourceApp"] = sourceAppUserModelId,
                ["title"] = title,
                ["error"] = exception.Message
            });
            return null;
        }
    }

    private static nint TryExtractIconHandle(string executablePath)
    {
        var flags = ShgfiIcon | ShgfiLargeIcon;
        var result = SHGetFileInfo(
            executablePath,
            0,
            out var fileInfo,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);
        return result == nint.Zero ? nint.Zero : fileInfo.hIcon;
    }

    private static bool IsGenericFallbackTitle(string? title, string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        var normalizedTitle = title.Trim();
        var normalizedSource = sourceApp?.Trim() ?? string.Empty;
        return normalizedTitle.Equals("Unknown track", StringComparison.OrdinalIgnoreCase)
               || normalizedTitle.Equals("Nothing playing", StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrWhiteSpace(normalizedSource)
                   && normalizedTitle.Equals(normalizedSource, StringComparison.OrdinalIgnoreCase))
               || normalizedTitle.Equals($"{normalizedSource} Premium", StringComparison.OrdinalIgnoreCase)
               || normalizedTitle.Equals($"{normalizedSource} Free", StringComparison.OrdinalIgnoreCase);
    }

    private void RememberResolvedMetadata(string title, string artist, string sourceApp)
    {
        if (IsGenericFallbackTitle(title, sourceApp))
        {
            return;
        }

        _lastResolvedTrackTitle = title;
        _lastResolvedArtist = artist;
        _lastResolvedSourceApp = sourceApp;
    }

    private static object? InvokeRequired(object target, string methodName)
    {
        var type = target as Type ?? target.GetType();
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        if (method is null)
        {
            throw new MissingMethodException(type.FullName, methodName);
        }

        return method.Invoke(target is Type ? null : target, null);
    }

    private static object? GetPropertyValue(object? target, string propertyName)
    {
        if (target is null)
        {
            return null;
        }

        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(target);
    }

    private static async Task<object?> InvokeAsyncOperationAsync(object? operation)
    {
        if (operation is null)
        {
            return null;
        }

        dynamic dynamicOperation = operation;
        return await dynamicOperation;
    }

    private static Type? ResolveWinRtType(string fullName)
    {
        return Type.GetType($"{fullName}, Windows, ContentType=WindowsRuntime");
    }

    private static string ToDisplayName(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return string.Empty;
        }

        var leaf = sourceAppUserModelId.Split('!')[0];
        var tokens = leaf.Split(['.', '_'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0
            ? sourceAppUserModelId
            : string.Join(' ', tokens.Select(token => char.ToUpperInvariant(token[0]) + token[1..]));
    }

    private static bool TrySendAppCommand(nint windowHandle, int appCommand)
    {
        if (windowHandle == nint.Zero)
        {
            return false;
        }

        var lParam = (nint)(appCommand << 16);
        return SendMessageTimeout(windowHandle, WmAppcommand, windowHandle, lParam, SmtoAbortIfHung, 120, out _) != nint.Zero;
    }

    private static void SendMediaKey(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, 0);
        keybd_event(virtualKey, 0, KeyeventfKeyup, 0);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        int flags,
        int timeout,
        out nint result);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

    private delegate bool EnumWindowsProc(nint windowHandle, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint windowHandle, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint iconHandle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private readonly record struct MediaFallbackCandidate(
        string TrackTitle,
        string Artist,
        string SourceApp,
        bool IsPlaying,
        string ArtworkPath,
        string? ExecutablePath,
        nint WindowHandle,
        float Score);

    private readonly record struct WindowTitleCandidate(string? Title, nint WindowHandle);
}
