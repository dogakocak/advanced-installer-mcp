using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Lint;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Lint;

// ADR 0005. Every dialog here is 370 x 270 units with a closed tab cycle Next > Cancel > Next unless a test changes
// it, so each test sees only the findings of the rule it is about.
public sealed class LayoutRulesTests
{
    private const string Dlg = "TestDlg";
    private const int VisibleEnabled = ControlAttributes.Visible | ControlAttributes.Enabled;

    [Fact]
    public void Dialog_with_a_closed_tab_cycle_and_nothing_outside_is_clean()
    {
        Lint(p => p).ShouldBeEmpty();
    }

    [Fact]
    public void Control_partly_outside_the_dialog_is_an_error()
    {
        var finding = Lint(p => p.WithControl(Dlg, "Wide", "Edit", 300, 100, 80, 12, property: "P", next: "Next"),
                cancelNext: "Wide")
            .ShouldHaveSingleItem();

        finding.Rule.ShouldBe("control-out-of-bounds");
        finding.Severity.ShouldBe(Severity.Error);
        finding.Control.ShouldBe("Wide");
        finding.Message.ShouldContain("spans 300..380 x 100..112, the dialog is 370 x 270 units");
        finding.Message.ShouldContain("X + Width at most 370");
    }

    // Only what a Text control paints counts: a short label in a box that runs past the edge shows completely.
    [Fact]
    public void Text_box_past_the_edge_is_fine_while_its_text_stays_inside()
    {
        Lint(p => p.WithControl(Dlg, "Short", "Text", 300, 100, 80, 12, "Label")).ShouldBeEmpty();

        Lint(p => p.WithControl(Dlg, "Long", "Text", 300, 100, 80, 12, "A label much longer than the room left"))
            .ShouldContain(f => f.Rule == "control-out-of-bounds" && f.Control == "Long");
    }

