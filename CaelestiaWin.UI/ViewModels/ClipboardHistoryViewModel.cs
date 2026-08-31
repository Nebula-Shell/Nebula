using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using CaelestiaWin.Core.Common;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.UI.Commands;

namespace CaelestiaWin.UI.ViewModels;

public sealed class ClipboardHistoryViewModel : ObservableObjectBase
{
    private const int MaxItems = 20;
    private readonly IAppStateService _appStateService;
    private readonly DispatcherTimer _pollTimer;
    private readonly string _imageCacheDirectory;
    private string _lastSignature = string.Empty;
    private bool _isOpen;

    public ClipboardHistoryViewModel(IAppStateService appStateService)
    {
        _appStateService = appStateService;
        _imageCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NebulaShell",
            "clipboard-history");
        Directory.CreateDirectory(_imageCacheDirectory);
        Items = [];
        CloseCommand = new RelayCommand(() => _appStateService.IsClipboardHistoryOpen = false);
        ClearCommand = new RelayCommand(Clear);
        IsOpen = _appStateService.IsClipboardHistoryOpen;

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _pollTimer.Tick += (_, _) => CaptureCurrentClipboardContent();
        _pollTimer.Start();

        _appStateService.PropertyChanged += OnAppStatePropertyChanged;
        CaptureCurrentClipboardContent();
    }

    public ObservableCollection<ClipboardHistoryItemViewModel> Items { get; }

    public ICommand CloseCommand { get; }

    public ICommand ClearCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public bool HasItems => Items.Count > 0;

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(IAppStateService.IsClipboardHistoryOpen))
        {
            return;
        }

        IsOpen = _appStateService.IsClipboardHistoryOpen;
        if (IsOpen)
        {
            CaptureCurrentClipboardContent();
        }
    }

    private void CaptureCurrentClipboardContent()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                CaptureCurrentClipboardFiles();
                return;
            }

            if (Clipboard.ContainsImage())
            {
                CaptureCurrentClipboardImage();
                return;
            }

            if (!Clipboard.ContainsText())
            {
                return;
            }

            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var signature = $"text:{ComputeHash(text)}";
            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                return;
            }

            AddItem(new ClipboardHistoryItemViewModel(
                signature,
                ClipboardHistoryItemKind.Text,
                DateTime.Now,
                new RelayCommand(() => RestoreText(text, signature)),
                "Text",
                text.Length > 180 ? $"{text[..180]}..." : text,
                "Copied text",
                "\uE8C8",
                text: text));
        }
        catch (Exception)
        {
            // Ignore any clipboard access exceptions to prevent shell crash
        }
    }

    private void CaptureCurrentClipboardFiles()
    {
        var fileDropList = Clipboard.GetFileDropList();
        if (fileDropList.Count == 0)
        {
            return;
        }

        var paths = fileDropList.Cast<string>().Where(File.Exists).Concat(fileDropList.Cast<string>().Where(Directory.Exists)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var signature = $"files:{ComputeHash(string.Join("|", paths))}";
        if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
        {
            return;
        }

        var title = paths.Length == 1 ? Path.GetFileName(paths[0]) : $"{paths.Length} items";
        var preview = paths.Length == 1
            ? paths[0]
            : string.Join(Environment.NewLine, paths.Take(3).Select(Path.GetFileName));
        var secondaryText = paths.Length == 1 ? "Copied file or folder" : "Copied files and folders";

        AddItem(new ClipboardHistoryItemViewModel(
            signature,
            ClipboardHistoryItemKind.Files,
            DateTime.Now,
            new RelayCommand(() => RestoreFiles(paths, signature)),
            title,
            preview,
            secondaryText,
            "\uE8B7",
            filePaths: paths));
    }

    private void CaptureCurrentClipboardImage()
    {
        var image = Clipboard.GetImage();
        if (image is null)
        {
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var bytes = stream.ToArray();
        var hash = ComputeHash(bytes);
        var signature = $"image:{hash}";
        if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
        {
            return;
        }

        var imagePath = Path.Combine(_imageCacheDirectory, $"{hash}.png");
        if (!File.Exists(imagePath))
        {
            File.WriteAllBytes(imagePath, bytes);
        }

        AddItem(new ClipboardHistoryItemViewModel(
            signature,
            ClipboardHistoryItemKind.Image,
            DateTime.Now,
            new RelayCommand(() => RestoreImage(imagePath, signature)),
            "Image",
            "Copied image",
            "Image data",
            "\uE91B",
            imagePath: imagePath));
    }

    private void AddItem(ClipboardHistoryItemViewModel item)
    {
        for (var index = Items.Count - 1; index >= 0; index--)
        {
            if (string.Equals(Items[index].Signature, item.Signature, StringComparison.Ordinal))
            {
                Items.RemoveAt(index);
            }
        }

        _lastSignature = item.Signature;
        Items.Insert(0, item);

        while (Items.Count > MaxItems)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        OnPropertyChanged(nameof(HasItems));
    }

    private void RestoreText(string text, string signature)
    {
        try
        {
            Clipboard.SetText(text);
            _lastSignature = signature;
            _appStateService.IsClipboardHistoryOpen = false;
        }
        catch (Exception)
        {
        }
    }

    private void RestoreImage(string imagePath, string signature)
    {
        try
        {
            if (!File.Exists(imagePath))
            {
                return;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(imagePath, UriKind.Absolute);
            image.EndInit();
            if (image.CanFreeze)
            {
                image.Freeze();
            }

            Clipboard.SetImage(image);
            _lastSignature = signature;
            _appStateService.IsClipboardHistoryOpen = false;
        }
        catch (Exception)
        {
        }
    }

    private void RestoreFiles(IReadOnlyList<string> paths, string signature)
    {
        try
        {
            var collection = new StringCollection();
            foreach (var path in paths)
            {
                collection.Add(path);
            }

            Clipboard.SetFileDropList(collection);
            _lastSignature = signature;
            _appStateService.IsClipboardHistoryOpen = false;
        }
        catch (Exception)
        {
        }
    }

    private void Clear()
    {
        Items.Clear();
        _lastSignature = string.Empty;
        OnPropertyChanged(nameof(HasItems));
    }

    private static string ComputeHash(string value)
    {
        return ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static string ComputeHash(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
