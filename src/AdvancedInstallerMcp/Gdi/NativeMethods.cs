using System.Runtime.InteropServices;

namespace AdvancedInstallerMcp.Gdi;

// The few gdi32/user32 functions text measurement needs (ADR 0003). Handles come back as nint and are wrapped by the
// caller; a memory DC needs no window and does not depend on the machine's display scaling.
internal static partial class NativeMethods
{
    private const string Gdi32 = "gdi32.dll";
    private const string User32 = "user32.dll";

    public const int FwNormal = 400;
    public const int FwBold = 700;
    public const uint DefaultCharset = 1;

    public const uint DtRight = 0x00000002;
    public const uint DtSingleLine = 0x00000020;
    public const uint DtWordBreak = 0x00000010;
    public const uint DtCalcRect = 0x00000400;
    public const uint DtNoPrefix = 0x00000800;

    // TEXTMETRIC.tmPitchAndFamily: neither bit set means a raster (bitmap) font.
    public const byte TmpfVector = 0x02;
    public const byte TmpfTrueType = 0x04;

    // LF_FACESIZE: face names are at most 31 characters plus the terminating null.
    public const int FaceSize = 32;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // TEXTMETRICW; WCHAR fields as ushort so the struct stays blittable for LibraryImport.
    [StructLayout(LayoutKind.Sequential)]
    public struct TextMetric
    {
        public int Height;
        public int Ascent;
        public int Descent;
        public int InternalLeading;
        public int ExternalLeading;
        public int AveCharWidth;
        public int MaxCharWidth;
        public int Weight;
        public int Overhang;
        public int DigitizedAspectX;
        public int DigitizedAspectY;
        public ushort FirstChar;
        public ushort LastChar;
        public ushort DefaultChar;
        public ushort BreakChar;
        public byte Italic;
        public byte Underlined;
        public byte StruckOut;
        public byte PitchAndFamily;
        public byte CharSet;
    }

    [LibraryImport(Gdi32)]
    public static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport(Gdi32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint dc);

    [LibraryImport(Gdi32, EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFont(
        int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet,
        uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

    [LibraryImport(Gdi32)]
    public static partial nint SelectObject(DeviceContextHandle dc, nint gdiObject);

    [LibraryImport(Gdi32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint gdiObject);

    // 0 on failure; otherwise the buffer holds the null-terminated face name.
    [LibraryImport(Gdi32, EntryPoint = "GetTextFaceW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetTextFace(DeviceContextHandle dc, int count, [Out] char[] faceName);

    [LibraryImport(Gdi32, EntryPoint = "GetTextMetricsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextMetrics(DeviceContextHandle dc, out TextMetric metric);

    // With DT_CALCRECT nothing is drawn: rect.Right is the wrap width on input, the result is the text's extent.
    [LibraryImport(User32, EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawText(DeviceContextHandle dc, string text, int length, ref Rect rect, uint format);
}
