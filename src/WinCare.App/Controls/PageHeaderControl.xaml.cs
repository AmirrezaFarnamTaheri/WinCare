using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinCare.App.Controls;

public sealed partial class PageHeaderControl : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeaderControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(PageHeaderControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BadgeTextProperty =
        DependencyProperty.Register(nameof(BadgeText), typeof(string), typeof(PageHeaderControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionContentProperty =
        DependencyProperty.Register(nameof(ActionContent), typeof(object), typeof(PageHeaderControl), new PropertyMetadata(null));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string BadgeText { get => (string)GetValue(BadgeTextProperty); set => SetValue(BadgeTextProperty, value); }
    public object ActionContent { get => GetValue(ActionContentProperty); set => SetValue(ActionContentProperty, value); }
    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

    public PageHeaderControl() => InitializeComponent();
}
