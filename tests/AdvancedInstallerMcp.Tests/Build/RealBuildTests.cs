using AdvancedInstallerMcp.Build;
using AdvancedInstallerMcp.Tools;

namespace AdvancedInstallerMcp.Tests.Build;

// Runs a real Advanced Installer build when ADVINST_MCP_REAL_AIP names a project that builds cleanly.
// Skipped otherwise, so the suite never needs Advanced Installer.
public sealed class RealBuildTests
{
    [Fact]
    public async Task A_real_project_builds_and_then_is_up_to_date()
    {
        var project = Environment.GetEnvironmentVariable("ADVINST_MCP_REAL_AIP");
        Assert.SkipWhen(string.IsNullOrEmpty(project), "ADVINST_MCP_REAL_AIP is not set.");

        var built = await BuildTool.Build(project!, rebuild: true, cancellationToken: TestContext.Current.CancellationToken);

        built.Status.ShouldBe(BuildStatus.Succeeded, string.Join(Environment.NewLine, built.Errors.Concat(built.Unrecognized)));
        built.Builds.ShouldNotBeEmpty();
        built.Builds.ShouldAllBe(build => build.Package != null && File.Exists(build.Package));
        built.Unrecognized.ShouldBeEmpty();

        var again = await BuildTool.Build(project!, cancellationToken: TestContext.Current.CancellationToken);

        again.Status.ShouldBe(BuildStatus.UpToDate);
        again.Builds.Select(build => build.Name).ShouldBe(built.Builds.Select(build => build.Name));
    }
}
