using System.ComponentModel;

namespace AdvancedInstallerMcp.Mock;

// mock_dialog's input (ADR 0008): rows of the Property, TextStyle, Dialog, Control, RadioButton / ComboBox / ListBox
// and ControlCondition tables, with their column names. Description and Rationale are not table data; they only go
// on the approval page.
public sealed record MockSpec(
    [property: Description("Property rows: name → default value. Inputs without a row start empty.")]
    Dictionary<string, string>? Properties,
    [property: Description("Extra TextStyle rows. The wizard frame brings Advanced Installer's own.")]
    IReadOnlyList<MockTextStyle>? TextStyles,
    [property: Description("The proposed pages, in wizard order.")]
    IReadOnlyList<MockDialogSpec> Dialogs);

public sealed record MockTextStyle(string Name, string FaceName, int Size, int? Color = null, int? StyleBits = null);

public sealed record MockDialogSpec(
    [property: Description("Dialog name (Identifier), e.g. 'CertificateDlg'.")] string Name,
    [property: Description("Controls of the page body; with frame 'wizard' place them between y 52 and 226.")]
    IReadOnlyList<MockControl> Controls,
    [property: Description("One or two sentences for the approval page: what the page asks and how it behaves.")]
    string? Description = null,
    [property: Description("'wizard' (default): Advanced Installer's banner, lines and Back/Next/Cancel. 'none': only these controls.")]
    string Frame = "wizard",
    [property: Description("Banner title (frame 'wizard').")] string? BannerTitle = null,
    [property: Description("Banner description line (frame 'wizard').")] string? BannerText = null,
    [property: Description("Window title. Default: the frame's ('[ProductName] Setup').")] string? Title = null,
    int? Width = null,
    int? Height = null,
    [property: Description("ControlCondition rows of this page.")] IReadOnlyList<MockCondition>? Conditions = null);

public sealed record MockControl(
    string Name,
    [property: Description("A Windows Installer control type: Text, Edit, MaskedEdit, PathEdit, PushButton, CheckBox, RadioButtonGroup, ComboBox, ListBox, GroupBox, Line, Bitmap, Icon, ...")]
    string Type,
    int X,
    int Y,
    int Width,
    int Height,
    [property: Description("Control attributes as in the Control table. Default 3 (visible, enabled).")] int? Attributes = null,
    [property: Description("Property the input sets. Upper case for every input.")] string? Property = null,
    string? Text = null,
    [property: Description("Control_Next. Omit to tab in the order of 'controls', then the frame's buttons.")] string? Next = null,
    [property: Description("Why this control for this value; shown on the approval page.")] string? Rationale = null,
    [property: Description("RadioButtonGroup buttons or ComboBox / ListBox entries. Radio buttons without a position stack one per 15 units.")]
    IReadOnlyList<MockItem>? Items = null);

public sealed record MockItem(string Value, string? Text = null, int? X = null, int? Y = null, int? Width = null, int? Height = null);

public sealed record MockCondition(string Control, string Action, string Condition);
