using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Lint;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Lint;

public sealed class DialogLinterTests
{
    private const string Dlg = "TestDlg";
    private const int VisibleEnabled = ControlAttributes.Visible | ControlAttributes.Enabled;

    [Fact]
    public void Broken_connection_dialog_reports_only_the_clipped_hint()
    {
        var findings = LintConnection(ConnectionDialogs.Broken);

        var finding = findings.ShouldHaveSingleItem();
        finding.Rule.ShouldBe("text-clipped");
        finding.Severity.ShouldBe(Severity.Error);
        finding.Control.ShouldBe("Hint");
        finding.SuggestedHeight.ShouldBe(20);
        finding.SuggestedWidth.ShouldBeNull();
        finding.Approximate.ShouldBeFalse();
        finding.Checks.ShouldNotBeNull().ShouldBe([
            new DpiCheck(96, 260, 26, 307, 24, false),
            new DpiCheck(120, 381, 32, 383, 30, false),
            new DpiCheck(144, 457, 38, 460, 36, false),
        ]);
        finding.Message.ShouldContain("wraps to 2 lines, 26 px, in a 24 px high text area; Windows shows 1 line ending in \"...\"");
    }

    [Fact]
    public void Fixed_connection_dialog_is_clean()
    {
        LintConnection(ConnectionDialogs.Fixed).ShouldBeEmpty();
    }

    // Calibration (docs/research/2026-10-11-control-calibration.md): the 110 px label is cut at 94 units, whole at 95.
    [Theory]
    [InlineData(94, true)]
    [InlineData(95, false)]
    public void Check_box_label_needs_its_width_plus_the_box(int width, bool clipped)
    {
        var findings = Lint(p => p.WithControl(Dlg, "Check", "CheckBox", 10, 10, width, 12, ConnectionDialogs.TrustLabel, property: "P"));

        findings.Any(f => f.Rule == "text-clipped" && f.Severity == Severity.Error).ShouldBe(clipped);
        if (clipped)
        {
            findings.Single().SuggestedWidth.ShouldBe(95);
        }
    }

    // Calibration: "Install now" (52 px) is cut at 43 units, whole at 44.
    [Theory]
    [InlineData(43, true)]
    [InlineData(44, false)]
    public void Push_button_label_needs_its_width_plus_margins(int width, bool clipped)
    {
        var findings = Lint(p => p.WithControl(Dlg, "Install", "PushButton", 10, 10, width, 17, "Install now"));

        findings.Any(f => f.Rule == "button-text-clipped" && f.Severity == Severity.Error).ShouldBe(clipped);
    }

    [Fact]
    public void NoWrap_text_that_is_too_long_is_truncated_and_gets_a_width_that_fits_everywhere()
    {
        var finding = Lint(p => p.WithControl(Dlg, "Line", "Text", 10, 10, 100, 12, ConnectionDialogs.Hint,
                attributes: VisibleEnabled | ControlAttributes.NoWrap))
            .ShouldHaveSingleItem();

        finding.Rule.ShouldBe("text-truncated");
        finding.Severity.ShouldBe(Severity.Error);
        var width = finding.SuggestedWidth.ShouldNotBeNull();
        foreach (var dpi in InstallerUnits.CheckedDpis)
        {
            var line = TextMeasurer.Measure(ConnectionDialogs.Hint, Tahoma8, dpi, 10000, MeasureOptions.NoWrap).Width;
            InstallerUnits.ToPixels(width, dpi).ShouldBeGreaterThanOrEqualTo(line);
        }

        InstallerUnits.CheckedDpis.ShouldContain(dpi =>
            InstallerUnits.ToPixels(width - 1, dpi) <
            TextMeasurer.Measure(ConnectionDialogs.Hint, Tahoma8, dpi, 10000, MeasureOptions.NoWrap).Width);
    }

