using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Tests.Fixtures;

// Builds a small MSI with the standard Property, TextStyle, Dialog and Control tables (column types as in the
// Windows Installer docs), so dialog tests run without Advanced Installer. Deleted on Dispose.
public sealed class TestPackage : IDisposable
{
    private readonly List<(string Name, string Value)> _properties = [];
    private readonly List<object?[]> _textStyles = [];
    private readonly List<object?[]> _dialogs = [];
    private readonly List<object?[]> _controls = [];
    private readonly List<object?[]> _eventMappings = [];
    private readonly List<object?[]> _controlConditions = [];
    private bool _withUiTables = true;

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}.msi");

    public TestPackage WithoutUiTables()
    {
        _withUiTables = false;
        return this;
    }

    public TestPackage WithProperty(string name, string value)
    {
        _properties.Add((name, value));
        return this;
    }

    public TestPackage WithTextStyle(string name, string faceName, int size, int? color = null, int? styleBits = null)
    {
        _textStyles.Add([name, faceName, size, color, styleBits]);
        return this;
    }

    public TestPackage WithDialog(string name, int width, int height, string firstControl, string? title = null,
        string? defaultControl = null, string? cancelControl = null, int attributes = 3)
    {
        _dialogs.Add([name, 50, 50, width, height, attributes, title, firstControl, defaultControl, cancelControl]);
        return this;
    }

    public TestPackage WithControl(string dialog, string name, string type, int x, int y, int width, int height,
        string? text = null, int attributes = 3, string? property = null, string? next = null)
    {
        _controls.Add([dialog, name, type, x, y, width, height, attributes, property, text, next, null]);
        return this;
    }

    // Only packages with at least one row get an EventMapping table, so the reader is tested without one too.
    public TestPackage WithEventMapping(string dialog, string control, string @event, string attribute)
    {
        _eventMappings.Add([dialog, control, @event, attribute]);
        return this;
    }

    public TestPackage WithControlCondition(string dialog, string control, string action, string condition)
    {
        _controlConditions.Add([dialog, control, action, condition]);
        return this;
    }

    public MsiDatabase Build()
    {
        using (var database = MsiDatabase.Create(Path))
        {
            database.Execute("CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) NOT NULL LOCALIZABLE PRIMARY KEY `Property`)");
            foreach (var (name, value) in _properties)
            {
                database.Execute("INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)", name, value);
            }

            if (_withUiTables)
            {
                CreateUiTables(database);
            }

            database.Commit();
        }

        return MsiDatabase.OpenReadOnly(Path);
    }

    public void Dispose() => File.Delete(Path);

    private void CreateUiTables(MsiDatabase database)
    {
        database.Execute(
            "CREATE TABLE `TextStyle` (`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL, `Size` SHORT NOT NULL, " +
            "`Color` LONG, `StyleBits` SHORT PRIMARY KEY `TextStyle`)");
        database.Execute(
            "CREATE TABLE `Dialog` (`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, " +
            "`Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, " +
            "`Control_First` CHAR(50) NOT NULL, `Control_Default` CHAR(50), `Control_Cancel` CHAR(50) PRIMARY KEY `Dialog`)");
        database.Execute(
            "CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, " +
            "`X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, " +
            "`Property` CHAR(72), `Text` CHAR(0) LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(50) LOCALIZABLE " +
            "PRIMARY KEY `Dialog_`, `Control`)");

        foreach (var row in _textStyles)
        {
            database.Execute("INSERT INTO `TextStyle` (`TextStyle`, `FaceName`, `Size`, `Color`, `StyleBits`) VALUES (?, ?, ?, ?, ?)", row);
        }

        foreach (var row in _dialogs)
        {
            database.Execute(
                "INSERT INTO `Dialog` (`Dialog`, `HCentering`, `VCentering`, `Width`, `Height`, `Attributes`, `Title`, " +
                "`Control_First`, `Control_Default`, `Control_Cancel`) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", row);
        }

        foreach (var row in _controls)
        {
            database.Execute(
                "INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, " +
                "`Text`, `Control_Next`, `Help`) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", row);
        }

        if (_controlConditions.Count > 0)
        {
            database.Execute(
                "CREATE TABLE `ControlCondition` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, " +
                "`Action` CHAR(50) NOT NULL, `Condition` CHAR(255) NOT NULL PRIMARY KEY `Dialog_`, `Control_`, `Action`, `Condition`)");
            foreach (var row in _controlConditions)
            {
                database.Execute(
                    "INSERT INTO `ControlCondition` (`Dialog_`, `Control_`, `Action`, `Condition`) VALUES (?, ?, ?, ?)", row);
            }
        }

        if (_eventMappings.Count > 0)
        {
            database.Execute(
                "CREATE TABLE `EventMapping` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, " +
                "`Event` CHAR(50) NOT NULL, `Attribute` CHAR(50) NOT NULL PRIMARY KEY `Dialog_`, `Control_`, `Event`)");
            foreach (var row in _eventMappings)
            {
                database.Execute(
                    "INSERT INTO `EventMapping` (`Dialog_`, `Control_`, `Event`, `Attribute`) VALUES (?, ?, ?, ?)", row);
            }
        }
    }
}
