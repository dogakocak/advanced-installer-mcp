using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Lint;

// ADR 0009 "Used by the install".
public sealed class InstallUsageTests
{
    [Fact]
    public void A_custom_action_scheduled_in_the_execute_sequence_counts()
    {
        var usage = Read(p => p.WithCustomAction("SetConn", 51, "CONN", "Server=[DB_HOST]").WithExecuteSequence("SetConn", 100));

        usage.IsUsed("DB_HOST").ShouldBeTrue();
        usage.IsUsed("CONN").ShouldBeTrue(); // the setter's target is named in the row too
    }

    // The probe's one false positive: a property-setting action that only runs in the UI.
    [Fact]
    public void A_custom_action_only_in_the_UI_sequence_does_not_count()
    {
        var usage = Read(p => p.WithCustomAction("InitFlag", 51, "FLAG", "[FLAG_UI]").WithUiSequence("InitFlag", 100));

        usage.IsUsed("FLAG_UI").ShouldBeFalse();
    }

    [Fact]
    public void Execute_sequence_and_launch_conditions_count()
    {
        var usage = Read(p => p
            .WithCustomAction("Act", 1, "Dll", "Entry")
            .WithExecuteSequence("Act", 100, "REMOVEDATA = \"1\" AND NOT Installed")
            .WithLaunchCondition("DB_USER <> \"\"", "A user is required."));

        usage.IsUsed("REMOVEDATA").ShouldBeTrue();
        usage.IsUsed("DB_USER").ShouldBeTrue();
        usage.IsUsed("Installed").ShouldBeTrue();
    }

    [Fact]
    public void Names_inside_longer_names_do_not_count()
    {
        var usage = Read(p => p.WithCustomAction("Set", 51, "X", "[DB_HOST_NAME]").WithExecuteSequence("Set", 100));

        usage.IsUsed("DB_HOST").ShouldBeFalse();
    }

    [Fact]
    public void UI_tables_do_not_count()
    {
        var usage = Read(p => p
            .WithDialog("Dlg", 370, 270, "Box")
            .WithControl("Dlg", "Box", "Edit", 10, 10, 100, 18, property: "ONLY_UI", text: "[ONLY_UI]")
            .WithControlCondition("Dlg", "Box", "Disable", "ONLY_UI = \"x\""));

        usage.IsUsed("ONLY_UI").ShouldBeFalse();
    }

    private static InstallUsage Read(Func<TestPackage, TestPackage> configure)
    {
        using var fixture = configure(new TestPackage());
        using var database = fixture.Build();
        return DialogPackage.Load(database).InstallUsage;
    }
}
