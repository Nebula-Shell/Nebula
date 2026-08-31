using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.Platform.Windows.Services;

public static class ShellAssetCache
{
    private static readonly string[] LegacyAssetDirectories =
    [
        "tray-icons",
        "audio-session-icons",
        "launcher-icons",
        "media-artwork",
        "media-artwork-cache"
    ];

    public static async Task<string> SaveBytesAsync(
        string directory,
        string prefix,
        string extension,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);

        var normalizedExtension = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..20].ToLowerInvariant();
        var assetDirectory = GetSharedAssetDirectory(directory);
        Directory.CreateDirectory(assetDirectory);
        var assetPath = Path.Combine(assetDirectory, $"{prefix}-{hash}{normalizedExtension}");

        if (File.Exists(assetPath))
        {
            return assetPath;
        }

        var tempPath = Path.Combine(directory, $"{prefix}-{hash}-{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

        try
        {
            File.Move(tempPath, assetPath, overwrite: false);
        }
        catch (IOException) when (File.Exists(assetPath))
        {
            File.Delete(tempPath);
        }

        return assetPath;
    }

    public static string SaveBitmapSourceAsPng(string directory, string prefix, BitmapSource bitmapSource)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
        encoder.Save(stream);
        return SaveBytesAsync(directory, prefix, ".png", stream.ToArray()).GetAwaiter().GetResult();
    }

    public static void RunSafeCleanup(ILoggerService? logService = null)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NebulaShell");
            if (!Directory.Exists(root))
            {
                return;
            }

            var sharedDirectory = Path.Combine(root, "shared-assets");
            Directory.CreateDirectory(sharedDirectory);
            var sharedHashes = BuildSharedHashIndex(sharedDirectory);
            var duplicateCount = 0;
            var tempCount = 0;

            foreach (var legacyDirectoryName in LegacyAssetDirectories)
            {
                var legacyDirectory = Path.Combine(root, legacyDirectoryName);
                if (!Directory.Exists(legacyDirectory))
                {
                    continue;
                }

                foreach (var filePath in Directory.EnumerateFiles(legacyDirectory))
                {
                    try
                    {
                        var fileName = Path.GetFileName(filePath);
                        if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                            || fileName.Contains(".tmp", StringComparison.OrdinalIgnoreCase))
                        {
                            if (IsOlderThan(filePath, TimeSpan.FromHours(12)))
                            {
                                File.Delete(filePath);
                                tempCount++;
                            }

                            continue;
                        }

                        if (!IsAssetExtension(filePath) || !IsOlderThan(filePath, TimeSpan.FromDays(2)))
                        {
                            continue;
                        }

                        var hash = ComputeFileHash(filePath);
                        if (hash is null)
                        {
                            continue;
                        }

                        if (sharedHashes.Contains(hash))
                        {
                            File.Delete(filePath);
                            duplicateCount++;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (duplicateCount > 0 || tempCount > 0)
            {
                logService?.Info("Shell asset cache cleanup completed.", new Dictionary<string, object?>
                {
                    ["duplicateFilesRemoved"] = duplicateCount,
                    ["tempFilesRemoved"] = tempCount
                });
            }
        }
        catch (Exception exception)
        {
            logService?.Warn("Shell asset cache cleanup failed.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
        }
    }

    private static string GetSharedAssetDirectory(string directory)
    {
        var parent = Directory.GetParent(directory)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
        {
            return directory;
        }

        return Path.Combine(parent, "shared-assets");
    }

    private static HashSet<string> BuildSharedHashIndex(string sharedDirectory)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.EnumerateFiles(sharedDirectory))
        {
            var hash = ComputeFileHash(filePath);
            if (!string.IsNullOrWhiteSpace(hash))
            {
                hashes.Add(hash);
            }
        }

        return hashes;
    }

    private static string? ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsAssetExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOlderThan(string filePath, TimeSpan threshold)
    {
        var lastWrite = File.GetLastWriteTimeUtc(filePath);
        return DateTime.UtcNow - lastWrite >= threshold;
    }
}
