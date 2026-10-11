using AdvancedInstallerMcp.Build;

namespace AdvancedInstallerMcp.Tests.Build;

public sealed class BuildResultTests
{
    private const string Project = @"C:\Work\Sample.aip";

    private static BuildOutput Output(
        bool finished = true, bool upToDate = false, IReadOnlyList<string>? errors = null, IReadOnlyList<string>? unrecognized = null) =>
        new([new BuildBlock("DefaultBuild", @"C:\Work\Sample-SetupFiles\Sample.msi")], [], errors ?? [],
            unrecognized ?? [], upToDate, finished);

    [Fact]
    public void Exit_code_zero_and_success_line_is_succeeded()
    {
        var result = BuildResult.Create(Project, new CliRun(0, "", "", TimedOut: false), Output(), []);

        result.Status.ShouldBe(BuildStatus.Succeeded);
        result.ExitCode.ShouldBe("0x00000000");
        result.ExitCodeName.ShouldBe("AI_NO_ERROR");
    }

    [Fact]
    public void Up_to_date_reports_the_builds_of_the_project()
    {
        BuildBlock[] fromProject = [new("DefaultBuild", @"C:\Work\Sample-SetupFiles\Sample.msi")];
        var output = Output(upToDate: true) with { Builds = [] };

        var result = BuildResult.Create(Project, new CliRun(0, "", "", false), output, fromProject);

        result.Status.ShouldBe(BuildStatus.UpToDate);
        result.Builds.ShouldBe(fromProject);
        result.Hint.ShouldBeNull();
    }

    [Fact]
    public void Up_to_date_without_a_package_path_suggests_a_rebuild()
    {
        var output = Output(upToDate: true) with { Builds = [] };

        BuildResult.Create(Project, new CliRun(0, "", "", false), output, [new BuildBlock("DefaultBuild", null)])
            .Hint.ShouldNotBeNull().ShouldContain("rebuild: true");
    }

    [Fact]
    public void Documented_failure_code_gets_its_name()
    {
        var result = BuildResult.Create(
            Project, new CliRun(unchecked((int)0xE001006C), "", "", false), Output(finished: false), []);

        result.Status.ShouldBe(BuildStatus.Failed);
        result.ExitCode.ShouldBe("0xE001006C");
        result.ExitCodeName.ShouldBe("AI_ERROR_NO_LICENSE");
    }

    [Fact]
    public void Unknown_code_is_undocumented()
    {
        BuildResult.Create(Project, new CliRun(-1, "", "", false), Output(finished: false), [])
            .ExitCodeName.ShouldBe("undocumented");
    }

    [Fact]
    public void Exit_code_zero_without_success_line_is_failed()
    {
        BuildResult.Create(Project, new CliRun(0, "", "", false), Output(finished: false), [])
            .Status.ShouldBe(BuildStatus.Failed);
    }

    [Fact]
    public void Timeout_is_failed_and_says_so()
    {
        var result = BuildResult.Create(Project, new CliRun(-1, "", "", TimedOut: true), Output(finished: false), []);

        result.Status.ShouldBe(BuildStatus.Failed);
        result.TimedOut.ShouldBeTrue();
    }

    [Fact]
    public void Standard_error_is_kept_as_unrecognized()
    {
        BuildResult.Create(Project, new CliRun(0, "", "boom \r\n", false), Output(), [])
            .Unrecognized.ShouldBe(["boom"]);
    }

    [Fact]
    public void Unrecognized_lines_are_capped()
    {
        var many = Enumerable.Range(0, 80).Select(i => $"line {i}").ToList();

        var result = BuildResult.Create(Project, new CliRun(0, "", "", false), Output(unrecognized: many), []);

        result.Unrecognized.Count.ShouldBe(BuildResult.MaxUnrecognized + 1);
        result.Unrecognized[^1].ShouldBe("… 30 more lines not shown");
    }
}
