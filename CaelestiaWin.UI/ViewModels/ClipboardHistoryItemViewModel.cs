using System.Windows.Input;

namespace CaelestiaWin.UI.ViewModels;

public enum ClipboardHistoryItemKind
{
    Text,
    Image,
    Files
}

public sealed class ClipboardHistoryItemViewModel
{
    public ClipboardHistoryItemViewModel(
        string signature,
        ClipboardHistoryItemKind kind,
        DateTime capturedAt,
        ICommand copyCommand,
        string title,
        string preview,
        string secondaryText,
        string glyph,
        string? imagePath = null,
        IReadOnlyList<string>? filePaths = null,
        string? text = null)
    {
        Signature = signature;
        Kind = kind;
        CapturedAt = capturedAt;
        CopyCommand = copyCommand;
        Title = title;
        Preview = preview;
        SecondaryText = secondaryText;
        Glyph = glyph;
        ImagePath = imagePath;
        FilePaths = filePaths ?? [];
        Text = text;
    }

    public string Signature { get; }

    public ClipboardHistoryItemKind Kind { get; }

    public DateTime CapturedAt { get; }

    public ICommand CopyCommand { get; }

    public string Title { get; }

    public string Preview { get; }

    public string SecondaryText { get; }

    public string Glyph { get; }

    public string? ImagePath { get; }

    public IReadOnlyList<string> FilePaths { get; }

    public string? Text { get; }

    public string Timestamp => CapturedAt.ToString("HH:mm");

    public bool HasImagePreview => Kind == ClipboardHistoryItemKind.Image && !string.IsNullOrWhiteSpace(ImagePath);

    public bool HasFiles => Kind == ClipboardHistoryItemKind.Files;

    public bool HasText => Kind == ClipboardHistoryItemKind.Text;
}
