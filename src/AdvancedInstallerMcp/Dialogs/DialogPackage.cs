using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Dialogs;

// The UI-related content of one MSI package: properties, text styles and dialogs.
public sealed class DialogPackage
{
    private readonly MsiDatabase _database;
    private ILookup<string, RadioButton>? _radioButtons;

    private DialogPackage(
        MsiDatabase database,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyDictionary<string, TextStyle> textStyles,
        IReadOnlyList<string> dialogNames,
        bool hasEmbeddedUi)
    {
        _database = database;
        Properties = properties;
        TextStyles = textStyles;
        DialogNames = dialogNames;
        HasEmbeddedUi = hasEmbeddedUi;
    }

    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyDictionary<string, TextStyle> TextStyles { get; }

    public IReadOnlyList<string> DialogNames { get; }

    public string? DefaultUIFont => Properties.GetValueOrDefault("DefaultUIFont");

    // The package brings its own UI DLL (MsiEmbeddedUI row with msidbEmbeddedUI, 0x01). When it runs, Windows
    // Installer does not draw the Dialog table; the dialogs are only the fallback.
    public bool HasEmbeddedUi { get; }

    // A package without UI has no Dialog, Control or TextStyle table; that reads as empty, not as an error.
    public static DialogPackage Load(MsiDatabase database)
    {
        var properties = database.HasTable("Property")
            ? database.Query("SELECT `Property`, `Value` FROM `Property`", r => (Name: r.GetString(1), Value: r.GetString(2)))
                .ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var textStyles = database.HasTable("TextStyle")
            ? database.Query(
                    "SELECT `TextStyle`, `FaceName`, `Size`, `Color`, `StyleBits` FROM `TextStyle`",
                    r => new TextStyle(r.GetString(1), r.GetString(2), r.GetInteger(3) ?? 0, r.GetInteger(4),
                        (TextStyleBits)(r.GetInteger(5) ?? 0)))
                .ToDictionary(s => s.Name, StringComparer.Ordinal)
            : new Dictionary<string, TextStyle>(StringComparer.Ordinal);

        var dialogNames = database.HasTable("Dialog")
            ? database.Query("SELECT `Dialog` FROM `Dialog`", r => r.GetString(1)).Order(StringComparer.Ordinal).ToList()
            : [];

        var hasEmbeddedUi = database.HasTable("MsiEmbeddedUI") &&
            database.Query("SELECT `Attributes` FROM `MsiEmbeddedUI`", r => r.GetInteger(1) ?? 0).Any(a => (a & 1) != 0);

        return new DialogPackage(database, properties, textStyles, dialogNames, hasEmbeddedUi);
    }

    public Dialog? FindDialog(string name)
    {
        var dialog = _database.Query(
                "SELECT `Dialog`, `Width`, `Height`, `Attributes`, `Title`, `Control_First`, `Control_Default`, `Control_Cancel` " +
                "FROM `Dialog` WHERE `Dialog` = ?",
                r => new Dialog(
                    r.GetString(1),
                    r.GetInteger(2) ?? 0,
                    r.GetInteger(3) ?? 0,
                    r.GetInteger(4) ?? 0,
                    r.GetString(5),
                    r.GetString(6),
                    NullIfEmpty(r.GetString(7)),
                    NullIfEmpty(r.GetString(8)),
                    []),
                name)
            .SingleOrDefault();

        return dialog is null ? null : dialog with { Controls = ReadControls(name) };
    }

    // The buttons of the RadioButtonGroup tied to this property, in their Order. Positions are relative to the group.
    public IReadOnlyList<RadioButton> RadioButtons(string property)
    {
        _radioButtons ??= _database.HasTable("RadioButton")
            ? _database.Query(
                    "SELECT `Property`, `Order`, `Value`, `X`, `Y`, `Width`, `Height`, `Text` FROM `RadioButton`",
                    r => (Property: r.GetString(1), Order: r.GetInteger(2) ?? 0, Button: new RadioButton(
                        r.GetString(3), r.GetInteger(4) ?? 0, r.GetInteger(5) ?? 0, r.GetInteger(6) ?? 0,
                        r.GetInteger(7) ?? 0, r.GetString(8))))
                .OrderBy(b => b.Order)
                .ToLookup(b => b.Property, b => b.Button, StringComparer.Ordinal)
            : Enumerable.Empty<RadioButton>().ToLookup(_ => "", StringComparer.Ordinal);

        return _radioButtons[property].ToList();
    }

    // The Binary row's data (a picture for a Bitmap or Icon control), or null when there is no such row.
    public byte[]? ReadBinary(string name) =>
        _database.HasTable("Binary")
            ? _database.Query("SELECT `Data` FROM `Binary` WHERE `Name` = ?", r => r.ReadStream(1), name).SingleOrDefault()
            : null;

    // The dialog's ControlCondition rows: Show, Hide, Enable, Disable or Default when the condition is true.
    public IReadOnlyList<ControlConditionRow> ControlConditions(string dialogName) =>
        _database.HasTable("ControlCondition")
            ? _database.Query(
                "SELECT `Control_`, `Action`, `Condition` FROM `ControlCondition` WHERE `Dialog_` = ?",
                r => new ControlConditionRow(r.GetString(1), r.GetString(2), r.GetString(3)), dialogName)
            : [];

    private IReadOnlyList<Control> ReadControls(string dialogName)
    {
        if (!_database.HasTable("Control"))
        {
            return [];
        }

        var textFromEvent = TextEventSubscribers(dialogName);
        var conditions = ConditionActions(dialogName);

        return _database.Query(
            "SELECT `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next` " +
            "FROM `Control` WHERE `Dialog_` = ?",
            r => new Control(
                r.GetString(1),
                r.GetString(2),
                r.GetInteger(3) ?? 0,
                r.GetInteger(4) ?? 0,
                r.GetInteger(5) ?? 0,
                r.GetInteger(6) ?? 0,
                r.GetInteger(7) ?? 0,
                NullIfEmpty(r.GetString(8)),
                r.GetString(9),
                NullIfEmpty(r.GetString(10)),
                textFromEvent.Contains(r.GetString(1)),
                conditions.Contains((r.GetString(1), "Show")),
                conditions.Contains((r.GetString(1), "Hide")),
                conditions.Contains((r.GetString(1), "Enable"))),
            dialogName);
    }

    // Controls whose Text an EventMapping row replaces at run time (SelectionDescription, ActionText, ...).
    private HashSet<string> TextEventSubscribers(string dialogName) =>
        _database.HasTable("EventMapping")
            ? _database.Query(
                    "SELECT `Control_` FROM `EventMapping` WHERE `Dialog_` = ? AND `Attribute` = 'Text'",
                    r => r.GetString(1), dialogName)
                .ToHashSet(StringComparer.Ordinal)
            : [];

    // (control, action) pairs of the ControlCondition table: Show, Hide, Enable, Disable, Default.
    private HashSet<(string Control, string Action)> ConditionActions(string dialogName) =>
        _database.HasTable("ControlCondition")
            ? _database.Query(
                    "SELECT `Control_`, `Action` FROM `ControlCondition` WHERE `Dialog_` = ?",
                    r => (r.GetString(1), r.GetString(2)), dialogName)
                .ToHashSet()
            : [];

    private static bool HasTable(MsiDatabase database, string table) =>
        database.Query("SELECT `Name` FROM `_Tables` WHERE `Name` = ?", r => r.GetString(1), table).Count > 0;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
