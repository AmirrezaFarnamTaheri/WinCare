using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinCare.App.Controls;

/// <summary>
/// Represents a unified header control displaying page title, description, optional status badge, and action slots.
/// </summary>
public sealed partial class PageHeaderControl : UserControl
{
    /// <summary>
    /// Identifies the <see cref="Title"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeaderControl), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the <see cref="Description"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(PageHeaderControl), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the <see cref="BadgeText"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty BadgeTextProperty =
        DependencyProperty.Register(nameof(BadgeText), typeof(string), typeof(PageHeaderControl), new PropertyMetadata(string.Empty, OnBadgeTextChanged));

    /// <summary>
    /// Identifies the <see cref="ActionContent"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ActionContentProperty =
        DependencyProperty.Register(nameof(ActionContent), typeof(object), typeof(PageHeaderControl), new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="BadgeVisibility"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty BadgeVisibilityProperty =
        DependencyProperty.Register(nameof(BadgeVisibility), typeof(Visibility), typeof(PageHeaderControl), new PropertyMetadata(Visibility.Collapsed));

    /// <summary>
    /// Gets or sets the title text displayed in the header.
    /// </summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the description text displayed under the title.
    /// </summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Gets or sets the badge text displayed next to the title.
    /// </summary>
    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the action content displayed on the right slot.
    /// </summary>
    public object ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    /// <summary>
    /// Gets the visibility state of the badge based on whether <see cref="BadgeText"/> is present.
    /// </summary>
    public Visibility BadgeVisibility
    {
        get => (Visibility)GetValue(BadgeVisibilityProperty);
        private set => SetValue(BadgeVisibilityProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PageHeaderControl"/> class.
    /// </summary>
    public PageHeaderControl() => InitializeComponent();

    private static void OnBadgeTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PageHeaderControl header)
        {
            header.BadgeVisibility = string.IsNullOrEmpty(e.NewValue as string)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
