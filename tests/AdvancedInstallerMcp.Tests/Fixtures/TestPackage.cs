using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Tests.Fixtures;

// Builds a small MSI through the server's PackageWriter, so dialog tests run without Advanced Installer. Deleted on
// Dispose.
public sealed class TestPackage : IDisposable
{
    private readonly PackageRows _rows = new();

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}.msi");

    public TestPackage WithoutUiTables()
    {
        _rows.WithUiTables = false;
        return this;
    }

    public TestPackage AsInstallPackage()
    {
        _rows.InstallPackage = true;
        return this;
    }

    public TestPackage WithCustomAction(string action, int type, string? source, string? target)
    {
        _rows.CustomActions.Add(new CustomActionRow(action, type, source, target));
        return this;
    }

    public TestPackage WithControlEvent(string dialog, string control, string @event, string argument,
        string? condition = null, int? ordering = null)
    {
        _rows.ControlEvents.Add(new EventRow(dialog, control, @event, argument, condition ?? "1", ordering));
        return this;
    }

    public TestPackage WithUiSequence(string action, int sequence, string? condition = null)
    {
        _rows.UiSequence.Add(new SequenceRow(action, condition, sequence));
        return this;
    }

    public TestPackage WithExecuteSequence(string action, int sequence, string? condition = null)
    {
        _rows.ExecuteSequence.Add(new SequenceRow(action, condition, sequence));
        return this;
    }

    public TestPackage WithLaunchCondition(string condition, string description)
    {
        _rows.LaunchConditions.Add(new LaunchConditionRow(condition, description));
        return this;
    }

    public TestPackage WithProperty(string name, string value)
    {
        _rows.Properties.Add((name, value));
        return this;
    }

    public TestPackage WithTextStyle(string name, string faceName, int size, int? color = null, int? styleBits = null)
    {
        _rows.TextStyles.Add(new TextStyleRow(name, faceName, size, color, styleBits));
        return this;
    }

    public TestPackage WithDialog(string name, int width, int height, string firstControl, string? title = null,
        string? defaultControl = null, string? cancelControl = null, int attributes = 3)
    {
        _rows.Dialogs.Add(new DialogRow(name, width, height, attributes, title, firstControl, defaultControl, cancelControl));
        return this;
    }

    public TestPackage WithControl(string dialog, string name, string type, int x, int y, int width, int height,
        string? text = null, int attributes = 3, string? property = null, string? next = null)
    {
        _rows.Controls.Add(new ControlRow(dialog, name, type, x, y, width, height, attributes, property, text, next));
        return this;
    }

    // Only packages with at least one row get an EventMapping table, so the reader is tested without one too.
    public TestPackage WithEventMapping(string dialog, string control, string @event, string attribute)
    {
        _rows.EventMappings.Add(new EventMappingRow(dialog, control, @event, attribute));
        return this;
    }

    public TestPackage WithControlCondition(string dialog, string control, string action, string condition)
    {
        _rows.ControlConditions.Add(new ConditionRow(dialog, control, action, condition));
        return this;
    }

    public TestPackage WithEmbeddedUi(string fileName, int attributes)
    {
        _rows.EmbeddedUi.Add(new EmbeddedUiRow(fileName, fileName, attributes));
        return this;
    }

    public TestPackage WithBinary(string name, byte[] data)
    {
        _rows.Binaries.Add((name, data));
        return this;
    }

    public TestPackage WithRadioButton(string property, int order, string value, int x, int y, int width, int height,
        string? text = null)
    {
        _rows.RadioButtons.Add(new RadioButtonRow(property, order, value, x, y, width, height, text));
        return this;
    }

    public TestPackage WithComboItem(string property, int order, string value, string? text = null)
    {
        _rows.ComboBoxItems.Add(new ListItemRow(property, order, value, text));
        return this;
    }

    public MsiDatabase Build()
    {
        PackageWriter.Write(Path, _rows);
        return MsiDatabase.OpenReadOnly(Path);
    }

    public void Dispose() => File.Delete(Path);
}
