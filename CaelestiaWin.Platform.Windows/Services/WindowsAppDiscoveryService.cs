using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsAppDiscoveryService(
    IDiagnosticLogService logService,
    IAppStateService appStateService) : IAppDiscoveryService
{
    private static readonly HashSet<char> InvalidFileNameChars = new(Path.GetInvalidFileNameChars());
    private readonly ConcurrentDictionary<string, string> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _packageInstallLocationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<AppLaunchItem>? _cachedApps;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<AppLaunchItem>> GetAppsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var cacheMinutes = Math.Max(1, appStateService.Config.Performance.AppDiscoveryCacheMinutes);
        var cacheStillValid = _cachedApps is not null && DateTimeOffset.UtcNow - _lastRefresh < TimeSpan.FromMinutes(cacheMinutes);

        if (!forceRefresh && cacheStillValid)
        {
            return _cachedApps ?? [];
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            cacheStillValid = _cachedApps is not null && DateTimeOffset.UtcNow - _lastRefresh < TimeSpan.FromMinutes(cacheMinutes);
            if (!forceRefresh && cacheStillValid)
            {
                return _cachedApps ?? [];
            }

            var entries = new ConcurrentDictionary<string, AppLaunchItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var shortcutDirectory in GetShortcutDirectories())
            {
                if (!Directory.Exists(shortcutDirectory))
                {
                    continue;
                }

                foreach (var shortcutPath in Directory.EnumerateFiles(shortcutDirectory, "*.lnk", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = CreateShortcutItem(shortcutPath);
                    if (item is not null)
                    {
                        entries.TryAdd(item.Id, item);
                    }
                }
            }

            foreach (var executableDirectory in GetExecutableDirectories())
            {
                if (!Directory.Exists(executableDirectory))
                {
                    continue;
                }

                foreach (var executablePath in Directory.EnumerateFiles(executableDirectory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = CreateExecutableItem(executablePath);
                    if (item is not null)
                    {
                        entries.TryAdd(item.Id, item);
                    }
                }
            }

            foreach (var startApp in EnumeratePackagedApps())
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.TryAdd(startApp.Id, startApp);
            }

            _cachedApps = DeduplicateEntries(entries.Values)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _lastRefresh = DateTimeOffset.UtcNow;

            return _cachedApps;
        }
        catch (Exception exception)
        {
            logService.Error("App discovery failed. Returning cached launcher data if available.", exception);
            return _cachedApps ?? [];
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static IEnumerable<string> GetShortcutDirectories()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
    }

    private static IEnumerable<string> GetExecutableDirectories()
    {
        // WindowsApps contains user-facing app aliases. Avoid System32/SysWOW64 here so the
        // launcher does not surface low-level command-line utilities like at.exe as apps.
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
    }

    private AppLaunchItem? CreateShortcutItem(string shortcutPath)
    {
        var displayName = Path.GetFileNameWithoutExtension(shortcutPath);
        if (ShouldSkip(displayName))
        {
            return null;
        }

        var (targetPath, arguments) = TryResolveShortcut(shortcutPath);
        var iconPath = TryImportAppIcon(targetPath ?? shortcutPath, displayName);
        return new AppLaunchItem(
            $"{displayName}|{shortcutPath}".ToLowerInvariant(),
            displayName,
            shortcutPath,
            arguments,
            targetPath,
            "StartMenu",
            targetPath,
            iconPath);
    }

    private AppLaunchItem? CreateExecutableItem(string executablePath)
    {
        var displayName = Path.GetFileNameWithoutExtension(executablePath);
        if (ShouldSkip(displayName))
        {
            return null;
        }

        return new AppLaunchItem(
            $"{displayName}|{executablePath}".ToLowerInvariant(),
            displayName,
            executablePath,
            null,
            executablePath,
            "Executable",
            executablePath,
            TryImportAppIcon(executablePath, displayName));
    }

    private IReadOnlyList<AppLaunchItem> EnumeratePackagedApps()
    {
        try
        {
            var output = ExecutePowerShell(
                "Get-StartApps | Select-Object Name, AppID | ConvertTo-Json -Compress");
            if (string.IsNullOrWhiteSpace(output))
            {
                return [];
            }

            using var document = JsonDocument.Parse(output);
            var entries = new List<AppLaunchItem>();

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    AddPackagedApp(entries, element);
                }
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                AddPackagedApp(entries, document.RootElement);
            }

            return entries;
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to enumerate packaged apps from Windows Start inventory.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message
            });
            return [];
        }
    }

    private void AddPackagedApp(List<AppLaunchItem> entries, JsonElement element)
    {
        if (!TryReadString(element, "Name", out var displayName)
            || string.IsNullOrWhiteSpace(displayName)
            || ShouldSkip(displayName)
            || !TryReadString(element, "AppID", out var appId)
            || string.IsNullOrWhiteSpace(appId)
            || !LooksLikeAppUserModelId(appId))
        {
            return;
        }

        entries.Add(new AppLaunchItem(
            $"uwp|{appId}".ToLowerInvariant(),
            displayName,
            $"shell:AppsFolder\\{appId}",
            null,
            "Packaged Windows app",
            "UWP",
            appId,
            TryImportPackagedAppIcon(appId, displayName)));
    }

    private string? TryImportAppIcon(string executablePath, string displayName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return null;
            }

            if (_iconCache.TryGetValue(executablePath, out var cachedPath) && File.Exists(cachedPath))
            {
                return cachedPath;
            }

            var iconDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NebulaShell",
                "launcher-icons");
            Directory.CreateDirectory(iconDirectory);

            var safeName = CreateSafeFileName(displayName);
            var iconHandle = TryExtractIconHandle(executablePath);
            if (iconHandle == nint.Zero)
            {
                return null;
            }

            string iconPath;
            try
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(64, 64));
                if (bitmapSource.CanFreeze)
                {
                    bitmapSource.Freeze();
                }

                iconPath = ShellAssetCache.SaveBitmapSourceAsPng(iconDirectory, safeName, bitmapSource);
            }
            finally
            {
                _ = DestroyIcon(iconHandle);
            }

            _iconCache[executablePath] = iconPath;
            return iconPath;
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to import launcher app icon.", new Dictionary<string, object?>
            {
                ["app"] = displayName,
                ["error"] = exception.Message
            });
            return null;
        }
    }

    private string? TryImportPackagedAppIcon(string appUserModelId, string displayName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(appUserModelId))
            {
                return null;
            }

            var cacheKey = $"uwp:{appUserModelId}";
            if (_iconCache.TryGetValue(cacheKey, out var cachedPath) && File.Exists(cachedPath))
            {
                return cachedPath;
            }

            var iconDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NebulaShell",
                "launcher-icons");
            Directory.CreateDirectory(iconDirectory);

            var safeName = CreateSafeFileName(displayName);
            var bitmapSource = TryCreateShellItemBitmapSource($"shell:AppsFolder\\{appUserModelId}");
            if (bitmapSource is not null)
            {
                var shellIconPath = ShellAssetCache.SaveBitmapSourceAsPng(iconDirectory, safeName, bitmapSource);
                _iconCache[cacheKey] = shellIconPath;
                return shellIconPath;
            }

            var logoPath = TryResolvePackagedLogoPath(appUserModelId);
            if (string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath))
            {
                return null;
            }

            var iconPath = ImportBitmapFile(logoPath, iconDirectory, safeName);
            if (iconPath is null)
            {
                return null;
            }

            _iconCache[cacheKey] = iconPath;
            return iconPath;
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to import launcher packaged app icon.", new Dictionary<string, object?>
            {
                ["app"] = displayName,
                ["aumid"] = appUserModelId,
                ["error"] = exception.Message
            });
            return null;
        }
    }

    private string? TryResolvePackagedLogoPath(string appUserModelId)
    {
        var separatorIndex = appUserModelId.IndexOf('!');
        if (separatorIndex <= 0 || separatorIndex >= appUserModelId.Length - 1)
        {
            return null;
        }

        var packageFamilyName = appUserModelId[..separatorIndex];
        var appId = appUserModelId[(separatorIndex + 1)..];
        var installLocation = GetPackageInstallLocation(packageFamilyName);
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return null;
        }

        var manifestPath = Path.Combine(installLocation, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(manifestPath);
            var applicationsNode = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "Applications");
            var applicationNode = applicationsNode?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Application"
                                           && string.Equals(element.Attribute("Id")?.Value, appId, StringComparison.OrdinalIgnoreCase))
                ?? applicationsNode?.Elements().FirstOrDefault(element => element.Name.LocalName == "Application");

            if (applicationNode is null)
            {
                return null;
            }

            var visualElementsNode = applicationNode.Elements().FirstOrDefault(element => element.Name.LocalName.Contains("VisualElements", StringComparison.Ordinal));
            var logoCandidates = new[]
            {
                visualElementsNode?.Attribute("Square150x150Logo")?.Value,
                visualElementsNode?.Attribute("Square44x44Logo")?.Value,
                visualElementsNode?.Attribute("Logo")?.Value,
                visualElementsNode?.Attribute("SmallLogo")?.Value
            };

            foreach (var candidate in logoCandidates)
            {
                var resolved = ResolvePackagedAssetPath(installLocation, candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }
        }
        catch (Exception exception)
        {
            logService.Warn("Failed to resolve packaged app logo from manifest.", new Dictionary<string, object?>
            {
                ["aumid"] = appUserModelId,
                ["error"] = exception.Message
            });
        }

        return null;
    }

    private string? GetPackageInstallLocation(string packageFamilyName)
    {
        if (_packageInstallLocationCache.TryGetValue(packageFamilyName, out var cachedLocation))
        {
            return cachedLocation;
        }

        var output = ExecutePowerShell(
            $"Get-AppxPackage -PackageFamilyName '{packageFamilyName.Replace("'", "''", StringComparison.Ordinal)}' | Select-Object -First 1 -ExpandProperty InstallLocation");
        var installLocation = string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        _packageInstallLocationCache[packageFamilyName] = installLocation;
        return installLocation;
    }

    private static string? ResolvePackagedAssetPath(string installLocation, string? manifestRelativePath)
    {
        if (string.IsNullOrWhiteSpace(manifestRelativePath))
        {
            return null;
        }

        var normalizedRelativePath = manifestRelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var absolutePath = Path.Combine(installLocation, normalizedRelativePath);
        if (File.Exists(absolutePath))
        {
            return absolutePath;
        }

        var directory = Path.GetDirectoryName(absolutePath);
        var stem = Path.GetFileNameWithoutExtension(absolutePath);
        var extension = Path.GetExtension(absolutePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, $"{stem}*{extension}", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => ScorePackagedAssetVariant(path, stem))
            .FirstOrDefault();
    }

    private static int ScorePackagedAssetVariant(string path, string stem)
    {
        var score = 0;
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(fileName, stem, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        if (fileName.Contains("targetsize-256", StringComparison.OrdinalIgnoreCase))
        {
            score += 700;
        }
        else if (fileName.Contains("targetsize-128", StringComparison.OrdinalIgnoreCase))
        {
            score += 600;
        }
        else if (fileName.Contains("scale-400", StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }
        else if (fileName.Contains("scale-200", StringComparison.OrdinalIgnoreCase))
        {
            score += 400;
        }
        else if (fileName.Contains("scale-150", StringComparison.OrdinalIgnoreCase))
        {
            score += 300;
        }

        return score;
    }

    private static string? ImportBitmapFile(string bitmapPath, string iconDirectory, string safeName)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(bitmapPath, UriKind.Absolute);
            bitmap.DecodePixelWidth = 64;
            bitmap.DecodePixelHeight = 64;
            bitmap.EndInit();
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return ShellAssetCache.SaveBitmapSourceAsPng(iconDirectory, safeName, bitmap);
        }
        catch
        {
            return null;
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

    private static BitmapSource? TryCreateShellItemBitmapSource(string parsingName)
    {
        IShellItemImageFactory? imageFactory = null;
        nint hBitmap = nint.Zero;

        try
        {
            var imageFactoryGuid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(parsingName, nint.Zero, ref imageFactoryGuid, out imageFactory);

            var hr = imageFactory.GetImage(
                new NativeSize(64, 64),
                ShellItemImageFlags.IconOnly | ShellItemImageFlags.BiggerSizeOk | ShellItemImageFlags.ResizeToFit,
                out hBitmap);

            if (hr != 0 || hBitmap == nint.Zero)
            {
                return null;
            }

            var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(64, 64));

            if (bitmapSource.CanFreeze)
            {
                bitmapSource.Freeze();
            }

            return bitmapSource;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != nint.Zero)
            {
                _ = DeleteObject(hBitmap);
            }

            if (imageFactory is not null)
            {
                Marshal.FinalReleaseComObject(imageFactory);
            }
        }
    }

    private static string CreateSafeFileName(string displayName)
    {
        var builder = new StringBuilder(displayName.Length);
        foreach (var character in displayName)
        {
            builder.Append(InvalidFileNameChars.Contains(character) ? '_' : character);
        }

        return builder.Length == 0 ? "app" : builder.ToString();
    }

    private static bool ShouldSkip(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return true;
        }

        return displayName.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
               || displayName.Contains("update", StringComparison.OrdinalIgnoreCase)
               || displayName.Contains("helper", StringComparison.OrdinalIgnoreCase)
               || displayName.Contains("crash", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeAppUserModelId(string appId)
    {
        return !string.IsNullOrWhiteSpace(appId)
               && !Path.IsPathRooted(appId)
               && (appId.Contains('!') || !appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<AppLaunchItem> DeduplicateEntries(IEnumerable<AppLaunchItem> entries)
    {
        var deduplicated = new Dictionary<string, AppLaunchItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var key = NormalizeDisplayName(entry.DisplayName);
            if (!deduplicated.TryGetValue(key, out var existing))
            {
                deduplicated[key] = entry;
                continue;
            }

            if (GetSourcePriority(entry.Source) > GetSourcePriority(existing.Source))
            {
                deduplicated[key] = entry;
            }
        }

        return deduplicated.Values.ToArray();
    }

    private static string NormalizeDisplayName(string displayName)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim().Replace("  ", " ", StringComparison.Ordinal);
    }

    private static int GetSourcePriority(string source)
    {
        return source switch
        {
            "UWP" => 3,
            "StartMenu" => 2,
            "Executable" => 1,
            _ => 0
        };
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static (string? TargetPath, string? Arguments) TryResolveShortcut(string shortcutPath)
    {
        object? shellObject = null;
        object? shortcutObject = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return (null, null);
            }

            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null)
            {
                return (null, null);
            }

            shortcutObject = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shellObject,
                [shortcutPath]);

            if (shortcutObject is null)
            {
                return (null, null);
            }

            var shortcutType = shortcutObject.GetType();
            var targetPath = shortcutType.InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.GetProperty,
                null,
                shortcutObject,
                null) as string;

            var arguments = shortcutType.InvokeMember(
                "Arguments",
                System.Reflection.BindingFlags.GetProperty,
                null,
                shortcutObject,
                null) as string;

            return (targetPath, arguments);
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            if (shortcutObject is not null && Marshal.IsComObject(shortcutObject))
            {
                Marshal.FinalReleaseComObject(shortcutObject);
            }

            if (shellObject is not null && Marshal.IsComObject(shellObject))
            {
                Marshal.FinalReleaseComObject(shellObject);
            }
        }
    }

    private static string ExecutePowerShell(string command)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(8000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return string.Empty;
        }

        Task.WaitAll([outputTask, errorTask], 2000);

        var output = outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty;
        var error = errorTask.IsCompletedSuccessfully ? errorTask.Result : string.Empty;
        return string.IsNullOrWhiteSpace(output) ? error : output;
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

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(nint objectHandle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path,
        nint pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory imageFactory);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public NativeSize(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [Flags]
    private enum ShellItemImageFlags
    {
        ResizeToFit = 0x0,
        BiggerSizeOk = 0x1,
        IconOnly = 0x4
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, ShellItemImageFlags flags, out nint hBitmap);
    }
}
