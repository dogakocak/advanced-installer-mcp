using AdvancedInstallerMcp.Drive;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Drive;

[Collection(InstallSessionCollection.Name)]
public sealed class DialogDriverTests
{
    private const string Welcome = "WelcomeDlg";
    private const string Maint = "MaintDlg";
    private const string Sql = "SqlDlg";
    private const string Fail = "SqlFailDlg";
    private const string Done = "DoneDlg";

    // A connection page like the measured setup UI task: a trust check box set by a property-setting action when
    // the wizard opens, a Test button whose real check is a script (refused by the restricted engine), and Next
    // enabled only after a successful test.
    private static TestPackage ConnectionWizard()
    {
        var package = new TestPackage()
            .AsInstallPackage()
            .WithProperty("DefaultUIFont", "DlgFont8")
            .WithProperty("MsiHiddenProperties", "DB_PASSWORD")
            .WithTextStyle("DlgFont8", "Tahoma", 8)
            .WithCustomAction("InitTrust", 51, "UI_TRUST", "1")
            .WithCustomAction("TestSql", 38, null, "Session.Property(\"SQL_OK\") = \"1\"")
            .WithUiSequence("InitTrust", 10)
            .WithUiSequence("PrepareDlg", 20)
            .WithDialog("PrepareDlg", 370, 270, "Next", attributes: 1) // modeless: msiexec shows it and goes on
            .WithControl("PrepareDlg", "Next", "PushButton", 236, 243, 56, 17, "Next")
            .WithUiSequence(Welcome, 100, "NOT Installed")
            .WithUiSequence(Maint, 101, "Installed");

        foreach (var name in new[] { Welcome, Maint, Done })
        {
            package
                .WithDialog(name, 370, 270, "Next", defaultControl: "Next")
                .WithControl(name, "Next", "PushButton", 236, 243, 56, 17, "Next");
        }

        return package
            .WithControlEvent(Welcome, "Next", "NewDialog", Sql)
            .WithControlEvent(Maint, "Next", "NewDialog", Sql)
            .WithControlEvent(Done, "Next", "EndDialog", "Return")
            .WithDialog(Sql, 370, 270, "ServerEdit", defaultControl: "Next")
            .WithControl(Sql, "ServerEdit", "Edit", 20, 60, 200, 18, property: "DB_SERVER", next: "PasswordEdit")
            .WithControl(Sql, "PasswordEdit", "Edit", 20, 84, 200, 18, attributes: 0x200003, property: "DB_PASSWORD",
                next: "TrustCheck")
            .WithControl(Sql, "TrustCheck", "CheckBox", 20, 110, 200, 12, "Trust server certificate",
                property: "UI_TRUST", next: "TestButton")
            .WithControl(Sql, "TestButton", "PushButton", 230, 108, 80, 17, "Test Connection", next: "Next")
            .WithControl(Sql, "Next", "PushButton", 236, 243, 56, 17, "Next", next: "ServerEdit")
            .WithControlEvent(Sql, "TestButton", "[DB_TRUST]", "True", "UI_TRUST", 1)
            .WithControlEvent(Sql, "TestButton", "[DB_TRUST]", "False", "NOT UI_TRUST", 2)
            .WithControlEvent(Sql, "TestButton", "[SQL_OK]", "0", null, 3)
            .WithControlEvent(Sql, "TestButton", "DoAction", "TestSql", null, 4)
            .WithControlEvent(Sql, "TestButton", "[SQL_TESTED]", "1", "SQL_OK = \"1\"", 5)
            .WithControlEvent(Sql, "TestButton", "SpawnDialog", Fail, "SQL_OK <> \"1\"", 6)
            .WithControlEvent(Sql, "Next", "NewDialog", Done, "SQL_TESTED = \"1\"")
            .WithControlCondition(Sql, "Next", "Disable", "SQL_TESTED <> \"1\"")
            .WithControlCondition(Sql, "Next", "Enable", "SQL_TESTED = \"1\"")
            .WithDialog(Fail, 200, 80, "OK")
            .WithControl(Fail, "OK", "PushButton", 70, 50, 56, 17, "OK");
    }

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
        using var fixture = ConnectionWizard();

        var result = Drive(fixture, Request());

        result.Opening.FirstPage.ShouldBe(Welcome);
        result.Opening.Actions.ShouldBe([new ActionReport("InitTrust", ActionReports.Ran)]);
        result.Opening.Properties.ShouldContain(new PropertyChange("UI_TRUST", "", "1"));
        result.Dialog.ShouldBe(Welcome);
    }

    [Fact]
    public void Maintenance_opens_the_page_a_change_shows()
    {
        using var fixture = ConnectionWizard();

        var result = Drive(fixture, Request() with { Maintenance = true });

        result.Opening.FirstPage.ShouldBe(Maint);
    }

    [Fact]
    public void A_click_on_next_replaces_the_page()
    {
        using var fixture = ConnectionWizard();

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
        using var fixture = ConnectionWizard();

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
        using var fixture = ConnectionWizard();
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
        using var fixture = ConnectionWizard();

        var result = Drive(fixture, Request(new ClickStep("Next"), new ClickStep("Next")));

        var blocked = result.Steps[1];
        blocked.Blocked.ShouldBe("disabled");
        blocked.Events.ShouldBeEmpty();
        result.Dialog.ShouldBe(Sql);
    }

    [Fact]
    public void An_empty_braces_argument_clears_the_property()
    {
        using var fixture = ConnectionWizard().WithControlEvent(Sql, "TestButton", "[UI_TRUST]", "{}", null, 0);

        var result = Drive(fixture, Request(new ClickStep("Next"), new ClickStep("TestButton")));

        result.Steps[1].Properties.ShouldContain(new PropertyChange("UI_TRUST", "1", ""));
        result.Steps[1].Properties.ShouldContain(new PropertyChange("DB_TRUST", "", "False"));
    }

    [Fact]
    public void An_unchecked_box_leaves_its_property_empty()
    {
        using var fixture = ConnectionWizard();

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
        using var fixture = ConnectionWizard();
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
        using var fixture = ConnectionWizard();

        var result = Drive(fixture, Request(
            new ClickStep("Next"),
            new SetStep(new Dictionary<string, string?> { ["DB_PASSWORD"] = "s3cret", ["DB_SERVER"] = "sql01" })));

        result.Steps[1].Properties.ShouldBe([
            new PropertyChange("DB_PASSWORD", "", "***"),
            new PropertyChange("DB_SERVER", "", "sql01"),
        ], ignoreOrder: true);
    }

    [Fact]
    public void A_named_dialog_is_opened_directly_after_the_opening()
    {
        using var fixture = ConnectionWizard();

        var result = Drive(fixture, Request(new ClickStep("TestButton")) with { Dialog = Sql });

        result.Opening.FirstPage.ShouldBe(Welcome);
        result.Steps.ShouldHaveSingleItem().Dialog.ShouldBe(Sql);
    }

    [Theory]
    [InlineData("NoSuchDlg", "Next", "NoSuchDlg")]
    [InlineData(null, "NoSuchButton", "NoSuchButton")]
    public void Unknown_dialogs_and_controls_are_explained(string? dialog, string control, string expected)
    {
        using var fixture = ConnectionWizard();

        Should.Throw<DriveException>(() => Drive(fixture, Request(new ClickStep(control)) with { Dialog = dialog }))
            .Message.ShouldContain(expected);
    }
}
