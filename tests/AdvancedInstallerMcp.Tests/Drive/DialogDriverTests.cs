using AdvancedInstallerMcp.Drive;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Drive;

[Collection(InstallSessionCollection.Name)]
public sealed class DialogDriverTests
{
    private const string Welcome = ConnectionWizard.Welcome;
    private const string Maint = ConnectionWizard.Maint;
    private const string Sql = ConnectionWizard.Sql;
    private const string Fail = ConnectionWizard.Fail;

    private static DriveResult Drive(TestPackage fixture, DriveRequest request)
    {
        fixture.Build().Dispose();
        return DialogDriver.Drive(fixture.Path, request);
    }

    private static DriveRequest Request(params DriveStep[] steps) => new(Steps: steps);

    private static bool Enabled(DriveResult result, string control) =>
        result.State.Controls.Single(c => c.Control.Name == control).Enabled;

    [Fact]
    public void Opening_runs_the_ui_sequence_up_to_the_first_page()
    {
        using var fixture = ConnectionWizard.Create();

        var result = Drive(fixture, Request());

        result.Opening.FirstPage.ShouldBe(Welcome);
        result.Opening.Actions.ShouldBe([new ActionReport("InitTrust", ActionReports.Ran)]);
        result.Opening.Properties.ShouldContain(new PropertyChange("UI_TRUST", "", "1"));
        result.Dialog.ShouldBe(Welcome);
    }

    [Fact]
    public void Maintenance_opens_the_page_a_change_shows()
    {
        using var fixture = ConnectionWizard.Create();

        var result = Drive(fixture, Request() with { Maintenance = true });

        result.Opening.FirstPage.ShouldBe(Maint);
    }

    [Fact]
    public void A_click_on_next_replaces_the_page()
    {
        using var fixture = ConnectionWizard.Create();

        var result = Drive(fixture, Request(new ClickStep("Next")));

        result.Steps.ShouldHaveSingleItem().Events.ShouldBe([
            new EventReport("NewDialog", Sql, "1", EventReports.PageReplaced),
        ]);
        result.Dialog.ShouldBe(Sql);
        Enabled(result, "Next").ShouldBeFalse();
    }

    [Fact]
    public void A_refused_action_is_reported_and_its_outcome_left_unknown()
    {
        using var fixture = ConnectionWizard.Create();

        var result = Drive(fixture, Request(new ClickStep("Next"), new ClickStep("TestButton")));

        var test = result.Steps[1];
        test.Events.ShouldContain(new EventReport("DoAction", "TestSql", "1", EventReports.ActionRefused));
        test.Events.ShouldContain(new EventReport("SpawnDialog", Fail, "SQL_OK <> \"1\"", EventReports.DialogOpened));
        test.Properties.ShouldBe([new PropertyChange("DB_TRUST", "", "True"), new PropertyChange("SQL_OK", "", "0")],
            ignoreOrder: true);
        test.DialogsOpened.ShouldBe([Fail]);
        Enabled(result, "Next").ShouldBeFalse();
    }

    [Fact]
    public void Action_results_stand_in_for_a_refused_action()
    {
        using var fixture = ConnectionWizard.Create();
        var request = Request(new ClickStep("Next"), new ClickStep("TestButton")) with
        {
            ActionResults = new Dictionary<string, IReadOnlyDictionary<string, string?>>
            {
                ["TestSql"] = new Dictionary<string, string?> { ["SQL_OK"] = "1" },
            },
        };

        var result = Drive(fixture, request);

        var test = result.Steps[1];
        test.Events.ShouldContain(new EventReport("DoAction", "TestSql", "1", EventReports.ActionResultsApplied));
        test.Events.ShouldContain(new EventReport("SpawnDialog", Fail, "SQL_OK <> \"1\"", EventReports.ConditionFalse));
        test.Properties.ShouldContain(new PropertyChange("SQL_TESTED", "", "1"));
        test.Controls.ShouldBe([new ControlChange("Next", Visible: true, Enabled: true)]);
        Enabled(result, "Next").ShouldBeTrue();
    }

    [Fact]
    public void A_click_on_a_disabled_button_does_nothing()
    {
        using var fixture = ConnectionWizard.Create();

        var result = Drive(fixture, Request(new ClickStep("Next"), new ClickStep("Next")));

        var blocked = result.Steps[1];
        blocked.Blocked.ShouldBe("disabled");
        blocked.Events.ShouldBeEmpty();
        result.Dialog.ShouldBe(Sql);
    }

