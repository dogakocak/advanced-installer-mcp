using System.Text.Json;
using AdvancedInstallerMcp.Mock;
using AdvancedInstallerMcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace AdvancedInstallerMcp.Tests.Tools;

public sealed class MockDialogToolTests
{
    private static readonly MockSpec Spec = new(
        new Dictionary<string, string> { ["MODE"] = "a" },
        null,
        [
            new MockDialogSpec("FirstDlg", [new MockControl("Mode", "RadioButtonGroup", 20, 60, 300, 30, Property: "MODE",
                Items: [new MockItem("a", "&First"), new MockItem("b", "&Second")])], BannerTitle: "One"),
            new MockDialogSpec("SecondDlg", [new MockControl("Host", "Edit", 20, 60, 200, 18, Property: "DB_HOST")], BannerTitle: "Two"),
        ]);

    [Fact]
    public void Returns_a_picture_per_page_and_one_report()
    {
        var result = MockDialogTool.MockDialog(Spec);

        result.Content.OfType<ImageContentBlock>().Count().ShouldBe(2);
        using var report = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
        report.RootElement.GetProperty("dialogs").GetArrayLength().ShouldBe(2);
        report.RootElement.GetProperty("findings").ValueKind.ShouldBe(JsonValueKind.Array);
        report.RootElement.GetProperty("approvalPage").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void One_dialog_when_asked()
    {
        MockDialogTool.MockDialog(Spec, dialog: "SecondDlg").Content.OfType<ImageContentBlock>().Count().ShouldBe(1);
    }

    [Fact]
    public void Unknown_dialog_names_the_pages()
    {
        Should.Throw<McpException>(() => MockDialogTool.MockDialog(Spec, dialog: "Nope")).Message.ShouldContain("FirstDlg");
    }

    [Fact]
    public void Invalid_spec_lists_every_problem()
    {
        var bad = Spec with { Dialogs = [new MockDialogSpec("BadDlg", [
            new MockControl("A", "Toggle", 0, 0, 10, 10),
            new MockControl("B", "Edit", 0, 20, 10, 10, Property: "lower")])] };

        var message = Should.Throw<McpException>(() => MockDialogTool.MockDialog(bad)).Message;
        message.ShouldContain("Toggle");
        message.ShouldContain("lower");
    }

    [Fact]
    public void Writes_pictures_and_the_approval_page()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}");
        try
        {
            var result = MockDialogTool.MockDialog(Spec, outputDir: dir);

            File.Exists(Path.Combine(dir, "FirstDlg.png")).ShouldBeTrue();
            File.Exists(Path.Combine(dir, "SecondDlg.png")).ShouldBeTrue();
            File.ReadAllText(Path.Combine(dir, "index.html")).ShouldContain("MODE=b");
            using var report = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
            report.RootElement.GetProperty("approvalPage").GetString().ShouldBe(Path.Combine(dir, "index.html"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Leaves_no_temporary_package_behind()
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "advinst-mock-*.msi").Length;

        MockDialogTool.MockDialog(Spec);

        Directory.GetFiles(Path.GetTempPath(), "advinst-mock-*.msi").Length.ShouldBe(before);
    }

    [Fact]
    public void Severity_is_written_as_a_name()
    {
        var noDefault = Spec with { Properties = null };

        var result = MockDialogTool.MockDialog(noDefault);

        using var report = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
        var findings = report.RootElement.GetProperty("findings");
        findings.GetArrayLength().ShouldBeGreaterThan(0);
        findings[0].GetProperty("severity").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Fact]
    public void A_null_spec_is_a_tool_error()
    {
        Should.Throw<McpException>(() => MockDialogTool.MockDialog(null!)).Message.ShouldContain("spec");
    }

    [Fact]
    public void A_relative_output_dir_is_refused()
    {
        Should.Throw<McpException>(() => MockDialogTool.MockDialog(Spec, outputDir: "out")).Message.ShouldContain("full path");
    }

    [Fact]
    public void Writer_and_file_errors_become_tool_errors_and_leave_no_temporary_package()
    {
        var file = Path.Combine(Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "not a folder");
        var before = Directory.GetFiles(Path.GetTempPath(), "advinst-mock-*.msi").Length;
        try
        {
            Should.Throw<McpException>(() => MockDialogTool.MockDialog(Spec, outputDir: file));

            Directory.GetFiles(Path.GetTempPath(), "advinst-mock-*.msi").Length.ShouldBe(before);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