    // Line height does not scale linearly: Segoe UI 9 pt is 15 px a line at 96 DPI but 20 px at 120 DPI. Three lines
    // fill a 34-unit control exactly at 96 DPI (45 of 45 px) and overflow it at 120 DPI (60 of 57 px).
    [Fact]
    public void Text_that_only_breaks_at_higher_scaling_is_a_warning()
    {
        var finding = Lint(p => p
                .WithTextStyle("Segoe9", "Segoe UI", 9)
                .WithControl(Dlg, "Hint", "Text", 10, 10, 105, 34, "{\\Segoe9}" + ConnectionDialogs.Hint))
            .ShouldHaveSingleItem();

        finding.Severity.ShouldBe(Severity.Warning);
        finding.Checks.ShouldNotBeNull()[0].ShouldBe(new DpiCheck(96, 140, 45, 140, 45, true));
        finding.Message.ShouldBe(
            "At 120 DPI (Segoe UI 9 pt), '" + ConnectionDialogs.Hint + "' wraps to 3 lines, 60 px, in a 57 px high " +
            "text area; Windows shows 2 lines ending in \"...\". Fails at 120, 144 DPI. " +
            "Smallest height that fits at 100, 125 and 150 %: 38 units (now 34).");
    }

    [Fact]
    public void Sunken_border_takes_space_from_the_text()
    {
        Lint(p => p.WithControl(Dlg, "Hint", "Text", 10, 10, 230, 20, ConnectionDialogs.Hint)).ShouldBeEmpty();

        Lint(p => p.WithControl(Dlg, "Hint", "Text", 10, 10, 230, 20, ConnectionDialogs.Hint,
                attributes: VisibleEnabled | ControlAttributes.Sunken))
            .ShouldHaveSingleItem().Rule.ShouldBe("text-clipped");
    }

    // Measuring "[ButtonText_Cancel]" as written would report a 56-unit Cancel button as clipped.
    [Fact]
    public void Property_references_are_measured_with_their_values()
    {
        var finding = Lint(p => p
                .WithProperty("ButtonText_Cancel", "Cancel")
                .WithProperty("ButtonText_Long", "This label is far too long for the button")
                .WithControl(Dlg, "Cancel", "PushButton", 10, 10, 56, 17, "[ButtonText_Cancel]")
                .WithControl(Dlg, "Long", "PushButton", 10, 30, 56, 17, "[ButtonText_Long]"))
            .ShouldHaveSingleItem();

        finding.Control.ShouldBe("Long");
        finding.LowerBound.ShouldBeFalse();
        finding.Message.ShouldContain("'This label is far too long for the button'");
        finding.Message.ShouldContain("values from the Property table");
    }

    [Fact]
    public void Property_references_without_a_value_count_as_empty_and_make_a_lower_bound()
    {
        var finding = Lint(p => p.WithControl(Dlg, "Welcome", "Text", 10, 10, 40, 12, "Welcome to the setup of [RUNTIME_NAME]"))
            .ShouldHaveSingleItem();

        finding.LowerBound.ShouldBeTrue();
        finding.Message.ShouldContain("'Welcome to the setup of '");
        finding.Message.ShouldContain("at least this long");
    }

