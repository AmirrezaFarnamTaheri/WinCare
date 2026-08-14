namespace WinCare.Application.Plugins;

/// <summary>
/// Abstraction for extensible UI widgets rendered on the WinCare Home Dashboard.
/// </summary>
public interface IPluginWidget
{
    /// <summary>
    /// Unique identifier for this widget instance.
    /// </summary>
    string WidgetId { get; }

    /// <summary>
    /// Display title for the widget header.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Category or area label for organizing dashboard cards.
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Current display status or metric summary string.
    /// </summary>
    string StatusSummary { get; }
}
