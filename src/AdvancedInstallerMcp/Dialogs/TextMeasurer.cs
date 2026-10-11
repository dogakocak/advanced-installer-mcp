using AdvancedInstallerMcp.Gdi;

namespace AdvancedInstallerMcp.Dialogs;

[Flags]
public enum MeasureOptions
{
    None = 0,
    NoWrap = 1,
    NoPrefix = 2,
    RightAligned = 4,
}

// Pixels the text takes up. LineHeight is the step between lines, so Height / LineHeight is the line count.
// FaceName is the font GDI actually used; FaceSubstituted means the requested face is not installed here.
// Raster: a bitmap font (e.g. MS Sans Serif), which comes in a few fixed sizes and does not scale smoothly with DPI.
public sealed record TextMeasurement(
    int Width, int Height, int LineHeight, string FaceName, bool FaceSubstituted, bool Raster = false);

// ADR 0003: measures text the way a STATIC control lays it out, with GDI DrawText on a memory DC. No verdicts here;
// whether a size fits a control is lint_dialog's call.
public static class TextMeasurer
{
    public static TextMeasurement Measure(string text, ResolvedFont font, int dpi, int maxWidth, MeasureOptions options)
    {
        using var dc = DeviceContextHandle.CreateMemory();
        using var gdiFont = GdiFonts.Create(font, dpi);

        var previous = NativeMethods.SelectObject(dc, gdiFont.DangerousGetHandle());
        try
        {
            // CreateFont never fails for an unknown face: GDI picks a substitute, and msiexec gets the same one.
            var faceName = ActualFaceName(dc);
            NativeMethods.GetTextMetrics(dc, out var metric);
            var lineHeight = metric.Height + metric.ExternalLeading;

            var (width, height) = text.Length == 0 ? (0, 0) : Extent(dc, text, maxWidth, options);

            return new TextMeasurement(
                width, height, lineHeight, faceName,
                !string.Equals(faceName, font.FaceName, StringComparison.OrdinalIgnoreCase),
                (metric.PitchAndFamily & (NativeMethods.TmpfVector | NativeMethods.TmpfTrueType)) == 0);
        }
        finally
        {
            NativeMethods.SelectObject(dc, previous);
        }
    }

    private static (int Width, int Height) Extent(DeviceContextHandle dc, string text, int maxWidth, MeasureOptions options)
    {
        var format = NativeMethods.DtCalcRect;
        format |= options.HasFlag(MeasureOptions.NoWrap) ? NativeMethods.DtSingleLine : NativeMethods.DtWordBreak;
        if (options.HasFlag(MeasureOptions.NoPrefix))
        {
            format |= NativeMethods.DtNoPrefix;
        }

        if (options.HasFlag(MeasureOptions.RightAligned))
        {
            format |= NativeMethods.DtRight;
        }

        var rect = new NativeMethods.Rect { Right = maxWidth };
        NativeMethods.DrawText(dc, text, text.Length, ref rect, format);
        return (rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static string ActualFaceName(DeviceContextHandle dc)
    {
        var buffer = new char[NativeMethods.FaceSize];
        if (NativeMethods.GetTextFace(dc, buffer.Length, buffer) == 0)
        {
            return "";
        }

        // The documented return value is ambiguous about the terminating null; the null itself is not.
        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }
}
