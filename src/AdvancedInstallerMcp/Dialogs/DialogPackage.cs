using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Dialogs;

// The UI-related content of one MSI package: properties, text styles and dialogs.
public sealed class DialogPackage
{
    private readonly MsiDatabase _database;

    private DialogPackage(
        MsiDatabase database,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyDictionary<string, TextStyle> textStyles,
        IReadOnlyList<string> dialogNames)
    {
        _database = database;
        Properties = properties;
        TextStyles = textStyles;
        DialogNames = dialogNames;
    }

    public IReadOnlyDictionary<string, string> Properties { get; }

    public IReadOnlyDictionary<string, TextStyle> TextStyles { get; }

    public IReadOnlyList<string> DialogNames { get; }

    public string? DefaultUIFont => Properties.GetValueOrDefault("DefaultUIFont");

    // A package without UI has no Dialog, Control or TextStyle table; that reads as empty, not as an error.
    public static DialogPackage Load(MsiDatabase database)
    {
        var properties = HasTable(database, "Property")
            ? database.Query("SELECT `Property`, `Value` FROM `Property`", r => (Name: r.GetString(1), Value: r.GetString(2)))
                .ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var textStyles = HasTable(database, "TextStyle")
            ? database.Query(
                    "SELECT `TextStyle`, `FaceName`, `Size`, `Color`, `StyleBits` FROM `TextStyle`",
                    r => new TextStyle(r.GetString(1), r.GetString(2), r.GetInteger(3) ?? 0, r.GetInteger(4),
                        (TextStyleBits)(r.GetInteger(5) ?? 0)))
                .ToDictionary(s => s.Name, StringComparer.Ordinal)
            : new Dictionary<string, TextStyle>(StringComparer.Ordinal);

        var dialogNames = HasTable(database, "Dialog")
            ? database.Query("SELECT `Dialog` FROM `Dialog`", r => r.GetString(1)).Order(StringComparer.Ordinal).ToList()
            : [];

        return new DialogPackage(database, properties, textStyles, dialogNames);
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

    private IReadOnlyList<Control> ReadControls(string dialogName)
    {
        if (!HasTable(_database, "Control"))
        {
            return [];
        }

        var textFromEvent = TextEventSubscribers(dialogName);
        var shownByCondition = ShownByCondition(dialogName);

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
                shownByCondition.Contains(r.GetString(1))),
            dialogName);
    }

    // Controls whose Text an EventMapping row replaces at run time (SelectionDescription, ActionText, ...).
    private HashSet<string> TextEventSubscribers(string dialogName) =>
        HasTable(_database, "EventMapping")
            ? _database.Query(
                    "SELECT `Control_` FROM `EventMapping` WHERE `Dialog_` = ? AND `Attribute` = 'Text'",
                    r => r.GetString(1), dialogName)
                .ToHashSet(StringComparer.Ordinal)
            : [];

    private HashSet<string> ShownByCondition(string dialogName) =>
        HasTable(_database, "ControlCondition")
            ? _database.Query(
                    "SELECT `Control_` FROM `ControlCondition` WHERE `Dialog_` = ? AND `Action` = 'Show'",
                    r => r.GetString(1), dialogName)
                .ToHashSet(StringComparer.Ordinal)
            : [];

    private static bool HasTable(MsiDatabase database, string table) =>
        database.Query("SELECT `Name` FROM `_Tables` WHERE `Name` = ?", r => r.GetString(1), table).Count > 0;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
