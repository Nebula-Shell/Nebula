using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Platform.Windows.Interop;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsWallpaperService : IWallpaperService
{
    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"];
    private static readonly Regex ResolutionSuffixPattern = new(@"_(\d{3,5})x(\d{3,5})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string? TryGetCurrentWallpaperPath()
    {
        var path = TryGetViaSystemParametersInfo();
        if (IsUsableFile(path))
        {
            return path;
        }

        var transcodedWallpaper = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Themes",
            "TranscodedWallpaper");

        return IsUsableFile(transcodedWallpaper) ? transcodedWallpaper : null;
    }

    public bool TrySetWallpaper(string wallpaperPath)
    {
        if (!IsUsableFile(wallpaperPath))
        {
            return false;
        }

        return User32.SystemParametersInfo(
            User32.SpiSetDeskWallpaper,
            0,
            wallpaperPath,
            User32.SpifUpdateIniFile | User32.SpifSendWinIniChange);
    }

    public IReadOnlyList<string> GetDefaultWallpaperPaths()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var roots = new[]
        {
            Path.Combine(windowsDirectory, "Web", "Wallpaper"),
            Path.Combine(windowsDirectory, "Web", "4K", "Wallpaper", "Screen")
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .GroupBy(GetWallpaperVariantKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(GetWallpaperVariantScore)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(path => Path.GetDirectoryName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => GetWallpaperVariantKey(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? TryGetViaSystemParametersInfo()
    {
        var buffer = new StringBuilder(1024);
        return User32.SystemParametersInfo(User32.SpiGetDeskWallpaper, (uint)buffer.Capacity, buffer, 0)
            ? buffer.ToString().TrimEnd('\0').Trim()
            : null;
    }

    private static bool IsUsableFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private static string GetWallpaperVariantKey(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var normalizedName = ResolutionSuffixPattern.Replace(fileName, string.Empty);
        return Path.Combine(directory, normalizedName);
    }

    private static long GetWallpaperVariantScore(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var match = ResolutionSuffixPattern.Match(fileName);
        if (match.Success
            && int.TryParse(match.Groups[1].Value, out var width)
            && int.TryParse(match.Groups[2].Value, out var height))
        {
            return (long)width * height;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}
