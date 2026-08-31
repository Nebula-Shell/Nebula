using System.Collections.Concurrent;
using System.Data.OleDb;
using System.IO;
using CaelestiaWin.Core.Interfaces;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class WindowsFileExplorerIndexService(IDiagnosticLogService logService) : IFileExplorerIndexService
{
    private const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<string, CachedSuggestion> _cache = new(StringComparer.OrdinalIgnoreCase);
    private int _availabilityState;

    public string? TryGetPathSuggestion(string input, string currentBase)
    {
        if (Volatile.Read(ref _availabilityState) < 0
            || !TryCreateLookup(input, currentBase, out var parentPath, out var leaf))
        {
            return null;
        }

        var cacheKey = $"{parentPath}|{leaf}";
        if (_cache.TryGetValue(cacheKey, out var cached)
            && cached.ExpiresAt >= DateTimeOffset.UtcNow)
        {
            return cached.Value;
        }

        var suggestion = QuerySuggestion(parentPath, leaf);
        _cache[cacheKey] = new CachedSuggestion(suggestion, DateTimeOffset.UtcNow.Add(CacheLifetime));
        return suggestion;
    }

    private string? QuerySuggestion(string parentPath, string leaf)
    {
        try
        {
            using var connection = new OleDbConnection(ConnectionString);
            connection.Open();

            Interlocked.Exchange(ref _availabilityState, 1);

            using var command = connection.CreateCommand();
            command.CommandText = BuildQuery(parentPath, leaf);

            using var reader = command.ExecuteReader();
            if (reader is null)
            {
                return null;
            }

            var parentDirectory = NormalizeDirectory(parentPath);
            var bestDirectoryMatch = string.Empty;
            var bestFileMatch = string.Empty;

            while (reader.Read())
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var candidate = reader.GetString(0);
                if (!IsDirectChildOf(candidate, parentDirectory))
                {
                    continue;
                }

                var candidateLeaf = Path.GetFileName(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!candidateLeaf.StartsWith(leaf, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Directory.Exists(candidate))
                {
                    if (string.IsNullOrEmpty(bestDirectoryMatch)
                        || string.Compare(candidateLeaf, Path.GetFileName(bestDirectoryMatch.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        bestDirectoryMatch = candidate;
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(bestFileMatch)
                    || string.Compare(candidateLeaf, Path.GetFileName(bestFileMatch), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    bestFileMatch = candidate;
                }
            }

            return !string.IsNullOrEmpty(bestDirectoryMatch)
                ? bestDirectoryMatch
                : string.IsNullOrEmpty(bestFileMatch)
                    ? null
                    : bestFileMatch;
        }
        catch (OleDbException exception)
        {
            MarkUnavailable("Windows Search index provider is unavailable for Nebula Files path suggestions.", exception);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            MarkUnavailable("Windows Search index provider failed to initialize for Nebula Files.", exception);
            return null;
        }
        catch (Exception exception)
        {
            logService.Warn("Nebula Files Windows Search lookup failed. Falling back to direct filesystem suggestions.", new Dictionary<string, object?>
            {
                ["error"] = exception.Message,
                ["exceptionType"] = exception.GetType().Name
            });
            return null;
        }
    }

    private void MarkUnavailable(string message, Exception exception)
    {
        if (Interlocked.Exchange(ref _availabilityState, -1) >= 0)
        {
            logService.Warn(message, new Dictionary<string, object?>
            {
                ["error"] = exception.Message,
                ["exceptionType"] = exception.GetType().Name
            });
        }
    }

    private static bool TryCreateLookup(string input, string currentBase, out string parentPath, out string leaf)
    {
        parentPath = string.Empty;
        leaf = string.Empty;

        var trimmedInput = input.Trim();
        if (trimmedInput.Length < 2)
        {
            return false;
        }

        string expandedInput;
        try
        {
            expandedInput = Path.IsPathRooted(trimmedInput)
                ? trimmedInput
                : Path.GetFullPath(Path.Combine(currentBase, trimmedInput));
        }
        catch
        {
            return false;
        }

        var normalizedInput = expandedInput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        parentPath = Path.GetDirectoryName(expandedInput) ?? string.Empty;
        leaf = Path.GetFileName(normalizedInput);

        return !string.IsNullOrWhiteSpace(parentPath)
            && !string.IsNullOrWhiteSpace(leaf);
    }

    private static string BuildQuery(string parentPath, string leaf)
    {
        var normalizedScope = "file:" + NormalizeDirectory(parentPath).Replace('\\', '/');
        return $"""
                SELECT TOP 64 System.ItemPathDisplay
                FROM SYSTEMINDEX
                WHERE SCOPE='{EscapeSqlLiteral(normalizedScope)}'
                  AND System.FileName LIKE '{EscapeLikeLiteral(leaf)}%'
                ORDER BY System.ItemPathDisplay
                """;
    }

    private static bool IsDirectChildOf(string candidatePath, string parentPath)
    {
        try
        {
            var candidateParent = NormalizeDirectory(Path.GetDirectoryName(candidatePath) ?? string.Empty);
            return candidateParent.Equals(parentPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeDirectory(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string EscapeLikeLiteral(string value)
    {
        return value
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal);
    }

    private readonly record struct CachedSuggestion(string? Value, DateTimeOffset ExpiresAt);
}
