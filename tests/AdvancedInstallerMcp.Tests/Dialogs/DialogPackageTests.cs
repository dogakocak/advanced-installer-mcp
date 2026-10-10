using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Dialogs;

public sealed class DialogPackageTests
{
    [Fact]
    public void Loads_properties_text_styles_and_dialog_names()
    {
        using var fixture = ConnectionDialogs.Create();
        using var database = fixture.Build();

        var package = DialogPackage.Load(database);

        package.DefaultUIFont.ShouldBe("DlgFont8");
        package.TextStyles["DlgFontBold8"].ShouldBe(new TextStyle("DlgFontBold8", "Tahoma", 8, null, TextStyleBits.Bold));
        package.DialogNames.ShouldBe([ConnectionDialogs.Broken, ConnectionDialogs.Fixed]);
    }

    [Fact]
    public void Finds_a_dialog_with_its_controls()
    {
        using var fixture = ConnectionDialogs.Create();
        using var database = fixture.Build();

        var dialog = DialogPackage.Load(database).FindDialog(ConnectionDialogs.Broken).ShouldNotBeNull();

        dialog.Width.ShouldBe(370);
        dialog.Height.ShouldBe(270);
        dialog.Title.ShouldBe("[ProductName] Setup");
        dialog.FirstControl.ShouldBe("ServerEdit");
        dialog.DefaultControl.ShouldBe("Next");
        dialog.Controls.Count.ShouldBe(7);

        dialog.Controls.Single(c => c.Name == "Hint")
            .ShouldBe(new Control("Hint", "Text", 20, 60, 230, 18, 3, null, ConnectionDialogs.Hint, null));
        dialog.Controls.Single(c => c.Name == "TrustCheck")
            .ShouldBe(new Control("TrustCheck", "CheckBox", 20, 90, 125, 12, 3, "DB_TRUST", ConnectionDialogs.TrustLabel, "Next"));
    }

    [Fact]
    public void Unknown_dialog_is_null()
    {
        using var fixture = ConnectionDialogs.Create();
        using var database = fixture.Build();

        DialogPackage.Load(database).FindDialog("NoSuchDlg").ShouldBeNull();
    }

    [Fact]
    public void Package_without_ui_tables_loads_empty()
    {
        using var fixture = new TestPackage().WithoutUiTables().WithProperty("ProductName", "Sample");
        using var database = fixture.Build();

        var package = DialogPackage.Load(database);

        package.DialogNames.ShouldBeEmpty();
        package.TextStyles.ShouldBeEmpty();
        package.DefaultUIFont.ShouldBeNull();
        package.Properties["ProductName"].ShouldBe("Sample");
    }
}
