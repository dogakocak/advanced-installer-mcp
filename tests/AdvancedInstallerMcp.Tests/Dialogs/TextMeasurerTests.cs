using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Dialogs;

// The numbers are the measurement table of ADR 0003 (Tahoma and Segoe UI ship with Windows). They are plain DrawText
// widths: TextRenderer.MeasureText without a device context adds 7 px of padding and does not match.
public sealed class TextMeasurerTests
{
    private static readonly ResolvedFont Tahoma8 =
        new("Tahoma", 8, TextStyleBits.None, FontSource.DefaultUIFont, FontIssue.None);

    private static readonly ResolvedFont SegoeUI9 =
        new("Segoe UI", 9, TextStyleBits.None, FontSource.Fallback, FontIssue.DefaultUIFontMissing);

    [Theory]
    [InlineData(96, 13)]
    [InlineData(120, 16)]
    [InlineData(144, 19)]
    public void Tahoma_8_line_height(int dpi, int lineHeight)
    {
        TextMeasurer.Measure("x", Tahoma8, dpi, 1000, MeasureOptions.None).LineHeight.ShouldBe(lineHeight);
    }

    [Fact]
    public void Segoe_UI_9_line_height()
    {
        TextMeasurer.Measure("x", SegoeUI9, 96, 1000, MeasureOptions.None).LineHeight.ShouldBe(15);
    }

    [Theory]
    [InlineData(96, 260, 26)]
    [InlineData(120, 381, 32)]
    [InlineData(144, 457, 38)]
    public void Hint_wraps_to_two_lines_in_a_230_unit_control(int dpi, int width, int height)
    {
        var maxWidth = InstallerUnits.ToPixels(230, dpi);

        var measured = TextMeasurer.Measure(ConnectionDialogs.Hint, Tahoma8, dpi, maxWidth, MeasureOptions.None);

        measured.Height.ShouldBe(height);
        (measured.Height / measured.LineHeight).ShouldBe(2);
        measured.Width.ShouldBe(width);
        measured.Width.ShouldBeLessThanOrEqualTo(maxWidth);
    }

    [Fact]
    public void Hint_in_Segoe_UI_9_is_30_px_high()
    {
        var measured = TextMeasurer.Measure(ConnectionDialogs.Hint, SegoeUI9, 96, 307, MeasureOptions.None);

        measured.Width.ShouldBe(275);
        measured.Height.ShouldBe(30);
    }

    [Theory]
    [InlineData(96, 110)]
    [InlineData(120, 130)]
    [InlineData(144, 157)]
    public void Checkbox_label_width(int dpi, int width)
    {
        TextMeasurer.Measure(ConnectionDialogs.TrustLabel, Tahoma8, dpi, 1000, MeasureOptions.NoWrap).Width.ShouldBe(width);
    }

    [Fact]
    public void NoWrap_keeps_one_line_however_narrow()
    {
        var measured = TextMeasurer.Measure(ConnectionDialogs.Hint, Tahoma8, 96, 50, MeasureOptions.NoWrap);

        measured.Height.ShouldBe(measured.LineHeight);
        measured.Width.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void Ampersand_is_a_mnemonic_unless_NoPrefix()
    {
        var mnemonic = TextMeasurer.Measure("&Next >", Tahoma8, 96, 1000, MeasureOptions.NoWrap);
        var plain = TextMeasurer.Measure("Next >", Tahoma8, 96, 1000, MeasureOptions.NoWrap);
        var literal = TextMeasurer.Measure("&Next >", Tahoma8, 96, 1000, MeasureOptions.NoWrap | MeasureOptions.NoPrefix);

        mnemonic.Width.ShouldBe(plain.Width);
        literal.Width.ShouldBeGreaterThan(plain.Width);
    }

    [Fact]
    public void Bold_is_wider()
    {
        var bold = Tahoma8 with { Bits = TextStyleBits.Bold };

        TextMeasurer.Measure(ConnectionDialogs.TrustLabel, bold, 96, 1000, MeasureOptions.NoWrap).Width
            .ShouldBeGreaterThan(TextMeasurer.Measure(ConnectionDialogs.TrustLabel, Tahoma8, 96, 1000, MeasureOptions.NoWrap).Width);
    }

    [Fact]
    public void Installed_face_is_reported_as_is()
    {
        var measured = TextMeasurer.Measure("x", Tahoma8, 96, 1000, MeasureOptions.None);

        measured.FaceName.ShouldBe("Tahoma");
        measured.FaceSubstituted.ShouldBeFalse();
    }

    [Fact]
    public void Missing_face_reports_the_substitute_GDI_used()
    {
        var missing = Tahoma8 with { FaceName = "No Such Font 4f7a" };

        var measured = TextMeasurer.Measure("x", missing, 96, 1000, MeasureOptions.None);

        measured.FaceSubstituted.ShouldBeTrue();
        measured.FaceName.ShouldNotBeNullOrEmpty();
        measured.FaceName.ShouldNotBe(missing.FaceName);
    }

    [Fact]
    public void Empty_text_takes_no_space()
    {
        var measured = TextMeasurer.Measure("", Tahoma8, 96, 307, MeasureOptions.None);

        measured.Width.ShouldBe(0);
        measured.Height.ShouldBe(0);
    }
}
