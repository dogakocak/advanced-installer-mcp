using System.Text.RegularExpressions;

namespace AdvancedInstallerMcp.Build;

// One "[ Name ]" block of the output. Package is the "Building package:" path.
// UpToDate: the CLI did not build it because nothing changed; it prints nothing for such a build, so the name and
// package come from the .aip (AipBuilds).
public sealed record BuildBlock(string Name, string? Package, bool UpToDate = false);

// An "ERROR: AI_ICE05. …" style line: a validator's finding, never a build failure by itself (ADR 0002).
public sealed record ValidationNote(string? Build, string Code, string Severity, string Message);

public sealed record BuildOutput(
    IReadOnlyList<BuildBlock> Builds,
    IReadOnlyList<ValidationNote> Validation,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Unrecognized,
    bool UpToDate,
    bool FinishedSuccessfully);

// Sorts AdvancedInstaller.com's standard output (ADR 0002). Only the known progress lines are dropped; a line the
// parser does not know ends up in Unrecognized, so nothing a newer Advanced Installer prints gets lost.
public static partial class BuildOutputParser
{
    private static readonly HashSet<string> ProgressLines = new(StringComparer.Ordinal)
    {
        "Checking builds status",
        "Build required.",
        "Prepare build",
        "Detecting MSI incompatible resources",
        "Preparing files",
        "Reusing archives from cache",
        "Creating CAB file(s)",
        "Preparing binaries",
        "Creating MSI database",
        "Inserting CAB file(s) into MSI",
        "Writing Summary Information",
        "Validating MSI",
    };

    private const string UpToDateLine = "All builds are up to date.";
    private const string SuccessLine = "Build finished successfully.";
    private const string PackagePrefix = "Building package: ";
    private const string ErrorPrefix = "Error: ";

    public static BuildOutput Parse(string output)
    {
        var lines = output.Split('\n').Select(line => line.TrimEnd(' ', '\r', '\t')).ToList();

        var builds = new List<BuildBlock>();
        var validation = new List<ValidationNote>();
        var errors = new List<string>();
        var unrecognized = new List<string>();
        var upToDate = false;
        string? finished = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var current = builds.Count > 0 ? builds[^1] : null;

            if (line.Length == 0 || ProgressLines.Contains(line) ||
                line.StartsWith("Build time:", StringComparison.Ordinal) ||
                line.StartsWith("Total build time:", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("Build finished", StringComparison.Ordinal))
            {
                finished = line;
            }
            else if (line == UpToDateLine)
            {
                upToDate = true;
            }
            else if (BuildHeader().Match(line) is { Success: true } header)
            {
                builds.Add(new BuildBlock(header.Groups["name"].Value, null));
            }
            else if (line.StartsWith(PackagePrefix, StringComparison.Ordinal) && current is not null)
            {
                builds[^1] = current with { Package = line[PackagePrefix.Length..].Trim() };
            }
            else if (ValidationLine().Match(line) is { Success: true } note)
            {
                validation.Add(new ValidationNote(
                    current?.Name, note.Groups["code"].Value, note.Groups["severity"].Value, note.Groups["message"].Value));
            }
            else if (line.StartsWith(ErrorPrefix, StringComparison.Ordinal))
            {
                // "Error: Resources referred by the project are missing:" lists one path per line up to a blank line.
                var details = new List<string>();
                while (i + 1 < lines.Count && lines[i + 1].Length > 0 &&
                       !lines[i + 1].StartsWith("Build finished", StringComparison.Ordinal))
                {
                    details.Add(lines[++i]);
                }

                var message = line[ErrorPrefix.Length..];
                errors.Add(details.Count == 0 ? message : $"{message} {string.Join(", ", details)}");
            }
            else if (line.StartsWith("Build not found:", StringComparison.Ordinal) ||
                     line.StartsWith("File not found:", StringComparison.Ordinal))
            {
                errors.Add(line);
            }
            else
            {
                unrecognized.Add(line);
            }
        }

        return new BuildOutput(builds, validation, errors, unrecognized, upToDate, finished == SuccessLine);
    }

    [GeneratedRegex(@"^\[ (?<name>.+) \]$")]
    private static partial Regex BuildHeader();

    [GeneratedRegex(@"^(?<severity>ERROR|WARNING): (?<code>(?:AI_)?ICE\d+)\.\s*(?<message>.*)$")]
    private static partial Regex ValidationLine();
}
