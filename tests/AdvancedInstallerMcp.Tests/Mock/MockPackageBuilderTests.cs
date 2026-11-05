using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Lint;
using AdvancedInstallerMcp.Mock;
using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Tests.Mock;

public sealed class MockPackageBuilderTests
{
    private static readonly MockSpec Spec = new(
        Properties: new Dictionary<string, string> { ["MODE"] = "a" },
        Dialogs:
        [new MockDialogSpec("PageDlg",
            [
                new MockControl("Mode", "RadioButtonGroup", 20, 60, 300, 30, Property: "MODE",
                    Items: [new MockItem("a", "&First"), new MockItem("b", "&Second")]),
                new MockControl("Host", "Edit", 20, 100, 200, 18, Property: "DB_HOST"),
            ],
            BannerTitle: "Connection", BannerText: "Where the database is.")]);

    [Fact]
    public void Writes_a_package_lint_dialog_accepts()
    {
        using var database = Write(Spec, out var path);
        try
        {
            var package = DialogPackage.Load(database);
            var dialog = package.FindDialog("PageDlg").ShouldNotBeNull();

            var findings = DialogLinter.Lint(package, [dialog]);
            findings.Where(f => f.Rule.StartsWith("tab-") || f.Rule == "not-in-tab-order" || f.Rule == "dialog-control-missing")
                .ShouldBeEmpty(string.Join(", ", dialog.Controls.Select(c => $"{c.Name}->{c.Next}")));
            dialog.Controls.ShouldContain(c => c.Name == WizardFrame.Load().DefaultControl);
            dialog.FirstControl.ShouldBe("Mode");
        }
        finally
        {
            database.Dispose();
            File.Delete(path);
        }
    }

    [Fact]
    public void Radio_buttons_without_a_position_stack()
    {
        var rows = MockPackageBuilder.Build(Spec);

        rows.RadioButtons.Select(b => (b.Value, b.Y, b.Width)).ShouldBe([("a", 0, 300), ("b", 15, 300)]);
    }

    [Fact]
    public void The_banner_shows_the_page_texts()
    {
        var frame = WizardFrame.Load();
        var rows = MockPackageBuilder.Build(Spec);

        rows.Controls.Single(c => c.Name == frame.BannerTitle).Text.ShouldEndWith("Connection");
        rows.Controls.Single(c => c.Name == frame.BannerText).Text.ShouldEndWith("Where the database is.");
        rows.Binaries.ShouldContain(b => b.Name == frame.BannerBinary);
    }

    [Fact]
    public void Frame_none_adds_nothing()
    {
        var spec = Spec with { Dialogs = [Spec.Dialogs[0] with { Frame = "none" }] };

        MockPackageBuilder.Build(spec).Controls.Select(c => c.Name).ShouldBe(["Mode", "Host"]);
    }

    [Fact]
    public void Identical_items_on_two_pages_are_written_once_and_the_package_writes()
    {
        MockControl Mode(string name) => new(name, "RadioButtonGroup", 20, 60, 300, 30, Property: "MODE",
            Items: [new MockItem("a", "&First"), new MockItem("b", "&Second")]);
        var spec = new MockSpec([new MockDialogSpec("OneDlg", [Mode("ModeOne")]), new MockDialogSpec("TwoDlg", [Mode("ModeTwo")])]);

        MockSpecValidator.Problems(spec).ShouldBeEmpty();
        MockPackageBuilder.Build(spec).RadioButtons.Count.ShouldBe(2);
        using var database = Write(spec, out var path);
        database.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void The_wizard_tab_chain_runs_through_the_body_and_the_frame()
    {
        var rows = MockPackageBuilder.Build(Spec);

        var chain = new List<string>();
        var name = "Next";
        do
        {
            chain.Add(name);
            name = rows.Controls.Single(c => c.Dialog == "PageDlg" && c.Name == name).Next!;
        }
        while (name != "Next");

        chain.ShouldBe(["Next", "Mode", "Host", "BottomLine", "Logo", "Cancel", "Back", "BannerBitmap", "Title", "Description", "BannerLine"]);
    }

    [Fact]
    public void Frame_none_body_controls_form_their_own_closed_cycle()
    {
        var spec = Spec with { Dialogs = [Spec.Dialogs[0] with { Frame = "none" }] };

        var rows = MockPackageBuilder.Build(spec);

        rows.Controls.Select(c => (c.Name, c.Next)).ShouldBe([("Mode", "Host"), ("Host", "Mode")]);
        rows.Dialogs.Single().FirstControl.ShouldBe("Mode");
    }

    private static MsiDatabase Write(MockSpec spec, out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"advinst-mcp-{Guid.NewGuid():N}.msi");
        PackageWriter.Write(path, MockPackageBuilder.Build(spec));
        return MsiDatabase.OpenReadOnly(path);
    }
}
