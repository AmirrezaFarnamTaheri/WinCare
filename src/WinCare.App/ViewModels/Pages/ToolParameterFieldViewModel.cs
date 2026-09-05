using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.CommandCatalog.Models;

namespace WinCare.App.ViewModels.Pages;

/// <summary>
/// Editable UI state for one declared native command parameter. The definition comes from
/// <see cref="CommandParameterCatalog"/>; this type contains no XAML dependencies.
/// </summary>
public sealed class ToolParameterFieldViewModel : ObservableObject
{
    private string _value;

    public ToolParameterFieldViewModel(CommandParameterDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _value = definition.DefaultValue ?? string.Empty;
    }

    public CommandParameterDefinition Definition { get; }
    public string Name => Definition.Name;
    public CommandParameterKind Kind => Definition.Kind;
    public bool Required => Definition.Required;
    public IReadOnlyList<string> Options => Definition.Options ?? Array.Empty<string>();
    public bool HasOptions => Options.Count > 0;

    public string Label => Humanize(Name) + (Required ? " *" : string.Empty);

    public string Hint
    {
        get
        {
            string requirement = Required ? "Required" : "Optional";
            if (HasOptions)
            {
                return $"{requirement}. Choose one of the supported values.";
            }

            if (Kind is CommandParameterKind.Integer or CommandParameterKind.Long or CommandParameterKind.Number)
            {
                if (Definition.Minimum is not null && Definition.Maximum is not null)
                    return $"{requirement}. Allowed range: {Definition.Minimum}–{Definition.Maximum}.";
                if (Definition.Minimum is not null)
                    return $"{requirement}. Minimum: {Definition.Minimum}.";
                if (Definition.Maximum is not null)
                    return $"{requirement}. Maximum: {Definition.Maximum}.";
            }

            return Kind switch
            {
                CommandParameterKind.Boolean => $"{requirement}. On or off.",
                CommandParameterKind.StringList => $"{requirement}. Enter one value per line or separate values with commas.",
                CommandParameterKind.Json => $"{requirement}. Structured JSON value for this parameter.",
                CommandParameterKind.DateTime => $"{requirement}. ISO-8601 date/time, for example 2026-09-05T18:30:00Z.",
                _ => requirement + ".",
            };
        }
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? string.Empty);
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var chars = new List<char>(value.Length + 8) { value[0] };
        for (int i = 1; i < value.Length; i++)
        {
            char current = value[i];
            char previous = value[i - 1];
            if (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
                chars.Add(' ');
            chars.Add(current);
        }
        return new string(chars.ToArray());
    }
}
