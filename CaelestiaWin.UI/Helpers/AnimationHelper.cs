using System.Windows;
using System.Windows.Media.Animation;

namespace CaelestiaWin.UI.Helpers;

public static class AnimationHelper
{
    public static double GetDoubleResource(string resourceKey, double fallback)
    {
        return Application.Current?.Resources[resourceKey] is double value ? value : fallback;
    }

    public static IEasingFunction CreateOverlayEasing()
    {
        var easingName = Application.Current?.Resources["AnimationOverlayEasing"] as string;
        return easingName switch
        {
            "QuintOut" => new QuinticEase { EasingMode = EasingMode.EaseOut },
            "QuartOut" => new QuarticEase { EasingMode = EasingMode.EaseOut },
            _ => new CubicEase { EasingMode = EasingMode.EaseOut }
        };
    }

    public static DoubleAnimation CreateDoubleAnimation(double to, TimeSpan duration, IEasingFunction? easing = null)
    {
        var animation = new DoubleAnimation(to, duration)
        {
            EasingFunction = easing
        };
        ApplyDesiredFrameRate(animation);
        return animation;
    }

    public static DoubleAnimation CreateDoubleAnimation(double from, double to, TimeSpan duration, IEasingFunction? easing = null)
    {
        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = easing
        };
        ApplyDesiredFrameRate(animation);
        return animation;
    }

    public static void ApplyDesiredFrameRate(Timeline animation)
    {
        var desiredFrameRate = Application.Current?.Resources["AnimationDesiredFrameRate"] is int value
            ? value
            : 45;
        desiredFrameRate = Math.Clamp(desiredFrameRate, 15, 60);

#pragma warning disable 618
        Timeline.SetDesiredFrameRate(animation, desiredFrameRate);
#pragma warning restore 618
    }
}
