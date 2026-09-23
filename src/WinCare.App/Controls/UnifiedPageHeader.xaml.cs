namespace WinCare.App.Controls;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

/// <summary>
/// Adaptive unified page header control consolidating titles, breadcrumbs, status badges, and action presenter slots.
/// </summary>
public sealed partial class UnifiedPageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(UnifiedPageHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(UnifiedPageHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BreadcrumbTextProperty =
        DependencyProperty.Register(nameof(BreadcrumbText), typeof(string), typeof(UnifiedPageHeader), new PropertyMetadata("WORKSPACE"));

    public static readonly DependencyProperty BadgeTextProperty =
        DependencyProperty.Register(nameof(BadgeText), typeof(string), typeof(UnifiedPageHeader), new PropertyMetadata("READY"));

    public static readonly DependencyProperty PrimaryActionProperty =
        DependencyProperty.Register(nameof(PrimaryAction), typeof(object), typeof(UnifiedPageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty SecondaryActionsProperty =
        DependencyProperty.Register(nameof(SecondaryActions), typeof(object), typeof(UnifiedPageHeader), new PropertyMetadata(null));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string BreadcrumbText
    {
        get => (string)GetValue(BreadcrumbTextProperty);
        set => SetValue(BreadcrumbTextProperty, value);
    }

    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public object PrimaryAction
    {
        get => GetValue(PrimaryActionProperty);
        set => SetValue(PrimaryActionProperty, value);
    }

    public object SecondaryActions
    {
        get => GetValue(SecondaryActionsProperty);
        set => SetValue(SecondaryActionsProperty, value);
    }

    public UnifiedPageHeader()
    {
        InitializeComponent();
    }
}
