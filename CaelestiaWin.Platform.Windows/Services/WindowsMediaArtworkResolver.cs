using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsMediaArtworkResolver(IDiagnosticLogService logService) : IMediaArtworkResolver
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private readonly Dictionary<string, string> _memoryCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> ResolveAsync(MediaArtworkRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedTitle = NormalizeTitle(request.TrackTitle, request.SourceApp);
        var normalizedArtist = NormalizeArtist(request.Artist, request.SourceApp);
        if (string.IsNullOrWhiteSpace(normalizedArtist)
            && TrySplitArtistAndTitle(request.TrackTitle, out var splitArtist, out var splitTitle))
        {
            normalizedArtist = splitArtist;
            normalizedTitle = splitTitle;
        }

        if (!string.IsNullOrWhiteSpace(normalizedTitle))
        {
            var providerArtwork = await TryResolveProviderArtworkAsync(
                request.SourceApp,
                normalizedTitle,
                normalizedArtist,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(providerArtwork))
            {
                return providerArtwork;
            }
        }

        return request.AllowAppIconFallback
            ? TryImportAppIcon(request.ExecutablePath, request.SourceApp)
            : null;
    }

    private async Task<string?> TryResolveProviderArtworkAsync(
        string sourceApp,
        string trackTitle,
        string artist,
        CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(artist)
            ? trackTitle
            : $"{artist} {trackTitle}";
        var cacheKey = $"itunes|{sourceApp}|{trackTitle}|{artist}";
        var cachePath = GetCachePath("cover", cacheKey, ".jpg");
        var missPath = cachePath + ".miss";

        if (_memoryCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached))
        {
            return cached;
        }

        if (File.Exists(cachePath))
        {
            _memoryCache[cacheKey] = cachePath;
            return cachePath;
        }

        if (IsRecentMiss(missPath))
        {
            return null;
        }

        try
        {
            var requestUri = $"https://itunes.apple.com/search?media=music&entity=song&limit=8&term={Uri.EscapeDataString(query)}";
            using var response = await HttpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                MarkMiss(missPath);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                MarkMiss(missPath);
                return null;
            }

            var best = PickBestArtworkUrl(results, trackTitle, artist);
            if (string.IsNullOrWhiteSpace(best))
            {
                MarkMiss(missPath);
                return null;
            }

            var artworkUri = UpgradeArtworkUrl(best);
            var bytes = await HttpClient.GetByteArrayAsync(artworkUri, cancellationToken).ConfigureAwait(false);
            if (bytes.Length is 0 or > 10_000_000)
            {
                MarkMiss(missPath);
                return null;
            }

            var resolvedCachePath = await ShellAssetCache.SaveBytesAsync(
                Path.GetDirectoryName(cachePath)!,
                "cover",
                ".jpg",
                bytes,
                cancellationToken).ConfigureAwait(false);
            _memoryCache[cacheKey] = resolvedCachePath;

            if (File.Exists(missPath))
            {
                File.Delete(missPath);
            }

            logService.Info("Resolved media artwork through provider cache.", new Dictionary<string, object?>
            {
                ["sourceApp"] = sourceApp,
                ["track"] = trackTitle,
                ["artist"] = artist
            });
            return resolvedCachePath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            MarkMiss(missPath);
            logService.Warn("External media artwork lookup failed.", new Dictionary<string, object?>
            {
                ["sourceApp"] = sourceApp,
                ["track"] = trackTitle,
                ["artist"] = artist,
                ["error"] = exception.Message
            });
            return null;
        }
    }

    private static string? PickBestArtworkUrl(JsonElement results, string title, string artist)
    {
        string? bestUrl = null;
        var bestScore = 0;
        var normalizedTitle = NormalizeForCompare(title);
        var normalizedArtist = NormalizeForCompare(artist);

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("artworkUrl100", out var artworkElement))
            {
                continue;
            }

            var artworkUrl = artworkElement.GetString();
            if (string.IsNullOrWhiteSpace(artworkUrl))
            {
                continue;
            }

            var resultTitle = result.TryGetProperty("trackName", out var trackElement)
                ? NormalizeForCompare(trackElement.GetString())
                : string.Empty;
            var resultArtist = result.TryGetProperty("artistName", out var artistElement)
                ? NormalizeForCompare(artistElement.GetString())
                : string.Empty;

            var score = 10;
            if (!string.IsNullOrWhiteSpace(normalizedTitle) && resultTitle.Contains(normalizedTitle, StringComparison.Ordinal))
            {
                score += 70;
            }
            else if (!string.IsNullOrWhiteSpace(normalizedTitle) && normalizedTitle.Contains(resultTitle, StringComparison.Ordinal))
            {
                score += 45;
            }

            if (!string.IsNullOrWhiteSpace(normalizedArtist) && resultArtist.Contains(normalizedArtist, StringComparison.Ordinal))
            {
                score += 35;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestUrl = artworkUrl;
            }
        }

        return bestScore >= 45 ? bestUrl : null;
    }

    private string? TryImportAppIcon(string? executablePath, string sourceApp)
    {
        executablePath = string.IsNullOrWhiteSpace(executablePath)
            ? TryFindExecutablePathForSource(sourceApp)
            : executablePath;

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        var cacheKey = $"app-icon|{executablePath}";
        if (_memoryCache.TryGetValue(cacheKey, out var cachedPath) && File.Exists(cachedPath))
        {
            return cachedPath;
        }

        var iconHandle = TryExtractIconHandle(executablePath);
        if (iconHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(128, 128));
            if (bitmapSource.CanFreeze)
            {
                bitmapSource.Freeze();
            }

            var artworkDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NebulaShell",
                "media-artwork-cache");
            var artworkPath = ShellAssetCache.SaveBitmapSourceAsPng(artworkDirectory, "app", bitmapSource);
            _memoryCache[cacheKey] = artworkPath;
            return artworkPath;
        }
        catch (Exception exception)
        {
            logService.Warn("App icon artwork fallback failed.", new Dictionary<string, object?>
            {
                ["sourceApp"] = sourceApp,
                ["executablePath"] = executablePath,
                ["error"] = exception.Message
            });
            return null;
        }
        finally
        {
            _ = DestroyIcon(iconHandle);
        }
    }

    private static string NormalizeTitle(string title, string sourceApp)
    {
        if (IsGeneric(title, sourceApp))
        {
            return string.Empty;
        }

        var cleaned = title.Trim();
        if (cleaned.Contains(" - ", StringComparison.Ordinal)
            && !cleaned.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            var parts = cleaned.Split(" - ", 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                return parts[1];
            }
        }

        return cleaned;
    }

    private static string NormalizeArtist(string artist, string sourceApp)
    {
        if (IsGeneric(artist, sourceApp) || artist.Equals("Audio session", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return artist.Trim();
    }

    private static bool TrySplitArtistAndTitle(string value, out string artist, out string title)
    {
        artist = string.Empty;
        title = string.Empty;

        if (string.IsNullOrWhiteSpace(value) || !value.Contains(" - ", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value.Split(" - ", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        artist = parts[0];
        title = parts[1];
        return true;
    }

    private static bool IsGeneric(string? value, string sourceApp)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim();
        return normalized.Equals("Unknown track", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("Nothing playing", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("Audio session", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(sourceApp, StringComparison.OrdinalIgnoreCase)
               || normalized.Equals($"{sourceApp} Premium", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals($"{sourceApp} Free", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string UpgradeArtworkUrl(string artworkUrl)
    {
        return artworkUrl
            .Replace("100x100bb", "600x600bb", StringComparison.OrdinalIgnoreCase)
            .Replace("100x100-75", "600x600-75", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCachePath(string prefix, string cacheKey, string extension)
    {
        var artworkDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NebulaShell",
            "media-artwork");
        Directory.CreateDirectory(artworkDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))[..16];
        return Path.Combine(artworkDirectory, $"{prefix}-{hash}{extension}");
    }

    private static bool IsRecentMiss(string missPath)
    {
        return File.Exists(missPath)
               && DateTime.UtcNow - File.GetLastWriteTimeUtc(missPath) < TimeSpan.FromHours(12);
    }

    private static void MarkMiss(string missPath)
    {
        try
        {
            File.WriteAllText(missPath, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch
        {
        }
    }

    private static nint TryExtractIconHandle(string executablePath)
    {
        var result = SHGetFileInfo(
            executablePath,
            0,
            out var fileInfo,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShgfiIcon | ShgfiLargeIcon);
        return result == nint.Zero ? nint.Zero : fileInfo.hIcon;
    }

    private static string? TryFindExecutablePathForSource(string sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp))
        {
            return null;
        }

        var normalizedSource = NormalizeForCompare(sourceApp);
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                var normalizedProcess = NormalizeForCompare(process.ProcessName);
                if (string.IsNullOrWhiteSpace(normalizedProcess))
                {
                    continue;
                }

                if (!normalizedSource.Contains(normalizedProcess, StringComparison.Ordinal)
                    && !normalizedProcess.Contains(normalizedSource, StringComparison.Ordinal))
                {
                    continue;
                }

                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return path;
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

        return null;
    }

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
}
