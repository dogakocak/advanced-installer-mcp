namespace AdvancedInstallerMcp.Dialogs;

// Bits of the Control table's Attributes column. The high bits mean different things per control type: 0x00040000 is
// NoWrap on a Text control but Bitmap on a PushButton or CheckBox.
public static class ControlAttributes
{
    public const int Visible = 0x00000001;
    public const int Enabled = 0x00000002;
    public const int Sunken = 0x00000004;
    public const int Indirect = 0x00000008;
    public const int RightAligned = 0x00000040;

    // Text
    public const int Transparent = 0x00010000;
    public const int NoPrefix = 0x00020000;
    public const int NoWrap = 0x00040000;

    // Edit
    public const int Multiline = 0x00010000;
    public const int Password = 0x00200000;

    // PushButton, CheckBox, RadioButton
    public const int PushLike = 0x00020000;
    public const int Bitmap = 0x00040000;
    public const int Icon = 0x00080000;
    public const int FixedSize = 0x00100000;
    public const int IconSize16 = 0x00200000;
    public const int IconSize32 = 0x00400000;
    public const int IconSize48 = 0x00600000; // both bits: 16 and 32 together mean 48
    public const int ElevationShield = 0x00800000;

    // RadioButtonGroup
    public const int HasBorder = 0x01000000;

    public static bool Has(this Control control, int attribute) => (control.Attributes & attribute) == attribute;
}
