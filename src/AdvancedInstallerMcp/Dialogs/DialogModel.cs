namespace AdvancedInstallerMcp.Dialogs;

// Rows of the Dialog, Control and TextStyle tables. Sizes and positions are installer units (ADR 0003).
// Control.TextFromEvent: an EventMapping row sets the control's Text attribute at run time.
// Control.ShownByCondition: a ControlCondition row can show the control, even if it starts hidden.
public sealed record Dialog(
    string Name,
    int Width,
    int Height,
    int Attributes,
    string Title,
    string FirstControl,
    string? DefaultControl,
    string? CancelControl,
    IReadOnlyList<Control> Controls);

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
    bool ShownByCondition = false);

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
