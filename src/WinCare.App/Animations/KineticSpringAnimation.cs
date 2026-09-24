namespace WinCare.App.Animations;

using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

/// <summary>
/// Helper applying hardware-accelerated spring animations via Microsoft.UI.Composition.
/// </summary>
public static class KineticSpringAnimation
{
    /// <summary>
    /// Applies a natural spring scaling animation to a UI element with default damping and period.
    /// </summary>
    public static void ApplyNaturalSpring(UIElement element, Vector3 targetScale)
    {
        ApplyNaturalSpring(element, targetScale, dampingRatio: 0.75f, periodMs: 50);
    }

    /// <summary>
    /// Applies a natural spring scaling animation to a UI element with explicit physics parameters.
    /// </summary>
    public static void ApplyNaturalSpring(UIElement element, Vector3 targetScale, float dampingRatio, int periodMs)
    {
        if (element is null ||
            !float.IsFinite(targetScale.X) || targetScale.X <= 0 ||
            !float.IsFinite(targetScale.Y) || targetScale.Y <= 0 ||
            !float.IsFinite(targetScale.Z) || targetScale.Z <= 0)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual?.Compositor;
        if (visual is null || compositor is null) return;

        // Honor the operating system's animation preference. When the setting cannot be read,
        // choose the accessible immediate state instead of assuming motion is acceptable.
        bool animationsEnabled;
        try
        {
            animationsEnabled = new UISettings().AnimationsEnabled;
        }
        catch (Exception)
        {
            animationsEnabled = false;
        }

        if (!animationsEnabled)
        {
            visual.Scale = targetScale;
            return;
        }

        var springAnimation = compositor.CreateSpringVector3Animation();
        springAnimation.FinalValue = targetScale;
        springAnimation.DampingRatio = float.IsFinite(dampingRatio)
            ? Math.Clamp(dampingRatio, 0.2f, 1.0f)
            : 0.75f;
        springAnimation.Period = TimeSpan.FromMilliseconds(Math.Clamp(periodMs, 16, 1000));

        visual.StartAnimation("Scale", springAnimation);
    }
}
