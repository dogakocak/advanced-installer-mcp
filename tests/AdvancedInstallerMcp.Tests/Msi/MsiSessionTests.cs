using AdvancedInstallerMcp.Msi;
using AdvancedInstallerMcp.Tests.Fixtures;

namespace AdvancedInstallerMcp.Tests.Msi;

[Collection(InstallSessionCollection.Name)]
public sealed class MsiSessionTests
{
    private static TestPackage Package() =>
        new TestPackage()
            .AsInstallPackage()
            .WithProperty("SERVER", "sql01")
            .WithProperty("TRUST", "1");

    private static MsiSession Open(TestPackage fixture)
    {
        fixture.Build().Dispose();
        return MsiSession.Open(fixture.Path);
    }

    [Fact]
    public void Reads_the_package_properties()
    {
        using var fixture = Package();
        using var session = Open(fixture);

        session.GetProperty("SERVER").ShouldBe("sql01");
        session.GetProperty("NOT_SET").ShouldBe("");
    }

    [Fact]
    public void Conditions_see_properties_set_on_the_session()
    {
        using var fixture = Package();
        using var session = Open(fixture);

        session.SetProperty("SERVER", "sql02");
        session.SetProperty("TRUST", "");

        session.EvaluateCondition("SERVER = \"sql02\" AND NOT TRUST").ShouldBe(true);
        session.EvaluateCondition("SERVER ~= \"SQL01\"").ShouldBe(false);
    }

    [Fact]
    public void An_empty_condition_is_true_and_a_broken_one_is_null()
    {
        using var fixture = Package();
        using var session = Open(fixture);

        session.EvaluateCondition("").ShouldBe(true);
        session.EvaluateCondition("SERVER = ").ShouldBeNull();
    }

    [Fact]
    public void Format_resolves_property_references()
    {
        using var fixture = Package();
        using var session = Open(fixture);

        session.Format("Server [SERVER], trust [TRUST][MISSING].").ShouldBe("Server sql01, trust 1.");
        session.Format("{}").ShouldBe(""); // how a ControlEvent sets a property to nothing
    }

    [Fact]
    public void A_property_setting_custom_action_runs()
    {
        using var fixture = Package().WithCustomAction("SetLabel", 51, "LABEL", "[SERVER]-test");
        using var session = Open(fixture);

        session.DoAction("SetLabel").ShouldBe(ActionOutcome.Ran);

        session.GetProperty("LABEL").ShouldBe("sql01-test");
    }

    [Fact]
    public void A_script_custom_action_is_refused_by_the_restricted_engine()
    {
        // Inline VBScript (type 38): if it ran, SCRIPT_RAN would be set.
        using var fixture = Package().WithCustomAction("RunScript", 38, null, "Session.Property(\"SCRIPT_RAN\") = \"1\"");
        using var session = Open(fixture);

        session.DoAction("RunScript").ShouldBe(ActionOutcome.NotRun);

        session.GetProperty("SCRIPT_RAN").ShouldBe("");
    }

    [Fact]
    public void An_unknown_action_is_not_run()
    {
        using var fixture = Package();
        using var session = Open(fixture);

        session.DoAction("NoSuchAction").ShouldBe(ActionOutcome.NotRun);
    }

    [Fact]
    public void A_database_without_the_package_summary_is_refused()
    {
        using var fixture = new TestPackage().WithProperty("SERVER", "sql01");
        fixture.Build().Dispose();

        Should.Throw<MsiException>(() => MsiSession.Open(fixture.Path)).ErrorCode.ShouldBe(1620u);
    }

    [Fact]
    public void A_missing_file_is_reported_as_such()
    {
        Should.Throw<FileNotFoundException>(() => MsiSession.Open(Path.Combine(Path.GetTempPath(), "no-such-package.msi")));
    }

    [Fact]
    public void Dispose_releases_the_package_file()
    {
        using var fixture = Package();
        var session = Open(fixture);

        session.Dispose();

        Should.NotThrow(() => File.Move(fixture.Path, fixture.Path + ".moved"));
        File.Move(fixture.Path + ".moved", fixture.Path);
    }
}
