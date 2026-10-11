using AdvancedInstallerMcp.Build;
using AdvancedInstallerMcp.Tools;
using ModelContextProtocol;

namespace AdvancedInstallerMcp.Tests.Tools;

public sealed class BuildToolTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("advinst-mcp-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Project()
    {
        var path = Path.Combine(_folder, "Sample.aip");
        File.WriteAllText(path, "<DOCUMENT />");
        return path;
    }

    private static Task<BuildResult> Build(string aip, string[]? builds = null, string? com = @"C:\fake\AdvancedInstaller.com") =>
        BuildTool.BuildAsync(aip, builds, rebuild: false, timeoutMinutes: 1, com, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Missing_project_is_explained()
    {
        (await Should.ThrowAsync<McpException>(() => Build(Path.Combine(_folder, "NoSuch.aip"))))
            .Message.ShouldContain("not found");
    }

    [Fact]
    public async Task Only_aip_projects_are_built()
    {
        var msi = Path.Combine(_folder, "Sample.msi");
        File.WriteAllText(msi, "");

        (await Should.ThrowAsync<McpException>(() => Build(msi))).Message.ShouldContain(".aip");
    }

    [Fact]
    public async Task Build_names_with_a_semicolon_are_rejected()
    {
        (await Should.ThrowAsync<McpException>(() => Build(Project(), ["A;B"]))).Message.ShouldContain("';'");
    }

    [Fact]
    public async Task Missing_Advanced_Installer_is_explained()
    {
        (await Should.ThrowAsync<McpException>(() => Build(Project(), com: null))).Message.ShouldContain("ADVINST_COM");
    }
}
