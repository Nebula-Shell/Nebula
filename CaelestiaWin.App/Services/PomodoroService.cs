using System.Text.Json;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;
using System.Windows.Threading;
using System.IO;

namespace CaelestiaWin.App.Services;

public sealed class PomodoroService : ObservableObjectBase, IPomodoroService
{
    private const int DefaultSessionLengthMinutes = 25;
    private const int DefaultBreakLengthMinutes = 5;
    private const int MinSessionLengthMinutes = 5;
    private const int MaxSessionLengthMinutes = 180;
    private readonly INotificationService _notificationService;
    private readonly ILoggerService _logService;
    private readonly DispatcherTimer _timer;
    private readonly string _statePath;
    private readonly Dictionary<DateOnly, int> _dailyFocusSeconds = [];
    private PomodoroStateKind _state;
    private PomodoroPhaseKind _phase = PomodoroPhaseKind.Focus;
    private int _sessionLengthMinutes = DefaultSessionLengthMinutes;
    private int _breakLengthMinutes = DefaultBreakLengthMinutes;
    private int _remainingSeconds;
    private int _elapsedSeconds;
    private bool _autoCycleEnabled = true;
    private DateTimeOffset _lastTickAtUtc;
    private int _lastPersistedTickSecond;

    public PomodoroService(INotificationService notificationService, ILoggerService logService)
    {
        _notificationService = notificationService;
        _logService = logService;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => OnTimerTick();
        _statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NebulaShell",
            "pomodoro.json");

