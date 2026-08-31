using System.Text.Json;
using System.Text.Json.Serialization;
using CaelestiaWin.Core.Enums;
using CaelestiaWin.Config.Helpers;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Config.Services;

public sealed class JsonConfigurationService(IDiagnosticLogService logService) : IConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public async Task<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ConfigurationPaths.RootDirectory);

        if (!File.Exists(ConfigurationPaths.ConfigPath))
        {
            var defaults = AppConfig.CreateDefault();
            await SaveAsync(defaults, cancellationToken);

            return new ConfigLoadResult
            {
                Config = defaults,
                ConfigPath = ConfigurationPaths.ConfigPath,
                UsedDefaults = true,
                Warnings = ["Configuration file was missing. Generated a default config."]
            };
        }

        try
        {
            AppConfig config;
            await using (var stream = File.OpenRead(ConfigurationPaths.ConfigPath))
            {
                config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, JsonOptions, cancellationToken)
                         ?? AppConfig.CreateDefault();
            }

            config = Normalize(config, out var wasUpdated);
            var warnings = Validate(config);

            if (warnings.Count > 0)
            {
                logService.Warn("Configuration loaded with validation warnings.", new Dictionary<string, object?>
                {
                    ["warningCount"] = warnings.Count
                });
            }

            if (wasUpdated)
            {
                await SaveAsync(config, cancellationToken);
            }

            return new ConfigLoadResult
            {
                Config = config,
                ConfigPath = ConfigurationPaths.ConfigPath,
                UsedDefaults = false,
                Warnings = warnings
            };
        }
        catch (JsonException exception)
        {
            logService.Error("Malformed JSON configuration detected; regenerating defaults.", exception);
            var backupPath = $"{ConfigurationPaths.ConfigPath}.broken-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(ConfigurationPaths.ConfigPath, backupPath, overwrite: true);

            var defaults = AppConfig.CreateDefault();
            await SaveAsync(defaults, cancellationToken);

            return new ConfigLoadResult
            {
                Config = defaults,
                ConfigPath = ConfigurationPaths.ConfigPath,
                UsedDefaults = true,
                Warnings =
                [
                    "Configuration JSON was malformed. A backup was created and defaults were restored.",
                    $"Broken config backup: {backupPath}"
                ]
            };
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ConfigurationPaths.RootDirectory);
        var serializedConfig = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);

        if (File.Exists(ConfigurationPaths.ConfigPath))
        {
            var existingConfig = await File.ReadAllBytesAsync(ConfigurationPaths.ConfigPath, cancellationToken);
            if (existingConfig.AsSpan().SequenceEqual(serializedConfig))
            {
                return;
            }
        }

        await File.WriteAllBytesAsync(ConfigurationPaths.ConfigPath, serializedConfig, cancellationToken);
    }

    private static List<string> Validate(AppConfig config)
    {
        var warnings = new List<string>();

        if (!IsOpacityValid(config.Theme.BackgroundOpacity))
        {
            warnings.Add("Theme.BackgroundOpacity should be between 0 and 1.");
        }

        if (!IsOpacityValid(config.Theme.PanelOpacity))
        {
            warnings.Add("Theme.PanelOpacity should be between 0 and 1.");
        }

        if (config.Theme.AccentPalette.Count == 0)
        {
            warnings.Add("Theme.AccentPalette should include at least one accent option.");
        }

        if (config.Animations.FastMs <= 0 || config.Animations.NormalMs <= 0 || config.Animations.SlowMs <= 0)
        {
            warnings.Add("Animation durations must be positive integers.");
        }

        if (config.Logging.MaxFileSizeMb is < 1 or > 64)
        {
            warnings.Add("Logging.MaxFileSizeMb should stay between 1 and 64.");
        }

        if (!Enum.IsDefined(config.ControlCenter.InputWidgetPlacement))
        {
            warnings.Add("ControlCenter.InputWidgetPlacement should be Auto, ConnectivityCard, or BottomRow.");
        }

        if (config.Animations.LauncherScaleFrom is < 0.7 or > 1.0)
        {
            warnings.Add("Animations.LauncherScaleFrom should stay between 0.7 and 1.0.");
        }

        if (config.Animations.SidePanelOffset is < 0 or > 200)
        {
            warnings.Add("Animations.SidePanelOffset should stay between 0 and 200.");
        }

        if (config.Animations.DesiredFrameRate is < 15 or > 60)
        {
            warnings.Add("Animations.DesiredFrameRate should stay between 15 and 60.");
        }

        if (config.Launcher.MaxResults is < 3 or > 32)
        {
            warnings.Add("Launcher.MaxResults should stay between 3 and 32.");
        }

        if (config.Launcher.SearchDebounceMs is < 0 or > 1000)
        {
            warnings.Add("Launcher.SearchDebounceMs should stay between 0 and 1000.");
        }

        if (config.Launcher.RecentAppLimit is < 0 or > 12)
        {
            warnings.Add("Launcher.RecentAppLimit should stay between 0 and 12.");
        }

        if (!Enum.IsDefined(config.Launcher.DefaultTerminal))
        {
            warnings.Add("Launcher.DefaultTerminal should be Nebula, WindowsTerminal, PowerShell, CommandPrompt, or Custom.");
        }

        if (!Enum.IsDefined(config.Launcher.DefaultFileExplorer))
        {
            warnings.Add("Launcher.DefaultFileExplorer should be WindowsExplorer or Nebula.");
        }

        if (!IsOpacityValid(config.Terminal.Opacity))
        {
            warnings.Add("Terminal.Opacity should be between 0 and 1.");
        }

        if (config.Performance.ActiveWindowDebounceMs is < 0 or > 1000)
        {
            warnings.Add("Performance.ActiveWindowDebounceMs should stay between 0 and 1000.");
        }

        if (config.Performance.AppDiscoveryCacheMinutes is < 1 or > 240)
        {
            warnings.Add("Performance.AppDiscoveryCacheMinutes should stay between 1 and 240.");
        }

        if (config.Performance.WorkspaceSyncThrottleMs is < 0 or > 2000)
        {
            warnings.Add("Performance.WorkspaceSyncThrottleMs should stay between 0 and 2000.");
        }

        if (config.Windowing.LayoutGap is < 0 or > 64)
        {
            warnings.Add("Windowing.LayoutGap should stay between 0 and 64.");
        }

        if (!Enum.IsDefined(config.Windowing.TilingStrategy))
        {
            warnings.Add("Windowing.TilingStrategy should be Grid or GoldenRatio.");
        }

        if (config.Windowing.FocusOutlineOffset is < 0 or > 24)
        {
            warnings.Add("Windowing.FocusOutlineOffset should stay between 0 and 24.");
        }

        if (config.Windowing.FocusOutlineThickness is < 1 or > 12)
        {
            warnings.Add("Windowing.FocusOutlineThickness should stay between 1 and 12.");
        }

        if (config.Windowing.OuterMargin is < 0 or > 120)
        {
            warnings.Add("Windowing.OuterMargin should stay between 0 and 120.");
        }

        if (config.Windowing.TopReservedSpace is < 0 or > 240)
        {
            warnings.Add("Windowing.TopReservedSpace should stay between 0 and 240.");
        }

        if (config.Windowing.OverviewColumns is < 1 or > 6)
        {
            warnings.Add("Windowing.OverviewColumns should stay between 1 and 6.");
        }

        if (config.Notifications.MaxItems is < 1 or > 100)
        {
            warnings.Add("Notifications.MaxItems should stay between 1 and 100.");
        }

        if (config.Startup.StartOnLogin && config.Startup.EnableAutoStart)
        {
            warnings.Add("Startup.StartOnLogin supersedes the legacy Startup.EnableAutoStart setting.");
        }

        if (config.Hotkeys.Bindings.Count == 0)
        {
            warnings.Add("Hotkeys.Bindings is empty. Default hotkeys are recommended.");
        }

        return warnings;
    }

    private static AppConfig Normalize(AppConfig config, out bool wasUpdated)
    {
        const int workspaceCount = 8;
        wasUpdated = false;
        var normalizedTheme = NormalizeTheme(config.Theme, ref wasUpdated);
        var defaultBindings = HotkeyConfig.CreateDefaultBindings();
        var existingBindings = config.Hotkeys.Bindings.ToList();
        var mergedBindings = new List<HotkeyBindingConfig>(existingBindings.Count + defaultBindings.Count);

        foreach (var binding in existingBindings)
        {
            if (binding.Workspace is int workspace && (workspace < 1 || workspace > workspaceCount))
            {
                wasUpdated = true;
                continue;
            }

            if (binding.Action == Core.Enums.HotkeyActionKind.ToggleSettingsPanel)
            {
                if (!string.Equals(binding.Gesture, "Win+C", StringComparison.OrdinalIgnoreCase))
                {
                    mergedBindings.Add(new HotkeyBindingConfig
                    {
                        Action = binding.Action,
                        Gesture = "Win+C",
                        Workspace = binding.Workspace,
                        Direction = binding.Direction
                    });
                    wasUpdated = true;
                    continue;
                }
            }

            mergedBindings.Add(binding);
        }

        foreach (var defaultBinding in defaultBindings)
        {
            if (mergedBindings.Any(binding => Matches(binding, defaultBinding)))
            {
                continue;
            }

            mergedBindings.Add(defaultBinding);
            wasUpdated = true;
        }

        if (!wasUpdated)
        {
            return config;
        }

        return new AppConfig
        {
            Theme = normalizedTheme,
            Logging = config.Logging,
            Animations = config.Animations,
            Performance = config.Performance,
            ControlCenter = config.ControlCenter,
            Windowing = config.Windowing,
            Launcher = config.Launcher,
            Terminal = config.Terminal,
            Notifications = config.Notifications,
            Session = config.Session,
            Startup = config.Startup,
            Hotkeys = new HotkeyConfig
            {
                Bindings = mergedBindings
            },
            GameMode = config.GameMode
        };
    }

    private static bool Matches(HotkeyBindingConfig left, HotkeyBindingConfig right)
    {
        return left.Action == right.Action
               && left.Workspace == right.Workspace
               && left.Direction == right.Direction;
    }

    private static ThemeConfig NormalizeTheme(ThemeConfig theme, ref bool wasUpdated)
    {
        var normalizedRecentWallpapers = theme.RecentWallpapers
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        var normalizedPalette = theme.AccentPalette
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedPalette.Count == 0)
        {
            normalizedPalette = new ThemeConfig().AccentPalette;
            wasUpdated = true;
        }

        if (normalizedPalette.Count != theme.AccentPalette.Count)
        {
            wasUpdated = true;
        }

        if (normalizedRecentWallpapers.Count != theme.RecentWallpapers.Count)
        {
            wasUpdated = true;
        }

        return new ThemeConfig
        {
            KeepWindowsAccentSeparate = theme.KeepWindowsAccentSeparate,
            TintShellSurfacesWithAccent = theme.TintShellSurfacesWithAccent,
            AccentColor = theme.AccentColor,
            SecondaryAccentColor = theme.SecondaryAccentColor,
            ForegroundColor = theme.ForegroundColor,
            MutedForegroundColor = theme.MutedForegroundColor,
            PanelColor = theme.PanelColor,
            BackgroundColor = theme.BackgroundColor,
            WallpaperPath = theme.WallpaperPath,
            ShowDesktopDecorations = theme.ShowDesktopDecorations,
            RecentWallpapers = normalizedRecentWallpapers,
            AccentPalette = normalizedPalette,
            BackgroundOpacity = theme.BackgroundOpacity,
            PanelOpacity = theme.PanelOpacity,
            EnableBackdropBlur = theme.EnableBackdropBlur,
            EnableShadows = theme.EnableShadows,
            EnableTransparency = theme.EnableTransparency,
            CornerRadius = theme.CornerRadius
        };
    }

    private static bool IsOpacityValid(double value) => value >= 0d && value <= 1d;
}
