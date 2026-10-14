using System.Text.Json;
using AdvancedInstallerMcp.Tests.Fixtures;
using AdvancedInstallerMcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace AdvancedInstallerMcp.Tests.Tools;

[Collection(InstallSessionCollection.Name)]
public sealed class DriveDialogToolTests
{
    private static string Built(TestPackage fixture)
    {
        fixture.Build().Dispose();
        return fixture.Path;
    }

    private static JsonElement Report(CallToolResult result) =>
        JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement;

    [Fact]
    public void Returns_the_step_report_and_the_final_page()
    {
        using var fixture = ConnectionWizard.Create();

        var result = DriveDialogTool.DriveDialog(Built(fixture), [
            new DriveStepInput(Click: "Next"),
            new DriveStepInput(Set: new() { ["DB_SERVER"] = "sql01" }),
            new DriveStepInput(Click: "TestButton"),
        ], actionResults: new() { ["TestSql"] = new() { ["SQL_OK"] = "1" } });

        result.IsError.ShouldNotBe(true);
        result.Content[0].ShouldBeOfType<ImageContentBlock>().MimeType.ShouldBe("image/png");

        var report = Report(result);
        report.GetProperty("opening").GetProperty("firstPage").GetString().ShouldBe(ConnectionWizard.Welcome);
        report.GetProperty("dialog").GetString().ShouldBe(ConnectionWizard.Sql);
        report.GetProperty("steps").GetArrayLength().ShouldBe(3);
        report.GetProperty("steps")[2].GetProperty("controls")[0].GetProperty("control").GetString().ShouldBe("Next");

        var next = report.GetProperty("controls").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Next");
        next.GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Without_render_only_the_report_comes_back()
    {
        using var fixture = ConnectionWizard.Create();

        var result = DriveDialogTool.DriveDialog(Built(fixture), [], render: false);

        result.Content.ShouldHaveSingleItem().ShouldBeOfType<TextContentBlock>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_step_is_either_set_or_click(bool both)
    {
        using var fixture = ConnectionWizard.Create();
        var step = both ? new DriveStepInput(Set: new() { ["A"] = "1" }, Click: "Next") : new DriveStepInput();

        Should.Throw<McpException>(() => DriveDialogTool.DriveDialog(Built(fixture), [step]))
            .Message.ShouldContain("step 1");
    }

    [Fact]
    public void Driver_errors_come_back_as_explanations()
    {
        using var fixture = ConnectionWizard.Create();

        Should.Throw<McpException>(() => DriveDialogTool.DriveDialog(Built(fixture), [new DriveStepInput(Click: "Nope")]))
            .Message.ShouldContain("Nope");
    }

    [Fact]
    public void A_project_file_is_refused()
    {
        Should.Throw<McpException>(() => DriveDialogTool.DriveDialog(Path.Combine(Path.GetTempPath(), "x.aip"), []))
            .Message.ShouldContain("not found");
    }

    [Fact]
    public void A_database_that_is_not_an_install_package_is_explained()
    {
        using var fixture = new TestPackage().WithDialog("OnlyDlg", 100, 100, "X").WithControl("OnlyDlg", "X", "PushButton", 0, 0, 10, 10);

        Should.Throw<McpException>(() => DriveDialogTool.DriveDialog(Built(fixture), [], dialog: "OnlyDlg"))
            .Message.ShouldContain("session");
    }
}
