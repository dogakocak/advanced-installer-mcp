using System.Text.Json;
using AdvancedInstallerMcp.Tests.Fixtures;
using AdvancedInstallerMcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace AdvancedInstallerMcp.Tests.Tools;

public sealed class RenderDialogToolTests
{
    [Fact]
    public void Returns_the_picture_and_a_report()
    {
        using var fixture = ConnectionDialogs.Create();
        fixture.Build().Dispose();

        var result = RenderDialogTool.RenderDialog(fixture.Path, ConnectionDialogs.Fixed);

        result.IsError.ShouldNotBe(true);
        var image = result.Content[0].ShouldBeOfType<ImageContentBlock>();
        image.MimeType.ShouldBe("image/png");
        image.DecodedData.Span[..4].ToArray().ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        using var report = JsonDocument.Parse(result.Content[1].ShouldBeOfType<TextContentBlock>().Text);
        report.RootElement.GetProperty("dialog").GetString().ShouldBe(ConnectionDialogs.Fixed);
        report.RootElement.GetProperty("dpi").GetInt32().ShouldBe(96);
    }

    [Fact]
    public void Scaling_sets_the_dpi()
    {
        using var fixture = ConnectionDialogs.Create();
        fixture.Build().Dispose();

        var result = RenderDialogTool.RenderDialog(fixture.Path, ConnectionDialogs.Fixed, scaling: 150);

        using var report = JsonDocument.Parse(((TextContentBlock)result.Content[1]).Text);
        report.RootElement.GetProperty("dpi").GetInt32().ShouldBe(144);
    }

    [Fact]
    public void Writes_the_file_when_asked()
    {
        using var fixture = ConnectionDialogs.Create();
        fixture.Build().Dispose();
        var output = Path.Combine(Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}", "dialog.png");

        try
        {
            var result = RenderDialogTool.RenderDialog(fixture.Path, ConnectionDialogs.Fixed, outputPath: output);

            File.ReadAllBytes(output).ShouldBe(((ImageContentBlock)result.Content[0]).DecodedData.ToArray());
            ((TextContentBlock)result.Content[1]).Text.ShouldContain("dialog.png");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(output)!, recursive: true);
        }
    }

    [Theory]
    [InlineData(110, "scaling")]
    [InlineData(100, "NoSuchDlg")]
    public void Bad_arguments_are_explained(int scaling, string expected)
    {
        using var fixture = ConnectionDialogs.Create();
        fixture.Build().Dispose();
        var dialog = expected == "NoSuchDlg" ? "NoSuchDlg" : ConnectionDialogs.Fixed;

        Should.Throw<McpException>(() => RenderDialogTool.RenderDialog(fixture.Path, dialog, scaling: scaling))
            .Message.ShouldContain(expected == "NoSuchDlg" ? ConnectionDialogs.Broken : "scaling");
    }

    [Fact]
    public void Output_path_must_be_a_png()
    {
        using var fixture = ConnectionDialogs.Create();
        fixture.Build().Dispose();

        Should.Throw<McpException>(() =>
                RenderDialogTool.RenderDialog(fixture.Path, ConnectionDialogs.Fixed, outputPath: @"C:\temp\dialog.jpg"))
            .Message.ShouldContain(".png");
    }

    [Fact]
    public void Missing_file_and_aip_project_are_explained()
    {
        Should.Throw<McpException>(() => RenderDialogTool.RenderDialog(@"C:\no\such\package.msi", "WelcomeDlg"))
            .Message.ShouldContain("not found");
    }
}
