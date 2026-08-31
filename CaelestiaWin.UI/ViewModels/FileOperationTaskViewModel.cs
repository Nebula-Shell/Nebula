using CaelestiaWin.Core.Common;

namespace CaelestiaWin.UI.ViewModels;

public sealed class FileOperationTaskViewModel(string title) : ObservableObjectBase
{
    private string _title = title;
    private string _statusText = "Preparing...";
    private int _completedUnits;
    private int _totalUnits;
    private bool _isCompleted;
    private bool _hasError;

    public Guid Id { get; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public int CompletedUnits
    {
        get => _completedUnits;
        set
        {
            if (SetProperty(ref _completedUnits, value))
            {
                OnPropertyChanged(nameof(ProgressFraction));
                OnPropertyChanged(nameof(ProgressPercentage));
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IndicatorGlyph));
            }
        }
    }

    public int TotalUnits
    {
        get => _totalUnits;
        set
        {
            if (SetProperty(ref _totalUnits, value))
            {
                OnPropertyChanged(nameof(ProgressFraction));
                OnPropertyChanged(nameof(ProgressPercentage));
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IndicatorGlyph));
            }
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (SetProperty(ref _isCompleted, value))
            {
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IndicatorGlyph));
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        set
        {
            if (SetProperty(ref _hasError, value))
            {
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IndicatorGlyph));
            }
        }
    }

    public double ProgressFraction => TotalUnits <= 0
        ? 0
        : Math.Clamp((double)CompletedUnits / TotalUnits, 0d, 1d);

    public int ProgressPercentage => (int)Math.Round(ProgressFraction * 100d);

    public string SummaryText => IsCompleted
        ? (HasError ? "Needs attention" : "Done")
        : TotalUnits > 0
            ? $"{ProgressPercentage}%"
            : StatusText;

    public string IndicatorGlyph => HasError
        ? "\uE783"
        : IsCompleted
            ? "\uE73E"
            : ProgressPercentage switch
            {
                >= 75 => "\uE895",
                >= 35 => "\uE76A",
                _ => "\uE823"
            };

    public bool IsProgressVisible => !IsCompleted && !HasError;
}
