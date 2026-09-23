namespace WinCare.App.Animations;

using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

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
        if (element == null) return;

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual?.Compositor;
        if (compositor == null) return;

        var springAnimation = compositor.CreateSpringVector3Animation();
        springAnimation.FinalValue = targetScale;
        springAnimation.DampingRatio = Math.Clamp(dampingRatio, 0.1f, 1.0f);
        springAnimation.Period = TimeSpan.FromMilliseconds(Math.Max(10, periodMs));

        visual.StartAnimation("Scale", springAnimation);
    }
}
