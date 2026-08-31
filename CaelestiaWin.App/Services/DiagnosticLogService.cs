using System.Diagnostics;
using System.IO;
using System.Text;
using CaelestiaWin.Config.Helpers;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.App.Services;

public sealed class DiagnosticLogService : IDiagnosticLogService
{
    private readonly object _sync = new();
    private LogLevelKind _minimumLevel = LogLevelKind.Info;
    private long _maxFileSizeBytes = 4L * 1024L * 1024L;

    public LogLevelKind MinimumLevel => _minimumLevel;

    public void Configure(LogLevelKind minimumLevel, int maxFileSizeMb)
    {
        _minimumLevel = minimumLevel;
        _maxFileSizeBytes = Math.Max(1, maxFileSizeMb) * 1024L * 1024L;
    }

    public void Info(string message, IReadOnlyDictionary<string, object?>? data = null)
    {
        Write(LogLevelKind.Info, "INFO", message, null, data);
    }

    public void Warn(string message, IReadOnlyDictionary<string, object?>? data = null)
    {
        Write(LogLevelKind.Warning, "WARN", message, null, data);
    }

    public void Error(string message, Exception exception, IReadOnlyDictionary<string, object?>? data = null)
    {
        Write(LogLevelKind.Error, "ERROR", message, exception, data);
    }

    private void Write(LogLevelKind level, string label, string message, Exception? exception, IReadOnlyDictionary<string, object?>? data)
    {
        if (level < _minimumLevel)
        {
            return;
        }

        Directory.CreateDirectory(ConfigurationPaths.LogDirectory);
        var builder = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" [")
            .Append(label)
            .Append("] ")
            .Append(message);

        if (data is { Count: > 0 })
        {
            foreach (var pair in data)
            {
                builder.Append(" | ").Append(pair.Key).Append('=').Append(pair.Value);
            }
        }

        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append(exception);
        }

        var line = builder.AppendLine().ToString();
        Debug.Write(line);

        lock (_sync)
        {
            RotateIfNeeded();
            File.AppendAllText(ConfigurationPaths.LogPath, line);
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(ConfigurationPaths.LogPath))
        {
            return;
        }

        var fileInfo = new FileInfo(ConfigurationPaths.LogPath);
        if (fileInfo.Length < _maxFileSizeBytes)
        {
            return;
        }

        var archivePath = Path.Combine(ConfigurationPaths.LogDirectory, $"nebula-{DateTime.Now:yyyyMMddHHmmss}.log");
        File.Move(ConfigurationPaths.LogPath, archivePath, overwrite: true);
    }
}
