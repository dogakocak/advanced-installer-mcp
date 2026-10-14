using System.Text.RegularExpressions;
using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Drive;

public sealed record ControlEventRow(string Dialog, string Control, string Event, string Argument, string Condition, int? Ordering);

public sealed record SequenceRow(string Action, string Condition, int Sequence);

// The tables drive_dialog plays from, read once per call.
public sealed partial class DriveTables
{
    private const int ModalDialog = 0x2;

    // CustomAction.Type low six bits: 51 sets a property, 35 a directory. Every other type runs package code.
    private static readonly HashSet<int> PropertySetterTypes = [51, 35];

    private static readonly HashSet<string> ConditionKeywords =
        new(["AND", "OR", "NOT", "XOR", "EQV", "IMP"], StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyDictionary<string, int> _dialogAttributes;
    private readonly IReadOnlyDictionary<string, int> _customActionTypes;
    private readonly ILookup<(string Dialog, string Control), ControlEventRow> _events;

    private DriveTables(
        IReadOnlyDictionary<string, int> dialogAttributes,
        IReadOnlyDictionary<string, int> customActionTypes,
        IReadOnlyList<ControlEventRow> events,
        IReadOnlyList<SequenceRow> uiSequence,
        IReadOnlySet<string> propertyNames)
    {
        _dialogAttributes = dialogAttributes;
        _customActionTypes = customActionTypes;
        _events = events.ToLookup(e => (e.Dialog, e.Control));
        UiSequence = uiSequence;
        PropertyNames = propertyNames;
    }

    // Positive sequence numbers in order; negative ones are the exit dialogs, 0 never runs.
    public IReadOnlyList<SequenceRow> UiSequence { get; }

    // Every property the package's UI logic reads or writes: the Property table, controls' properties, [Property]
    // events and references, property-setting custom actions and the names in conditions. Changes are reported for
    // these.
    public IReadOnlySet<string> PropertyNames { get; }

    public static DriveTables Load(MsiDatabase database)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        var dialogAttributes = database.HasTable("Dialog")
            ? database.Query("SELECT `Dialog`, `Attributes` FROM `Dialog`", r => (Name: r.GetString(1), Attributes: r.GetInteger(2) ?? 0))
                .ToDictionary(d => d.Name, d => d.Attributes, StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);

        var customActions = database.HasTable("CustomAction")
            ? database.Query("SELECT `Action`, `Type`, `Source` FROM `CustomAction`",
                r => (Action: r.GetString(1), Type: r.GetInteger(2) ?? 0, Source: r.GetString(3)))
            : [];
        foreach (var action in customActions.Where(a => (a.Type & 0x3F) == 51))
        {
            names.Add(action.Source);
        }

        var events = database.HasTable("ControlEvent")
            ? database.Query(
                "SELECT `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering` FROM `ControlEvent`",
                r => new ControlEventRow(r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInteger(6)))
            : [];
        foreach (var row in events)
        {
            if (PropertyEvent(row.Event) is { } property)
            {
                names.Add(property);
            }

            AddReferences(names, row.Argument);
            AddConditionNames(names, row.Condition);
        }

        var uiSequence = database.HasTable("InstallUISequence")
            ? database.Query("SELECT `Action`, `Condition`, `Sequence` FROM `InstallUISequence`",
                    r => new SequenceRow(r.GetString(1), r.GetString(2), r.GetInteger(3) ?? 0))
                .Where(s => s.Sequence > 0)
                .OrderBy(s => s.Sequence)
                .ToList()
            : [];
        foreach (var row in uiSequence)
        {
            AddConditionNames(names, row.Condition);
        }

        if (database.HasTable("Property"))
        {
            names.UnionWith(database.Query("SELECT `Property` FROM `Property`", r => r.GetString(1)));
        }

        if (database.HasTable("Control"))
        {
            foreach (var (property, text) in database.Query("SELECT `Property`, `Text` FROM `Control`", r => (r.GetString(1), r.GetString(2))))
            {
                if (property.Length > 0)
                {
                    names.Add(property);
                }

                AddReferences(names, text);
            }
        }

        if (database.HasTable("ControlCondition"))
        {
            foreach (var condition in database.Query("SELECT `Condition` FROM `ControlCondition`", r => r.GetString(1)))
            {
                AddConditionNames(names, condition);
            }
        }

        return new DriveTables(
            dialogAttributes,
            customActions.ToDictionary(a => a.Action, a => a.Type, StringComparer.Ordinal),
            events,
            uiSequence,
            names);
    }

    public bool IsDialog(string name) => _dialogAttributes.ContainsKey(name);

    public bool IsModalDialog(string name) => _dialogAttributes.TryGetValue(name, out var attributes) && (attributes & ModalDialog) != 0;

    public bool IsCustomAction(string action) => _customActionTypes.ContainsKey(action);

    public bool RunsPackageCode(string action) =>
        _customActionTypes.TryGetValue(action, out var type) && !PropertySetterTypes.Contains(type & 0x3F);

    // In Ordering order. Rows without Ordering keep their table position among rows ordered 0 (Windows Installer
    // documents no order for them; ADR 0007 open point).
    public IReadOnlyList<ControlEventRow> Events(string dialog, string control) =>
        _events[(dialog, control)].OrderBy(e => e.Ordering ?? 0).ToList();

    // "[NAME]" as a ControlEvent's Event column sets property NAME.
    public static string? PropertyEvent(string @event) =>
        @event.Length > 2 && @event[0] == '[' && @event[^1] == ']' ? @event[1..^1] : null;

    private static void AddReferences(HashSet<string> names, string text)
    {
        foreach (Match match in PropertyReference().Matches(text))
        {
            names.Add(match.Groups[1].Value);
        }
    }

    private static void AddConditionNames(HashSet<string> names, string condition)
    {
        foreach (Match match in ConditionIdentifier().Matches(QuotedString().Replace(condition, " ")))
        {
            if (!ConditionKeywords.Contains(match.Value))
            {
                names.Add(match.Value);
            }
        }
    }

    [GeneratedRegex(@"\[([A-Za-z_][A-Za-z0-9_.]*)\]")]
    private static partial Regex PropertyReference();

    // A property name in a condition; &Feature, !Feature, $Component, ?Component and %ENV are not properties.
    [GeneratedRegex(@"(?<![&!$?%\w.])[A-Za-z_][A-Za-z0-9_.]*")]
    private static partial Regex ConditionIdentifier();

    [GeneratedRegex("\"[^\"]*\"")]
    private static partial Regex QuotedString();
}