    [Fact]
    public void Control_ending_exactly_at_the_edge_fits()
    {
        Lint(p => p
                .WithControl(Dlg, "Right", "Text", 290, 0, 80, 12, "Label")
                .WithControl(Dlg, "Bottom", "Text", 0, 258, 80, 12, "Label"))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Control_entirely_outside_the_dialog_is_a_warning()
    {
        var finding = Lint(p => p.WithControl(Dlg, "Parked", "Text", 400, 10, 50, 12, "Label"))
            .ShouldHaveSingleItem();

        finding.Rule.ShouldBe("control-off-dialog");
        finding.Severity.ShouldBe(Severity.Warning);
    }

    [Fact]
    public void Hidden_and_zero_sized_controls_are_not_checked_for_bounds_or_overlap()
    {
        Lint(p => p
                .WithControl(Dlg, "Hidden", "Text", 300, 100, 80, 12, "Label", attributes: ControlAttributes.Enabled)
                .WithControl(Dlg, "Zero", "Text", 400, 0, 0, 0, "Label")
                .WithControl(Dlg, "Under", "Text", 300, 100, 60, 12, "Label"))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Overlapping_controls_without_conditions_are_an_error()
    {
        var finding = Lint(p => p
                .WithControl(Dlg, "Server", "Edit", 20, 80, 200, 18, property: "SERVER", next: "Trust")
                .WithControl(Dlg, "Trust", "CheckBox", 20, 90, 125, 12, "Trust", property: "TRUST", next: "Next"),
                first: "Server", cancelNext: "Server")
            .ShouldHaveSingleItem();

        finding.Rule.ShouldBe("controls-overlap");
        finding.Severity.ShouldBe(Severity.Error);
        finding.Control.ShouldBe("Trust");
        // ServerEdit covers y 107..131 px, TrustCheck 120..136 px at 96 DPI.
        finding.Message.ShouldContain("at 96 DPI what they show shares 166 x 11 px");
    }

    [Fact]
    public void Controls_that_only_touch_do_not_overlap()
    {
        Lint(p => p
                .WithControl(Dlg, "Above", "Text", 20, 80, 200, 10, "Above")
                .WithControl(Dlg, "Below", "Text", 20, 90, 200, 10, "Below")
                .WithControl(Dlg, "Right", "Text", 220, 80, 50, 10, "Right"))
            .ShouldBeEmpty();
    }

    // Standard WiX DiskCostDlg: the 50-unit text box reaches 3 units into the list below, its two lines do not.
    [Fact]
    public void Text_box_that_reaches_another_control_is_fine_while_its_text_does_not()
    {
        Lint(p => p
                .WithControl(Dlg, "Text", "Text", 20, 53, 330, 50,
                    "The highlighted volumes do not have enough disk space available for the currently selected features.")
                .WithControl(Dlg, "VolumeList", "VolumeCostList", 20, 100, 330, 120))
            .ShouldBeEmpty();
    }

    // Height of a combo box includes its dropped-down list (standard BrowseDlg: DirectoryCombo 80 units high above
    // the DirectoryList).
    [Fact]
    public void Combo_box_overlaps_only_with_its_closed_box()
    {
        Lint(p => p
                .WithControl(Dlg, "Combo", "DirectoryCombo", 70, 55, 220, 80, property: "DIR", next: "List")
                .WithControl(Dlg, "List", "DirectoryList", 25, 83, 320, 98, property: "DIR", next: "Next"),
                first: "Combo", cancelNext: "Combo")
            .ShouldBeEmpty();
    }

    // MaintenanceTypeDlg: icons and descriptions sit between the buttons of a tall radio button group.
    [Fact]
    public void Radio_button_group_overlaps_only_with_its_buttons()
    {
        var findings = Lint(p => p
                .WithRadioButton("MODE", 1, "Repair", 60, 0, 200, 15, "&Repair")
                .WithRadioButton("MODE", 2, "Remove", 60, 60, 200, 15, "Re&move")
                .WithControl(Dlg, "Group", "RadioButtonGroup", 20, 55, 290, 120, property: "MODE", next: "Next")
                .WithControl(Dlg, "RepairText", "Text", 80, 72, 260, 20, "Repairs errors in the installation.")
                .WithControl(Dlg, "RepairIcon", "Icon", 35, 55, 24, 24, "RepairIco")
                .WithControl(Dlg, "Clash", "Text", 80, 117, 200, 12, "Sits on the Remove button"),
                first: "Group", cancelNext: "Group");

        findings.ShouldHaveSingleItem().Control.ShouldBe("Clash");
    }

    [Fact]
    public void Same_text_a_pixel_apart_is_a_shadow_not_an_overlap()
    {
        Lint(p => p
                .WithControl(Dlg, "Branding2", "Text", 3, 228, 50, 13, "Contoso")
                .WithControl(Dlg, "Branding1", "Text", 4, 229, 50, 13, "Contoso"))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Stacked_buttons_of_an_error_dialog_are_not_an_overlap()
    {
        Lint(p => p
                .WithControl(Dlg, "O", "PushButton", 100, 80, 56, 17, "OK")
                .WithControl(Dlg, "C", "PushButton", 100, 80, 56, 17, "Cancel"),
                attributes: 3 | 0x00010000)
            .ShouldBeEmpty();
    }

    // Advanced Installer's own setup: text on the logo bitmap, a group box around a check box, a line under the title.
    [Fact]
    public void Background_and_frame_controls_may_be_overlapped()
    {
        Lint(p => p
                .WithControl(Dlg, "Banner", "Bitmap", 0, 0, 370, 44, "BannerBmp")
                .WithControl(Dlg, "Title", "Text", 15, 6, 200, 15, "Title")
                .WithControl(Dlg, "Line", "Line", 0, 44, 370, 0)
                .WithControl(Dlg, "Frame", "GroupBox", 10, 60, 300, 60, "Options")
                .WithControl(Dlg, "Empty", "Text", 10, 60, 300, 60, "")
                .WithControl(Dlg, "Label", "Text", 20, 80, 200, 12, "Inside the frame"))
            .ShouldBeEmpty();
    }

    // UpgradeDlg of Advanced Installer 24.0: Question and QuestionReplace share one rectangle, and complementary Hide
    // conditions keep exactly one of them on screen.
    [Fact]
    public void Two_conditioned_controls_in_one_place_are_not_reported()
    {
        Lint(p => p
                .WithControl(Dlg, "Question", "Text", 20, 100, 300, 30, "Search for older versions?")
                .WithControl(Dlg, "QuestionReplace", "Text", 20, 100, 300, 30, "The older version must be removed.")
                .WithControlCondition(Dlg, "Question", "Hide", "AI_REPLACE_PRODUCTS")
                .WithControlCondition(Dlg, "QuestionReplace", "Hide", "NOT AI_REPLACE_PRODUCTS"))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Overlap_with_a_control_that_a_condition_shows_is_a_warning()
    {
        var finding = Lint(p => p
                .WithControl(Dlg, "Hint", "Text", 20, 100, 300, 12, "Enter the server name.")
                .WithControl(Dlg, "Error", "Text", 20, 105, 300, 12, "The connection failed.",
                    attributes: ControlAttributes.Enabled)
                .WithControlCondition(Dlg, "Error", "Show", "CONNECTION_FAILED"))
            .ShouldHaveSingleItem();

        finding.Rule.ShouldBe("controls-overlap");
        finding.Severity.ShouldBe(Severity.Warning);
        finding.Message.ShouldContain("whenever a ControlCondition shows 'Error'");
    }

    [Fact]
    public void Missing_dialog_controls_are_errors()
    {
        var findings = Lint(p => p, first: "Nope", defaultControl: "Gone", cancelControl: "Lost");

        findings.Where(f => f.Rule == "dialog-control-missing").Select(f => f.Message).ShouldBe([
            "The dialog's Control_First is 'Nope', which is not a control of this dialog.",
            "The dialog's Control_Default is 'Gone', which is not a control of this dialog.",
            "The dialog's Control_Cancel is 'Lost', which is not a control of this dialog.",
        ]);
    }

    [Fact]
    public void Control_Next_naming_no_control_is_an_error()
    {
        var findings = Lint(p => p, cancelNext: "Nowhere");

        findings.ShouldContain(f => f.Rule == "tab-next-missing" && f.Control == "Cancel" && f.Severity == Severity.Error);
    }

    [Fact]
    public void Tab_chain_that_ends_is_open()
    {
        var finding = Lint(p => p.WithControl(Dlg, "Edit", "Edit", 20, 80, 200, 18, property: "P"),
                cancelNext: "Edit", first: "Edit")
            .Where(f => f.Rule == "tab-chain-open")
            .ShouldHaveSingleItem();

        finding.Severity.ShouldBe(Severity.Error);
        finding.Control.ShouldBe("Edit");
        finding.Message.ShouldStartWith("'Cancel' passes the focus to 'Edit', which has no Control_Next");
    }

    [Fact]
    public void Focusable_control_off_the_cycle_is_not_in_tab_order()
    {
        var finding = Lint(p => p.WithControl(Dlg, "Browse", "PushButton", 20, 80, 56, 17, "Browse"))
            .ShouldHaveSingleItem();

        finding.Rule.ShouldBe("not-in-tab-order");
        finding.Severity.ShouldBe(Severity.Warning);
        finding.Control.ShouldBe("Browse");
    }

    [Fact]
    public void Static_text_disabled_and_hidden_controls_may_stay_off_the_cycle()
    {
        Lint(p => p
                .WithControl(Dlg, "Label", "Text", 20, 60, 200, 12, "Server:")
                .WithControl(Dlg, "Disabled", "PushButton", 20, 80, 56, 17, "Later", attributes: ControlAttributes.Visible)
                .WithControl(Dlg, "Hidden", "PushButton", 100, 80, 56, 17, "Never", attributes: ControlAttributes.Enabled))
            .ShouldBeEmpty();
    }

    // Microsoft recommends the Text label of the first input as Control_First, for screen readers; it leads into the
    // cycle without being on it.
    [Fact]
    public void Control_First_may_be_a_label_that_leads_into_the_cycle()
    {
        Lint(p => p
                .WithControl(Dlg, "Label", "Text", 20, 60, 200, 12, "&Server:", next: "Server")
                .WithControl(Dlg, "Server", "Edit", 20, 80, 200, 18, property: "SERVER", next: "Next"),
                first: "Label", cancelNext: "Server")
            .ShouldBeEmpty();
    }

    [Fact]
    public void Two_separate_cycles_split_the_tab_order()
    {
        var finding = Lint(p => p
                .WithControl(Dlg, "A", "Edit", 20, 80, 200, 18, property: "A", next: "B")
                .WithControl(Dlg, "B", "Edit", 20, 100, 200, 18, property: "B", next: "A"))
            .ShouldHaveSingleItem();

        finding.Rule.ShouldBe("tab-chain-split");
        finding.Message.ShouldContain("2 separate cycles (A > B; Cancel > Next)");
    }

    [Fact]
    public void Error_dialog_skips_the_tab_checks()
    {
        Lint(p => p, first: "Nope", attributes: 3 | 0x00010000).ShouldBeEmpty();
    }

    [Fact]
    public void Embedded_ui_is_reported_first_and_once()
    {
        using var fixture = new TestPackage()
            .WithEmbeddedUi("support.dll", 0)
            .WithEmbeddedUi("ui.dll", 3)
            .WithDialog(Dlg, 370, 270, "Next", cancelControl: "Next")
            .WithControl(Dlg, "Next", "PushButton", 236, 243, 56, 17, null, next: "Next")
            .WithControl(Dlg, "Wide", "Edit", 300, 100, 80, 12, property: "P");
        using var database = fixture.Build();
        var package = DialogPackage.Load(database);

        package.HasEmbeddedUi.ShouldBeTrue();
        var findings = DialogLinter.Lint(package, [package.FindDialog(Dlg)!]);
        findings.Select(f => f.Rule).ShouldBe(["embedded-ui", "control-out-of-bounds", "not-in-tab-order"]);
        findings[0].Severity.ShouldBe(Severity.Warning);
    }

    [Fact]
    public void Embedded_ui_support_files_alone_are_not_an_embedded_ui()
    {
        using var fixture = new TestPackage().WithEmbeddedUi("resources.pri", 0);
        using var database = fixture.Build();

        DialogPackage.Load(database).HasEmbeddedUi.ShouldBeFalse();
    }

    private static IReadOnlyList<Finding> Lint(
        Func<TestPackage, TestPackage> controls, string first = "Next", string? cancelNext = "Next",
        string? defaultControl = "Next", string? cancelControl = "Cancel", int attributes = 3)
    {
        var builder = new TestPackage()
            .WithProperty("DefaultUIFont", "DlgFont8")
            .WithTextStyle("DlgFont8", "Tahoma", 8)
            .WithDialog(Dlg, 370, 270, first, defaultControl: defaultControl, cancelControl: cancelControl,
                attributes: attributes)
            .WithControl(Dlg, "Next", "PushButton", 236, 243, 56, 17, "&Next >", attributes: VisibleEnabled, next: "Cancel")
            .WithControl(Dlg, "Cancel", "PushButton", 304, 243, 56, 17, "Cancel", attributes: VisibleEnabled, next: cancelNext);

        using var fixture = controls(builder);
        using var database = fixture.Build();
        var package = DialogPackage.Load(database);

        return DialogLinter.Lint(package, [package.FindDialog(Dlg)!]);
    }
}
