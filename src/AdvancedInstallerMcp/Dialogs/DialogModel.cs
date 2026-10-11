namespace AdvancedInstallerMcp.Dialogs;

// Rows of the Dialog, Control and TextStyle tables. Sizes and positions are installer units (ADR 0003).
// Control.TextFromEvent: an EventMapping row sets the control's Text attribute at run time.
// Control.ShownByCondition / HiddenByCondition / EnabledByCondition: a ControlCondition row can show, hide or enable
// the control at run time, whatever its Attributes say at the start.
public sealed record Dialog(
    string Name,
    int Width,
    int Height,
    int Attributes,
    string Title,
    string FirstControl,
    string? DefaultControl,
    string? CancelControl,
    IReadOnlyList<Control> Controls)
{
    // msidbDialogAttributesError: the dialog shows installer messages; Control_First, Control_Default and
    // Control_Cancel are ignored.
    public bool IsErrorDialog => (Attributes & 0x00010000) != 0;
}

public sealed record Control(
    string Name,
    string Type,
    int X,
    int Y,
    int Width,
    int Height,
    int Attributes,
    string? Property,
    string Text,
    string? Next,
    bool TextFromEvent = false,
    bool ShownByCondition = false,
    bool HiddenByCondition = false,
    bool EnabledByCondition = false)
{
    // A ControlCondition row can show or hide it, so whether it is on screen depends on the install.
    public bool Conditional => ShownByCondition || HiddenByCondition;
}

// A row of the ControlCondition table: Action (Show, Hide, Enable, Disable, Default) applies when Condition is true.
public sealed record ControlConditionRow(string Control, string Action, string Condition);

// A row of the RadioButton table: one button of the RadioButtonGroup whose Property matches, placed relative to the group.
public sealed record RadioButton(string Value, int X, int Y, int Width, int Height, string Text);

public sealed record TextStyle(string Name, string FaceName, int Size, int? Color, TextStyleBits Bits);

[Flags]
public enum TextStyleBits
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strike = 8,
}
