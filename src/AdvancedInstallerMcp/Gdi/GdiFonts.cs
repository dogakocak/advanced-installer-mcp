using AdvancedInstallerMcp.Dialogs;

namespace AdvancedInstallerMcp.Gdi;

// The one place a GDI font is made from a resolved TextStyle, so measurement (ADR 0003) and drawing (ADR 0006) use
// the same font for the same text.
internal static class GdiFonts
{
    public static FontHandle Create(ResolvedFont font, int dpi) => FontHandle.Own(NativeMethods.CreateFont(
        -InstallerUnits.PointsToPixels(font.Size, dpi), 0, 0, 0,
        font.Bits.HasFlag(TextStyleBits.Bold) ? NativeMethods.FwBold : NativeMethods.FwNormal,
        font.Bits.HasFlag(TextStyleBits.Italic) ? 1u : 0u,
        font.Bits.HasFlag(TextStyleBits.Underline) ? 1u : 0u,
        font.Bits.HasFlag(TextStyleBits.Strike) ? 1u : 0u,
        NativeMethods.DefaultCharset, 0, 0, 0, 0, font.FaceName));
}
