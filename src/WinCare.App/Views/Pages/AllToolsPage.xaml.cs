using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WinCare.App.ViewModels.Pages;
using WinCare.CommandCatalog.Models;

namespace WinCare.App.Views.Pages;

public sealed partial class AllToolsPage : Page
{
    private Grid? _filterGrid;
    private ComboBox? _areaFilter;
    private ComboBox? _riskFilter;
    private CheckBox? _readOnlyFilter;
    private TextBlock? _resultCount;
    private Expander? _parameterExpander;

    public AllToolsPage()
    {
        ViewModel = new AllToolsPageViewModel();
        InitializeComponent();
        ToolTabs.SelectedItem = ToolTabs.Items[0] as SelectorBarItem;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        CaptureResponsiveControls();
        ReplaceRawParameterEditor();
    }

    public AllToolsPageViewModel ViewModel { get; }

    public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility InvertBoolToVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query))
        {
            ViewModel.SearchText = query;
            (ViewModel.IsCompactLayout ? FindCompactSearchBox() : ToolSearchBox)?.Focus(FocusState.Programmatic);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // This page is cached: its view model must remain subscribed when navigating back.
        base.OnNavigatedFrom(e);
    }

    private void ToolTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is SelectorBarItem item)
            ViewModel.SelectTab(item.Text);
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleFavorite();

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = LayoutVisibility.IsCompact(e.NewSize.Width);
        ViewModel.SetCompactLayout(compact);
        DetailsSplitView.DisplayMode = compact ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
        ApplyFilterLayout(compact);
    }

    private void ToolSearch_FocusAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        TextBox target = ViewModel.IsCompactLayout ? FindCompactSearchBox() ?? ToolSearchBox : ToolSearchBox;
        target.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AllToolsPageViewModel.SelectedTool))
            RebuildParameterEditor();
    }

    private void CaptureResponsiveControls()
    {
        _filterGrid = ToolSearchBox.Parent as Grid;
        if (_filterGrid is null) return;

        _areaFilter = _filterGrid.Children.OfType<ComboBox>().FirstOrDefault();
        _riskFilter = _filterGrid.Children.OfType<ComboBox>().Skip(1).FirstOrDefault();
        _readOnlyFilter = _filterGrid.Children.OfType<CheckBox>().FirstOrDefault();
        _resultCount = _filterGrid.Children.OfType<TextBlock>().FirstOrDefault();
        ApplyFilterLayout(LayoutVisibility.IsCompact(ActualWidth));
    }

    private TextBox? FindCompactSearchBox()
    {
        if (_filterGrid is null || !ViewModel.IsCompactLayout) return null;
        return ToolSearchBox;
    }

    private void ApplyFilterLayout(bool compact)
    {
        if (_filterGrid is null || _areaFilter is null || _riskFilter is null || _readOnlyFilter is null || _resultCount is null)
            return;

        _filterGrid.ColumnDefinitions.Clear();
        _filterGrid.RowDefinitions.Clear();

        if (compact)
        {
            _filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _filterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _filterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _filterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _filterGrid.RowSpacing = 8;
            _filterGrid.ColumnSpacing = 10;

            Grid.SetRow(ToolSearchBox, 0);
            Grid.SetColumn(ToolSearchBox, 0);
            Grid.SetColumnSpan(ToolSearchBox, 2);

            Grid.SetRow(_areaFilter, 1);
            Grid.SetColumn(_areaFilter, 0);
            Grid.SetColumnSpan(_areaFilter, 1);
            Grid.SetRow(_riskFilter, 1);
            Grid.SetColumn(_riskFilter, 1);
            Grid.SetColumnSpan(_riskFilter, 1);

            Grid.SetRow(_readOnlyFilter, 2);
            Grid.SetColumn(_readOnlyFilter, 0);
            Grid.SetRow(_resultCount, 2);
            Grid.SetColumn(_resultCount, 1);

            _readOnlyFilter.Margin = new Thickness(0, 4, 0, 0);
            _resultCount.Margin = new Thickness(0, 6, 0, 0);
            _resultCount.HorizontalAlignment = HorizontalAlignment.Right;
        }
        else
        {
            foreach (GridLength width in new[]
            {
                new GridLength(2, GridUnitType.Star),
                new GridLength(220),
                new GridLength(190),
                GridLength.Auto,
                GridLength.Auto,
            })
                _filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

            _filterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _filterGrid.RowSpacing = 0;
            _filterGrid.ColumnSpacing = 12;

            Grid.SetRow(ToolSearchBox, 0);
            Grid.SetColumn(ToolSearchBox, 0);
            Grid.SetColumnSpan(ToolSearchBox, 1);
            Grid.SetRow(_areaFilter, 0);
            Grid.SetColumn(_areaFilter, 1);
            Grid.SetRow(_riskFilter, 0);
            Grid.SetColumn(_riskFilter, 2);
            Grid.SetRow(_readOnlyFilter, 0);
            Grid.SetColumn(_readOnlyFilter, 3);
            Grid.SetRow(_resultCount, 0);
            Grid.SetColumn(_resultCount, 4);

            _readOnlyFilter.Margin = new Thickness(0, 26, 0, 0);
            _resultCount.Margin = new Thickness(8, 28, 0, 0);
            _resultCount.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }

    private void ReplaceRawParameterEditor()
    {
        _parameterExpander = FindVisualDescendant<Expander>(this, expander =>
            string.Equals(AutomationProperties.GetName(expander), "Command parameters JSON", StringComparison.Ordinal));
        if (_parameterExpander is null) return;

        _parameterExpander.Header = "Command parameters";
        AutomationProperties.SetName(_parameterExpander, "Command parameters");
        _parameterExpander.IsExpanded = true;
        RebuildParameterEditor();
    }

    private void RebuildParameterEditor()
    {
        if (_parameterExpander is null) return;

        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(new TextBlock
        {
            Text = ViewModel.Execution.ParameterEditorSummary,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
        });

        var structuredPanel = new StackPanel { Spacing = 12 };
        foreach (ToolParameterFieldViewModel field in ViewModel.Execution.ParameterFields)
            structuredPanel.Children.Add(CreateParameterField(field));
        root.Children.Add(structuredPanel);

        var advancedToggle = new ToggleSwitch
        {
            Header = "Advanced parameter editing",
            OffContent = "Typed inputs",
            OnContent = "Raw JSON",
            IsOn = ViewModel.Execution.UseAdvancedParameterJson,
        };
        AutomationProperties.SetAutomationId(advancedToggle, "AdvancedParameterEditing");
        AutomationProperties.SetName(advancedToggle, "Use raw JSON command parameters");
        root.Children.Add(advancedToggle);

        var rawEditor = new TextBox
        {
            MinHeight = 120,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Text = ViewModel.Execution.ParameterJson,
            PlaceholderText = "{}",
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Microsoft.UI.Xaml.Application.Current.Resources["TelemetryFontFamily"],
            FontSize = 12,
            Visibility = advancedToggle.IsOn ? Visibility.Visible : Visibility.Collapsed,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(rawEditor, ScrollBarVisibility.Auto);
        AutomationProperties.SetAutomationId(rawEditor, "CommandParameterJson");
        AutomationProperties.SetName(rawEditor, "Advanced command parameters as JSON");
        rawEditor.TextChanged += (_, _) =>
        {
            if (ViewModel.Execution.UseAdvancedParameterJson)
                ViewModel.Execution.ParameterJson = rawEditor.Text;
        };
        root.Children.Add(rawEditor);

        void ApplyMode(bool advanced)
        {
            ViewModel.Execution.UseAdvancedParameterJson = advanced;
            structuredPanel.Visibility = advanced ? Visibility.Collapsed : Visibility.Visible;
            rawEditor.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
            if (advanced)
                rawEditor.Text = ViewModel.Execution.ParameterJson;
        }

        advancedToggle.Toggled += (_, _) => ApplyMode(advancedToggle.IsOn);
        ApplyMode(advancedToggle.IsOn);
        _parameterExpander.Content = root;
    }

    private FrameworkElement CreateParameterField(ToolParameterFieldViewModel field)
    {
        var container = new StackPanel { Spacing = 5 };
        container.Children.Add(new TextBlock
        {
            Text = field.Label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        FrameworkElement editor;
        if (field.HasOptions)
        {
            var combo = new ComboBox { ItemsSource = field.Options, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectedItem = field.Options.FirstOrDefault(option => string.Equals(option, field.Value, StringComparison.OrdinalIgnoreCase));
            combo.SelectionChanged += (_, _) => field.Value = combo.SelectedItem?.ToString() ?? string.Empty;
            editor = combo;
        }
        else if (field.Kind == CommandParameterKind.Boolean)
        {
            var toggle = new ToggleSwitch
            {
                OffContent = "False",
                OnContent = "True",
                IsOn = bool.TryParse(field.Value, out bool initial) && initial,
            };
            toggle.Toggled += (_, _) => field.Value = toggle.IsOn ? "true" : "false";
            editor = toggle;
        }
        else if (field.Kind is CommandParameterKind.Integer or CommandParameterKind.Number)
        {
            var number = new NumberBox
            {
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            if (double.TryParse(field.Definition.Minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out double min)) number.Minimum = min;
            if (double.TryParse(field.Definition.Maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out double max)) number.Maximum = max;
            if (double.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double initial)) number.Value = initial;
            number.ValueChanged += (_, args) => field.Value = double.IsNaN(args.NewValue)
                ? string.Empty
                : field.Kind == CommandParameterKind.Integer
                    ? Math.Round(args.NewValue).ToString(CultureInfo.InvariantCulture)
                    : args.NewValue.ToString("R", CultureInfo.InvariantCulture);
            editor = number;
        }
        else
        {
            bool multiline = field.Kind is CommandParameterKind.StringList or CommandParameterKind.Json;
            var text = new TextBox
            {
                Text = field.Value,
                AcceptsReturn = multiline,
                MinHeight = multiline ? 88 : 0,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            text.TextChanged += (_, _) => field.Value = text.Text;
            editor = text;
        }

        AutomationProperties.SetAutomationId(editor, "CommandParameter_" + field.Name);
        AutomationProperties.SetName(editor, field.Label);
        container.Children.Add(editor);
        container.Children.Add(new TextBlock
        {
            Text = field.Hint,
            FontSize = 11,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
        });
        return container;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T candidate && predicate(candidate)) return candidate;
            T? nested = FindVisualDescendant(child, predicate);
            if (nested is not null) return nested;
        }
        return null;
    }
}
