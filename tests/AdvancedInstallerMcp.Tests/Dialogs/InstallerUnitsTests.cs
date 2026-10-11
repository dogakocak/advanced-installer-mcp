using AdvancedInstallerMcp.Dialogs;

namespace AdvancedInstallerMcp.Tests.Dialogs;

public sealed class InstallerUnitsTests
{
    [Theory]
    [InlineData(370, 96, 493)]
    [InlineData(234, 96, 312)]
    [InlineData(230, 96, 307)]
    [InlineData(230, 120, 383)]
    [InlineData(18, 96, 24)]
    [InlineData(20, 96, 27)]
    [InlineData(18, 120, 30)]
    [InlineData(20, 120, 33)]
    [InlineData(18, 144, 36)]
    [InlineData(20, 144, 40)]
    [InlineData(125, 96, 167)]
    [InlineData(125, 120, 208)]
    [InlineData(125, 144, 250)]
    public void Units_to_pixels_match_ADR_0003(int units, int dpi, int pixels)
    {
        InstallerUnits.ToPixels(units, dpi).ShouldBe(pixels);
    }

    [Theory]
    [InlineData(8, 96, 11)]
    [InlineData(8, 120, 13)]
    [InlineData(8, 144, 16)]
    [InlineData(9, 96, 12)]
    public void Points_to_pixels_round_like_MulDiv(int points, int dpi, int pixels)
    {
        InstallerUnits.PointsToPixels(points, dpi).ShouldBe(pixels);
    }
}