    [Fact]
    public void Text_replaced_by_an_event_at_run_time_is_not_measured()
    {
        Lint(p => p
                .WithControl(Dlg, "Description", "Text", 10, 10, 40, 12, "PlaceholderThatIsMuchTooLongForTheControl")
                .WithEventMapping(Dlg, "Description", "SelectionDescription", "Text"))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Hidden_control_is_measured_only_when_a_condition_can_show_it()
    {
        const string tooLong = "Much too long for this label";
        Lint(p => p.WithControl(Dlg, "Never", "Text", 10, 10, 30, 12, tooLong, attributes: ControlAttributes.Enabled))
            .ShouldBeEmpty();

        Lint(p => p
                .WithControl(Dlg, "Later", "Text", 10, 10, 30, 12, tooLong, attributes: ControlAttributes.Enabled)
                .WithControlCondition(Dlg, "Later", "Show", "DETAILS = 1"))
            .ShouldHaveSingleItem().Control.ShouldBe("Later");
    }

    // A 10-unit check box with its label in a separate Text control: only the box shows.
    [Fact]
    public void Box_only_check_box_is_not_measured()
    {
        Lint(p => p.WithControl(Dlg, "Box", "CheckBox", 10, 10, 10, 12, "CheckBox1", property: "P")).ShouldBeEmpty();
    }

    [Fact]
    public void Wrapping_text_with_a_long_word_gets_the_width_of_that_word()
    {
        const string word = "Microsoft.NET.Workload.Sample.Manifest-10.0.100";
        var finding = Lint(p => p.WithControl(Dlg, "Title", "Text", 10, 10, 80, 30, $"Welcome to the {word} Setup Wizard"))
            .ShouldHaveSingleItem();

        finding.Message.ShouldContain("has a word");
        var width = finding.SuggestedWidth.ShouldNotBeNull();
        InstallerUnits.CheckedDpis.ShouldAllBe(dpi =>
            InstallerUnits.ToPixels(width, dpi) >= TextMeasurer.Measure(word, Tahoma8, dpi, 10000, MeasureOptions.NoWrap).Width);
        InstallerUnits.CheckedDpis.ShouldContain(dpi =>
            InstallerUnits.ToPixels(width - 1, dpi) < TextMeasurer.Measure(word, Tahoma8, dpi, 10000, MeasureOptions.NoWrap).Width);
    }

    [Fact]
    public void Missing_DefaultUIFont_is_reported_once_and_makes_text_checks_approximate()
    {
        var findings = Lint(
            p => p
                .WithControl(Dlg, "A", "Text", 10, 10, 40, 12, ConnectionDialogs.Hint)
                .WithControl(Dlg, "B", "Text", 10, 30, 40, 12, ConnectionDialogs.Hint),
            defaultUIFont: null);

        findings.Count(f => f.Rule == "default-ui-font-missing").ShouldBe(1);
        findings.Where(f => f.Control is not null).ShouldAllBe(f => f.Approximate && f.Font == "Segoe UI");
    }

    [Fact]
    public void Unknown_text_style_is_reported()
    {
        Lint(p => p.WithControl(Dlg, "Label", "Text", 10, 10, 100, 12, "{\\NoSuchStyle}Short"))
            .ShouldHaveSingleItem().Rule.ShouldBe("text-style-not-found");
    }

    [Fact]
    public void Font_that_is_not_installed_is_reported_once_with_its_substitute()
    {
        var findings = Lint(p => p
            .WithTextStyle("Odd", "No Such Font 4f7a", 8)
            .WithControl(Dlg, "A", "Text", 10, 10, 100, 12, "{\\Odd}Short")
            .WithControl(Dlg, "B", "Text", 10, 30, 100, 12, "{\\Odd}Also short"));

        var finding = findings.ShouldHaveSingleItem();
        finding.Rule.ShouldBe("font-not-installed");
        finding.Message.ShouldContain("No Such Font 4f7a");
        finding.Font.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Image_buttons_indirect_text_zero_sized_and_unmeasured_controls_are_skipped()
    {
        Lint(p => p
                .WithControl(Dlg, "Image", "PushButton", 10, 10, 10, 17, "Much too long for this",
                    attributes: VisibleEnabled | ControlAttributes.Bitmap)
                .WithControl(Dlg, "Indirect", "Text", 10, 30, 10, 12, "SOME_PROPERTY",
                    attributes: VisibleEnabled | ControlAttributes.Indirect)
                .WithControl(Dlg, "Edit", "Edit", 10, 50, 10, 18, "Much too long for this", property: "P")
                .WithControl(Dlg, "Hidden", "PushButton", 0, 0, 0, 0, "Cancel"))
            .ShouldBeEmpty();
    }

    private static readonly ResolvedFont Tahoma8 =
        new("Tahoma", 8, TextStyleBits.None, FontSource.DefaultUIFont, FontIssue.None);

    private static IReadOnlyList<Finding> LintConnection(string dialogName)
    {
        using var fixture = ConnectionDialogs.Create();
        using var database = fixture.Build();
        var package = DialogPackage.Load(database);

        return DialogLinter.Lint(package, [package.FindDialog(dialogName)!]);
    }

    private static IReadOnlyList<Finding> Lint(Func<TestPackage, TestPackage> controls, string? defaultUIFont = "DlgFont8")
    {
        var builder = new TestPackage().WithTextStyle("DlgFont8", "Tahoma", 8);
        if (defaultUIFont is not null)
        {
            builder = builder.WithProperty("DefaultUIFont", defaultUIFont);
        }

        using var fixture = controls(builder.WithDialog(Dlg, 370, 270, "A"));
        using var database = fixture.Build();
        var package = DialogPackage.Load(database);

        return DialogLinter.Lint(package, [package.FindDialog(Dlg)!]);
    }
}
