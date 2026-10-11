using System.Drawing;
using System.Drawing.Imaging;
using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Render;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Render;

// ADR 0006: no stored reference images (themes and ClearType differ per machine); these check what is drawn where.
public sealed class DialogRendererTests
{
    private static readonly Color Background = ColorTranslator.FromWin32(GetSysColor(15));

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSysColor(int index);

    private static (RenderedDialog Result, Bitmap Image) Render(
        TestPackage fixture, string dialog, int dpi = 96, Dictionary<string, string>? properties = null)
    {
        using var database = fixture.Build();
        var package = DialogPackage.Load(database);
        var result = DialogRenderer.Render(package, package.FindDialog(dialog)!, dpi, properties ?? []);
        return (result, new Bitmap(new MemoryStream(result.Png)));
    }

    private static Rectangle Pixels(int x, int y, int width, int height, int dpi = 96)
    {
        var left = InstallerUnits.ToPixels(x, dpi);
        var top = InstallerUnits.ToPixels(y, dpi);
        return Rectangle.FromLTRB(left, top, InstallerUnits.ToPixels(x + width, dpi), InstallerUnits.ToPixels(y + height, dpi));
    }

    // Rows of the area that have at least one pixel other than the dialog background.
    private static List<int> InkRows(Bitmap image, Rectangle area) =>
        Enumerable.Range(area.Top, area.Height)
            .Where(y => Enumerable.Range(area.Left, area.Width).Any(x => image.GetPixel(x, y).ToArgb() != Background.ToArgb()))
            .ToList();

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public void Image_is_the_dialog_client_area_in_pixels(int dpi)
    {
        using var fixture = ConnectionDialogs.Create();

        var (result, image) = Render(fixture, ConnectionDialogs.Fixed, dpi);

        image.Width.ShouldBe(InstallerUnits.ToPixels(370, dpi));
        image.Height.ShouldBe(InstallerUnits.ToPixels(270, dpi));
        result.Report.Width.ShouldBe(image.Width);
        result.Report.Dpi.ShouldBe(dpi);
    }

    // The broken hint is 18 units (24 px) high and needs two 13 px lines: msiexec shows the first line only, and
    // lint_dialog reports it as cut off. The picture must agree.
    [Fact]
    public void A_cut_off_label_shows_whole_lines_only()
    {
        using var fixture = ConnectionDialogs.Create();
        var font = FontResolver.Resolve(null, new Dictionary<string, TextStyle> { ["DlgFont8"] = new("DlgFont8", "Tahoma", 8, null, 0) }, "DlgFont8");
        var lineHeight = TextMeasurer.Measure("x", font, 96, 1000, MeasureOptions.None).LineHeight;

        var (_, image) = Render(fixture, ConnectionDialogs.Broken);
        var hint = Pixels(20, 60, 230, 18);

        var rows = InkRows(image, hint);
        rows.ShouldNotBeEmpty();
        rows.Max().ShouldBeLessThan(hint.Top + lineHeight);
    }

    [Fact]
    public void A_label_that_fits_shows_both_lines()
    {
        using var fixture = ConnectionDialogs.Create();

        var (_, image) = Render(fixture, ConnectionDialogs.Fixed);
        var hint = Pixels(20, 60, 230, 20);

        InkRows(image, hint).Max().ShouldBeGreaterThan(hint.Top + 14);
    }

    [Fact]
    public void A_control_hidden_by_its_condition_is_not_drawn()
    {
        using var fixture = ConnectionDialogs.Create()
            .WithControlCondition(ConnectionDialogs.Fixed, "Hint", "Hide", "NOT DB_SERVER");

        var (hidden, hiddenImage) = Render(fixture, ConnectionDialogs.Fixed);
        InkRows(hiddenImage, Pixels(20, 60, 230, 20)).ShouldBeEmpty();
        hidden.Report.Conditions.Single().Result.ShouldBe(true);
    }

    [Fact]
    public void Caller_properties_change_what_is_drawn()
    {
        using var fixture = ConnectionDialogs.Create()
            .WithControlCondition(ConnectionDialogs.Fixed, "Hint", "Hide", "NOT DB_SERVER");

        var (_, image) = Render(fixture, ConnectionDialogs.Fixed, properties: new() { ["DB_SERVER"] = "sql01" });

        InkRows(image, Pixels(20, 60, 230, 20)).ShouldNotBeEmpty();
    }

    [Fact]
    public void Pictures_come_from_the_Binary_table()
    {
        using var red = new Bitmap(8, 8);
        using (var g = Graphics.FromImage(red))
        {
            g.Clear(Color.Red);
        }

        using var png = new MemoryStream();
        red.Save(png, ImageFormat.Png);

        using var fixture = ConnectionDialogs.Create()
            .WithBinary("Banner", png.ToArray())
            .WithControl(ConnectionDialogs.Fixed, "BannerBitmap", "Bitmap", 0, 0, 370, 44, "Banner", attributes: 1);

        var (result, image) = Render(fixture, ConnectionDialogs.Fixed);

        // The middle of the banner, away from the edges where stretching blends in the background, and right of the title.
        image.GetPixel(InstallerUnits.ToPixels(300, 96), InstallerUnits.ToPixels(30, 96)).ToArgb().ShouldBe(Color.Red.ToArgb());
        result.Report.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_picture_is_reported()
    {
        using var fixture = ConnectionDialogs.Create()
            .WithControl(ConnectionDialogs.Fixed, "BannerBitmap", "Bitmap", 0, 0, 370, 44, "NoSuchBinary", attributes: 1);

        Render(fixture, ConnectionDialogs.Fixed).Result.Report.Problems.Single().ShouldContain("NoSuchBinary");
    }

    [Fact]
    public void The_report_says_what_the_picture_cannot_show()
    {
        using var fixture = ConnectionDialogs.Create()
            .WithControl(ConnectionDialogs.Fixed, "Features", "SelectionTree", 20, 130, 200, 80, attributes: 3)
            .WithEventMapping(ConnectionDialogs.Fixed, "Title", "SelectionDescription", "Text");

        var report = Render(fixture, ConnectionDialogs.Fixed).Result.Report;

        report.Title.ShouldBe("[ProductName] Setup");
        report.UnresolvedProperties.ShouldBe(["ProductName"]);
        report.Placeholders.ShouldBe(["Features (SelectionTree)"]);
        report.RunTimeText.ShouldBe(["Title"]);
        report.Fonts.ShouldContain(f => f.Face == "Tahoma" && f.Size == 8);
    }

    [Fact]
    public void Edit_boxes_show_the_property_value()
    {
        using var fixture = ConnectionDialogs.Create();

        var (_, empty) = Render(fixture, ConnectionDialogs.Fixed);
        var (_, filled) = Render(fixture, ConnectionDialogs.Fixed, properties: new() { ["DB_SERVER"] = "sql01.contoso.local" });
        var edit = Pixels(20, 82, 200, 18);
        var inner = Rectangle.Inflate(edit, -4, -4);

        Count(filled, inner).ShouldBeGreaterThan(Count(empty, inner));

        static int Count(Bitmap image, Rectangle area) =>
            Enumerable.Range(area.Top, area.Height)
                .Sum(y => Enumerable.Range(area.Left, area.Width).Count(x => image.GetPixel(x, y).GetBrightness() < 0.5f));
    }
}
