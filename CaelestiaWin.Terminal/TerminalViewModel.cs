using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;

namespace CaelestiaWin.Terminal;

public sealed class TerminalViewModel : INotifyPropertyChanged
{
    private static readonly string[] BuiltInCommands = ["help", "clear", "cls", "pwd", "cd", "history", "exit"];
    private static readonly string[] ExecutableExtensions = [".exe", ".cmd", ".bat", ".ps1"];
    private static readonly TimeSpan ExecutableCacheLifetime = TimeSpan.FromMinutes(5);
    private const int MaxHistoryEntries = 500;
    private static readonly object ExecutableCacheSync = new();
    private static string[]? _cachedExecutableNames;
    private static DateTimeOffset _cachedExecutableNamesAt;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private string _commandText = string.Empty;
    private string _currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _inlineSuggestionPrefix = string.Empty;
    private string _inlineSuggestionSuffix = string.Empty;
    private bool _isBusy;
    private Process? _activeProcess;
    private StreamWriter? _activeProcessInput;

    public TerminalViewModel()
    {
        LoadHistory();
        _historyIndex = _history.Count;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string UserName { get; } = Environment.UserName;

    public ObservableCollection<TerminalLine> Lines { get; } =
    [
        TerminalLine.FromPlain("Nebula Terminal", TerminalLine.AccentForeground),
        TerminalLine.FromPlain("A native shell-themed command surface. Type 'help' for built-in commands.", TerminalLine.MutedForeground),
        TerminalLine.Empty
    ];

    public string CommandText
    {
        get => _commandText;
        set
        {
            if (_commandText != value)
            {
                _commandText = value;
                OnPropertyChanged();
                UpdateInlineSuggestion();
            }
        }
    }

    public string InlineSuggestionPrefix
    {
        get => _inlineSuggestionPrefix;
        private set
        {
            if (_inlineSuggestionPrefix != value)
            {
                _inlineSuggestionPrefix = value;
                OnPropertyChanged();
            }
        }
    }

    public string InlineSuggestionSuffix
    {
        get => _inlineSuggestionSuffix;
        private set
        {
            if (_inlineSuggestionSuffix != value)
            {
                _inlineSuggestionSuffix = value;
                OnPropertyChanged();
            }
        }
    }

    public string CurrentDirectory
    {
        get => _currentDirectory;
        private set
        {
            if (_currentDirectory != value)
            {
                _currentDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                UpdateInlineSuggestion();
            }
        }
    }

    public async Task SubmitAsync()
    {
        if (IsBusy)
        {
            await SubmitInteractiveInputAsync(CommandText);
            CommandText = string.Empty;
            return;
        }

        var command = CommandText.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        CommandText = string.Empty;
        AddHistoryEntry(command);
        _historyIndex = _history.Count;
        Lines.Add(TerminalLine.FromSegments(
            new TerminalTextRun(UserName, TerminalLine.DefaultForeground),
            new TerminalTextRun(" ❯ ", TerminalLine.AccentForeground),
            new TerminalTextRun(command, TerminalLine.DefaultForeground)));

        if (await TryHandleBuiltInAsync(command))
        {
            Lines.Add(TerminalLine.Empty);
            return;
        }

        IsBusy = true;
        try
        {
            var exitCode = await RunPowerShellAsync(command);
            if (exitCode != 0)
            {
                Lines.Add(TerminalLine.FromPlain($"process exited with code {exitCode}", TerminalLine.MutedForeground));
            }
        }
        catch (Exception exception)
        {
            Lines.Add(TerminalLine.FromPlain($"error: {exception.Message}", TerminalLine.ErrorForeground));
        }
        finally
        {
            IsBusy = false;
            Lines.Add(TerminalLine.Empty);
        }
    }

    private async Task SubmitInteractiveInputAsync(string input)
    {
        if (_activeProcessInput is null || _activeProcess is null || _activeProcess.HasExited)
        {
            return;
        }

        Lines.Add(TerminalLine.FromPlain(input, TerminalLine.DefaultForeground));

        try
        {
            await _activeProcessInput.WriteLineAsync(input);
            await _activeProcessInput.FlushAsync();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    public void PreviousHistory()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Max(0, _historyIndex - 1);
        CommandText = _history[_historyIndex];
    }

    public void NextHistory()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
        CommandText = _historyIndex == _history.Count ? string.Empty : _history[_historyIndex];
    }

    public bool TryAcceptInlineSuggestion()
    {
        if (IsBusy || string.IsNullOrEmpty(InlineSuggestionSuffix))
        {
            return false;
        }

        CommandText = InlineSuggestionPrefix + InlineSuggestionSuffix;
        return true;
    }

    public bool TryCompleteCommand()
    {
        if (IsBusy)
        {
            return false;
        }

        var text = CommandText;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var completions = GetCompletionCandidates(text).ToList();
        if (completions.Count == 0)
        {
            return false;
        }

        if (completions.Count == 1)
        {
            CommandText = ApplyCompletion(text, completions[0]);
            return true;
        }

        var commonPrefix = GetCommonPrefix(completions);
        if (commonPrefix.Length > 0 && !string.Equals(commonPrefix, GetCurrentToken(text), StringComparison.OrdinalIgnoreCase))
        {
            CommandText = ApplyCompletion(text, commonPrefix);
        }

        Lines.Add(TerminalLine.FromPlain(string.Join("  ", completions.Take(12)), TerminalLine.MutedForeground));
        Lines.Add(TerminalLine.Empty);
        return true;
    }

    private async Task<bool> TryHandleBuiltInAsync(string command)
    {
        if (command.Equals("clear", StringComparison.OrdinalIgnoreCase)
            || command.Equals("cls", StringComparison.OrdinalIgnoreCase))
        {
            Lines.Clear();
            return true;
        }

        if (command.Equals("pwd", StringComparison.OrdinalIgnoreCase))
        {
            Lines.Add(TerminalLine.FromPlain(CurrentDirectory, TerminalLine.MutedForeground));
            return true;
        }

        if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Lines.Add(TerminalLine.FromPlain("Built-ins: help, clear, cls, pwd, cd <path>, history, exit", TerminalLine.AccentForeground));
            Lines.Add(TerminalLine.FromPlain("Other commands stream through PowerShell in the current directory.", TerminalLine.MutedForeground));
            Lines.Add(TerminalLine.FromPlain("History: Up/Down navigates, Right/Tab accepts inline suggestions.", TerminalLine.MutedForeground));
            return true;
        }

        if (command.Equals("history", StringComparison.OrdinalIgnoreCase))
        {
            ShowHistory();
            return true;
        }

        if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            Application.Current.Shutdown();
            return true;
        }