    [Fact]
    public void An_empty_braces_argument_clears_the_property()
    {
        using var fixture = ConnectionWizard.Create().WithControlEvent(Sql, "TestButton", "[UI_TRUST]", "{}", null, 0);

        var result = Drive(fixture, Request(new ClickStep("Next"), new ClickStep("TestButton")));

        result.Steps[1].Properties.ShouldContain(new PropertyChange("UI_TRUST", "1", ""));
        result.Steps[1].Properties.ShouldContain(new PropertyChange("DB_TRUST", "", "False"));
    }

    [Fact]
    public void An_unchecked_box_leaves_its_property_empty()
    {
        using var fixture = ConnectionWizard.Create();

        var result = Drive(fixture, Request(
            new ClickStep("Next"),
            new SetStep(new Dictionary<string, string?> { ["UI_TRUST"] = "" }),
            new ClickStep("TestButton")));

        result.Steps[1].Properties.ShouldBe([new PropertyChange("UI_TRUST", "1", "")]);
        result.Steps[2].Properties.ShouldContain(new PropertyChange("DB_TRUST", "", "False"));
    }

    [Fact]
    public void The_last_page_ends_the_wizard_and_later_steps_are_refused()
    {
        using var fixture = ConnectionWizard.Create();
        var request = Request(new ClickStep("Next"), new ClickStep("TestButton"), new ClickStep("Next"),
            new ClickStep("Next")) with
        {
            ActionResults = new Dictionary<string, IReadOnlyDictionary<string, string?>>
            {
                ["TestSql"] = new Dictionary<string, string?> { ["SQL_OK"] = "1" },
            },
        };

        var result = Drive(fixture, request);

        result.Ended.ShouldBe("Return");
        result.Steps[3].Events.ShouldBe([new EventReport("EndDialog", "Return", "1", EventReports.WizardEnded)]);

        Should.Throw<DriveException>(() => DialogDriver.Drive(fixture.Path, request with
        {
            Steps = [.. request.Steps, new ClickStep("Next")],
        })).Message.ShouldContain("ended");
    }

    [Fact]
    public void Hidden_property_values_are_masked()
    {
        using var fixture = ConnectionWizard.Create();

        var result = Drive(fixture, Request(
            new ClickStep("Next"),
            new SetStep(new Dictionary<string, string?> { ["DB_PASSWORD"] = "s3cret", ["DB_SERVER"] = "sql01" })));

        result.Steps[1].Properties.ShouldBe([
            new PropertyChange("DB_PASSWORD", "", "***"),
            new PropertyChange("DB_SERVER", "", "sql01"),
        ], ignoreOrder: true);
    }

    [Fact]
    public void Long_values_are_shortened_in_the_report()
    {
        // Advanced Installer passes whole PowerShell scripts through CustomActionData; 20,000 characters per change
        // would drown the report.
        using var fixture = ConnectionWizard.Create();
        var script = new string('x', 1000);

        var result = Drive(fixture, Request(new SetStep(new Dictionary<string, string?> { ["CustomActionData"] = script })));

        var change = result.Steps.ShouldHaveSingleItem().Properties.ShouldHaveSingleItem();
        change.New.ShouldBe(new string('x', 120) + "… (1000 characters)");
    }

    [Fact]
    public void A_named_dialog_is_opened_directly_after_the_opening()
    {
        using var fixture = ConnectionWizard.Create();

        var result = Drive(fixture, Request(new ClickStep("TestButton")) with { Dialog = Sql });

        result.Opening.FirstPage.ShouldBe(Welcome);
        result.Steps.ShouldHaveSingleItem().Dialog.ShouldBe(Sql);
    }

    [Theory]
    [InlineData("NoSuchDlg", "Next", "NoSuchDlg")]
    [InlineData(null, "NoSuchButton", "NoSuchButton")]
    public void Unknown_dialogs_and_controls_are_explained(string? dialog, string control, string expected)
    {
        using var fixture = ConnectionWizard.Create();

        Should.Throw<DriveException>(() => Drive(fixture, Request(new ClickStep(control)) with { Dialog = dialog }))
            .Message.ShouldContain(expected);
    }
}
