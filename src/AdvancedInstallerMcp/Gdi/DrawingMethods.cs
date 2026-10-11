using System.Runtime.InteropServices;

namespace AdvancedInstallerMcp.Gdi;

// What render_dialog needs on top of measurement (ADR 0006): drawing on the HDC of a System.Drawing bitmap, and the
// visual-style parts Windows draws buttons, check boxes, radio buttons and edit boxes with.
internal static partial class NativeMethods
{
    private const string UxTheme = "uxtheme.dll";

    // GetSysColor indexes.
    public const int ColorWindow = 5;
    public const int ColorWindowText = 8;
    public const int ColorBtnFace = 15;
    public const int ColorGrayText = 17;

    public const int Transparent = 1;

    public const uint DtCenter = 0x00000001;
    public const uint DtVCenter = 0x00000004;
    public const uint DtEditControl = 0x00002000;
    public const uint DtEndEllipsis = 0x00008000;
    public const uint DtHidePrefix = 0x00100000;

    // DrawEdge
    public const uint EdgeSunken = 0x000A;
    public const uint EdgeEtched = 0x0006;
    public const uint BfRect = 0x000F;
    public const uint BfTop = 0x0002;

    public const uint DiNormal = 0x0003;

    // Theme parts and states (vssym32.h).
    public const int BpPushButton = 1;
    public const int BpRadioButton = 2;
    public const int BpCheckBox = 3;
    public const int BpGroupBox = 4;
    public const int PbsNormal = 1;
    public const int PbsDisabled = 4;
    public const int PbsDefaulted = 5;
    public const int ButtonUncheckedNormal = 1; // RBS_ and CBS_ share these values
    public const int ButtonUncheckedDisabled = 4;
    public const int ButtonCheckedNormal = 5;
    public const int ButtonCheckedDisabled = 8;
    public const int EpEditText = 1;
    public const int EtsNormal = 1;
    public const int EtsDisabled = 4;
    public const int CpDropDownButtonRight = 6;
    public const int CbxsNormal = 1;
    public const int TsDraw = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct Size
    {
        public int Cx;
        public int Cy;
    }

    [LibraryImport(User32)]
    public static partial uint GetSysColor(int index);

    [LibraryImport(User32, EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawText(nint dc, string text, int length, ref Rect rect, uint format);

    [LibraryImport(User32)]
    public static partial int FillRect(nint dc, ref Rect rect, nint brush);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawEdge(nint dc, ref Rect rect, uint edge, uint flags);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);

    [LibraryImport(Gdi32)]
    public static partial nint SelectObject(nint dc, nint gdiObject);

    [LibraryImport(Gdi32)]
    public static partial nint CreateSolidBrush(uint color);

    [LibraryImport(Gdi32)]
    public static partial int SetBkMode(nint dc, int mode);

    [LibraryImport(Gdi32)]
    public static partial uint SetTextColor(nint dc, uint color);

    [LibraryImport(Gdi32)]
    public static partial int SaveDC(nint dc);

    [LibraryImport(Gdi32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RestoreDC(nint dc, int savedDc);

    [LibraryImport(Gdi32)]
    public static partial int IntersectClipRect(nint dc, int left, int top, int right, int bottom);

    // NULL window: the theme data does not depend on a window, only on the DPI it is opened for.
    [LibraryImport(UxTheme, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenThemeDataForDpi(nint window, string classList, uint dpi);

    [LibraryImport(UxTheme)]
    public static partial int CloseThemeData(nint theme);

    [LibraryImport(UxTheme)]
    public static partial int DrawThemeBackground(nint theme, nint dc, int part, int state, ref Rect rect, nint clip);

    [LibraryImport(UxTheme)]
    public static partial int GetThemePartSize(nint theme, nint dc, int part, int state, nint rect, int sizeType, out Size size);
}
