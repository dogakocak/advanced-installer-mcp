using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Render;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Render;

public sealed class DialogStateTests
{
    private const string Dlg = "UpgradeDlg";

    // Like Advanced Installer's UpgradeDlg: two texts in one place, ControlCondition hides one or the other.
    private static TestPackage StackedQuestions() =>
        new TestPackage()
            .WithProperty("DefaultUIFont", "DlgFont8")
            .WithTextStyle("DlgFont8", "Tahoma", 8)
            .WithDialog(Dlg, 370, 270, "Next", defaultControl: "Next")
            .WithControl(Dlg, "Question", "Text", 20, 60, 330, 20, "Remove older versions?")
            .WithControl(Dlg, "QuestionReplace", "Text", 20, 60, 330, 20, "Replace the installed version?")
            .WithControl(Dlg, "Next", "PushButton", 236, 243, 56, 17, "Next", next: "Back")
            .WithControl(Dlg, "Back", "PushButton", 170, 243, 56, 17, "Back", attributes: 1, next: "Next")
            .WithControlCondition(Dlg, "Question", "Hide", "AI_REPLACE")
            .WithControlCondition(Dlg, "QuestionReplace", "Hide", "NOT AI_REPLACE");

    private static DialogState Evaluate(TestPackage fixture, IReadOnlyDictionary<string, string?>? properties = null)
    {
        using var database = fixture.Build();
        var package = DialogPackage.Load(database);
        return DialogState.Evaluate(package, package.FindDialog(Dlg)!, properties ?? new Dictionary<string, string?>());
    }

    private static IEnumerable<string> VisibleNames(DialogState state) =>
        state.Controls.Where(c => c.Visible).Select(c => c.Control.Name);

    [Fact]
    public void Conditions_pick_one_of_two_stacked_texts()
    {
        using var fixture = StackedQuestions();

        var state = Evaluate(fixture);

        VisibleNames(state).ShouldBe(["Question", "Next", "Back"], ignoreOrder: true);
        state.Conditions.ShouldBe([
            new ConditionOutcome("Question", "Hide", "AI_REPLACE", false),
            new ConditionOutcome("QuestionReplace", "Hide", "NOT AI_REPLACE", true),
        ], ignoreOrder: true);
    }

    [Fact]
    public void Caller_properties_override_the_package()
    {
        using var fixture = StackedQuestions();

        VisibleNames(Evaluate(fixture, new Dictionary<string, string?> { ["AI_REPLACE"] = "1" }))
            .ShouldBe(["QuestionReplace", "Next", "Back"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_null_or_empty_caller_value_unsets_the_property(string? value)
    {
        // Setting a property to an empty value deletes it in Windows Installer; JSON callers say the same with null.
        using var fixture = StackedQuestions().WithProperty("AI_REPLACE", "1");

        var state = Evaluate(fixture, new Dictionary<string, string?> { ["AI_REPLACE"] = value });

        VisibleNames(state).ShouldBe(["Question", "Next", "Back"], ignoreOrder: true);
        state.Properties.ContainsKey("AI_REPLACE").ShouldBeFalse();
    }

    [Fact]
    public void A_condition_that_needs_install_time_state_leaves_the_control_as_authored()
    {
        using var fixture = StackedQuestions().WithControlCondition(Dlg, "Next", "Hide", "&MainFeature = 3");

        var state = Evaluate(fixture);

        state.Controls.Single(c => c.Control.Name == "Next").Visible.ShouldBeTrue();
        state.Conditions.ShouldContain(new ConditionOutcome("Next", "Hide", "&MainFeature = 3", null));
    }

    [Fact]
    public void Enable_disable_and_default_follow_their_conditions()
    {
        using var fixture = StackedQuestions()
            .WithControlCondition(Dlg, "Back", "Enable", "1")
            .WithControlCondition(Dlg, "Next", "Disable", "1")
            .WithControlCondition(Dlg, "Back", "Default", "1");

        var state = Evaluate(fixture);

        state.Controls.Single(c => c.Control.Name == "Back").Enabled.ShouldBeTrue();
        state.Controls.Single(c => c.Control.Name == "Next").Enabled.ShouldBeFalse();
        state.DefaultControl.ShouldBe("Back");
    }

    [Fact]
    public void Without_conditions_the_authored_attributes_decide()
    {
        using var fixture = StackedQuestions();

        var state = Evaluate(fixture);

        state.Controls.Single(c => c.Control.Name == "Back").Enabled.ShouldBeFalse(); // attributes 1: visible only
        state.DefaultControl.ShouldBe("Next");
    }
}
