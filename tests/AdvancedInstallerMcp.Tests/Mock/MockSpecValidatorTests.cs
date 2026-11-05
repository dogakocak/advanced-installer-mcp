using AdvancedInstallerMcp.Mock;

namespace AdvancedInstallerMcp.Tests.Mock;

// ADR 0008 "Validation".
public sealed class MockSpecValidatorTests
{
    private static MockSpec Page(params MockControl[] controls) => new([new MockDialogSpec("PageDlg", controls)]);

    [Fact]
    public void A_plain_page_is_valid()
    {
        MockSpecValidator.Problems(Page(
                new MockControl("Host", "Edit", 20, 60, 200, 18, Property: "DB_HOST"),
                new MockControl("Mode", "RadioButtonGroup", 20, 90, 200, 40, Property: "MODE",
                    Items: [new MockItem("a", "A"), new MockItem("b", "B")])))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_control_type_is_refused_with_the_allowed_list()
    {
        var problem = MockSpecValidator.Problems(Page(new MockControl("Toggle", "ToggleSwitch", 20, 60, 40, 18)))
            .ShouldHaveSingleItem();

        problem.ShouldContain("ToggleSwitch");
        problem.ShouldContain("RadioButtonGroup");
    }

    [Fact]
    public void Lower_case_input_property_is_refused()
    {
        MockSpecValidator.Problems(Page(new MockControl("Host", "Edit", 20, 60, 200, 18, Property: "dbHost")))
            .ShouldHaveSingleItem().ShouldContain("upper case");
    }

    [Fact]
    public void Names_must_be_identifiers()
    {
        MockSpecValidator.Problems(Page(new MockControl("Host Name", "Edit", 20, 60, 200, 18, Property: "HOST")))
            .ShouldHaveSingleItem().ShouldContain("Host Name");
    }

    [Fact]
    public void A_body_control_may_not_reuse_a_frame_name()
    {
        var next = WizardFrame.Load().DefaultControl;

        MockSpecValidator.Problems(Page(new MockControl(next, "PushButton", 20, 60, 56, 17, Text: "Go")))
            .ShouldHaveSingleItem().ShouldContain(next);
    }

    [Fact]
    public void Items_only_on_list_controls_and_required_on_radio_groups()
    {
        var problems = MockSpecValidator.Problems(Page(
            new MockControl("Host", "Edit", 20, 60, 200, 18, Property: "HOST", Items: [new MockItem("x")]),
            new MockControl("Mode", "RadioButtonGroup", 20, 90, 200, 40, Property: "MODE")));

        problems.Count.ShouldBe(2);
        problems.ShouldContain(p => p.Contains("Host") && p.Contains("only RadioButtonGroup, ComboBox and ListBox take items"));
        problems.ShouldContain(p => p.Contains("Mode") && p.Contains("needs its buttons in 'items'"));
    }

    [Fact]
    public void Condition_must_name_a_control_and_a_known_action()
    {
        var spec = new MockSpec([new MockDialogSpec("PageDlg",
            [new MockControl("Host", "Edit", 20, 60, 200, 18, Property: "HOST")],
            Conditions: [new MockCondition("Nope", "Disable", "1"), new MockCondition("Host", "Grey", "1")])]);

        var problems = MockSpecValidator.Problems(spec);

        problems.Count.ShouldBe(2);
        problems.ShouldContain(p => p.Contains("'Nope'") && p.Contains("not on the page"));
        problems.ShouldContain(p => p.Contains("'Grey'") && p.Contains("condition action"));
    }

    [Fact]
    public void Duplicate_dialogs_and_an_unknown_frame_are_refused()
    {
        var spec = new MockSpec([
            new MockDialogSpec("PageDlg", []),
            new MockDialogSpec("PageDlg", [], Frame: "fancy")]);

        var problems = MockSpecValidator.Problems(spec);

        problems.Count.ShouldBe(2);
        problems.ShouldContain(p => p.Contains("Dialog 'PageDlg' appears 2 times"));
        problems.ShouldContain(p => p.Contains("frame 'fancy'"));
    }

    [Fact]
    public void Every_problem_on_different_dialogs_is_listed()
    {
        var spec = new MockSpec([
            new MockDialogSpec("OneDlg", [new MockControl("A", "ToggleSwitch", 0, 60, 10, 10)]),
            new MockDialogSpec("TwoDlg", [new MockControl("B", "Edit", 0, 60, 10, 10, Property: "lower")]),
            new MockDialogSpec("Bad Name", [], Frame: "fancy")]);

        var problems = MockSpecValidator.Problems(spec);

        problems.Count.ShouldBe(4);
        problems.ShouldContain(p => p.Contains("ToggleSwitch"));
        problems.ShouldContain(p => p.Contains("upper case"));
        problems.ShouldContain(p => p.Contains("Bad Name") && p.Contains("identifier"));
        problems.ShouldContain(p => p.Contains("fancy"));
    }

    [Fact]
    public void Unknown_type_still_gets_the_property_and_items_checks()
    {
        var problems = MockSpecValidator.Problems(Page(
            new MockControl("Odd", "ToggleSwitch", 0, 60, 10, 10, Property: "bad name", Items: [new MockItem("x")])));

        problems.ShouldContain(p => p.Contains("ToggleSwitch"));
        problems.ShouldContain(p => p.Contains("bad name") && p.Contains("identifier"));
    }

    [Fact]
    public void A_trailing_newline_is_not_an_identifier()
    {
        MockSpecValidator.Problems(Page(new MockControl("Host\n", "Text", 0, 60, 10, 10)))
            .ShouldHaveSingleItem().ShouldContain("identifier");
    }

    [Fact]
    public void Null_dialogs_give_a_problem()
    {
        MockSpecValidator.Problems(new MockSpec(null!)).ShouldHaveSingleItem().ShouldContain("dialogs");
    }

    [Fact]
    public void Null_elements_are_listed_and_skipped()
    {
        var spec = new MockSpec(TextStyles: [null!], Dialogs: [
            null!,
            new MockDialogSpec("PageDlg", [null!, new MockControl("Mode", "RadioButtonGroup", 0, 60, 10, 10,
                Property: "MODE", Items: [null!, new MockItem(null!)])],
                Conditions: [null!])]);

        var problems = MockSpecValidator.Problems(spec);

        problems.Count.ShouldBe(6);
        problems.ShouldContain(p => p.Contains("text style entry is null"));
        problems.ShouldContain(p => p.Contains("dialog entry is null"));
        problems.ShouldContain(p => p.Contains("control entry is null"));
        problems.ShouldContain(p => p.Contains("item entry is null"));
        problems.ShouldContain(p => p.Contains("item has no value"));
        problems.ShouldContain(p => p.Contains("condition entry is null"));
    }

    [Fact]
    public void Null_names_types_and_controls_are_problems_not_exceptions()
    {
        var spec = new MockSpec(TextStyles: [new MockTextStyle(null!, "Tahoma", 8)], Dialogs: [
            new MockDialogSpec(null!, null!),
            new MockDialogSpec("PageDlg", [
                new MockControl(null!, "Text", 0, 60, 10, 10),
                new MockControl("Typeless", null!, 0, 60, 10, 10)])]);

        var problems = MockSpecValidator.Problems(spec);

        problems.Count(p => p.Contains("missing name")).ShouldBe(3);
        problems.ShouldContain(p => p.Contains("'controls' is missing"));
        problems.ShouldContain(p => p.Contains("Typeless") && p.Contains("no type"));
    }

    [Fact]
    public void A_null_frame_means_wizard()
    {
        var dialog = new MockDialogSpec("PageDlg", [new MockControl("Host", "Edit", 0, 60, 10, 10, Property: "HOST")]) { Frame = null! };
        MockSpecValidator.Problems(new MockSpec([dialog])).ShouldBeEmpty();

        var clash = new MockDialogSpec("PageDlg", [new MockControl(WizardFrame.Load().DefaultControl, "Text", 0, 60, 10, 10)]) { Frame = null! };
        MockSpecValidator.Problems(new MockSpec([clash])).ShouldHaveSingleItem().ShouldContain("wizard frame");
    }

    [Fact]
    public void Frame_none_allows_a_frame_control_name()
    {
        var spec = new MockSpec([new MockDialogSpec("PageDlg",
            [new MockControl(WizardFrame.Load().DefaultControl, "PushButton", 0, 60, 56, 17, Text: "Go")], Frame: "none")]);

        MockSpecValidator.Problems(spec).ShouldBeEmpty();
    }

    [Fact]
    public void Empty_or_null_property_values_are_refused()
    {
        var spec = new MockSpec(Properties: new Dictionary<string, string> { ["A_EMPTY"] = "", ["B_NULL"] = null! }, Dialogs:
            [new MockDialogSpec("PageDlg", [new MockControl("Host", "Edit", 20, 60, 200, 18, Property: "HOST")])]);

        var problems = MockSpecValidator.Problems(spec);

        problems.Count.ShouldBe(2);
        problems.ShouldContain(p => p.Contains("A_EMPTY") && p.Contains("empty"));
        problems.ShouldContain(p => p.Contains("B_NULL") && p.Contains("empty"));
    }

    [Fact]
    public void An_empty_item_value_is_refused()
    {
        MockSpecValidator.Problems(Page(new MockControl("Mode", "RadioButtonGroup", 20, 90, 200, 40, Property: "MODE",
                Items: [new MockItem("")])))
            .ShouldHaveSingleItem().ShouldContain("item");
    }

    [Fact]
    public void An_empty_or_null_face_name_is_refused()
    {
        var spec = new MockSpec(TextStyles: [new MockTextStyle("OneFont", "", 8), new MockTextStyle("TwoFont", null!, 8)], Dialogs:
            [new MockDialogSpec("PageDlg", [])]);

        var problems = MockSpecValidator.Problems(spec);

        problems.Count.ShouldBe(2);
        problems.ShouldContain(p => p.Contains("OneFont") && p.Contains("face name"));
        problems.ShouldContain(p => p.Contains("TwoFont") && p.Contains("face name"));
    }

    [Fact]
    public void An_empty_or_null_condition_text_is_refused()
    {
        var spec = new MockSpec([new MockDialogSpec("PageDlg",
            [new MockControl("Host", "Edit", 20, 60, 200, 18, Property: "HOST")],
            Conditions: [new MockCondition("Host", "Hide", ""), new MockCondition("Host", "Show", null!)])]);

        var problems = MockSpecValidator.Problems(spec);

        problems.Count.ShouldBe(2);
        problems.ShouldAllBe(p => p.Contains("condition has no text"));
    }

    [Fact]
    public void Two_identical_condition_rows_are_refused()
    {
        var spec = new MockSpec([new MockDialogSpec("PageDlg",
            [new MockControl("Host", "Edit", 20, 60, 200, 18, Property: "HOST")],
            Conditions: [new MockCondition("Host", "Hide", "X=1"), new MockCondition("Host", "Hide", "X=1"), new MockCondition("Host", "Hide", "X=2")])]);

        MockSpecValidator.Problems(spec).ShouldHaveSingleItem().ShouldContain("twice");
    }

    [Fact]
    public void Frame_none_without_controls_is_refused()
    {
        MockSpecValidator.Problems(new MockSpec([new MockDialogSpec("PageDlg", [], Frame: "none")]))
            .ShouldHaveSingleItem().ShouldContain("no controls");
    }

    [Fact]
    public void No_dialogs_is_refused()
    {
        MockSpecValidator.Problems(new MockSpec([])).ShouldHaveSingleItem().ShouldContain("nothing to draw");
    }

    [Fact]
    public void Dialog_names_that_differ_only_in_case_are_refused()
    {
        var spec = new MockSpec([new MockDialogSpec("PageDlg", []), new MockDialogSpec("pagedlg", [])]);

        MockSpecValidator.Problems(spec).ShouldHaveSingleItem().ShouldContain("differ only in case");
    }

    [Fact]
    public void The_same_items_property_with_different_items_is_refused_naming_both_places()
    {
        var spec = new MockSpec([
            new MockDialogSpec("OneDlg", [new MockControl("Mode", "RadioButtonGroup", 20, 60, 200, 40, Property: "MODE",
                Items: [new MockItem("a"), new MockItem("b")])]),
            new MockDialogSpec("TwoDlg", [new MockControl("Mode2", "RadioButtonGroup", 20, 60, 200, 40, Property: "MODE",
                Items: [new MockItem("a"), new MockItem("c")])])]);

        var problem = MockSpecValidator.Problems(spec).ShouldHaveSingleItem();

        problem.ShouldContain("OneDlg.Mode");
        problem.ShouldContain("TwoDlg.Mode2");
    }

    [Fact]
    public void The_same_items_property_with_identical_items_is_allowed()
    {
        var spec = new MockSpec([
            new MockDialogSpec("OneDlg", [new MockControl("Mode", "RadioButtonGroup", 20, 60, 200, 40, Property: "MODE",
                Items: [new MockItem("a", "A"), new MockItem("b", "B")])]),
            new MockDialogSpec("TwoDlg", [new MockControl("Mode2", "RadioButtonGroup", 20, 60, 200, 40, Property: "MODE",
                Items: [new MockItem("a", "A"), new MockItem("b", "B")])])]);

        MockSpecValidator.Problems(spec).ShouldBeEmpty();
    }

    [Fact]
    public void A_radio_group_and_a_combo_on_one_property_do_not_clash()
    {
        var spec = new MockSpec([new MockDialogSpec("OneDlg", [
            new MockControl("Mode", "RadioButtonGroup", 20, 60, 200, 40, Property: "MODE", Items: [new MockItem("a")]),
            new MockControl("Mode2", "ComboBox", 20, 120, 200, 18, Property: "MODE", Items: [new MockItem("b")])])]);

        MockSpecValidator.Problems(spec).ShouldBeEmpty();
    }
}
