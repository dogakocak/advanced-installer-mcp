using AdvancedInstallerMcp.Tests.Fixtures;
using AdvancedInstallerMcp.Tools;
using ModelContextProtocol;

namespace AdvancedInstallerMcp.Tests.Tools;

public sealed class LintDialogToolTests
{
    [Fact]
    public void Without_a_dialog_name_checks_every_dialog()
    {
        using var fixture = ConnectionDialogs.Create();
        fixture.Build().Dispose();

        var report = LintDialogTool.LintDialog(fixture.Path);

        report.Dialogs.ShouldBe([ConnectionDialogs.Broken, ConnectionDialogs.Fixed]);
        report.Findings.ShouldHaveSingleItem().Dialog.ShouldBe(ConnectionDialogs.Broken);
    }

    [Fact]
    public void Checks_one_dialog_by_name()
    {
        using var fixture = ConnectionDialogs.Create();
        fixture.Build().Dispose();

        LintDialogTool.LintDialog(fixture.Path, ConnectionDialogs.Fixed).Findings.ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_dialog_lists_the_dialogs_there_are()
    {
        using var fixture = ConnectionDialogs.Create();
        fixture.Build().Dispose();

        Should.Throw<McpException>(() => LintDialogTool.LintDialog(fixture.Path, "NoSuchDlg"))
            .Message.ShouldContain(ConnectionDialogs.Broken);
    }

    [Fact]
    public void Missing_file_and_aip_project_are_explained()
    {
        Should.Throw<McpException>(() => LintDialogTool.LintDialog(@"C:\no\such\package.msi"))
            .Message.ShouldContain("not found");

        var aip = Path.Combine(Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}.aip");
        File.WriteAllText(aip, "<DOCUMENT />");
        try
        {
            Should.Throw<McpException>(() => LintDialogTool.LintDialog(aip)).Message.ShouldContain("Build the project first");
        }
        finally
        {
            File.Delete(aip);
        }
    }

    [Fact]
    public void A_file_that_is_not_an_MSI_is_explained()
    {
        var path = Path.Combine(Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}.msi");
        File.WriteAllText(path, "not a package");
        try
        {
            Should.Throw<McpException>(() => LintDialogTool.LintDialog(path)).Message.ShouldContain("Not a readable MSI");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
