using System.ComponentModel;
using AdvancedInstallerMcp.Build;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AdvancedInstallerMcp.Tools;

[McpServerToolType]
public static class BuildTool
{
    // One build at a time per server process (ADR 0002).
    private static readonly SemaphoreSlim OneBuild = new(1, 1);

    [McpServerTool(Name = "build", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Builds an Advanced Installer project (.aip) with AdvancedInstaller.com and returns a structured result instead " +
        "of the raw log: status (Succeeded, UpToDate, Failed), the exit code with its documented name, every build with " +
        "the path of the package it produced, the build errors, and the validators' notes kept apart. 'ERROR: AI_ICE…' " +
        "and 'ICE…' lines are validation notes: they do not fail the build and are often expected in a project; do not " +
        "change the project because of them unless asked. Lines the server does not recognise are returned in " +
        "'unrecognized', so read them. Exit code 0 does not prove the package content is right. Pass a returned " +
        "package path to lint_dialog to check its dialogs.")]
    public static Task<BuildResult> Build(
        [Description("Full path of the .aip project.")] string aipPath,
        [Description("Names of the builds to run (the project's build names). Omit to run every build.")]
        string[]? builds = null,
        [Description("Rebuild everything without the cache, even when the project has not changed.")]
        bool rebuild = false,
        [Description("Stop the build after this many minutes.")] int timeoutMinutes = 30,
        CancellationToken cancellationToken = default) =>
        BuildAsync(aipPath, builds, rebuild, timeoutMinutes, AdvancedInstallerCli.Locate(), cancellationToken);

    internal static async Task<BuildResult> BuildAsync(
        string aipPath, string[]? builds, bool rebuild, int timeoutMinutes, string? advancedInstallerCom,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(aipPath), ".aip", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"build takes an Advanced Installer project (.aip), not '{aipPath}'.");
        }

        if (!File.Exists(aipPath))
        {
            throw new McpException($"File not found: {aipPath}");
        }

        if (builds is not null && builds.Any(name => string.IsNullOrWhiteSpace(name) || name.Contains(';')))
        {
            throw new McpException("Build names must not be empty or contain ';'.");
        }

        if (timeoutMinutes < 1)
        {
            throw new McpException("timeoutMinutes must be at least 1.");
        }

        if (advancedInstallerCom is null)
        {
            throw new McpException(
                "Advanced Installer was not found. Install it, or set ADVINST_COM to the full path of AdvancedInstaller.com.");
        }

        var project = Path.GetFullPath(aipPath);
        List<string> arguments = [rebuild ? "/rebuild" : "/build", project];
        if (builds is { Length: > 0 })
        {
            arguments.AddRange(["-buildslist", string.Join(';', builds)]);
        }

        await OneBuild.WaitAsync(cancellationToken);
        CliRun run;
        try
        {
            run = await AdvancedInstallerCli.RunAsync(
                advancedInstallerCom, arguments, TimeSpan.FromMinutes(timeoutMinutes), cancellationToken);
        }
        finally
        {
            OneBuild.Release();
        }

        return BuildResult.Create(project, run, BuildOutputParser.Parse(run.StandardOutput), ProjectBuilds(project, builds));
    }

    private static IReadOnlyList<BuildBlock> ProjectBuilds(string project, string[]? requested)
    {
        try
        {
            var all = AipBuilds.Read(project);
            return requested is { Length: > 0 }
                ? all.Where(build => requested.Contains(build.Name, StringComparer.OrdinalIgnoreCase)).ToList()
                : all;
        }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
