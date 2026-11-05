using System.Text.RegularExpressions;
using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Dialogs;

// Which property names the install itself uses (ADR 0009): named in a custom action scheduled in an execute
// sequence, in an execute-sequence condition or a LaunchCondition, or anywhere in a table that is not UI-only.
// A name counts as a whole word: DB_HOST is not used by [DB_HOST_NAME].
public sealed class InstallUsage
{
    private static readonly HashSet<string> UiOnlyTables = new(StringComparer.Ordinal)
    {
        "Dialog", "Control", "ControlEvent", "ControlCondition", "EventMapping", "RadioButton", "ComboBox", "ListBox",
        "ListView", "CheckBox", "TextStyle", "UIText", "Binary", "Icon", "Property", "InstallUISequence",
        "AdminUISequence", "AdvtUISequence", "_Validation", "_Tables", "_Columns", "_Streams", "_Storages",
    };

    private static readonly string[] ExecuteSequences = ["InstallExecuteSequence", "AdminExecuteSequence", "AdvtExecuteSequence"];

    // Windows Installer identifiers: letters, digits, underscores and periods.
    private static readonly Regex Word = new(@"[A-Za-z_][A-Za-z0-9_.]*", RegexOptions.CultureInvariant);

    private readonly HashSet<string> _used;
    private readonly HashSet<string> _directories;

    private InstallUsage(HashSet<string> used, HashSet<string> directories)
    {
        _used = used;
        _directories = directories;
    }

    public bool IsUsed(string property) => _used.Contains(property);

    public bool IsDirectory(string property) => _directories.Contains(property);

    public static InstallUsage Read(MsiDatabase database)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        void AddWords(string cell)
        {
            foreach (Match match in Word.Matches(cell))
            {
                used.Add(match.Value);
            }
        }

        var tables = database.Query("SELECT `Name` FROM `_Tables`", r => r.GetString(1)).ToHashSet(StringComparer.Ordinal);
        var scheduled = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sequence in ExecuteSequences.Where(tables.Contains))
        {
            foreach (var (action, condition) in database.Query($"SELECT `Action`, `Condition` FROM `{sequence}`",
                         r => (r.GetString(1), r.GetString(2))))
            {
                scheduled.Add(action);
                AddWords(condition);
            }
        }

        foreach (var table in tables.Where(t => !UiOnlyTables.Contains(t) && !ExecuteSequences.Contains(t)))
        {
            if (table == "CustomAction")
            {
                foreach (var cells in database.Query("SELECT `Action`, `Source`, `Target` FROM `CustomAction`",
                             r => (Action: r.GetString(1), Source: r.GetString(2), Target: r.GetString(3)))
                             .Where(a => scheduled.Contains(a.Action)))
                {
                    AddWords(cells.Source);
                    AddWords(cells.Target);
                }

                continue;
            }

            var columns = database.Query("SELECT `Name` FROM `_Columns` WHERE `Table` = ?", r => r.GetString(1), table);
            foreach (var column in columns)
            {
                // msi.dll returns an integer column as its number, so those are read (a number holds no name). A
                // stream column fails with an MsiException (ERROR_INVALID_DATATYPE); its bytes hold no names either.
                IReadOnlyList<string> cells;
                try
                {
                    cells = database.Query($"SELECT `{column}` FROM `{table}`", r => r.GetString(1));
                }
                catch (MsiException)
                {
                    continue;
                }

                foreach (var cell in cells)
                {
                    AddWords(cell);
                }
            }
        }

        var directories = tables.Contains("Directory")
            ? database.Query("SELECT `Directory` FROM `Directory`", r => r.GetString(1)).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        return new InstallUsage(used, directories);
    }
}