        LoadState();
        ResetSessionCounters();
    }

    public PomodoroStateKind State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsVisible));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsPaused));
            }
        }
    }

    public PomodoroPhaseKind Phase
    {
        get => _phase;
        private set => SetProperty(ref _phase, value);
    }

    public int SessionLengthMinutes
    {
        get => _sessionLengthMinutes;
        private set => SetProperty(ref _sessionLengthMinutes, value);
    }

    public int BreakLengthMinutes
    {
        get => _breakLengthMinutes;
        private set => SetProperty(ref _breakLengthMinutes, value);
    }

    public int RemainingSeconds
    {
        get => _remainingSeconds;
        private set => SetProperty(ref _remainingSeconds, value);
    }

    public int ElapsedSeconds
    {
        get => _elapsedSeconds;
        private set => SetProperty(ref _elapsedSeconds, value);
    }

    public bool AutoCycleEnabled
    {
        get => _autoCycleEnabled;
        private set => SetProperty(ref _autoCycleEnabled, value);
    }

    public bool IsVisible => State != PomodoroStateKind.Idle;

    public bool IsRunning => State == PomodoroStateKind.Running;

    public bool IsPaused => State == PomodoroStateKind.Paused;

    public void SetSessionLength(int minutes)
    {
        var normalized = Math.Clamp(minutes, MinSessionLengthMinutes, MaxSessionLengthMinutes);
        if (SessionLengthMinutes == normalized)
        {
            return;
        }

        SessionLengthMinutes = normalized;
        if (State == PomodoroStateKind.Idle)
        {
            ResetSessionCounters();
        }

        SaveState();
    }

    public void SetBreakLength(int minutes)
    {
        var normalized = Math.Clamp(minutes, MinSessionLengthMinutes, MaxSessionLengthMinutes);
        if (BreakLengthMinutes == normalized)
        {
            return;
        }

        BreakLengthMinutes = normalized;
        if (State == PomodoroStateKind.Idle && Phase == PomodoroPhaseKind.Break)
        {
            ResetSessionCounters();
        }

        SaveState();
    }

    public void SetAutoCycleEnabled(bool enabled)
    {
        if (AutoCycleEnabled == enabled)
        {
            return;
        }

        AutoCycleEnabled = enabled;
        SaveState();
    }

    public void Start()
    {
        Phase = PomodoroPhaseKind.Focus;
        ResetSessionCounters();
        State = PomodoroStateKind.Running;
        _lastTickAtUtc = DateTimeOffset.UtcNow;
        _lastPersistedTickSecond = 0;
        _timer.Start();
        SaveState();
    }

    public void StartBreak()
    {
        Phase = PomodoroPhaseKind.Break;
        ResetSessionCounters();
        State = PomodoroStateKind.Running;
        _lastTickAtUtc = DateTimeOffset.UtcNow;
        _lastPersistedTickSecond = 0;
        _timer.Start();
        SaveState();
    }

    public void Pause()
    {
        if (State != PomodoroStateKind.Running)
        {
            return;
        }

        ApplyElapsedProgress();
        _timer.Stop();
        State = PomodoroStateKind.Paused;
        SaveState();
    }

    public void Resume()
    {
        if (State != PomodoroStateKind.Paused)
        {
            return;
        }

        State = PomodoroStateKind.Running;
        _lastTickAtUtc = DateTimeOffset.UtcNow;
        _timer.Start();
        SaveState();
    }

    public void Restart()
    {
        if (Phase == PomodoroPhaseKind.Break)
        {
            StartBreak();
            return;
        }

        Start();
    }

    public void Stop()
    {
        if (State == PomodoroStateKind.Idle)
        {
            return;
        }

        if (State == PomodoroStateKind.Running)
        {
            ApplyElapsedProgress();
        }

        _timer.Stop();
        CommitElapsedFocus();
        State = PomodoroStateKind.Idle;
        Phase = PomodoroPhaseKind.Focus;
        ResetSessionCounters();
        SaveState();
    }

    public IReadOnlyList<PomodoroFocusBucket> GetFocusBuckets(PomodoroHistoryRangeKind range)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var start = range == PomodoroHistoryRangeKind.Week
            ? today.AddDays(-6)
            : new DateOnly(today.Year, today.Month, 1);
        var count = range == PomodoroHistoryRangeKind.Week
            ? 7
            : Math.Max(1, today.DayNumber - start.DayNumber + 1);
        var buckets = new List<PomodoroFocusBucket>(count);

        for (var index = 0; index < count; index++)
        {
            var date = start.AddDays(index);
            var seconds = _dailyFocusSeconds.TryGetValue(date, out var storedSeconds)
                ? storedSeconds
                : 0;
            if (date == today && State != PomodoroStateKind.Idle)
            {
                seconds += ElapsedSeconds;
            }

            buckets.Add(new PomodoroFocusBucket(date, seconds));
        }

        return buckets;
    }

    private void OnTimerTick()
    {
        if (State != PomodoroStateKind.Running)
        {
            return;
        }

        ApplyElapsedProgress();
        if (RemainingSeconds <= 0)
        {
            _timer.Stop();
            var completedPhase = Phase;
            var completedDurationMinutes = completedPhase == PomodoroPhaseKind.Focus
                ? SessionLengthMinutes
                : BreakLengthMinutes;
            CommitElapsedFocus();
            _notificationService.Push(
                completedPhase == PomodoroPhaseKind.Focus ? "Pomodoro complete" : "Break complete",
                completedPhase == PomodoroPhaseKind.Focus
                    ? $"{completedDurationMinutes}-minute focus session finished."
                    : $"{completedDurationMinutes}-minute break finished.",
                kind: NotificationKind.Info,
                source: "Pomodoro",
                showToast: true);

            if (AutoCycleEnabled)
            {
                Phase = completedPhase == PomodoroPhaseKind.Focus ? PomodoroPhaseKind.Break : PomodoroPhaseKind.Focus;
                ResetSessionCounters();
                State = PomodoroStateKind.Running;
                _lastTickAtUtc = DateTimeOffset.UtcNow;
                _timer.Start();
            }
            else
            {
                State = PomodoroStateKind.Idle;
                ResetSessionCounters();
            }

            SaveState();
            return;
        }

        if (ElapsedSeconds - _lastPersistedTickSecond >= 30)
        {
            _lastPersistedTickSecond = ElapsedSeconds;
            SaveState();
        }
    }

    private void ApplyElapsedProgress()
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastTickAtUtc == default)
        {
            _lastTickAtUtc = now;
            return;
        }

        var wholeSeconds = (int)Math.Floor((now - _lastTickAtUtc).TotalSeconds);
        if (wholeSeconds <= 0)
        {
            return;
        }

        _lastTickAtUtc = _lastTickAtUtc.AddSeconds(wholeSeconds);
        var targetSeconds = GetCurrentPhaseLengthMinutes() * 60;
        var normalizedElapsed = Math.Min(targetSeconds, ElapsedSeconds + wholeSeconds);
        ElapsedSeconds = normalizedElapsed;
        RemainingSeconds = Math.Max(0, targetSeconds - ElapsedSeconds);
    }

    private void CommitElapsedFocus()
    {
        if (Phase == PomodoroPhaseKind.Focus && ElapsedSeconds > 0)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            _dailyFocusSeconds[today] = _dailyFocusSeconds.TryGetValue(today, out var existing)
                ? existing + ElapsedSeconds
                : ElapsedSeconds;
        }
    }

    private void ResetSessionCounters()
    {
        ElapsedSeconds = 0;
        RemainingSeconds = GetCurrentPhaseLengthMinutes() * 60;
        _lastTickAtUtc = default;
        _lastPersistedTickSecond = 0;
    }

    private int GetCurrentPhaseLengthMinutes()
    {
        return Phase == PomodoroPhaseKind.Break ? BreakLengthMinutes : SessionLengthMinutes;
    }

    private void LoadState()
    {
        if (!File.Exists(_statePath))
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<PomodoroPersistenceModel>(
                File.ReadAllText(_statePath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (payload is null)
            {
                return;
            }

            SessionLengthMinutes = Math.Clamp(payload.SessionLengthMinutes, MinSessionLengthMinutes, MaxSessionLengthMinutes);
            BreakLengthMinutes = Math.Clamp(payload.BreakLengthMinutes, MinSessionLengthMinutes, MaxSessionLengthMinutes);
            AutoCycleEnabled = payload.AutoCycleEnabled;
            foreach (var pair in payload.DailyFocusSeconds)
            {
                if (!DateOnly.TryParse(pair.Key, out var date))
                {
                    continue;
                }

                if (pair.Value <= 0)
                {
                    continue;
                }

                _dailyFocusSeconds[date] = pair.Value;
            }

            TrimHistory();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logService.Warn("Pomodoro state couldn't be loaded. Using defaults.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
    }

    private void SaveState()
    {
        try
        {
            var directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            TrimHistory();
            var payload = new PomodoroPersistenceModel
            {
                SessionLengthMinutes = SessionLengthMinutes,
                BreakLengthMinutes = BreakLengthMinutes,
                AutoCycleEnabled = AutoCycleEnabled,
                DailyFocusSeconds = _dailyFocusSeconds.ToDictionary(
                    pair => pair.Key.ToString("yyyy-MM-dd"),
                    pair => pair.Value,
                    StringComparer.Ordinal)
            };

            File.WriteAllText(_statePath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logService.Warn("Pomodoro state couldn't be saved.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
    }

    private void TrimHistory()
    {
        var cutoff = DateOnly.FromDateTime(DateTime.Now.AddMonths(-12));
        foreach (var date in _dailyFocusSeconds.Keys.Where(date => date < cutoff).ToArray())
        {
            _dailyFocusSeconds.Remove(date);
        }
    }

    private sealed class PomodoroPersistenceModel
    {
        public int SessionLengthMinutes { get; init; } = DefaultSessionLengthMinutes;

        public int BreakLengthMinutes { get; init; } = DefaultBreakLengthMinutes;

        public bool AutoCycleEnabled { get; init; } = true;

        public Dictionary<string, int> DailyFocusSeconds { get; init; } = new(StringComparer.Ordinal);
    }
}
