namespace AdvancedInstallerMcp.Dialogs;

// ADR 0003: one installer unit is 4/3 px at 96 DPI on both axes and scales with DPI.
public static class InstallerUnits
{
    // 100 %, 125 % and 150 %: every text check runs at all three.
    public static readonly IReadOnlyList<int> CheckedDpis = [96, 120, 144];

    // Integer numerator, so 18 units at 120 DPI is exactly 30 and not 29.999...; .5 rounds up like MulDiv.
    public static int ToPixels(int units, int dpi) =>
        (int)Math.Round(units * 4 * dpi / 288.0, MidpointRounding.AwayFromZero);

    // Font em height for CreateFont, as the MSDN formula -MulDiv(points, dpi, 72) without the sign.
    public static int PointsToPixels(int points, int dpi) =>
        (int)Math.Round(points * dpi / 72.0, MidpointRounding.AwayFromZero);
}
