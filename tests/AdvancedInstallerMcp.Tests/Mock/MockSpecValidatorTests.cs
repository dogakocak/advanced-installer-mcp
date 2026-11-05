using AdvancedInstallerMcp.Mock;

namespace AdvancedInstallerMcp.Tests.Mock;

// ADR 0008 "Validation".
public sealed class MockSpecValidatorTests
{
    private static MockSpec Page(params MockControl[] controls) => new(null, null, [new MockDialogSpec("PageDlg", controls)]);

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
    }

    [Fact]
    public void Condition_must_name_a_control_and_a_known_action()
    {
        var spec = new MockSpec(null, null, [new MockDialogSpec("PageDlg",
            [new MockControl("Host", "Edit", 20, 60, 200, 18, Property: "HOST")],
            Conditions: [new MockCondition("Nope", "Disable", "1"), new MockCondition("Host", "Grey", "1")])]);

        MockSpecValidator.Problems(spec).Count.ShouldBe(2);
    }

    [Fact]
    public void Duplicate_dialogs_and_an_unknown_frame_are_refused()
    {
        var spec = new MockSpec(null, null, [
            new MockDialogSpec("PageDlg", []),
            new MockDialogSpec("PageDlg", [], Frame: "fancy")]);

        MockSpecValidator.Problems(spec).Count.ShouldBe(2);
    }
}
