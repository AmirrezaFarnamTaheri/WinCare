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
    /// Applies a natural spring scaling animation to a UI element.
    /// </summary>
    public static void ApplyNaturalSpring(UIElement element, Vector3 targetScale)
    {
        if (element == null) return;

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual?.Compositor;
        if (compositor == null) return;

        var springAnimation = compositor.CreateSpringVector3Animation();
        springAnimation.FinalValue = targetScale;
        springAnimation.DampingRatio = 0.75f;
        springAnimation.Period = TimeSpan.FromMilliseconds(50);

        visual.StartAnimation("Scale", springAnimation);
    }
}
