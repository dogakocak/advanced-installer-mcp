namespace AdvancedInstallerMcp.Build;

public enum BuildStatus
{
    Succeeded,
    UpToDate,
    Failed,
}

// What the build tool returns (ADR 0002). The full output is not part of it; lines the parser does not know are.
public sealed record BuildResult(
    string Project,
    BuildStatus Status,
    string ExitCode,
    string ExitCodeName,
    IReadOnlyList<BuildBlock> Builds,
    IReadOnlyList<ValidationNote> Validation,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Unrecognized,
    bool TimedOut,
    string? Hint)
{
    public const int MaxUnrecognized = 50;

    // "Command line return codes" in the Advanced Installer 24.0 help. The help spells the first one AI_NO_ERORR.
    private static readonly Dictionary<uint, string> ExitCodeNames = new()
    {
        [0x00000000] = "AI_NO_ERROR",
        [0xE0010064] = "AI_ERROR_EXCEPTION",
        [0xE0010065] = "AI_ERROR_BAD_IDENTIFIER",
        [0xE0010066] = "AI_ERROR_DUPLICATED_SEARCH_SIGNATURE",
        [0xE0010067] = "AI_ERROR_SEARCH_NOT_FOUND",
        [0xE0010068] = "AI_ERROR_FILE_NOT_FOUND",
        [0xE0010069] = "AI_ERROR_MALFORMED_COMMAND_LINE",
        [0xE001006A] = "AI_ERROR_UNSUPPORTED_CMD_FILE_FORMAT",
        [0xE001006B] = "AI_ERROR_INVALID_MS_DOS_DATE",
        [0xE001006C] = "AI_ERROR_NO_LICENSE",
        [0xE001006D] = "AI_ERROR_NO_FLOATING_LICENSE",
    };

    // projectBuilds: the builds the call asked for, read from the .aip. The CLI prints a block only for the builds it
    // ran, so after a successful run every other one of them was up to date.
    public static BuildResult Create(string project, CliRun run, BuildOutput output, IReadOnlyList<BuildBlock> projectBuilds)
    {
        var exitCode = unchecked((uint)run.ExitCode);

        var status =
            run.TimedOut || exitCode != 0 || !output.FinishedSuccessfully ? BuildStatus.Failed
            : output.UpToDate ? BuildStatus.UpToDate
            : BuildStatus.Succeeded;

        var builds = status == BuildStatus.Failed ? output.Builds : WithUpToDateBuilds(output.Builds, projectBuilds);

        var unrecognized = output.Unrecognized
            .Concat(run.StandardError.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0))
            .ToList();
        if (unrecognized.Count > MaxUnrecognized)
        {
            var hidden = unrecognized.Count - MaxUnrecognized;
            unrecognized = [.. unrecognized.Take(MaxUnrecognized), $"… {hidden} more lines not shown"];
        }

        var hint = builds.Any(build => build.UpToDate && build.Package is null)
            ? "The project was already built and its package path could not be worked out. Pass rebuild: true to get it."
            : null;

        return new BuildResult(
            project,
            status,
            $"0x{exitCode:X8}",
            ExitCodeNames.GetValueOrDefault(exitCode, "undocumented"),
            builds,
            output.Validation,
            output.Errors,
            unrecognized,
            run.TimedOut,
            hint);
    }

    // In the project's build order: the block the output printed, or the project's own row marked up to date.
    private static List<BuildBlock> WithUpToDateBuilds(IReadOnlyList<BuildBlock> built, IReadOnlyList<BuildBlock> projectBuilds)
    {
        var merged = projectBuilds
            .Select(build => built.FirstOrDefault(b => string.Equals(b.Name, build.Name, StringComparison.OrdinalIgnoreCase))
                ?? build with { UpToDate = true })
            .ToList();
        merged.AddRange(built.Where(b => !merged.Contains(b)));
        return merged;
    }
}
