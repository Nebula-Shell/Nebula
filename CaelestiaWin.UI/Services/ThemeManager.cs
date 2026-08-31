using System.Windows;
using System.Windows.Media.Effects;
using System.Windows.Media;
using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.UI.Services;

public sealed class ThemeManager : IThemeManager
{
    public void ApplyTheme(AppConfig config)
    {
        if (Application.Current is null)
        {
            return;
        }

        var resources = Application.Current.Resources;
        ApplyThemeResources(resources, config);
    }

    public void ApplyAccentColor(string accentColor)
    {
        if (Application.Current is null)
        {
            return;
        }

        var resources = Application.Current.Resources;
        var accent = ParseColor(accentColor, Color.FromRgb(0x79, 0xE6, 0xF5));
        resources["ShellAccentColor"] = accent;
        resources["AccentBrush"] = CreateBrush(accent);
        resources["AccentSoftBrush"] = CreateBrush(Color.FromArgb(0x28, accent.R, accent.G, accent.B));
        resources["ShellActiveIndicatorBrush"] = CreateBrush(Lighten(accent, 0.42d));
        resources["ShellDesktopIndicatorBrush"] = CreateBrush(Color.FromRgb(0xF3, 0xFB, 0xFF));
    }

    private static void ApplyThemeResources(ResourceDictionary resources, AppConfig config)
    {
        var accent = ParseColor(config.Theme.AccentColor, Color.FromRgb(0x79, 0xE6, 0xF5));
        var secondaryAccent = ParseColor(config.Theme.SecondaryAccentColor, Lighten(accent, 0.42d));
        var foreground = ParseColor(config.Theme.ForegroundColor, Color.FromRgb(0xF5, 0xFB, 0xFF));
        var mutedForeground = ParseColor(config.Theme.MutedForegroundColor, Color.FromRgb(0x8F, 0xA7, 0xB7));
        var baseBackground = ParseColor(config.Theme.BackgroundColor, Color.FromRgb(0x0A, 0x11, 0x18));
        var baseSurface = ParseColor(config.Theme.PanelColor, Color.FromRgb(0x14, 0x1B, 0x23));
        var backgroundOpacity = config.Theme.EnableTransparency ? config.Theme.BackgroundOpacity : 1d;
        var panelOpacity = config.Theme.EnableTransparency ? config.Theme.PanelOpacity : 1d;
        var tintSurfaces = config.Theme.TintShellSurfacesWithAccent;
        var background = WithAlpha(baseBackground, backgroundOpacity);
        var surfaceBase = tintSurfaces ? Blend(baseSurface, accent, 0.12d) : baseSurface;
        var surface = WithAlpha(surfaceBase, panelOpacity);
        var surfaceAltBase = Lighten(baseSurface, 0.08d);
        var surfaceAlt = WithAlpha(tintSurfaces ? Blend(surfaceAltBase, accent, 0.20d) : surfaceAltBase, Math.Min(1d, panelOpacity + 0.07d));
        var backdropBase = Color.FromRgb(0x10, 0x18, 0x22);
        var backdrop = WithAlpha(tintSurfaces ? Blend(backdropBase, accent, 0.16d) : backdropBase, config.Theme.EnableTransparency ? 0.53d : 0.95d);
        var backgroundEnd = tintSurfaces ? Blend(Darken(baseBackground, 0.32d), accent, 0.08d) : Darken(baseBackground, 0.32d);
        var hairline = tintSurfaces
            ? Color.FromArgb(0x2A, secondaryAccent.R, secondaryAccent.G, secondaryAccent.B)
            : Color.FromArgb(0x28, foreground.R, foreground.G, foreground.B);
        var subtle = tintSurfaces
            ? Color.FromArgb(0x12, accent.R, accent.G, accent.B)
            : Color.FromArgb(0x0D, foreground.R, foreground.G, foreground.B);

        resources["ShellAccentColor"] = accent;
        resources["ShellForegroundColor"] = foreground;
        resources["ShellMutedForegroundColor"] = mutedForeground;
        resources["AccentBrush"] = CreateBrush(accent);
        resources["AccentSoftBrush"] = CreateBrush(Color.FromArgb(0x34, accent.R, accent.G, accent.B));
        resources["ShellActiveIndicatorBrush"] = CreateBrush(secondaryAccent);
        resources["ShellDesktopIndicatorBrush"] = CreateBrush(Color.FromRgb(0xF3, 0xFB, 0xFF));
        resources["ShellBackgroundBrush"] = new LinearGradientBrush(background, backgroundEnd, new Point(0, 0), new Point(1, 1));
        resources["ShellPanelBrush"] = CreateBrush(surface);
        resources["ShellPanelAltBrush"] = CreateBrush(surfaceAlt);
        resources["ShellBackdropBrush"] = CreateBrush(backdrop);
        resources["ShellForegroundBrush"] = CreateBrush(foreground);
        resources["ShellMutedBrush"] = CreateBrush(mutedForeground);
        resources["ShellHairlineBrush"] = CreateBrush(hairline);
        resources["ShellSubtleBrush"] = CreateBrush(subtle);
        resources["ShellShadow"] = config.Theme.EnableShadows
            ? new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 0,
                Opacity = 0.35,
                Color = Colors.Black
            }
            : null;
        resources["ShellCornerRadius"] = new CornerRadius(config.Theme.CornerRadius);
        resources["AnimationFastMs"] = (double)config.Animations.FastMs;
        resources["AnimationNormalMs"] = (double)config.Animations.NormalMs;
        resources["AnimationSlowMs"] = (double)config.Animations.SlowMs;
        resources["AnimationOverlayEasing"] = config.Animations.OverlayEasing;
        resources["AnimationLauncherScaleFrom"] = config.Animations.LauncherScaleFrom;
        resources["AnimationSidePanelOffset"] = config.Animations.SidePanelOffset;
        resources["AnimationDesiredFrameRate"] = config.Animations.DesiredFrameRate;
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(value);
            return converted is Color color ? color : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color color, double opacity)
    {
        return Color.FromArgb((byte)(Math.Clamp(opacity, 0d, 1d) * 255), color.R, color.G, color.B);
    }

    private static Color Lighten(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromRgb(
            (byte)(color.R + ((255 - color.R) * amount)),
            (byte)(color.G + ((255 - color.G) * amount)),
            (byte)(color.B + ((255 - color.B) * amount)));
    }

    private static Color Darken(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromRgb(
            (byte)(color.R * (1d - amount)),
            (byte)(color.G * (1d - amount)),
            (byte)(color.B * (1d - amount)));
    }

    private static Color Blend(Color baseColor, Color tintColor, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromRgb(
            (byte)(baseColor.R + ((tintColor.R - baseColor.R) * amount)),
            (byte)(baseColor.G + ((tintColor.G - baseColor.G) * amount)),
            (byte)(baseColor.B + ((tintColor.B - baseColor.B) * amount)));
    }
}
