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
    private const double ToolTableCompactBreakpointDip = 840;
    private const double InlineInspectorBreakpointDip = 1320;
    private Control? _inspectorReturnFocus;

    /// <summary>Initializes a new instance of <see cref="AllToolsPage"/>.</summary>
    public AllToolsPage()
    {
        ViewModel = new AllToolsPageViewModel();
        InitializeComponent();
        ToolTabs.SelectedItem = ToolTabs.Items[0] as SelectorBarItem;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ApplyFilterLayout(ActualWidth < ToolTableCompactBreakpointDip);
        RebuildParameterEditor();
    }

    public AllToolsPageViewModel ViewModel { get; }

    public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility InvertBoolToVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Handles navigation to the page.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ToolNavigationRequest request)
        {
            ToolTabs.SelectedItem = ToolTabs.Items[0] as SelectorBarItem;
            ViewModel.OpenTool(request.CommandId, request.Parameters);
            DispatcherQueue.TryEnqueue(() => InspectorCloseButton.Focus(FocusState.Programmatic));
        }
        else if (e.Parameter is string query)
        {
            ToolTabs.SelectedItem = ToolTabs.Items[0] as SelectorBarItem;
            ViewModel.OpenSearch(query);
            if (ViewModel.SelectedTool is not null)
                DispatcherQueue.TryEnqueue(() => InspectorCloseButton.Focus(FocusState.Programmatic));
            else
                ToolSearchBox.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>Handles the tool tabs selection changed event.</summary>
    private void ToolTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem item) return;
        ViewModel.SelectTab(item.Text);
        SearchFilterGrid.Visibility = ViewModel.IsCatalogTab ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Handles the category card click event.</summary>
    private void CategoryCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        string[] parts = key.Split('\u001f', 2);
        if (parts.Length != 2 || !ViewModel.OpenCategory(parts[0], parts[1])) return;
        ToolTabs.SelectedItem = ToolTabs.Items[0] as SelectorBarItem;
        ToolSearchBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Handles the review preset button click event.</summary>
    private void ReviewPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string presetId }) return;
        _inspectorReturnFocus = sender as Control;
        ViewModel.SelectPresetForReview(presetId);
        DispatcherQueue.TryEnqueue(() => InspectorCloseButton.Focus(FocusState.Programmatic));
    }

    private void ToolTable_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ToolRowViewModel tool) return;
        _inspectorReturnFocus = sender as Control;
        ViewModel.SelectedTool = tool;
        ViewModel.IsDetailsOpen = true;
        DispatcherQueue.TryEnqueue(() => InspectorCloseButton.Focus(FocusState.Programmatic));
    }

    /// <summary>Handles the close inspector click event.</summary>
    private void CloseInspector_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsDetailsOpen = false;
        if (_inspectorReturnFocus?.IsLoaded == true) _inspectorReturnFocus.Focus(FocusState.Programmatic);
        else if (!ViewModel.IsCatalogTab) ToolTabs.Focus(FocusState.Programmatic);
        else ToolSearchBox.Focus(FocusState.Programmatic);
        _inspectorReturnFocus = null;
    }

    private void Inspector_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        CloseInspector_Click(sender, e);
        e.Handled = true;
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleFavorite();

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < ToolTableCompactBreakpointDip;
        ViewModel.SetCompactLayout(compact);
        DetailsSplitView.DisplayMode = e.NewSize.Width < InlineInspectorBreakpointDip
            ? SplitViewDisplayMode.Overlay
            : SplitViewDisplayMode.Inline;
        ApplyFilterLayout(compact);
    }

    /// <summary>Handles the tool search focus accelerator event.</summary>
    private void ToolSearch_FocusAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ToolTabs.SelectedItem = ToolTabs.Items[0] as SelectorBarItem;
        ToolSearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AllToolsPageViewModel.SelectedTool) or nameof(AllToolsPageViewModel.IsDetailsOpen))
            RebuildParameterEditor();
    }

    /// <summary>Applies filter layout.</summary>
    private void ApplyFilterLayout(bool compact)
    {
        SearchFilterGrid.ColumnDefinitions.Clear();
        SearchFilterGrid.RowDefinitions.Clear();

        if (compact)
        {
            SearchFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SearchFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SearchFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SearchFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SearchFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SearchFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SearchFilterGrid.RowSpacing = 8;
            SearchFilterGrid.ColumnSpacing = 10;
            Grid.SetRow(ToolSearchBox, 0); Grid.SetColumn(ToolSearchBox, 0); Grid.SetColumnSpan(ToolSearchBox, 2);
            Grid.SetRow(AreaFilter, 1); Grid.SetColumn(AreaFilter, 0);
            Grid.SetRow(SectionFilter, 1); Grid.SetColumn(SectionFilter, 1);
            Grid.SetRow(RiskFilter, 2); Grid.SetColumn(RiskFilter, 0);
            Grid.SetRow(ReadOnlyFilter, 2); Grid.SetColumn(ReadOnlyFilter, 1);
            Grid.SetRow(ResultCountText, 3); Grid.SetColumn(ResultCountText, 1);
            ReadOnlyFilter.Margin = new Thickness(0, 4, 0, 0);
            ResultCountText.Margin = new Thickness(0, 6, 0, 0);
            ResultCountText.HorizontalAlignment = HorizontalAlignment.Right;
        }
        else
        {
            foreach (GridLength width in new[] { new GridLength(2, GridUnitType.Star), new GridLength(190), new GridLength(190), new GridLength(160), GridLength.Auto, GridLength.Auto })
                SearchFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            SearchFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SearchFilterGrid.RowSpacing = 0;
            SearchFilterGrid.ColumnSpacing = 12;
            Grid.SetRow(ToolSearchBox, 0); Grid.SetColumn(ToolSearchBox, 0); Grid.SetColumnSpan(ToolSearchBox, 1);
            Grid.SetRow(AreaFilter, 0); Grid.SetColumn(AreaFilter, 1);
            Grid.SetRow(SectionFilter, 0); Grid.SetColumn(SectionFilter, 2);
            Grid.SetRow(RiskFilter, 0); Grid.SetColumn(RiskFilter, 3);
            Grid.SetRow(ReadOnlyFilter, 0); Grid.SetColumn(ReadOnlyFilter, 4);
            Grid.SetRow(ResultCountText, 0); Grid.SetColumn(ResultCountText, 5);
            ReadOnlyFilter.Margin = new Thickness(0, 26, 0, 0);
            ResultCountText.Margin = new Thickness(8, 28, 0, 0);
            ResultCountText.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }

    /// <summary>Rebuilds parameter editor.</summary>
    private void RebuildParameterEditor()
    {
        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(new TextBlock
        {
            Text = ViewModel.Execution.ParameterEditorSummary,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
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
            IsOn = ViewModel.Execution.UseAdvancedParameterJson
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
            if (ViewModel.Execution.UseAdvancedParameterJson) ViewModel.Execution.ParameterJson = rawEditor.Text;
        };
        root.Children.Add(rawEditor);

        advancedToggle.Toggled += (_, _) =>
        {
            bool leavingAdvanced = !advancedToggle.IsOn;
            ViewModel.Execution.UseAdvancedParameterJson = advancedToggle.IsOn;
            if (leavingAdvanced)
            {
                if (ViewModel.Execution.UseAdvancedParameterJson)
                {
                    advancedToggle.IsOn = true;
                    rawEditor.Focus(FocusState.Programmatic);
                    return;
                }
                RebuildParameterEditor();
            }
            else
            {
                structuredPanel.Visibility = Visibility.Collapsed;
                rawEditor.Visibility = Visibility.Visible;
                rawEditor.Text = ViewModel.Execution.ParameterJson;
            }
        };

        structuredPanel.Visibility = advancedToggle.IsOn ? Visibility.Collapsed : Visibility.Visible;
        rawEditor.Visibility = advancedToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        ParameterExpander.Content = root;
    }

    /// <summary>Creates parameter field.</summary>
    private FrameworkElement CreateParameterField(ToolParameterFieldViewModel field)
    {
        var container = new StackPanel { Spacing = 5 };
        container.Children.Add(new TextBlock
        {
            Text = field.Label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
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
                IsOn = bool.TryParse(field.Value, out bool initial) && initial
            };
            toggle.Toggled += (_, _) => field.Value = toggle.IsOn ? "true" : "false";
            editor = toggle;
        }
        else if (field.Kind is CommandParameterKind.Integer or CommandParameterKind.Number)
        {
            var number = new NumberBox
            {
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                HorizontalAlignment = HorizontalAlignment.Stretch
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
                HorizontalAlignment = HorizontalAlignment.Stretch
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
            TextWrapping = TextWrapping.Wrap
        });
        return container;
    }
}
