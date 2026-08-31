using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsFileShellContextMenuService(IDiagnosticLogService logService) : IFileShellContextMenuService
{
    private readonly IDiagnosticLogService _logService = logService;

    public IReadOnlyList<ShellMenuItem> GetMenuItems(string path)
    {
        try
        {
            return GetMenuItemsCore(path);
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to resolve Windows shell context menu verbs for explorer item.",
                new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["error"] = exception.Message
                });

            return [];
        }
    }

    public bool TryInvoke(string path, string invokeToken)
    {
        if (!int.TryParse(invokeToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return false;
        }

        object? shellApplication = null;
        object? shellFolder = null;
        object? shellItem = null;
        object? verbs = null;
        object? verb = null;

        try
        {
            if (!TryResolveShellItem(path, out shellApplication, out shellFolder, out shellItem))
            {
                return false;
            }

            verbs = InvokeMember(shellItem, "Verbs");
            verb = InvokeMember(verbs, "Item", index);
            if (verb is null)
            {
                return false;
            }

            InvokeMember(verb, "DoIt");
            return true;
        }
        catch (Exception exception)
        {
            _logService.Warn("Failed to invoke Windows shell context menu verb for explorer item.",
                new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["invokeToken"] = invokeToken,
                    ["error"] = exception.Message
                });

            return false;
        }
        finally
        {
            ReleaseComObject(verb);
            ReleaseComObject(verbs);
            ReleaseComObject(shellItem);
            ReleaseComObject(shellFolder);
            ReleaseComObject(shellApplication);
        }
    }

    private IReadOnlyList<ShellMenuItem> GetMenuItemsCore(string path)
    {
        object? shellApplication = null;
        object? shellFolder = null;
        object? shellItem = null;
        object? verbs = null;

        try
        {
            if (!TryResolveShellItem(path, out shellApplication, out shellFolder, out shellItem))
            {
                return [];
            }

            verbs = InvokeMember(shellItem, "Verbs");
            if (verbs is null)
            {
                return [];
            }

            var count = Convert.ToInt32(GetProperty(verbs, "Count"), CultureInfo.InvariantCulture);
            var items = new List<ShellMenuItem>(count);
            var lastWasSeparator = true;

            for (var index = 0; index < count; index++)
            {
                var verb = InvokeMember(verbs, "Item", index);
                try
                {
                    var label = NormalizeVerbLabel(Convert.ToString(GetProperty(verb, "Name"), CultureInfo.InvariantCulture));
                    if (string.IsNullOrWhiteSpace(label) || ShouldSkipVerb(label))
                    {
                        if (!lastWasSeparator && items.Count > 0)
                        {
                            items.Add(ShellMenuItem.Separator);
                            lastWasSeparator = true;
                        }

                        continue;
                    }

                    items.Add(new ShellMenuItem
                    {
                        Label = label,
                        InvokeToken = index.ToString(CultureInfo.InvariantCulture)
                    });

                    lastWasSeparator = false;
                }
                finally
                {
                    ReleaseComObject(verb);
                }
            }

            while (items.Count > 0 && items[^1].IsSeparator)
            {
                items.RemoveAt(items.Count - 1);
            }

            return items;
        }
        finally
        {
            ReleaseComObject(verbs);
            ReleaseComObject(shellItem);
            ReleaseComObject(shellFolder);
            ReleaseComObject(shellApplication);
        }
    }

    private static bool TryResolveShellItem(string path, out object? shellApplication, out object? shellFolder, out object? shellItem)
    {
        shellApplication = null;
        shellFolder = null;
        shellItem = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
        if (shellType is null)
        {
            return false;
        }

        shellApplication = Activator.CreateInstance(shellType);
        if (shellApplication is null)
        {
            return false;
        }

        var normalizedPath = path.Trim();
        var isDirectory = Directory.Exists(normalizedPath);
        var isFile = File.Exists(normalizedPath);
        if (!isDirectory && !isFile)
        {
            return false;
        }

        if (isDirectory && IsRootPath(normalizedPath))
        {
            shellFolder = InvokeMember(shellApplication, "Namespace", normalizedPath);
            shellItem = shellFolder is null ? null : GetProperty(shellFolder, "Self");
            return shellItem is not null;
        }

        var parentPath = isDirectory
            ? Path.GetDirectoryName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetDirectoryName(normalizedPath);
        var leafName = isDirectory
            ? Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileName(normalizedPath);

        if (!string.IsNullOrWhiteSpace(parentPath) && !string.IsNullOrWhiteSpace(leafName))
        {
            shellFolder = InvokeMember(shellApplication, "Namespace", parentPath);
            shellItem = shellFolder is null ? null : InvokeMember(shellFolder, "ParseName", leafName);
            if (shellItem is not null)
            {
                return true;
            }
        }

        ReleaseComObject(shellFolder);
        shellFolder = InvokeMember(shellApplication, "Namespace", normalizedPath);
        shellItem = shellFolder is null ? null : GetProperty(shellFolder, "Self");
        return shellItem is not null;
    }

    private static bool IsRootPath(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrWhiteSpace(root)
               && string.Equals(
                   root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static object? InvokeMember(object? target, string memberName, params object?[]? args)
    {
        return target?.GetType().InvokeMember(
            memberName,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args);
    }

    private static object? GetProperty(object? target, string propertyName)
    {
        return target?.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null);
    }

    private static string NormalizeVerbLabel(string? rawLabel)
    {
        if (string.IsNullOrWhiteSpace(rawLabel))
        {
            return string.Empty;
        }

        var cleaned = rawLabel
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();

        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        return cleaned;
    }

    private static bool ShouldSkipVerb(string label)
    {
        return label.Equals("Pin to Start", StringComparison.OrdinalIgnoreCase)
               || label.Equals("Unpin from Start", StringComparison.OrdinalIgnoreCase)
               || label.Equals("Copy", StringComparison.OrdinalIgnoreCase)
               || label.Equals("Cut", StringComparison.OrdinalIgnoreCase)
               || label.Equals("Paste", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }
}