        if (command.Equals("cd", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
        {
            ChangeDirectory(command);
            return true;
        }

        await Task.CompletedTask;
        return false;
    }

    private void ShowHistory()
    {
        if (_history.Count == 0)
        {
            Lines.Add(TerminalLine.FromPlain("history is empty", TerminalLine.MutedForeground));
            return;
        }

        var start = Math.Max(0, _history.Count - 30);
        for (var index = start; index < _history.Count; index++)
        {
            Lines.Add(TerminalLine.FromPlain($"{index + 1,4}  {_history[index]}", TerminalLine.MutedForeground));
        }
    }

    private void ChangeDirectory(string command)
    {
        var target = command.Length <= 2 ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : command[2..].Trim();
        target = target.Trim('"');

        if (target == "~")
        {
            target = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (!Path.IsPathRooted(target))
        {
            target = Path.GetFullPath(Path.Combine(CurrentDirectory, target));
        }

        if (!Directory.Exists(target))
        {
            Lines.Add(TerminalLine.FromPlain($"cd: path not found: {target}", TerminalLine.ErrorForeground));
            return;
        }

        CurrentDirectory = target;
    }

    private IEnumerable<string> GetCompletionCandidates(string text)
    {
        var trimmedStart = text.TrimStart();
        if (trimmedStart.StartsWith("cd ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmedStart, "cd", StringComparison.OrdinalIgnoreCase))
        {
            return CompletePath(text.Length <= 2 ? string.Empty : text[2..].TrimStart(), directoriesOnly: true);
        }

        var currentToken = GetCurrentToken(text);
        if (ShouldCompletePath(currentToken))
        {
            return CompletePath(currentToken, directoriesOnly: false);
        }

        if (text.Contains(' ', StringComparison.Ordinal))
        {
            return CompletePath(currentToken, directoriesOnly: false);
        }

        var commandCandidates = BuiltInCommands
            .Concat(GetExecutableNames())
            .Where(candidate => candidate.StartsWith(currentToken, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase);

        return commandCandidates;
    }

    private void AddHistoryEntry(string command)
    {
        if (_history.Count > 0 && string.Equals(_history[^1], command, StringComparison.Ordinal))
        {
            return;
        }

        _history.Add(command);
        if (_history.Count > MaxHistoryEntries)
        {
            _history.RemoveRange(0, _history.Count - MaxHistoryEntries);
        }

        SaveHistory();
    }

    private void UpdateInlineSuggestion()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(CommandText))
        {
            ClearInlineSuggestion();
            return;
        }

        var suggestion = FindHistorySuggestion(CommandText) ?? FindCompletionSuggestion(CommandText);
        if (string.IsNullOrEmpty(suggestion)
            || suggestion.Length <= CommandText.Length
            || !suggestion.StartsWith(CommandText, StringComparison.OrdinalIgnoreCase))
        {
            ClearInlineSuggestion();
            return;
        }

        InlineSuggestionPrefix = CommandText;
        InlineSuggestionSuffix = suggestion[CommandText.Length..];
    }

    private void ClearInlineSuggestion()
    {
        InlineSuggestionPrefix = string.Empty;
        InlineSuggestionSuffix = string.Empty;
    }

    private string? FindHistorySuggestion(string text)
    {
        for (var index = _history.Count - 1; index >= 0; index--)
        {
            var candidate = _history[index];
            if (candidate.Length > text.Length
                && candidate.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? FindCompletionSuggestion(string text)
    {
        try
        {
            return GetCompletionCandidates(text)
                .FirstOrDefault(candidate => candidate.Length > text.Length
                                             && candidate.StartsWith(text, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void LoadHistory()
    {
        try
        {
            var path = GetHistoryPath();
            if (!File.Exists(path))
            {
                return;
            }

            var entries = File.ReadLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLast(MaxHistoryEntries);
            _history.AddRange(entries);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void SaveHistory()
    {
        try
        {
            var path = GetHistoryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, _history.TakeLast(MaxHistoryEntries));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetHistoryPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NebulaShell",
            "terminal-history.txt");
    }

    private IEnumerable<string> CompletePath(string token, bool directoriesOnly)
    {
        token = token.Trim().Trim('"');
        var expandedToken = token.StartsWith("~", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), token[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : token;

        var baseDirectory = CurrentDirectory;
        var searchPattern = expandedToken;

        if (!string.IsNullOrWhiteSpace(expandedToken))
        {
            var rooted = Path.IsPathRooted(expandedToken);
            var fullCandidate = rooted ? expandedToken : Path.Combine(CurrentDirectory, expandedToken);
            baseDirectory = Directory.Exists(fullCandidate)
                ? fullCandidate
                : Path.GetDirectoryName(fullCandidate) ?? CurrentDirectory;
            searchPattern = Directory.Exists(fullCandidate) ? string.Empty : Path.GetFileName(fullCandidate);
        }

        if (!Directory.Exists(baseDirectory))
        {
            return [];
        }

        try
        {
            var entries = Directory.EnumerateDirectories(baseDirectory, $"{searchPattern}*")
                .Select(path => FormatPathCompletion(token, path, isDirectory: true));

            if (!directoriesOnly)
            {
                entries = entries.Concat(Directory.EnumerateFiles(baseDirectory, $"{searchPattern}*")
                    .Select(path => FormatPathCompletion(token, path, isDirectory: false)));
            }

            return entries
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private string FormatPathCompletion(string originalToken, string path, bool isDirectory)
    {
        var relative = Path.GetRelativePath(CurrentDirectory, path);
        var value = originalToken.StartsWith("~", StringComparison.Ordinal)
            ? path
            : Path.IsPathRooted(originalToken)
                ? path
                : relative;

        if (isDirectory && !value.EndsWith(Path.DirectorySeparatorChar))
        {
            value += Path.DirectorySeparatorChar;
        }

        return value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
    }

    private static bool ShouldCompletePath(string token)
    {
        return token.Contains('\\', StringComparison.Ordinal)
               || token.Contains('/', StringComparison.Ordinal)
               || token.StartsWith(".", StringComparison.Ordinal)
               || token.StartsWith("~", StringComparison.Ordinal)
               || token.StartsWith("\"", StringComparison.Ordinal);
    }

    private static string GetCurrentToken(string text)
    {
        var trimmed = text.TrimEnd();
        var lastSpace = trimmed.LastIndexOf(' ');
        return lastSpace < 0 ? trimmed.Trim('"') : trimmed[(lastSpace + 1)..].Trim('"');
    }

    private static string ApplyCompletion(string text, string completion)
    {
        var lastSpace = text.TrimEnd().LastIndexOf(' ');
        return lastSpace < 0 ? completion : string.Concat(text.AsSpan(0, lastSpace + 1), completion);
    }

    private static string GetCommonPrefix(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        var prefix = values[0];
        for (var index = 1; index < values.Count; index++)
        {
            var candidate = values[index];
            var length = 0;
            while (length < prefix.Length
                   && length < candidate.Length
                   && char.ToUpperInvariant(prefix[length]) == char.ToUpperInvariant(candidate[length]))
            {
                length++;
            }

            prefix = prefix[..length];
            if (prefix.Length == 0)
            {
                break;
            }
        }

        return prefix;
    }

    private static IReadOnlyList<string> GetExecutableNames()
    {
        lock (ExecutableCacheSync)
        {
            if (_cachedExecutableNames is not null
                && DateTimeOffset.UtcNow - _cachedExecutableNamesAt < ExecutableCacheLifetime)
            {
                return _cachedExecutableNames;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (ExecutableExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        var result = names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        lock (ExecutableCacheSync)
        {
            _cachedExecutableNames = result;
            _cachedExecutableNamesAt = DateTimeOffset.UtcNow;
        }

        return result;
    }

    private async Task<int> RunPowerShellAsync(string command)
    {
        var script = BuildPowerShellScript(command);
        var psi = new ProcessStartInfo
        {
            FileName = GetPowerShellPath(),
            Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -EncodedCommand {EncodePowerShellScript(script)}",
            WorkingDirectory = CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        psi.Environment["TERM"] = "xterm-256color";
        psi.Environment["CLICOLOR_FORCE"] = "1";
        psi.Environment["FORCE_COLOR"] = "1";
        process.Start();
        _activeProcess = process;
        _activeProcessInput = process.StandardInput;

        var outputTask = ReadOutputAsync(process.StandardOutput, TerminalLine.DefaultForeground);
        var errorTask = ReadOutputAsync(process.StandardError, TerminalLine.ErrorForeground);
        try
        {
            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);
            return process.ExitCode;
        }
        finally
        {
            try
            {
                _activeProcessInput?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            _activeProcessInput = null;
            _activeProcess = null;
        }
    }

    private string BuildPowerShellScript(string command)
    {
        var escapedDirectory = CurrentDirectory.Replace("'", "''", StringComparison.Ordinal);
        return string.Join(
            Environment.NewLine,
            "$ErrorActionPreference = 'Continue'",
            $"Set-Location -LiteralPath '{escapedDirectory}'",
            "& {",
            command,
            "}",
            "if ($null -ne $global:LASTEXITCODE) { exit $global:LASTEXITCODE }",
            "exit 0");
    }

    private static string EncodePowerShellScript(string script)
    {
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }

    private async Task ReadOutputAsync(StreamReader reader, string fallbackForeground)
    {
        var buffer = new char[512];
        var lineBuilder = new StringBuilder();
        int? replaceableLineIndex = null;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                ProcessOutputCharacter(buffer[index], lineBuilder, fallbackForeground, ref replaceableLineIndex);
            }
        }

        if (lineBuilder.Length > 0)
        {
            ReplaceOrAppendOutputLine(lineBuilder.ToString(), fallbackForeground, ref replaceableLineIndex);
        }
    }

    private void ProcessOutputCharacter(
        char character,
        StringBuilder lineBuilder,
        string fallbackForeground,
        ref int? replaceableLineIndex)
    {
        switch (character)
        {
            case '\r':
                if (lineBuilder.Length > 0)
                {
                    ReplaceOrAppendOutputLine(lineBuilder.ToString(), fallbackForeground, ref replaceableLineIndex);
                    lineBuilder.Clear();
                }

                break;
            case '\n':
                if (lineBuilder.Length > 0)
                {
                    ReplaceOrAppendOutputLine(lineBuilder.ToString(), fallbackForeground, ref replaceableLineIndex);
                    lineBuilder.Clear();
                }

                replaceableLineIndex = null;
                break;
            case '\b':
                if (lineBuilder.Length > 0)
                {
                    lineBuilder.Length--;
                }

                break;
            case '\0':
                break;
            default:
                lineBuilder.Append(character);
                break;
        }
    }

    private void ReplaceOrAppendOutputLine(string text, string fallbackForeground, ref int? replaceableLineIndex)
    {
        var line = TerminalLine.FromAnsi(text, fallbackForeground);
        if (replaceableLineIndex is { } lineIndex && lineIndex >= 0 && lineIndex < Lines.Count)
        {
            Lines[lineIndex] = line;
            return;
        }

        Lines.Add(line);
        replaceableLineIndex = Lines.Count - 1;
    }

    private static string GetPowerShellPath()
    {
        var systemPowerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(systemPowerShell) ? systemPowerShell : "powershell.exe";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
