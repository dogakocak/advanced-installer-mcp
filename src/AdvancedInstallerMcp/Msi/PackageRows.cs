namespace AdvancedInstallerMcp.Msi;

// The rows of a package the server or a test writes (PackageWriter). Columns as in the Windows Installer tables;
// null is an empty cell.
public sealed record TextStyleRow(string Name, string FaceName, int Size, int? Color = null, int? StyleBits = null);

public sealed record DialogRow(
    string Name, int Width, int Height, int Attributes, string? Title, string FirstControl, string? DefaultControl,
    string? CancelControl);

public sealed record ControlRow(
    string Dialog, string Name, string Type, int X, int Y, int Width, int Height, int Attributes, string? Property,
    string? Text, string? Next);

public sealed record ConditionRow(string Dialog, string Control, string Action, string Condition);

public sealed record EventRow(string Dialog, string Control, string Event, string Argument, string Condition, int? Ordering);

public sealed record EventMappingRow(string Dialog, string Control, string Event, string Attribute);

public sealed record RadioButtonRow(string Property, int Order, string Value, int X, int Y, int Width, int Height, string? Text);

// A row of the ComboBox or ListBox table.
public sealed record ListItemRow(string Property, int Order, string Value, string? Text);

public sealed record CustomActionRow(string Action, int Type, string? Source, string? Target);

public sealed record SequenceRow(string Action, string? Condition, int Sequence);

public sealed record LaunchConditionRow(string Condition, string Description);

// Attributes 1 (msidbEmbeddedUI) marks the UI DLL, 0 a support file. No Data: only the attributes are read.
public sealed record EmbeddedUiRow(string Name, string FileName, int Attributes);

public sealed class PackageRows
{
    public List<(string Name, string Value)> Properties { get; } = [];
    public List<TextStyleRow> TextStyles { get; } = [];
    public List<DialogRow> Dialogs { get; } = [];
    public List<ControlRow> Controls { get; } = [];
    public List<ConditionRow> ControlConditions { get; } = [];
    public List<EventRow> ControlEvents { get; } = [];
    public List<EventMappingRow> EventMappings { get; } = [];
    public List<RadioButtonRow> RadioButtons { get; } = [];
    public List<ListItemRow> ComboBoxItems { get; } = [];
    public List<ListItemRow> ListBoxItems { get; } = [];
    public List<(string Name, byte[] Data)> Binaries { get; } = [];
    public List<CustomActionRow> CustomActions { get; } = [];
    public List<SequenceRow> UiSequence { get; } = [];
    public List<SequenceRow> ExecuteSequence { get; } = [];
    public List<LaunchConditionRow> LaunchConditions { get; } = [];
    public List<EmbeddedUiRow> EmbeddedUi { get; } = [];

    // Dialog, Control and TextStyle exist even when empty, as in any package with a UI.
    public bool WithUiTables { get; set; } = true;

    // Adds what Windows Installer checks before it opens a session on the package (MsiOpenPackage): the summary
    // stream and a ProductCode. Without them it refuses with 1620 and 1605.
    public bool InstallPackage { get; set; }
}
