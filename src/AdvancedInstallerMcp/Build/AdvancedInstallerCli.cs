using System.Diagnostics;
using System.Text;

namespace AdvancedInstallerMcp.Build;

public sealed record CliRun(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

// Finds and runs AdvancedInstaller.com (ADR 0002).
public static class AdvancedInstallerCli
{
    public static string? Locate() =>
        Locate(
            Environment.GetEnvironmentVariable("ADVINST_COM"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Caphyon"));

    // advinstCom: the ADVINST_COM value, which the installer writes with surrounding quotes.
    // caphyonFolder: holds one "Advanced Installer <version>" folder per installed version.
    public static string? Locate(string? advinstCom, string caphyonFolder)
    {
        var fromVariable = advinstCom?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(fromVariable) && File.Exists(fromVariable))
        {
            return fromVariable;
        }

        if (!Directory.Exists(caphyonFolder))
        {
            return null;
        }

        return Directory.EnumerateDirectories(caphyonFolder, "Advanced Installer *")
            .Select(folder => (
                Version: Version.TryParse(Path.GetFileName(folder)["Advanced Installer ".Length..], out var version)
                    ? version
                    : null,
                Com: Path.Combine(folder, "bin", "x86", "AdvancedInstaller.com")))
            .Where(candidate => candidate.Version is not null && File.Exists(candidate.Com))
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.Com)
            .FirstOrDefault();
    }

    // No console window: with one, AdvancedInstaller.com wraps long lines at the console width. Output is UTF-8.
    // On timeout or cancellation the whole process tree is killed, because AdvancedInstaller.com only starts
    // advinst.exe, which does the build.
    public static async Task<CliRun> RunAsync(
        string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            timedOut = true;
        }

        return new CliRun(process.ExitCode, await standardOutput, await standardError, timedOut);
    }
}
