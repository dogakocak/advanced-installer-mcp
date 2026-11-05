using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Lint;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Lint;

// ADR 0009.
public sealed class UnattendedRulesTests
{
    private const string Dlg = "InputDlg";

    [Fact]
    public void Private_input_used_by_the_install_is_an_error()
    {
        var finding = Findings("Edit", "dbHost", p => UsedByInstall(p, "dbHost"))
            .ShouldHaveSingleItem();

        finding.Rule.ShouldBe("input-property-private");
        finding.Severity.ShouldBe(Severity.Error);
        finding.Control.ShouldBe("Box");
        finding.Message.ShouldContain("dbHost");
        finding.Message.ShouldContain("upper case");
    }

    [Fact]
    public void Private_input_only_the_page_uses_is_fine()
    {
        Findings("CheckBox", "IAgree", p => p).ShouldBeEmpty();
    }

    [Fact]
    public void Public_input_used_by_the_install_and_not_secure_is_an_error()
    {
        var finding = Findings("Edit", "DB_HOST", p => UsedByInstall(p, "DB_HOST")).ShouldHaveSingleItem();

        finding.Rule.ShouldBe("input-property-not-secure");
        finding.Severity.ShouldBe(Severity.Error);
        finding.Message.ShouldContain("SecureCustomProperties");
    }

    [Fact]
    public void Secure_input_is_fine()
    {
        Findings("Edit", "DB_HOST", p => UsedByInstall(p, "DB_HOST").WithProperty("SecureCustomProperties", "OLDPRODUCTS;DB_HOST"))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Directory_input_is_not_required_to_be_secure()
    {
        Findings("PathEdit", "APPDIR", p => UsedByInstall(p, "APPDIR").WithDirectory("APPDIR", "TARGETDIR", "App"))
            .ShouldBeEmpty();
    }

    // The probe's false positive: a UI-only setter does not make the property used by the install.
    [Fact]
    public void Input_read_only_by_a_UI_sequence_action_is_fine()
    {
        Findings("CheckBox", "TRUST_UI", p => p.WithCustomAction("Init", 51, "TRUST", "[TRUST_UI]").WithUiSequence("Init", 100))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Password_not_hidden_is_a_warning()
    {
        var finding = Findings("Edit", "DB_PASSWORD", p => p, attributes: 3 | ControlAttributes.Password)
            .ShouldHaveSingleItem();

        finding.Rule.ShouldBe("password-not-hidden");
        finding.Severity.ShouldBe(Severity.Warning);
        finding.Message.ShouldContain("MsiHiddenProperties");
    }

    [Fact]
    public void Hidden_password_is_fine()
    {
        Findings("Edit", "DB_PASSWORD", p => p.WithProperty("MsiHiddenProperties", "DB_PASSWORD"),
                attributes: 3 | ControlAttributes.Password)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Radio_group_without_a_default_is_a_warning()
    {
        var finding = Findings("RadioButtonGroup", "MODE", p => Radios(p, "MODE", 2)).ShouldHaveSingleItem();

        finding.Rule.ShouldBe("choice-without-default");
        finding.Severity.ShouldBe(Severity.Warning);
    }

    [Fact]
    public void Combo_without_a_default_is_info()
    {
        var finding = Findings("ComboBox", "MODE", p => Combos(p, "MODE", 6)).ShouldHaveSingleItem();

        finding.Rule.ShouldBe("choice-without-default");
        finding.Severity.ShouldBe(Severity.Info);
    }

    [Fact]
    public void Radio_group_with_many_buttons_suggests_a_list()
    {
        Findings("RadioButtonGroup", "MODE", p => Radios(p, "MODE", 6).WithProperty("MODE", "v1"))
            .ShouldHaveSingleItem().Rule.ShouldBe("control-choice");
    }

    [Fact]
    public void Drop_down_list_with_few_items_suggests_radio_buttons()
    {
        const int comboList = 0x00020000;
        var finding = Findings("ComboBox", "MODE", p => Combos(p, "MODE", 2).WithProperty("MODE", "v1"), attributes: 3 | comboList)
            .ShouldHaveSingleItem();

        finding.Rule.ShouldBe("control-choice");
        finding.Severity.ShouldBe(Severity.Info);
    }

    [Fact]
    public void Editable_combo_with_few_items_is_fine()
    {
        Findings("ComboBox", "MODE", p => Combos(p, "MODE", 2).WithProperty("MODE", "v1")).ShouldBeEmpty();
    }

    private static TestPackage UsedByInstall(TestPackage p, string property) =>
        p.WithCustomAction("UseIt", 51, "SINK", $"[{property}]").WithExecuteSequence("UseIt", 100);

    private static TestPackage Radios(TestPackage p, string property, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            p.WithRadioButton(property, i, $"v{i}", 0, (i - 1) * 15, 200, 15, $"Option {i}");
        }

        return p;
    }

    private static TestPackage Combos(TestPackage p, string property, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            p.WithComboItem(property, i, $"v{i}", $"Option {i}");
        }

        return p;
    }

    private static IReadOnlyList<Finding> Findings(string type, string property, Func<TestPackage, TestPackage> configure,
        int attributes = 3)
    {
        using var fixture = configure(new TestPackage()
            .WithDialog(Dlg, 370, 270, "Box")
            .WithControl(Dlg, "Box", type, 20, 20, 200, type == "RadioButtonGroup" ? 100 : 18,
                attributes: attributes, property: property, next: "Box", text: type == "CheckBox" ? "Label" : null));
        using var database = fixture.Build();
        var package = DialogPackage.Load(database);
        return UnattendedRules.Check(package, package.FindDialog(Dlg)!).ToList();
    }
}
